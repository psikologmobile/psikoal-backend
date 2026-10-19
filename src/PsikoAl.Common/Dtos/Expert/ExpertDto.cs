namespace PsikoAl.Common.Dtos.Expert;

public sealed record ExpertDto(
    Guid Id,
    string FirstName,
    string LastName,
    string Title,
    IReadOnlyList<string> Specializations,
    int ExperienceYears,
    string Bio,
    string? AvatarUrl,
    double Rating,
    int ReviewCount,
    bool IsVerified,
    string Status,
    string Initials,
    bool AcceptsOffers,
    string? Education,
    string? CvUrl,
    IReadOnlyList<string> Certificates,
    string? PersonalWebsite,
    string? RejectionReason);
