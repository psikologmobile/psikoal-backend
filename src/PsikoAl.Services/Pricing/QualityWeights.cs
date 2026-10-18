using System.Text.Json;
using PsikoAl.Common.Dtos.Admin;

namespace PsikoAl.Services.Pricing;

/// <summary>Kalite puanı ağırlıkları ve tam puan eşikleri (REVIZE-2 §1.1).</summary>
/// <remarks>
/// pricing_periods.quality_weights jsonb'si bu kayıtla birebir: anahtarlar camelCase.
/// Ağırlıklar hesapta toplamları 1 olacak şekilde normalize edilir; admin "0.3" yerine
/// "30" yazsa da sonuç aynı çıkar.
/// </remarks>
public sealed record QualityWeights(
    decimal Description,
    decimal Title,
    decimal Budget,
    decimal Location,
    decimal SessionBoth,
    decimal Specialization,
    int DescriptionFullChars,
    int TitleFullChars,
    decimal BudgetFullMultiplier)
{
    // Başlangıç dönemiyle aynı değerler (migration 20261019100000_tl_wallet).
    public static readonly QualityWeights Default = new(0.30m, 0.10m, 0.25m, 0.15m, 0.10m, 0.10m, 400, 30, 4m);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public decimal WeightSum => Description + Title + Budget + Location + SessionBoth + Specialization;

    // Bozuk ya da eksik jsonb sessizce varsayılana düşmez: dönem kaydı doğrulanarak
    // yazıldığı için burada bozuk veri bir programlama hatasıdır.
    public static QualityWeights Parse(string json)
        => JsonSerializer.Deserialize<QualityWeights>(json, JsonOptions)
            ?? throw new InvalidOperationException("quality_weights okunamadı.");

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static QualityWeights FromDto(QualityWeightsDto dto)
        => new(
            dto.Description,
            dto.Title,
            dto.Budget,
            dto.Location,
            dto.SessionBoth,
            dto.Specialization,
            dto.DescriptionFullChars,
            dto.TitleFullChars,
            dto.BudgetFullMultiplier);

    public QualityWeightsDto ToDto()
        => new(
            Description,
            Title,
            Budget,
            Location,
            SessionBoth,
            Specialization,
            DescriptionFullChars,
            TitleFullChars,
            BudgetFullMultiplier);
}
