using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PsikoAl.Common.Constants;
using PsikoAl.Common.Dtos.Offer;
using PsikoAl.Common.Dtos.Offer.Create;
using PsikoAl.Common.Exceptions;
using PsikoAl.Common.Presentation;
using PsikoAl.Data.Entities;
using PsikoAl.Data.Repositories.Abstractions;
using PsikoAl.Services.Abstractions;
using PsikoAl.Services.Mapping;
using PsikoAl.Services.Pricing;
using PsikoAl.Services.Wallet;

namespace PsikoAl.Services;

public sealed class OfferService(
    IUnitOfWork unitOfWork,
    INotificationService notificationService,
    TimeProvider timeProvider) : IOfferService
{
    public async Task<OfferQuoteDto> QuoteAsync(Guid expertUserId, Guid listingId, CancellationToken cancellationToken)
    {
        await GetRequiredApprovedExpertAsync(expertUserId, cancellationToken);
        var listing = await GetRequiredOpenListingAsync(listingId, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var period = await ListingPricing.GetCurrentPeriodAsync(unitOfWork, now, cancellationToken);
        var fee = ListingPricing.CurrentFeeKurus(listing, period, now);
        var wallet = await unitOfWork.ExpertWallets.GetAsync(expertUserId, cancellationToken)
            ?? throw new DomainException(ErrorKeys.ExpertNotFound);

        var willUseFreeRight = wallet.FreeOfferRights > 0;
        var balanceAfter = willUseFreeRight ? wallet.BalanceKurus : wallet.BalanceKurus - fee;
        var sufficient = willUseFreeRight || balanceAfter >= 0;

        return new OfferQuoteDto(
            fee,
            MoneyLabel.Try(fee),
            wallet.BalanceKurus,
            MoneyLabel.Try(wallet.BalanceKurus),
            wallet.FreeOfferRights,
            willUseFreeRight,
            Math.Max(0, balanceAfter),
            MoneyLabel.Try(Math.Max(0, balanceAfter)),
            sufficient);
    }

    public async Task<OfferDto> CreateAsync(Guid expertUserId, CreateOfferDto request, CancellationToken cancellationToken)
    {
        var expert = await GetRequiredApprovedExpertAsync(expertUserId, cancellationToken);
        var expertProfile = expert.Profile ?? throw new DomainException(ErrorKeys.ProfileNotFound);
        var listing = await GetRequiredOpenListingAsync(request.ListingId, cancellationToken);

        if (await unitOfWork.Offers.ExistsForListingAndExpertAsync(request.ListingId, expertUserId, cancellationToken))
        {
            throw new DomainException(ErrorKeys.OfferAlreadyExists);
        }

        var offer = new Offer
        {
            ListingId = request.ListingId,
            ExpertId = expertUserId,
            Title = request.Title,
            Price = request.Price,
            Description = request.Description ?? string.Empty,
            SessionType = request.SessionType,
        };

        await AddOfferChargingFeeAsync(offer, listing, request.ExpectedFeeKurus, cancellationToken);

        await notificationService.NotifyAsync(
            listing.ClientId,
            NotificationTypes.OfferReceived,
            new Dictionary<string, string>
            {
                ["expertName"] = $"{expertProfile.FirstName} {expertProfile.LastName}",
                ["listingTitle"] = listing.Title,
            },
            JsonSerializer.Serialize(new { listingId = listing.Id }),
            cancellationToken);

        return await GetByIdAsync(offer.Id, expertUserId, cancellationToken);
    }

    // Ücret, teklif, defter satırı ve teklif sayacı TEK transaction'da (REVIZE-2 §1.2, §2.3):
    //  1. İlan satırı kilitlenir: ücret teklif sayısına bağlı; aynı anda gelen iki teklif
    //     aynı sayıyı görüp aynı ücreti ödeyemez.
    //  2. Anlık ücret uzmanın onayladığı ücretle (expectedFeeKurus) aynı değilse hiçbir
    //     şey yazılmadan 409 OFFER_FEE_CHANGED (yeni ücret meta'da).
    //  3. Ücretsiz hak varsa önce o kullanılır (OFFER_FREE_RIGHT, tutar 0); yoksa ücret
    //     koşullu UPDATE ile bakiyeden düşer (OFFER_FEE); yetmezse 422 INSUFFICIENT_BALANCE.
    // Teklif yazılamazsa (eşzamanlı ikinci teklif benzersizlik kısıtına takılırsa) düşüm geri
    // alınır. Ödenen ücret teklif geri çekilse/ilan kapansa da iade edilmez (ürün kararı).
    private async Task AddOfferChargingFeeAsync(
        Offer offer,
        Listing listing,
        long? expectedFeeKurus,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var period = await ListingPricing.GetCurrentPeriodAsync(unitOfWork, now, cancellationToken);

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var offerCount = await unitOfWork.Listings.LockOpenForOfferAsync(listing.Id, cancellationToken)
            ?? throw new DomainException(ErrorKeys.OfferListingNotOpen);

        var fee = ListingPricing.CurrentFeeKurus(listing, period, now, offerCount);
        if (expectedFeeKurus != fee)
        {
            throw new DomainException(
                ErrorKeys.OfferFeeChanged,
                "expectedFeeKurus",
                new { feeKurus = fee, feeLabel = MoneyLabel.Try(fee) });
        }

        await unitOfWork.Offers.AddAsync(offer, cancellationToken);

        var wallet = await unitOfWork.ExpertWallets.GetAsync(offer.ExpertId, cancellationToken)
            ?? throw new DomainException(ErrorKeys.ExpertNotFound);

        WalletTransaction? entry = null;
        if (wallet.FreeOfferRights > 0)
        {
            entry = await WalletLedger.TryRecordAsync(
                unitOfWork,
                new WalletTransaction
                {
                    ExpertId = offer.ExpertId,
                    FreeRightsDelta = -1,
                    Type = WalletTransactionTypes.OfferFreeRight,
                    ListingId = listing.Id,
                    OfferId = offer.Id,
                },
                cancellationToken);
        }

        if (entry is not null)
        {
            offer.UsedFreeRight = true;
            offer.FeePaidKurus = 0;
        }
        else if (fee > 0)
        {
            _ = await WalletLedger.TryRecordAsync(
                    unitOfWork,
                    new WalletTransaction
                    {
                        ExpertId = offer.ExpertId,
                        AmountKurus = -fee,
                        Type = WalletTransactionTypes.OfferFee,
                        ListingId = listing.Id,
                        OfferId = offer.Id,
                    },
                    cancellationToken)
                ?? throw new DomainException(
                    ErrorKeys.InsufficientBalance,
                    meta: new { feeKurus = fee, feeLabel = MoneyLabel.Try(fee) });
            offer.FeePaidKurus = fee;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await unitOfWork.Offers.IncrementListingOfferCountAsync(offer.ListingId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<OfferListResult> ListMyAsync(Guid expertUserId, string? status, CancellationToken cancellationToken)
    {
        var query = unitOfWork.Offers.QueryWithDetails().Where(offer => offer.ExpertId == expertUserId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(offer => offer.Status == status);
        }

        var offers = await query.OrderByDescending(offer => offer.CreatedAt).ToListAsync(cancellationToken);
        var rating = await unitOfWork.Reviews.GetRatingAsync(expertUserId, cancellationToken);
        var claimedListingIds = await ClaimedListingIdsAsync(expertUserId, cancellationToken);
        var dtos = offers
            .Select(offer => OfferMapper.ToOfferDto(offer, viewerIsListingOwner: false, rating, claimedListingIds.Contains(offer.ListingId)))
            .ToList();

        var pendingCount = await unitOfWork.Offers.QueryWithDetails()
            .CountAsync(offer => offer.ExpertId == expertUserId && offer.Status == OfferStatuses.Pending, cancellationToken);

        return new OfferListResult(dtos, dtos.Count, pendingCount);
    }

    public async Task<OfferListResult> ListForListingAsync(Guid listingId, Guid viewerUserId, CancellationToken cancellationToken)
    {
        var listing = await unitOfWork.Listings.GetByIdAsync(listingId, cancellationToken)
            ?? throw new DomainException(ErrorKeys.ListingNotFound);

        if (listing.ClientId != viewerUserId)
        {
            throw new DomainException(ErrorKeys.ListingNotFound);
        }

        var offers = await unitOfWork.Offers.QueryWithDetails()
            .Where(offer => offer.ListingId == listingId)
            .OrderByDescending(offer => offer.CreatedAt)
            .ToListAsync(cancellationToken);

        var dtos = new List<OfferDto>(offers.Count);
        foreach (var offer in offers)
        {
            var rating = await unitOfWork.Reviews.GetRatingAsync(offer.ExpertId, cancellationToken);
            dtos.Add(OfferMapper.ToOfferDto(offer, viewerIsListingOwner: true, rating));
        }

        return new OfferListResult(dtos, dtos.Count, PendingCount: null);
    }

    public async Task<OfferDto> GetByIdAsync(Guid offerId, Guid viewerUserId, CancellationToken cancellationToken)
    {
        var offer = await unitOfWork.Offers.GetWithListingAndExpertAsync(offerId, cancellationToken)
            ?? throw new DomainException(ErrorKeys.OfferNotFound);

        var listing = offer.Listing ?? throw new DomainException(ErrorKeys.ListingNotFound);
        var isListingOwner = listing.ClientId == viewerUserId;
        if (!isListingOwner && offer.ExpertId != viewerUserId)
        {
            throw new DomainException(ErrorKeys.OfferNotFound);
        }

        var rating = await unitOfWork.Reviews.GetRatingAsync(offer.ExpertId, cancellationToken);
        var claimed = !isListingOwner && await unitOfWork.ListingClaims.Query()
            .AnyAsync(claim => claim.ListingId == offer.ListingId && claim.ExpertId == offer.ExpertId, cancellationToken);
        return OfferMapper.ToOfferDto(offer, isListingOwner, rating, claimed);
    }

    public async Task<OfferDto> WithdrawAsync(Guid expertUserId, Guid offerId, CancellationToken cancellationToken)
    {
        var offer = await unitOfWork.Offers.GetWithListingAndExpertAsync(offerId, cancellationToken)
            ?? throw new DomainException(ErrorKeys.OfferNotFound);

        if (offer.ExpertId != expertUserId)
        {
            throw new DomainException(ErrorKeys.OfferNotFound);
        }

        if (offer.Status != OfferStatuses.Pending)
        {
            throw new DomainException(ErrorKeys.OfferNotPending);
        }

        offer.Status = OfferStatuses.Withdrawn;
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(offerId, expertUserId, cancellationToken);
    }

    private async Task<HashSet<Guid>> ClaimedListingIdsAsync(Guid expertUserId, CancellationToken cancellationToken)
        => [.. await unitOfWork.ListingClaims.Query()
            .Where(claim => claim.ExpertId == expertUserId)
            .Select(claim => claim.ListingId)
            .ToListAsync(cancellationToken)];

    private async Task<Expert> GetRequiredApprovedExpertAsync(Guid expertUserId, CancellationToken cancellationToken)
    {
        var expert = await unitOfWork.Experts.GetWithProfileAsync(expertUserId, cancellationToken)
            ?? throw await ExpertAccess.MissingExpertErrorAsync(
                unitOfWork, expertUserId, ErrorKeys.OfferExpertRoleRequired, cancellationToken);

        return expert.Status == ExpertStatuses.Approved
            ? expert
            : throw new DomainException(ErrorKeys.OfferExpertNotApproved);
    }

    private async Task<Listing> GetRequiredOpenListingAsync(Guid listingId, CancellationToken cancellationToken)
    {
        var listing = await unitOfWork.Listings.GetByIdAsync(listingId, cancellationToken)
            ?? throw new DomainException(ErrorKeys.ListingNotFound);

        return listing.Status == ListingStatuses.Open
            ? listing
            : throw new DomainException(ErrorKeys.OfferListingNotOpen);
    }
}
