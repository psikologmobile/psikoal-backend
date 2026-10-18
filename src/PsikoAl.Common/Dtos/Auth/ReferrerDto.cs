namespace PsikoAl.Common.Dtos.Auth;

public sealed record ReferrerDto(Guid Id, string Name, string Title, string? AvatarUrl, string? Initials);
