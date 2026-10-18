namespace PsikoAl.Common.Dtos.Offer.Create;

// ExpectedFeeKurus: uzmanın onayladığı ücret. Anlık ücret farklıysa (ya da alan hiç
// gönderilmemişse) teklif yazılmaz, 409 OFFER_FEE_CHANGED döner — kimse görmediği bir
// ücreti ödemez.
public sealed record CreateOfferDto(
    Guid ListingId,
    string? Title,
    decimal Price,
    string SessionType,
    string? Description,
    long? ExpectedFeeKurus = null);
