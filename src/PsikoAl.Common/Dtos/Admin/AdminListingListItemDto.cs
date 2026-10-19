namespace PsikoAl.Common.Dtos.Admin;

public sealed record AdminListingListItemDto(
    Guid Id,
    string ClientFullName,
    string ClientEmail,
    string Title,
    IReadOnlyList<string> Specialization,
    string BudgetLabel,
    string Status,
    DateTimeOffset CreatedAt,
    decimal? QualityScore,
    int OfferCount,
    bool IsInvited);
