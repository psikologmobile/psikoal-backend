using PsikoAl.Common.Dtos.Listing;
using PsikoAl.Common.Presentation;
using PsikoAl.Data.Entities;

namespace PsikoAl.Services.Mapping;

/// <summary>Görünüme özgü alanlar (REVIZE-2 §3.3).</summary>
/// <remarks>Uzman görünümü ücret/davet/iletişim, danışan görünümü eşleşme/anlaşma bildirimleri taşır.</remarks>
public sealed record ListingViewExtras(
    long? OfferFeeKurus = null,
    bool InvitedByViewer = false,
    ListingViewerClaimDto? ViewerClaim = null,
    ListingClientContactDto? ClientContact = null,
    bool MatchedWithOther = false,
    ListingMatchedExpertDto? MatchedExpert = null,
    IReadOnlyList<ListingClaimDto>? Claims = null)
{
    public static readonly ListingViewExtras None = new();
}

public static class ListingMapper
{
    public static ListingDto ToListingDto(
        Listing listing,
        Profile client,
        bool viewerIsOwner,
        bool viewerHasOffered,
        Guid? viewerOfferId,
        AssessmentResult? assessmentResult = null,
        ListingViewExtras? extras = null)
    {
        var view = extras ?? ListingViewExtras.None;
        return new ListingDto(
            listing.Id,
            listing.ClientId,
            new ListingClientDto(
                client.Id,
                ExpertMapper.InitialsOf(client.FirstName, client.LastName),
                client.AvatarUrl,
                client.CreatedAt),
            viewerIsOwner
                ? $"{client.FirstName} {client.LastName}"
                : NameMasker.Mask($"{client.FirstName} {client.LastName}"),
            listing.Title,
            listing.Description,
            listing.Specialization,
            listing.BudgetMin,
            listing.BudgetMax,
            listing.PreferredSessionType,
            listing.City,
            listing.OfferCount,
            listing.Status,
            BudgetLabelFormatter.Format(listing.BudgetMin, listing.BudgetMax),
            listing.ExpiresAt,
            listing.CreatedAt,
            RelativeTimeTr.From(listing.CreatedAt),
            viewerHasOffered,
            viewerOfferId,
            listing.RejectionReason,
            assessmentResult is null
                ? null
                : new ListingAssessmentResultDto(
                    assessmentResult.Id,
                    assessmentResult.Score,
                    assessmentResult.Level,
                    assessmentResult.Summary,
                    assessmentResult.Assessment?.Title ?? string.Empty),
            view.OfferFeeKurus,
            view.OfferFeeKurus is { } fee ? MoneyLabel.Try(fee) : null,
            view.InvitedByViewer,
            view.ViewerClaim,
            viewerHasOffered ? view.ClientContact : null,
            view.MatchedWithOther,
            viewerIsOwner ? view.MatchedExpert : null,
            viewerIsOwner ? view.Claims ?? [] : null,
            viewerIsOwner ? listing.ContactConsentAt : null);
    }
}
