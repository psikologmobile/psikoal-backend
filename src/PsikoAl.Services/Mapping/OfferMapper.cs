using PsikoAl.Common.Dtos.Listing;
using PsikoAl.Common.Dtos.Offer;
using PsikoAl.Common.Presentation;
using PsikoAl.Data.Entities;

namespace PsikoAl.Services.Mapping;

public static class OfferMapper
{
    // viewerIsListingOwner=false iken görüntüleyen teklifin sahibi uzmandır (servis bunu
    // önceden doğrular). Ücret ve danışan iletişimi YALNIZ ona gösterilir.
    public static OfferDto ToOfferDto(Offer offer, bool viewerIsListingOwner, double expertRating, bool claimed = false)
    {
        var listingDto = offer.Listing is { } listing ? ToListingEmbedDto(listing, viewerIsListingOwner) : null;
        var expertDto = offer.Expert is { Profile: { } profile } expert
            ? new OfferExpertDto(
                expert.Id,
                $"{profile.FirstName} {profile.LastName}",
                expert.Title,
                ExpertMapper.InitialsOf(profile.FirstName, profile.LastName),
                profile.AvatarUrl,
                expertRating)
            : null;

        var isExpertView = !viewerIsListingOwner;
        return new OfferDto(
            offer.Id,
            offer.ListingId,
            offer.ExpertId,
            offer.Title,
            offer.Price,
            offer.Description,
            offer.SessionType,
            offer.Status,
            offer.MatchId,
            offer.CreatedAt,
            RelativeTimeTr.From(offer.CreatedAt),
            listingDto,
            expertDto,
            isExpertView ? offer.FeePaidKurus : null,
            isExpertView ? MoneyLabel.FeePaid(offer.FeePaidKurus, offer.UsedFreeRight) : null,
            isExpertView && offer.UsedFreeRight,
            offer.ClosedReason,
            OfferStatusLabels.For(offer.Status, offer.ClosedReason),
            isExpertView && claimed,
            isExpertView ? ClientContactOf(offer.Listing) : null);
    }

    // Danışan ilan açarken iletişim paylaşımına açık rıza verdiyse (REVIZE-2 §3.2) teklif
    // veren uzman ad soyad, telefon ve e-postayı görür. Rıza kaydı olmayan (Revize 2 öncesi)
    // ilanlarda hiçbir şey gösterilmez.
    public static ListingClientContactDto? ClientContactOf(Listing? listing)
        => listing is { ContactConsentAt: not null, Client: { } client }
            ? new ListingClientContactDto($"{client.FirstName} {client.LastName}".Trim(), client.Phone, client.Email)
            : null;

    private static OfferListingDto ToListingEmbedDto(Listing listing, bool viewerIsListingOwner)
    {
        var client = listing.Client;
        var fullName = client is null ? "Danışan" : $"{client.FirstName} {client.LastName}";
        var clientDisplayName = viewerIsListingOwner ? fullName : NameMasker.Mask(fullName);
        var clientDto = client is null
            ? null
            : new OfferListingClientDto(client.Id, ExpertMapper.InitialsOf(client.FirstName, client.LastName), client.AvatarUrl);

        return new OfferListingDto(listing.Id, listing.Title, listing.ClientId, listing.City, clientDisplayName, clientDto);
    }
}
