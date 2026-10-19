namespace PsikoAl.Common.Dtos.Listing;

// Görünüme göre dolan alanlar (REVIZE-2 §3.3):
//   uzman   — OfferFeeKurus/Label, InvitedByViewer, ViewerClaim, ClientContact, MatchedWithOther
//   danışan — MatchedExpert, Claims, ContactConsentAt
// Diğer görünümde null/false döner; alanlar her zaman vardır (mobil şema sabit kalsın).
public sealed record ListingDto(
    Guid Id,
    Guid ClientId,
    ListingClientDto Client,
    string ClientDisplayName,
    string Title,
    string Description,
    IReadOnlyList<string> Specialization,
    decimal BudgetMin,
    decimal BudgetMax,
    string PreferredSessionType,
    string? City,
    int OfferCount,
    string Status,
    string BudgetLabel,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt,
    string CreatedAtRelative,
    bool ViewerHasOffered,
    Guid? ViewerOfferId,
    string? RejectionReason,
    ListingAssessmentResultDto? AssessmentResult,
    long? OfferFeeKurus,
    string? OfferFeeLabel,
    bool InvitedByViewer,
    ListingViewerClaimDto? ViewerClaim,
    ListingClientContactDto? ClientContact,
    bool MatchedWithOther,
    ListingMatchedExpertDto? MatchedExpert,
    IReadOnlyList<ListingClaimDto>? Claims,
    DateTimeOffset? ContactConsentAt);
