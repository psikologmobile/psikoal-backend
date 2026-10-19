using System.Text.Json;
using PsikoAl.Common.Constants;
using PsikoAl.Data.Entities;
using PsikoAl.Data.Repositories.Abstractions;
using PsikoAl.Services.Abstractions;
using PsikoAl.Services.Pricing;

namespace PsikoAl.Services;

/// <summary>İlanın yayına alınması: admin onayı ve LISTING_AUTO_APPROVE aynı adımlardan geçer.</summary>
/// <remarks>
/// Onay anında o anki dönemin ücret eğrisi ilana kopyalanır (REVIZE-2 §1.1). İki yol
/// ayrı ayrı yazılsaydı otomatik onaylanan ilanlar eğrisiz yayına çıkar ve dönem değişince
/// ücretleri kayardı.
/// </remarks>
public static class ListingPublication
{
    public static async Task PublishAsync(
        IUnitOfWork unitOfWork,
        Listing listing,
        Guid? approvedBy,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var expireDays = await unitOfWork.SystemSettings.GetIntAsync(SystemSettingKeys.ListingExpireDays, cancellationToken);
        var period = await ListingPricing.GetCurrentPeriodAsync(unitOfWork, now, cancellationToken);

        listing.Status = ListingStatuses.Open;
        listing.ApprovedAt = now;
        listing.ApprovedBy = approvedBy;
        listing.ExpiresAt = now.AddDays(expireDays);
        listing.RejectionReason = null;
        ListingPricing.ApplySnapshot(listing, period);
    }

    // Davetle gelen danışanın ilanı yayına çıkınca davet eden uzmana haber verilir (§3.2).
    public static Task NotifyInvitedExpertAsync(
        INotificationService notificationService,
        Listing listing,
        CancellationToken cancellationToken)
        => listing.InvitedExpertId is { } expertId
            ? notificationService.NotifyAsync(
                expertId,
                NotificationTypes.InvitedClientListing,
                new Dictionary<string, string> { ["listingTitle"] = listing.Title },
                JsonSerializer.Serialize(new { listingId = listing.Id.ToString() }),
                cancellationToken)
            : Task.CompletedTask;
}
