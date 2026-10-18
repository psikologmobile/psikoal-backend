namespace PsikoAl.Common.Dtos.Admin;

// Kind: ActivityKinds değerlerinden biri ya da virgülle birkaçı (ör. "OFFER,WALLET").
public sealed record AdminActivityQueryDto(
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Kind,
    int? Page,
    int? PerPage);

public sealed record AdminActivityItemDto(
    string Kind,
    string KindLabel,
    DateTimeOffset OccurredAt,
    string Title,
    string? Detail,
    string? EntityType,
    string? EntityId);
