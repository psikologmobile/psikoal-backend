using PsikoAl.Common.Dtos.Admin;
using PsikoAl.Common.Dtos.Wallet;
using PsikoAl.Common.Presentation;
using PsikoAl.Data.Entities;

namespace PsikoAl.Services.Mapping;

/// <summary>Defter satırı + (varsa) bağlı ilan başlığı ve admin adı.</summary>
public sealed record WalletTransactionReadModel(WalletTransaction Transaction, string? ListingTitle, string? AdminName);

public static class WalletMapper
{
    public static WalletTransactionDto ToTransactionDto(WalletTransactionReadModel row, DateTimeOffset now)
    {
        var transaction = row.Transaction;
        return new WalletTransactionDto(
            transaction.Id,
            transaction.Type,
            WalletLabels.TypeLabel(transaction.Type),
            transaction.AmountKurus,
            MoneyLabel.WithSign(transaction.AmountKurus),
            transaction.FreeRightsDelta,
            MoneyLabel.Rights(transaction.FreeRightsDelta),
            transaction.BalanceAfterKurus,
            MoneyLabel.Try(transaction.BalanceAfterKurus),
            WalletLabels.Description(transaction.Type, row.ListingTitle, transaction.Reason),
            transaction.ListingId,
            row.ListingTitle,
            transaction.OfferId,
            transaction.PaymentId,
            transaction.CreatedAt,
            RelativeTimeTr.From(transaction.CreatedAt, now));
    }

    public static AdminWalletTransactionDto ToAdminTransactionDto(WalletTransactionReadModel row)
    {
        var transaction = row.Transaction;
        return new AdminWalletTransactionDto(
            transaction.Id,
            transaction.Type,
            WalletLabels.TypeLabel(transaction.Type),
            transaction.AmountKurus,
            MoneyLabel.WithSign(transaction.AmountKurus),
            transaction.FreeRightsDelta,
            MoneyLabel.Rights(transaction.FreeRightsDelta),
            transaction.BalanceAfterKurus,
            MoneyLabel.Try(transaction.BalanceAfterKurus),
            transaction.FreeRightsAfter,
            WalletLabels.Description(transaction.Type, row.ListingTitle, transaction.Reason),
            transaction.Reason,
            transaction.ListingId,
            transaction.OfferId,
            transaction.PaymentId,
            row.AdminName,
            transaction.CreatedAt);
    }

    public static WalletPaymentDto ToPaymentDto(WalletPayment payment)
        => new(
            payment.Id,
            payment.AmountKurus,
            MoneyLabel.Try(payment.AmountKurus),
            payment.Status,
            WalletLabels.PaymentStatusLabel(payment.Status),
            payment.CreatedAt,
            payment.CompletedAt,
            payment is { InvoicePath: not null, InvoiceFileName: { } fileName, InvoiceUploadedAt: { } uploadedAt }
                ? new WalletInvoiceDto(fileName, uploadedAt)
                : null);
}
