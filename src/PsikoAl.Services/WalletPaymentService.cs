using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PsikoAl.Common.Constants;
using PsikoAl.Common.Dtos.Payment;
using PsikoAl.Common.Exceptions;
using PsikoAl.Common.Presentation;
using PsikoAl.Data.Entities;
using PsikoAl.Data.Repositories.Abstractions;
using PsikoAl.Services.Abstractions;
using PsikoAl.Services.Options;
using PsikoAl.Services.Payments;
using PsikoAl.Services.Wallet;

namespace PsikoAl.Services;

// Bakiye yükleme YALNIZ web sitesinden (docs/PAYMENTS.md). Akış:
//   init     → tutar aralık kontrolü → PENDING ödeme satırı + PayTR get-token → iframe adresi
//   callback → hash doğrulama → PENDING'den PAID/FAILED'a TEK geçiş → (PAID ise)
//              TL bakiyeye ekle + TOPUP defter satırı + BALANCE_TOPUP bildirimi
// PayTR aynı bildirimi birden fazla kez gönderebilir; geçiş koşullu UPDATE olduğu için
// ikinci bildirim bakiyeyi tekrar yüklemez (AlreadyProcessed → yine "OK").
public sealed class WalletPaymentService(
    IUnitOfWork unitOfWork,
    IPayTrClient payTrClient,
    INotificationService notificationService,
    IOptions<PayTrOptions> options,
    TimeProvider timeProvider,
    ILogger<WalletPaymentService> logger) : IWalletPaymentService
{
    public const string IframeUrlPrefix = "https://www.paytr.com/odeme/guvenli/";
    public const string SuccessStatus = "success";
    private const string MerchantOidPrefix = "PA";
    private const string FallbackAddress = "Türkiye";
    private const string FallbackPhone = "0000000000";

    public async Task<PayTrInitResultDto> InitPayTrAsync(
        Guid expertUserId,
        long amountKurus,
        string userIp,
        CancellationToken cancellationToken)
    {
        if (!options.Value.IsConfigured)
        {
            throw new DomainException(ErrorKeys.PaymentProviderUnavailable);
        }

        var expert = await unitOfWork.Experts.GetWithProfileAsync(expertUserId, cancellationToken)
            ?? throw await ExpertAccess.MissingExpertErrorAsync(
                unitOfWork, expertUserId, ErrorKeys.ExpertRoleRequired, cancellationToken);
        var profile = expert.Profile ?? throw new DomainException(ErrorKeys.ProfileNotFound);

        if (expert.Status != ExpertStatuses.Approved)
        {
            throw new DomainException(ErrorKeys.TopupExpertNotApproved);
        }

        // Kuruşlu tutar kabul edilmez: etiketler tam TL gösteriyor, yükleme de tam TL olmalı.
        var (minKurus, maxKurus) = await WalletService.TopupRangeAsync(unitOfWork.SystemSettings, cancellationToken);
        if (amountKurus < minKurus || amountKurus > maxKurus || amountKurus % 100 != 0)
        {
            throw new DomainException(
                ErrorKeys.TopupAmountOutOfRange,
                "amountKurus",
                new { minKurus, maxKurus });
        }

        var payment = new WalletPayment
        {
            ExpertId = expert.Id,
            MerchantOid = NewMerchantOid(),
            AmountKurus = amountKurus,
            Status = WalletPaymentStatuses.Pending,
        };

        await unitOfWork.WalletPayments.AddAsync(payment, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var amountTry = amountKurus / 100m;
        var tokenResult = await payTrClient.GetIframeTokenAsync(
            new PayTrTokenRequest(
                userIp,
                payment.MerchantOid,
                profile.Email,
                checked((int)amountKurus),
                [new PayTrBasketItem($"PsikoAl bakiye yükleme ({MoneyLabel.Try(amountKurus)})", amountTry, 1)],
                $"{profile.FirstName} {profile.LastName}".Trim(),
                string.IsNullOrWhiteSpace(profile.City) ? FallbackAddress : profile.City,
                string.IsNullOrWhiteSpace(profile.Phone) ? FallbackPhone : profile.Phone),
            cancellationToken);

        if (tokenResult.Token is not { Length: > 0 } token)
        {
            await unitOfWork.WalletPayments.TryCompleteAsync(
                payment.Id,
                WalletPaymentStatuses.Failed,
                JsonSerializer.Serialize(new { stage = "get-token", reason = tokenResult.FailureReason }),
                timeProvider.GetUtcNow(),
                cancellationToken);
            throw new DomainException(ErrorKeys.PaymentProviderUnavailable);
        }

        return new PayTrInitResultDto(token, payment.MerchantOid, IframeUrlPrefix + token);
    }

    public async Task<PayTrCallbackOutcome> HandlePayTrCallbackAsync(
        PayTrCallbackDto callback,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.IsConfigured
            || !PayTrHash.IsValidCallback(
                callback.MerchantOid,
                callback.Status,
                callback.TotalAmount,
                callback.Hash,
                settings.MerchantSalt ?? string.Empty,
                settings.MerchantKey ?? string.Empty))
        {
            logger.LogWarning("PayTR bildirimi reddedildi: hash geçersiz (oid: {MerchantOid})", callback.MerchantOid);
            return PayTrCallbackOutcome.InvalidHash;
        }

        var payment = await unitOfWork.WalletPayments.GetByMerchantOidAsync(callback.MerchantOid, cancellationToken);
        if (payment is null)
        {
            logger.LogError("PayTR bildirimi bilinmeyen sipariş için geldi (oid: {MerchantOid})", callback.MerchantOid);
            return PayTrCallbackOutcome.UnknownOrder;
        }

        if (payment.Status != WalletPaymentStatuses.Pending)
        {
            return PayTrCallbackOutcome.AlreadyProcessed;
        }

        var payload = ProviderPayload(callback);
        if (callback.Status == SuccessStatus && IsFullyPaid(callback.TotalAmount, payment.AmountKurus))
        {
            return await CompletePaidAsync(payment, payload, cancellationToken);
        }

        if (callback.Status == SuccessStatus)
        {
            logger.LogError(
                "PayTR başarılı bildirdi ama tahsil edilen tutar eksik (oid: {MerchantOid}, total_amount: {TotalAmount})",
                callback.MerchantOid,
                callback.TotalAmount);
        }

        var failed = await unitOfWork.WalletPayments.TryCompleteAsync(
            payment.Id,
            WalletPaymentStatuses.Failed,
            payload,
            timeProvider.GetUtcNow(),
            cancellationToken);
        return failed ? PayTrCallbackOutcome.Failed : PayTrCallbackOutcome.AlreadyProcessed;
    }

    private async Task<PayTrCallbackOutcome> CompletePaidAsync(
        WalletPayment payment,
        string payload,
        CancellationToken cancellationToken)
    {
        WalletTransaction entry;
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            var transitioned = await unitOfWork.WalletPayments.TryCompleteAsync(
                payment.Id,
                WalletPaymentStatuses.Paid,
                payload,
                timeProvider.GetUtcNow(),
                cancellationToken);
            if (!transitioned)
            {
                return PayTrCallbackOutcome.AlreadyProcessed;
            }

            entry = await WalletLedger.TryRecordAsync(
                    unitOfWork,
                    new WalletTransaction
                    {
                        ExpertId = payment.ExpertId,
                        AmountKurus = payment.AmountKurus,
                        Type = WalletTransactionTypes.Topup,
                        PaymentId = payment.Id,
                    },
                    cancellationToken)
                ?? throw new InvalidOperationException($"Ödemenin uzmanı bulunamadı (oid: {payment.MerchantOid}).");

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await notificationService.NotifyAsync(
            payment.ExpertId,
            NotificationTypes.BalanceTopup,
            new Dictionary<string, string>
            {
                ["amount"] = MoneyLabel.Try(payment.AmountKurus),
                ["balance"] = MoneyLabel.Try(entry.BalanceAfterKurus),
            },
            JsonSerializer.Serialize(new { paymentId = payment.Id.ToString() }),
            cancellationToken);

        return PayTrCallbackOutcome.Paid;
    }

    // Taksit/vade farkıyla total_amount ödeme tutarını AŞABİLİR; eksik olamaz.
    private static bool IsFullyPaid(string totalAmount, long expectedKurus)
        => long.TryParse(totalAmount, NumberStyles.None, CultureInfo.InvariantCulture, out var kurus)
            && kurus >= expectedKurus;

    private static string NewMerchantOid()
        => MerchantOidPrefix + Guid.NewGuid().ToString("N").ToUpperInvariant();

    // Hash ve kart bilgisi BİLEREK yok: yalnız mutabakat için gereken alanlar.
    private static string ProviderPayload(PayTrCallbackDto callback)
        => JsonSerializer.Serialize(new
        {
            status = callback.Status,
            totalAmount = callback.TotalAmount,
            paymentAmount = callback.PaymentAmount,
            paymentType = callback.PaymentType,
            currency = callback.Currency,
            testMode = callback.TestMode,
            failedReasonCode = callback.FailedReasonCode,
            failedReasonMessage = callback.FailedReasonMessage,
        });
}
