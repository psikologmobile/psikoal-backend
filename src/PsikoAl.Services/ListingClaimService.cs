using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PsikoAl.Common.Constants;
using PsikoAl.Common.Dtos.Claim;
using PsikoAl.Common.Exceptions;
using PsikoAl.Data.Entities;
using PsikoAl.Data.Repositories.Abstractions;
using PsikoAl.Services.Abstractions;
using PsikoAl.Services.Wallet;

namespace PsikoAl.Services;

// "Danışanla anlaştım" bildirimi (REVIZE-2 §3.5). Danışan uzmanla platform dışında
// anlaşıp ilanı "eşleşti" işaretlemezse uzman bunu bildirir; danışana hatırlatma gider,
// eşleşme gecikirse admin incelemesine düşer. Ücret iadesi YOKTUR.
public sealed class ListingClaimService(
    IUnitOfWork unitOfWork,
    INotificationService notificationService) : IListingClaimService
{
    public async Task<ClaimDto> CreateAsync(
        Guid expertUserId,
        Guid listingId,
        string? note,
        CancellationToken cancellationToken)
    {
        var listing = await unitOfWork.Listings.GetByIdAsync(listingId, cancellationToken)
            ?? throw new DomainException(ErrorKeys.ListingNotFound);

        var hasOffered = await unitOfWork.Offers.ExistsForListingAndExpertAsync(listingId, expertUserId, cancellationToken);
        if (!hasOffered)
        {
            throw new DomainException(ErrorKeys.ClaimNotAllowed);
        }

        if (await unitOfWork.ListingClaims.Query().AnyAsync(
                claim => claim.ListingId == listingId && claim.ExpertId == expertUserId,
                cancellationToken))
        {
            throw new DomainException(ErrorKeys.ClaimAlreadyExists);
        }

        var enabled = await SettingValues.GetBoolAsync(
            unitOfWork.SystemSettings, SystemSettingKeys.ClaimsEnabled, SettingValues.DefaultClaimsEnabled, cancellationToken);
        if (!enabled || listing.Status != ListingStatuses.Open)
        {
            throw new DomainException(ErrorKeys.ClaimNotAllowed);
        }

        var claim = new ListingClaim
        {
            ListingId = listingId,
            ExpertId = expertUserId,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        };

        await unitOfWork.ListingClaims.AddAsync(claim, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var expertName = await unitOfWork.Profiles.Query()
            .Where(profile => profile.Id == expertUserId)
            .Select(profile => profile.FirstName + " " + profile.LastName)
            .FirstOrDefaultAsync(cancellationToken) ?? "Uzman";

        await notificationService.NotifyAsync(
            listing.ClientId,
            NotificationTypes.ClaimReminder,
            new Dictionary<string, string> { ["expertName"] = expertName.Trim(), ["listingTitle"] = listing.Title },
            JsonSerializer.Serialize(new { listingId = listing.Id.ToString() }),
            cancellationToken);

        return ToDto(claim);
    }

    // Danışanın "Anlaşmadım" yanıtı: hatırlatmayı kapatır. Bildirim admin listesinde kalır
    // (inceleme için anlamlı olan tam da bu itirazdır).
    public async Task<ClaimDto> DismissAsync(
        Guid clientUserId,
        Guid listingId,
        Guid expertId,
        CancellationToken cancellationToken)
    {
        var listing = await unitOfWork.Listings.GetByIdAsync(listingId, cancellationToken)
            ?? throw new DomainException(ErrorKeys.ListingNotFound);

        if (listing.ClientId != clientUserId)
        {
            throw new DomainException(ErrorKeys.ListingNotOwner);
        }

        var claim = await unitOfWork.ListingClaims.QueryTracked()
            .FirstOrDefaultAsync(item => item.ListingId == listingId && item.ExpertId == expertId, cancellationToken)
            ?? throw new DomainException(ErrorKeys.ClaimNotFound);

        claim.DismissedAt ??= DateTimeOffset.UtcNow;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDto(claim);
    }

    private static ClaimDto ToDto(ListingClaim claim)
        => new(claim.Id, claim.ListingId, claim.ExpertId, claim.Note, claim.CreatedAt, claim.DismissedAt);
}
