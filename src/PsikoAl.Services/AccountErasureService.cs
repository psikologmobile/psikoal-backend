using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PsikoAl.Common.Constants;
using PsikoAl.Common.Exceptions;
using PsikoAl.Data.Entities;
using PsikoAl.Data.Repositories.Abstractions;
using PsikoAl.Services.Abstractions;

namespace PsikoAl.Services;

/// <summary>KVKK m.7 silme talebi: kullanıcının kişisel verisini gerçekten ortadan kaldırır.</summary>
/// <remarks>
/// Eskiden yalnız profil adı/e-postası değiştiriliyordu; test sonuçları, ilan metinleri,
/// bildirimler, push token'ları, Storage'daki avatar/CV/sertifikalar ve GoTrue'daki
/// e-posta olduğu gibi kalıyordu.
///
/// Auth kullanıcısı HARD DELETE edilmez: profiles.id -> auth.users ON DELETE CASCADE
/// zinciri ilanları, onlara bağlı teklif/eşleşmeleri (karşı tarafın geçmişi) ve uzmanın
/// aldığı değerlendirmeleri de silerdi. Bunun yerine kimlik verisi her yerde
/// anonimleştirilir, satırlar yalnız ilişkisel bütünlük için kalır.
///
/// Sıra bilinçli: (1) GoTrue anonimleştirme + kalıcı ban — başarısızsa DB'de hiçbir şey
/// değişmez ve kullanıcı geçerli token'ıyla tekrar deneyebilir; (2) tüm DB değişiklikleri
/// TEK SaveChanges'ta (tek transaction); (3) Storage temizliği en iyi çaba — başarısızlık
/// loglanır, hesap yine silinmiş sayılır. Her adım tekrar koşulabilir (idempotent).
/// </remarks>
public sealed partial class AccountErasureService(
    IUnitOfWork unitOfWork,
    ISupabaseAdminService supabaseAdmin,
    ISupabaseStorageService storage,
    IAccountStatusService accountStatus,
    ILogger<AccountErasureService> logger) : IAccountErasureService
{
    public const string DeletedFirstName = "Silinmiş";
    public const string DeletedLastName = "Kullanıcı";
    public const string DeletedListingTitle = "Silinmiş ilan";
    public const string AccountDeletedReason = "Hesap kullanıcı tarafından silindi.";

    private static readonly string[] OpenListingStatuses = [ListingStatuses.PendingApproval, ListingStatuses.Open];

    public async Task EraseAsync(Guid userId, CancellationToken cancellationToken)
    {
        var profile = await unitOfWork.Profiles.GetByIdAsync(userId, cancellationToken)
            ?? throw new DomainException(ErrorKeys.ProfileNotFound);

        await supabaseAdmin.AnonymizeUserAsync(userId, AnonymizedEmail(userId), cancellationToken);

        var oldStatus = profile.Status;
        AnonymizeProfile(profile);
        await EraseAssessmentResultsAsync(userId, cancellationToken);
        await EraseListingsAsync(userId, cancellationToken);
        await WithdrawPendingOffersAsync(userId, cancellationToken);
        await ReleaseActiveMatchesAsync(userId, cancellationToken);
        await EraseReviewCommentsAsync(userId, cancellationToken);
        await DeleteNotificationsAndPushTokensAsync(userId, cancellationToken);
        await EraseExpertProfileAsync(userId, cancellationToken);

        if (oldStatus != ProfileStatuses.Deleted)
        {
            await unitOfWork.AuditLogs.AddAsync(
                new AuditLog
                {
                    ActorType = AuditActorTypes.User,
                    Action = "user.delete_self",
                    EntityType = "profile",
                    EntityId = userId.ToString(),
                    OldValue = JsonSerializer.Serialize(new { status = oldStatus }),
                    NewValue = JsonSerializer.Serialize(new { status = ProfileStatuses.Deleted }),
                },
                cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        accountStatus.Invalidate(userId);

        await DeleteStorageFolderAsync("avatars", userId, cancellationToken);
        await DeleteStorageFolderAsync("documents", userId, cancellationToken);
    }

    public static string AnonymizedEmail(Guid userId) => $"deleted-{userId}@psikoal.invalid";

    private static void AnonymizeProfile(Profile profile)
    {
        profile.Email = AnonymizedEmail(profile.Id);
        profile.FirstName = DeletedFirstName;
        profile.LastName = DeletedLastName;
        profile.Phone = null;
        profile.City = null;
        profile.AvatarUrl = null;
        profile.ShareEmail = false;
        profile.SharePhone = false;
        profile.ShareLocation = false;
        profile.Status = ProfileStatuses.Deleted;
    }

    private async Task EraseAssessmentResultsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var results = await unitOfWork.AssessmentResults.QueryTracked()
            .Where(result => result.UserId == userId)
            .ToListAsync(cancellationToken);

        foreach (var result in results)
        {
            unitOfWork.AssessmentResults.Delete(result);
        }
    }

    private async Task EraseListingsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var listings = await unitOfWork.Listings.QueryTracked()
            .Where(listing => listing.ClientId == userId)
            .ToListAsync(cancellationToken);

        var closedListingIds = new List<Guid>();
        foreach (var listing in listings)
        {
            if (OpenListingStatuses.Contains(listing.Status))
            {
                listing.Status = ListingStatuses.Closed;
                closedListingIds.Add(listing.Id);
            }

            listing.Title = DeletedListingTitle;
            listing.Description = string.Empty;
            listing.City = null;
            listing.AssessmentResultId = null;
        }

        var pendingOffers = await unitOfWork.Offers.QueryTracked()
            .Where(offer => closedListingIds.Contains(offer.ListingId) && offer.Status == OfferStatuses.Pending)
            .ToListAsync(cancellationToken);

        foreach (var offer in pendingOffers)
        {
            offer.Status = OfferStatuses.Rejected;
            offer.ClosedReason = OfferClosedReasons.ListingClosed;
        }
    }

    private async Task WithdrawPendingOffersAsync(Guid userId, CancellationToken cancellationToken)
    {
        var offers = await unitOfWork.Offers.QueryTracked()
            .Where(offer => offer.ExpertId == userId && offer.Status == OfferStatuses.Pending)
            .ToListAsync(cancellationToken);

        foreach (var offer in offers)
        {
            offer.Status = OfferStatuses.Withdrawn;
        }
    }

    // Eşleşme durum makinesindeki zorla bırakmayla aynı son durum: RELEASED, iki tarafın
    // bırakma zamanı dolu. Karşı taraf eşleşmeyi geçmişinde sonlanmış olarak görür.
    private async Task ReleaseActiveMatchesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var matches = await unitOfWork.Matches.QueryTracked()
            .Where(match => (match.ClientId == userId || match.ExpertId == userId) && match.Status == MatchStatuses.Active)
            .ToListAsync(cancellationToken);

        var releasedAt = DateTimeOffset.UtcNow;
        foreach (var match in matches)
        {
            match.ClientReleasedAt ??= releasedAt;
            match.ExpertReleasedAt ??= releasedAt;
            match.ReleaseReason = AccountDeletedReason;
            match.Status = MatchStatuses.Released;
        }
    }

    // Puan uzmanın ortalamasına ait olduğu için kalır; serbest metin kişisel/sağlık
    // bilgisi içerebileceği için silinir.
    private async Task EraseReviewCommentsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var reviews = await unitOfWork.Reviews.QueryTracked()
            .Where(review => review.ClientId == userId)
            .ToListAsync(cancellationToken);

        foreach (var review in reviews)
        {
            review.Comment = string.Empty;
        }
    }

    private async Task DeleteNotificationsAndPushTokensAsync(Guid userId, CancellationToken cancellationToken)
    {
        var notifications = await unitOfWork.Notifications.QueryTracked()
            .Where(notification => notification.UserId == userId)
            .ToListAsync(cancellationToken);
        foreach (var notification in notifications)
        {
            unitOfWork.Notifications.Delete(notification);
        }

        var pushTokens = await unitOfWork.PushTokens.QueryTracked()
            .Where(pushToken => pushToken.UserId == userId)
            .ToListAsync(cancellationToken);
        foreach (var pushToken in pushTokens)
        {
            unitOfWork.PushTokens.Delete(pushToken);
        }
    }

    // Uzman satırı karşı tarafın teklif/eşleşme geçmişi için kalır; herkese açık profil
    // olmaktan çıkarılır (rejected) ve biyografi/belge/eğitim gibi kişisel alanlar boşaltılır.
    private async Task EraseExpertProfileAsync(Guid userId, CancellationToken cancellationToken)
    {
        var expert = await unitOfWork.Experts.QueryTracked()
            .FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);
        if (expert is null)
        {
            return;
        }

        expert.Bio = string.Empty;
        expert.Education = null;
        expert.CvUrl = null;
        expert.Certificates = [];
        expert.PersonalWebsite = null;
        expert.PendingRevision = null;
        expert.Status = ExpertStatuses.Rejected;
        expert.RejectionReason = AccountDeletedReason;
    }

    private async Task DeleteStorageFolderAsync(string bucket, Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            await storage.DeleteFolderAsync(bucket, userId.ToString(), cancellationToken);
        }
        catch (Exception exception) when (exception is DomainException or HttpRequestException)
        {
            LogStorageCleanupFailed(logger, exception, bucket, userId);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Hesap silme: {Bucket} klasörü temizlenemedi (userId: {UserId}); elle temizlenmeli")]
    private static partial void LogStorageCleanupFailed(ILogger logger, Exception exception, string bucket, Guid userId);
}
