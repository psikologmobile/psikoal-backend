using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PsikoAl.Common.Constants;
using PsikoAl.Common.Dtos.Listing;
using PsikoAl.Common.Dtos.Listing.Create;
using PsikoAl.Common.Exceptions;
using PsikoAl.Data.Entities;
using PsikoAl.Data.Repositories.Abstractions;
using PsikoAl.Services.Abstractions;
using PsikoAl.Services.Mapping;
using PsikoAl.Services.Pricing;

namespace PsikoAl.Services;

public sealed class ListingService(
    IUnitOfWork unitOfWork,
    ICategoryService categoryService,
    INotificationService notificationService,
    TimeProvider timeProvider) : IListingService
{
    public const int TitleMin = 10;
    public const int TitleMax = 100;
    public const int DescriptionMax = 2000;
    public static readonly IReadOnlyList<string> CityRequiredFor = ["yüz_yüze", "yüz_yüze_online"];

    private const string ListingNotFoundMessage = "LISTING_NOT_FOUND";
    private const string ListingNotOwnerMessage = "LISTING_NOT_OWNER";
    private const string ListingNotOpenMessage = "LISTING_NOT_OPEN";
    private const string OfferNotOnListingMessage = "OFFER_NOT_ON_LISTING";
    private const string OfferNotPendingMessage = "OFFER_NOT_PENDING";

    public async Task<ListingRulesDto> GetRulesAsync(CancellationToken cancellationToken)
    {
        var period = await ListingPricing.GetCurrentPeriodAsync(unitOfWork, timeProvider.GetUtcNow(), cancellationToken);
        var maxActive = await unitOfWork.SystemSettings.GetIntAsync(SystemSettingKeys.ListingMaxActive, cancellationToken);
        return new ListingRulesDto(
            period.BudgetMinTry,
            period.BudgetMaxTry,
            TitleMin,
            TitleMax,
            DescriptionMax,
            CityRequiredFor,
            maxActive);
    }

    public async Task<ListingDto> CreateAsync(
        Guid clientUserId,
        CreateListingDto request,
        CancellationToken cancellationToken)
    {
        var client = await unitOfWork.Profiles.GetByIdAsync(clientUserId, cancellationToken)
            ?? throw new DomainException(ErrorKeys.ProfileNotFound);

        if (client.Role != ProfileRoles.Client)
        {
            throw new DomainException(ErrorKeys.ListingClientRoleRequired);
        }

        if (!request.ContactConsent)
        {
            throw new DomainException(ErrorKeys.ContactConsentRequired, "contactConsent");
        }

        var phone = request.Phone?.Trim();
        if (string.IsNullOrEmpty(phone) && string.IsNullOrWhiteSpace(client.Phone))
        {
            throw new DomainException(ErrorKeys.PhoneRequired, "phone");
        }

        if (CityRequiredFor.Contains(request.PreferredSessionType) && string.IsNullOrWhiteSpace(request.City))
        {
            throw new DomainException(ErrorKeys.ValidationFailed, "city");
        }

        var now = timeProvider.GetUtcNow();
        var period = await ListingPricing.GetCurrentPeriodAsync(unitOfWork, now, cancellationToken);
        if (request.BudgetMin < period.BudgetMinTry
            || (period.BudgetMaxTry is { } budgetMaxTry && request.BudgetMax > budgetMaxTry))
        {
            throw new DomainException(
                ErrorKeys.ListingBudgetOutOfRange,
                "budgetMin",
                new { budgetMinTry = period.BudgetMinTry, budgetMaxTry = period.BudgetMaxTry });
        }

        if (!await categoryService.AllActiveNamesExistAsync(request.Specialization, cancellationToken))
        {
            throw new DomainException(ErrorKeys.ValidationFailed, "specialization");
        }

        if (request.AssessmentResultId is { } assessmentResultId)
        {
            await EnsureAssessmentResultOwnedAsync(assessmentResultId, clientUserId, cancellationToken);
        }

        if (!string.IsNullOrEmpty(phone))
        {
            client.Phone = phone;
        }

        var listing = new Listing
        {
            ClientId = clientUserId,
            Title = request.Title,
            Description = request.Description ?? string.Empty,
            Specialization = [.. request.Specialization],
            BudgetMin = request.BudgetMin,
            BudgetMax = request.BudgetMax,
            PreferredSessionType = request.PreferredSessionType,
            City = string.IsNullOrWhiteSpace(request.City) ? null : request.City.Trim(),
            AssessmentResultId = request.AssessmentResultId,
            ContactConsentAt = now,
            InvitedExpertId = client.ReferredByExpertId,
        };

        var autoApprove = await unitOfWork.SystemSettings.GetBoolAsync(SystemSettingKeys.ListingAutoApprove, cancellationToken);
        if (autoApprove)
        {
            await ListingPublication.PublishAsync(unitOfWork, listing, approvedBy: null, now, cancellationToken);
        }

        await unitOfWork.Listings.AddAsync(listing, cancellationToken);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { MessageText: "LISTING_MAX_ACTIVE_EXCEEDED" })
        {
            throw new DomainException(ErrorKeys.ListingMaxActiveExceeded);
        }

        if (autoApprove)
        {
            await ListingPublication.NotifyInvitedExpertAsync(notificationService, listing, cancellationToken);
        }

        var assessmentResult = request.AssessmentResultId.HasValue
            ? await unitOfWork.AssessmentResults.QueryWithAssessment()
                .FirstOrDefaultAsync(result => result.Id == request.AssessmentResultId.Value, cancellationToken)
            : null;

        return ListingMapper.ToListingDto(
            listing, client, viewerIsOwner: true, viewerHasOffered: false, viewerOfferId: null, assessmentResult);
    }

    // İlana bağlanan sonuç, ilanı gören uzmanlara gösterilir. Başkasının (ya da sahipsiz)
    // sonucunun id'si verilerek onun sağlık verisi bir ilana iliştirilip ifşa edilemez.
    private async Task EnsureAssessmentResultOwnedAsync(
        Guid assessmentResultId,
        Guid clientUserId,
        CancellationToken cancellationToken)
    {
        var ownerId = await unitOfWork.AssessmentResults.Query()
            .Where(result => result.Id == assessmentResultId)
            .Select(result => result.UserId)
            .FirstOrDefaultAsync(cancellationToken);

        if (ownerId != clientUserId)
        {
            throw new DomainException(ErrorKeys.AssessmentResultNotOwned, "assessmentResultId");
        }
    }

    public async Task<ListingListResult> ListFeedAsync(
        Guid viewerUserId,
        ListingFeedFilters filters,
        CancellationToken cancellationToken)
    {
        var query = unitOfWork.Listings.Query()
            .Include(listing => listing.Client)
            .Where(listing => listing.Status == ListingStatuses.Open);

        if (filters.Specialization is { Count: > 0 })
        {
            query = query.Where(listing => listing.Specialization.Any(spec => filters.Specialization.Contains(spec)));
        }

        if (filters.SessionType is { Count: > 0 })
        {
            query = query.Where(listing => filters.SessionType.Contains(listing.PreferredSessionType));
        }

        if (filters.BudgetMin.HasValue)
        {
            query = query.Where(listing => listing.BudgetMax >= filters.BudgetMin.Value);
        }

        if (filters.BudgetMax.HasValue)
        {
            query = query.Where(listing => listing.BudgetMin <= filters.BudgetMax.Value);
        }

        if (!string.IsNullOrWhiteSpace(filters.City))
        {
            var city = filters.City.Trim();
            query = query.Where(listing => listing.City != null && EF.Functions.ILike(listing.City, city));
        }

        if (filters.InvitedOnly)
        {
            query = query.Where(listing => listing.InvitedExpertId == viewerUserId);
        }

        var listings = await query.OrderByDescending(listing => listing.CreatedAt).ToListAsync(cancellationToken);

        var isExpert = await IsExpertAsync(viewerUserId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var period = isExpert ? await ListingPricing.GetCurrentPeriodAsync(unitOfWork, now, cancellationToken) : null;
        var listingIds = listings.Select(listing => listing.Id).ToList();
        var viewerOffers = await ViewerOffersAsync(viewerUserId, listingIds, cancellationToken);
        var viewerClaims = await ViewerClaimsAsync(viewerUserId, listingIds, cancellationToken);

        var rows = listings.Select(listing =>
        {
            var fee = period is null ? (long?)null : ListingPricing.CurrentFeeKurus(listing, period, now);
            var offerId = viewerOffers.GetValueOrDefault(listing.Id);
            var hasOffered = viewerOffers.ContainsKey(listing.Id);
            return (Fee: fee, Dto: ListingMapper.ToListingDto(
                listing,
                listing.Client ?? throw new DomainException(ErrorKeys.ProfileNotFound),
                viewerIsOwner: listing.ClientId == viewerUserId,
                viewerHasOffered: hasOffered,
                viewerOfferId: hasOffered ? offerId : null,
                extras: new ListingViewExtras(
                    OfferFeeKurus: fee,
                    InvitedByViewer: listing.InvitedExpertId == viewerUserId,
                    ViewerClaim: viewerClaims.TryGetValue(listing.Id, out var claimedAt) ? new ListingViewerClaimDto(claimedAt) : null,
                    ClientContact: hasOffered ? OfferMapper.ClientContactOf(listing) : null)));
        });

        var ordered = filters.Sort switch
        {
            "budget_desc" => rows.OrderByDescending(row => row.Dto.BudgetMax),
            "budget_asc" => rows.OrderBy(row => row.Dto.BudgetMin),
            "offer_asc" => rows.OrderBy(row => row.Dto.OfferCount),
            "fee_asc" => rows.OrderBy(row => row.Fee ?? long.MaxValue),
            _ => rows.OrderByDescending(row => row.Dto.CreatedAt),
        };

        var dtos = ordered.Select(row => row.Dto).ToList();
        return new ListingListResult(dtos, dtos.Count, ActiveCount: null);
    }

    public async Task<ListingListResult> ListMyAsync(Guid clientUserId, string? status, CancellationToken cancellationToken)
    {
        var client = await unitOfWork.Profiles.GetByIdAsync(clientUserId, cancellationToken)
            ?? throw new DomainException(ErrorKeys.ProfileNotFound);

        var query = unitOfWork.Listings.Query().Where(listing => listing.ClientId == clientUserId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(listing => listing.Status == status);
        }

        var listings = await query.OrderByDescending(listing => listing.CreatedAt).ToListAsync(cancellationToken);
        var listingIds = listings.Select(listing => listing.Id).ToList();
        var matchedExperts = await MatchedExpertsAsync(listingIds, cancellationToken);
        var openClaims = await OpenClaimsAsync(listingIds, cancellationToken);

        var dtos = listings.Select(listing => ListingMapper.ToListingDto(
            listing,
            client,
            viewerIsOwner: true,
            viewerHasOffered: false,
            viewerOfferId: null,
            extras: new ListingViewExtras(
                MatchedExpert: matchedExperts.GetValueOrDefault(listing.Id),
                Claims: openClaims.GetValueOrDefault(listing.Id) ?? []))).ToList();

        var activeCount = await unitOfWork.Listings.CountActiveByClientAsync(clientUserId, cancellationToken);
        return new ListingListResult(dtos, dtos.Count, activeCount);
    }

    // Uzman OPEN ilanı görür; teklif verdiği ilanı ise eşleştikten/kapandıktan sonra da
    // görür ("başka uzmanla eşleşti" bilgisi ve danışan iletişimi orada).
    public async Task<ListingDto> GetByIdAsync(Guid listingId, Guid viewerUserId, CancellationToken cancellationToken)
    {
        var listing = await unitOfWork.Listings.GetWithClientAsync(listingId, cancellationToken)
            ?? throw new DomainException(ErrorKeys.ListingNotFound);
        var client = listing.Client ?? throw new DomainException(ErrorKeys.ProfileNotFound);

        var assessmentResult = listing.AssessmentResultId.HasValue
            ? await unitOfWork.AssessmentResults.QueryWithAssessment()
                .FirstOrDefaultAsync(result => result.Id == listing.AssessmentResultId.Value, cancellationToken)
            : null;

        if (listing.ClientId == viewerUserId)
        {
            var matchedExperts = await MatchedExpertsAsync([listing.Id], cancellationToken);
            var openClaims = await OpenClaimsAsync([listing.Id], cancellationToken);
            return ListingMapper.ToListingDto(
                listing, client, viewerIsOwner: true, viewerHasOffered: false, viewerOfferId: null, assessmentResult,
                new ListingViewExtras(
                    MatchedExpert: matchedExperts.GetValueOrDefault(listing.Id),
                    Claims: openClaims.GetValueOrDefault(listing.Id) ?? []));
        }

        var viewerOffer = await unitOfWork.Offers.Query()
            .FirstOrDefaultAsync(offer => offer.ListingId == listingId && offer.ExpertId == viewerUserId, cancellationToken);

        if (listing.Status != ListingStatuses.Open && viewerOffer is null)
        {
            throw new DomainException(ErrorKeys.ListingNotFound);
        }

        var now = timeProvider.GetUtcNow();
        long? fee = null;
        if (listing.Status == ListingStatuses.Open && await IsExpertAsync(viewerUserId, cancellationToken))
        {
            var period = await ListingPricing.GetCurrentPeriodAsync(unitOfWork, now, cancellationToken);
            fee = ListingPricing.CurrentFeeKurus(listing, period, now);
        }

        var viewerClaim = await unitOfWork.ListingClaims.Query()
            .Where(claim => claim.ListingId == listingId && claim.ExpertId == viewerUserId)
            .Select(claim => (DateTimeOffset?)claim.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var matchedWithOther = listing.Status == ListingStatuses.Matched
            && await unitOfWork.Matches.Query().AnyAsync(
                match => match.ListingId == listingId && match.ExpertId != viewerUserId,
                cancellationToken);

        return ListingMapper.ToListingDto(
            listing, client, viewerIsOwner: false,
            viewerHasOffered: viewerOffer is not null,
            viewerOfferId: viewerOffer?.Id,
            assessmentResult,
            new ListingViewExtras(
                OfferFeeKurus: fee,
                InvitedByViewer: listing.InvitedExpertId == viewerUserId,
                ViewerClaim: viewerClaim is { } claimedAt ? new ListingViewerClaimDto(claimedAt) : null,
                ClientContact: viewerOffer is null ? null : OfferMapper.ClientContactOf(listing),
                MatchedWithOther: matchedWithOther));
    }

    public async Task<ListingDto> CloseAsync(Guid clientUserId, Guid listingId, CancellationToken cancellationToken)
    {
        var listing = await unitOfWork.Listings.GetWithClientAsync(listingId, cancellationToken)
            ?? throw new DomainException(ErrorKeys.ListingNotFound);
        var client = listing.Client ?? throw new DomainException(ErrorKeys.ProfileNotFound);

        if (listing.ClientId != clientUserId)
        {
            throw new DomainException(ErrorKeys.ListingNotFound);
        }

        if (listing.Status != ListingStatuses.Open)
        {
            throw new DomainException(ErrorKeys.ListingNotOpen);
        }

        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            listing.Status = ListingStatuses.Closed;
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.Offers.RejectPendingForListingAsync(listing.Id, OfferClosedReasons.ListingClosed, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return ListingMapper.ToListingDto(listing, client, viewerIsOwner: true, viewerHasOffered: false, viewerOfferId: null);
    }

    public async Task<MarkMatchedResultDto> MarkMatchedAsync(
        Guid clientUserId,
        Guid listingId,
        Guid offerId,
        CancellationToken cancellationToken)
    {
        MarkMatchedOutcome outcome;
        try
        {
            outcome = await unitOfWork.Matches.MarkListingMatchedAsync(listingId, offerId, clientUserId, cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.RaiseException)
        {
            throw new DomainException(exception.MessageText switch
            {
                ListingNotFoundMessage => ErrorKeys.ListingNotFound,
                ListingNotOwnerMessage => ErrorKeys.ListingNotOwner,
                ListingNotOpenMessage => ErrorKeys.ListingNotOpen,
                OfferNotOnListingMessage => ErrorKeys.OfferNotOnListing,
                OfferNotPendingMessage => ErrorKeys.OfferNotPending,
                _ => ErrorKeys.InternalError,
            });
        }

        var listing = await unitOfWork.Listings.Query()
            .Include(item => item.Client)
            .FirstAsync(item => item.Id == listingId, cancellationToken);
        var clientName = listing.Client is { } client ? $"{client.FirstName} {client.LastName}".Trim() : "Danışan";

        await notificationService.NotifyAsync(
            outcome.ExpertId,
            NotificationTypes.Matched,
            new Dictionary<string, string> { ["clientName"] = clientName, ["listingTitle"] = listing.Title },
            JsonSerializer.Serialize(new { matchId = outcome.MatchId.ToString() }),
            cancellationToken);

        foreach (var expertId in outcome.OtherExpertIds.Distinct())
        {
            await notificationService.NotifyAsync(
                expertId,
                NotificationTypes.ListingMatchedOther,
                new Dictionary<string, string> { ["listingTitle"] = listing.Title },
                JsonSerializer.Serialize(new { listingId = listing.Id.ToString() }),
                cancellationToken);
        }

        return new MarkMatchedResultDto(outcome.MatchId);
    }

    private Task<bool> IsExpertAsync(Guid userId, CancellationToken cancellationToken)
        => unitOfWork.Profiles.Query().AnyAsync(
            profile => profile.Id == userId && profile.Role == ProfileRoles.Expert,
            cancellationToken);

    private async Task<Dictionary<Guid, Guid>> ViewerOffersAsync(
        Guid viewerUserId,
        List<Guid> listingIds,
        CancellationToken cancellationToken)
        => await unitOfWork.Offers.Query()
            .Where(offer => offer.ExpertId == viewerUserId && listingIds.Contains(offer.ListingId))
            .ToDictionaryAsync(offer => offer.ListingId, offer => offer.Id, cancellationToken);

    private async Task<Dictionary<Guid, DateTimeOffset>> ViewerClaimsAsync(
        Guid viewerUserId,
        List<Guid> listingIds,
        CancellationToken cancellationToken)
        => await unitOfWork.ListingClaims.Query()
            .Where(claim => claim.ExpertId == viewerUserId && listingIds.Contains(claim.ListingId))
            .ToDictionaryAsync(claim => claim.ListingId, claim => claim.CreatedAt, cancellationToken);

    private async Task<Dictionary<Guid, ListingMatchedExpertDto>> MatchedExpertsAsync(
        List<Guid> listingIds,
        CancellationToken cancellationToken)
    {
        var rows = await unitOfWork.Matches.Query()
            .Where(match => listingIds.Contains(match.ListingId))
            .OrderByDescending(match => match.CreatedAt)
            .Select(match => new
            {
                match.ListingId,
                match.ExpertId,
                FirstName = match.Expert!.Profile!.FirstName,
                LastName = match.Expert.Profile.LastName,
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.ListingId)
            .ToDictionary(
                group => group.Key,
                group => new ListingMatchedExpertDto(group.First().ExpertId, $"{group.First().FirstName} {group.First().LastName}".Trim()));
    }

    // Danışana gösterilen anlaşma bildirimleri: "Anlaşmadım" denmemiş ve admin tarafından
    // çözülmemiş olanlar.
    private async Task<Dictionary<Guid, List<ListingClaimDto>>> OpenClaimsAsync(
        List<Guid> listingIds,
        CancellationToken cancellationToken)
    {
        var rows = await unitOfWork.ListingClaims.Query()
            .Where(claim => listingIds.Contains(claim.ListingId) && claim.DismissedAt == null && claim.ResolvedAt == null)
            .OrderBy(claim => claim.CreatedAt)
            .Select(claim => new
            {
                claim.ListingId,
                claim.ExpertId,
                FirstName = claim.Expert!.Profile!.FirstName,
                LastName = claim.Expert.Profile.LastName,
                claim.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.ListingId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(row => new ListingClaimDto(row.ExpertId, $"{row.FirstName} {row.LastName}".Trim(), row.CreatedAt))
                    .ToList());
    }
}
