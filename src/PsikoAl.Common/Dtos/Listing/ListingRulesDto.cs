namespace PsikoAl.Common.Dtos.Listing;

public sealed record ListingRulesDto(
    int BudgetMinTry,
    int? BudgetMaxTry,
    int TitleMin,
    int TitleMax,
    int DescriptionMax,
    IReadOnlyList<string> CityRequiredFor,
    int MaxActive);
