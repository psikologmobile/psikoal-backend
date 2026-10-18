using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PsikoAl.Common.Constants;
using PsikoAl.Common.Dtos.Admin;
using PsikoAl.Common.Exceptions;
using PsikoAl.Common.Presentation;
using PsikoAl.Data.Entities;
using PsikoAl.Data.Repositories.Abstractions;
using PsikoAl.Services.Abstractions;
using PsikoAl.Services.Pricing;

namespace PsikoAl.Services;

// Fiyatlandırma dönemleri (REVIZE-2 §1, §9). Dönemler eklenir, düzenlenmez: geçmiş bir
// dönemi değiştirmek o dönemde onaylanmış ilanların neden o ücretle yayına çıktığını
// açıklanamaz kılardı. Yayındaki ilanlar zaten kendi kopyalarını taşıyor.
public sealed class AdminPricingService(
    IUnitOfWork unitOfWork,
    IAdminGuard adminGuard,
    TimeProvider timeProvider) : IAdminPricingService
{
    public async Task<AdminPagedResultDto<AdminPricingPeriodDto>> ListAsync(
        int? page,
        int? perPage,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var current = await unitOfWork.PricingPeriods.GetCurrentAsync(now, cancellationToken);

        var (rows, meta) = await unitOfWork.PricingPeriods.Query()
            .OrderByDescending(period => period.EffectiveFrom)
            .ThenByDescending(period => period.CreatedAt)
            .Select(period => new
            {
                Period = period,
                CreatedByName = period.CreatedByAdmin != null ? period.CreatedByAdmin.DisplayName : null,
            })
            .ToPageAsync(page, perPage, cancellationToken);

        return new AdminPagedResultDto<AdminPricingPeriodDto>(
            [.. rows.Select(row => ToDto(row.Period, row.Period.Id == current?.Id, row.CreatedByName))],
            meta);
    }

    public async Task<AdminPricingPeriodDto> CreateAsync(
        Guid actorAuthUserId,
        CreatePricingPeriodDto request,
        CancellationToken cancellationToken)
    {
        var actor = await adminGuard.GetActiveAdminAsync(actorAuthUserId, cancellationToken)
            ?? throw new DomainException(ErrorKeys.AdminUserNotFound);

        EnsureValid(request);
        var now = timeProvider.GetUtcNow();
        if (request.EffectiveFrom is { } effectiveFrom && effectiveFrom < now.AddMinutes(-1))
        {
            throw new DomainException(ErrorKeys.ValidationFailed, "effectiveFrom");
        }

        var period = new PricingPeriod
        {
            EffectiveFrom = request.EffectiveFrom ?? now,
            BudgetMinTry = request.BudgetMinTry,
            BudgetMaxTry = request.BudgetMaxTry,
            FeeMinTry = request.FeeMinTry,
            FeeMaxTry = request.FeeMaxTry,
            DailyDecayPct = request.DailyDecayPct,
            PerOfferDecayPct = request.PerOfferDecayPct,
            QualityWeights = QualityWeights.FromDto(request.QualityWeights).ToJson(),
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            CreatedBy = actor.Id,
        };

        await unitOfWork.PricingPeriods.AddAsync(period, cancellationToken);
        await unitOfWork.AuditLogs.AddAsync(
            new AuditLog
            {
                AdminUserId = actor.Id,
                ActorType = AuditActorTypes.Admin,
                Action = AuditActions.PricingPeriodCreate,
                EntityType = "pricing_period",
                EntityId = period.Id.ToString(),
                NewValue = JsonSerializer.Serialize(request),
                Reason = period.Note,
            },
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var current = await unitOfWork.PricingPeriods.GetCurrentAsync(timeProvider.GetUtcNow(), cancellationToken);
        return ToDto(period, period.Id == current?.Id, actor.DisplayName);
    }

    public async Task<PricingPreviewResultDto> PreviewAsync(PricingPreviewRequestDto request, CancellationToken cancellationToken)
    {
        int budgetMinTry, feeMinTry, feeMaxTry;
        QualityWeights weights;
        if (request.Period is { } candidate)
        {
            EnsureValid(candidate);
            (budgetMinTry, feeMinTry, feeMaxTry) = (candidate.BudgetMinTry, candidate.FeeMinTry, candidate.FeeMaxTry);
            weights = QualityWeights.FromDto(candidate.QualityWeights);
        }
        else
        {
            var current = await ListingPricing.GetCurrentPeriodAsync(unitOfWork, timeProvider.GetUtcNow(), cancellationToken);
            (budgetMinTry, feeMinTry, feeMaxTry) = (current.BudgetMinTry, current.FeeMinTry, current.FeeMaxTry);
            weights = QualityWeights.Parse(current.QualityWeights);
        }

        var sample = request.Listing;
        var quality = QualityScoreCalculator.Calculate(
            new QualityInput(sample.Title, sample.Description, sample.BudgetMax, sample.City, sample.SessionType, sample.SpecializationCount),
            weights,
            budgetMinTry);

        var feeMin = feeMinTry * ListingPricing.KurusPerTry;
        var feeMax = feeMaxTry * ListingPricing.KurusPerTry;
        var initial = OfferFeeCalculator.InitialFeeKurus(quality.Score, feeMin, feeMax);

        return new PricingPreviewResultDto(
            quality.Score,
            new QualityBreakdownDto(
                quality.Description,
                quality.Title,
                quality.Budget,
                quality.Location,
                quality.SessionBoth,
                quality.Specialization),
            initial,
            MoneyLabel.Try(initial),
            feeMin,
            MoneyLabel.Try(feeMin),
            feeMax,
            MoneyLabel.Try(feeMax));
    }

    // Veritabanı CHECK'leriyle aynı kurallar + ağırlıkların anlamlı olması. Hata alanı,
    // panelin ilgili kutuyu işaretleyebilmesi için döner.
    internal static void EnsureValid(CreatePricingPeriodDto request)
    {
        static void Require(bool condition, string field)
        {
            if (!condition)
            {
                throw new DomainException(ErrorKeys.ValidationFailed, field);
            }
        }

        Require(request.BudgetMinTry > 0, "budgetMinTry");
        Require(request.BudgetMaxTry is null || request.BudgetMaxTry >= request.BudgetMinTry, "budgetMaxTry");
        Require(request.FeeMinTry >= 0, "feeMinTry");
        Require(request.FeeMaxTry >= request.FeeMinTry, "feeMaxTry");
        Require(request.DailyDecayPct is >= 0 and <= 100, "dailyDecayPct");
        Require(request.PerOfferDecayPct is >= 0 and <= 100, "perOfferDecayPct");
        Require(request.Note is null || request.Note.Length <= 500, "note");

        var weights = request.QualityWeights;
        Require(weights is not null, "qualityWeights");
        Require(
            weights!.Description >= 0 && weights.Title >= 0 && weights.Budget >= 0
                && weights.Location >= 0 && weights.SessionBoth >= 0 && weights.Specialization >= 0,
            "qualityWeights");
        Require(QualityWeights.FromDto(weights).WeightSum > 0, "qualityWeights");
        Require(weights.DescriptionFullChars is > 0 and <= 2000, "qualityWeights.descriptionFullChars");
        Require(weights.TitleFullChars is > QualityScoreCalculator.TitleMin and <= 100, "qualityWeights.titleFullChars");
        Require(weights.BudgetFullMultiplier is >= 1 and <= 100, "qualityWeights.budgetFullMultiplier");
    }

    private static AdminPricingPeriodDto ToDto(PricingPeriod period, bool isCurrent, string? createdByName)
        => new(
            period.Id,
            period.EffectiveFrom,
            period.BudgetMinTry,
            period.BudgetMaxTry,
            period.FeeMinTry,
            period.FeeMaxTry,
            period.DailyDecayPct,
            period.PerOfferDecayPct,
            QualityWeights.Parse(period.QualityWeights).ToDto(),
            period.Note,
            isCurrent,
            createdByName,
            period.CreatedAt);
}
