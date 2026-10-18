using PsikoAl.Common.Dtos.Listing;

namespace PsikoAl.Common.Dtos.Offer;

// FeePaid* yalnız teklifin sahibi uzmana dolu; ilan sahibi danışan için null (platform
// ücreti danışanı ilgilendirmez). ClientContact yalnız teklif sahibi uzmana ve yalnız
// danışan ilan açarken iletişim paylaşımına rıza verdiyse dolu.
public sealed record OfferDto(
    Guid Id,
    Guid ListingId,
    Guid ExpertId,
    string? Title,
    decimal Price,
    string Description,
    string SessionType,
    string Status,
    Guid? MatchId,
    DateTimeOffset CreatedAt,
    string CreatedAtRelative,
    OfferListingDto? Listing,
    OfferExpertDto? Expert,
    long? FeePaidKurus,
    string? FeePaidLabel,
    bool UsedFreeRight,
    string? ClosedReason,
    string StatusLabel,
    bool Claimed,
    ListingClientContactDto? ClientContact);
