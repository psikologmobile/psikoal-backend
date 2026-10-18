namespace PsikoAl.Common.Dtos.Wallet;

public sealed record TopupOptionsDto(
    IReadOnlyList<TopupPresetDto> Presets,
    long MinKurus,
    long MaxKurus,
    string VatNote);

public sealed record TopupPresetDto(long AmountKurus, string Label);
