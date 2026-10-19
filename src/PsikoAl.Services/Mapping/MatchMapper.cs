using PsikoAl.Common.Dtos.Match;
using PsikoAl.Common.Presentation;
using PsikoAl.Data.Entities;

namespace PsikoAl.Services.Mapping;

public static class MatchMapper
{
    public static MatchDto ToMatchDto(Match match)
    {
        var client = match.Client;

        // REVIZE-2 §5: eşleşmede iki taraf da karşı tarafın telefonunu ve e-postasını görür.
        // Eskiden profildeki "İletişim Paylaşımı" anahtarlarına bakılıyordu. Revize 2 ile
        // danışan iletişim paylaşımına İLAN AÇARKEN açık rıza veriyor (listings.contact_consent_at)
        // ve uzman bu bilgiyi zaten teklif gönderdiği anda görüyor; eşleşmede saklamak
        // tutarsız olurdu. Uzman tarafı: profildeki telefon ve hesabın (auth) e-postası.
        var clientDto = new MatchClientDto(
            client?.Id ?? match.ClientId,
            client is null ? "Danışan" : $"{client.FirstName} {client.LastName}",
            client is null ? null : ExpertMapper.InitialsOf(client.FirstName, client.LastName),
            client?.Email,
            client?.Phone,
            client?.CreatedAt);

        var expertDto = match.Expert is { Profile: { } expertProfile } expert
            ? new MatchExpertDto(
                expert.Id,
                $"{expertProfile.FirstName} {expertProfile.LastName}",
                expert.Title,
                ExpertMapper.InitialsOf(expertProfile.FirstName, expertProfile.LastName),
                expertProfile.Email,
                expertProfile.Phone)
            : null;

        var listingDto = match.Listing is { } listing
            ? new MatchListingDto(
                listing.Id,
                listing.Title,
                listing.Description,
                listing.Specialization,
                listing.BudgetMin,
                listing.BudgetMax,
                BudgetLabelFormatter.Format(listing.BudgetMin, listing.BudgetMax),
                listing.PreferredSessionType,
                listing.Status,
                listing.City)
            : null;

        var offerDto = match.AcceptedOffer is { } offer
            ? new MatchOfferDto(offer.Id, offer.Title, offer.Price, offer.Description, offer.SessionType, offer.Status)
            : null;

        return new MatchDto(
            match.Id,
            match.ListingId,
            match.AcceptedOfferId,
            match.ClientId,
            match.ExpertId,
            match.Status,
            match.CreatedAt,
            RelativeTimeTr.From(match.CreatedAt),
            clientDto,
            expertDto,
            listingDto,
            offerDto,
            match.ClientReleasedAt,
            match.ExpertReleasedAt);
    }
}
