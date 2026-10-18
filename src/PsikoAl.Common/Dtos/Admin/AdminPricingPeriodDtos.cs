namespace PsikoAl.Common.Dtos.Admin;

// Kalite puanı ağırlıkları + tam puan eşikleri (REVIZE-2 §1.1). Ağırlıklar hesapta
// toplamları 1 olacak şekilde normalize edilir; eşikler kısmi puanın doğrusal aralığıdır.
public sealed record QualityWeightsDto(
    decimal Description,
    decimal Title,
    decimal Budget,
    decimal Location,
    decimal SessionBoth,
    decimal Specialization,
    int DescriptionFullChars,
    int TitleFullChars,
    decimal BudgetFullMultiplier);

public sealed record AdminPricingPeriodDto(
    Guid Id,
    DateTimeOffset EffectiveFrom,
    int BudgetMinTry,
    int? BudgetMaxTry,
    int FeeMinTry,
    int FeeMaxTry,
    decimal DailyDecayPct,
    decimal PerOfferDecayPct,
    QualityWeightsDto QualityWeights,
    string? Note,
    bool IsCurrent,
    string? CreatedByName,
    DateTimeOffset CreatedAt);

// EffectiveFrom boşsa dönem hemen başlar; ileri bir tarih verilebilir, geçmiş verilemez.
public sealed record CreatePricingPeriodDto(
    DateTimeOffset? EffectiveFrom,
    int BudgetMinTry,
    int? BudgetMaxTry,
    int FeeMinTry,
    int FeeMaxTry,
    decimal DailyDecayPct,
    decimal PerOfferDecayPct,
    QualityWeightsDto QualityWeights,
    string? Note);

// Period boşsa GEÇERLİ dönemin parametreleriyle hesaplanır.
public sealed record PricingPreviewRequestDto(CreatePricingPeriodDto? Period, PricingPreviewListingDto Listing);

public sealed record PricingPreviewListingDto(
    string? Title,
    string? Description,
    decimal BudgetMax,
    string? City,
    string SessionType,
    int SpecializationCount);

public sealed record QualityBreakdownDto(
    decimal Description,
    decimal Title,
    decimal Budget,
    decimal Location,
    decimal SessionBoth,
    decimal Specialization);

public sealed record PricingPreviewResultDto(
    decimal QualityScore,
    QualityBreakdownDto Breakdown,
    long InitialFeeKurus,
    string InitialFeeLabel,
    long FeeMinKurus,
    string FeeMinLabel,
    long FeeMaxKurus,
    string FeeMaxLabel);
