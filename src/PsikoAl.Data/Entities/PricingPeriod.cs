namespace PsikoAl.Data.Entities;

public sealed class PricingPeriod
{
    public Guid Id { get; set; }

    public DateTimeOffset EffectiveFrom { get; set; }

    public int BudgetMinTry { get; set; }

    public int? BudgetMaxTry { get; set; }

    public int FeeMinTry { get; set; }

    public int FeeMaxTry { get; set; }

    public decimal DailyDecayPct { get; set; }

    public decimal PerOfferDecayPct { get; set; }

    // jsonb: {"description":0.30,...,"descriptionFullChars":400,...} — PricingPeriodWeights okur.
    public required string QualityWeights { get; set; }

    public string? Note { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public AdminUser? CreatedByAdmin { get; set; }
}
