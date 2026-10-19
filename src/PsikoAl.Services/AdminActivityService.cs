using Microsoft.EntityFrameworkCore;
using PsikoAl.Common.Constants;
using PsikoAl.Common.Dtos.Admin;
using PsikoAl.Common.Exceptions;
using PsikoAl.Common.Presentation;
using PsikoAl.Data.Repositories.Abstractions;
using PsikoAl.Services.Abstractions;

namespace PsikoAl.Services;

/// <summary>Admin "Kullanıcı işlem kaydı" (REVIZE-2 §9): bir kişinin bütün hareketleri, en yeni önce.</summary>
/// <remarks>
/// Ayrı bir olay tablosu YOK: zaman çizelgesi alan tablolarından (profil, ilan, teklif,
/// eşleşme, cüzdan defteri, bildirim, anlaşma bildirimi) ve audit_logs'tan (giriş, admin
/// işlemleri) türetilir. Böylece yeni bir yazma yolu eklenmez ve kayıt hiçbir zaman
/// gerçeklikten kopmaz.
///
/// Sayfalama birleştirmeyle yapılır: p. sayfanın satırları her kaynağın en yeni
/// p × perPage satırı içinde olmak ZORUNDA. Her kaynaktan o kadarı çekilir, birleştirilir,
/// sıralanır ve dilimlenir; toplam, kaynak sayımlarının toplamıdır.
/// </remarks>
public sealed class AdminActivityService(IUnitOfWork unitOfWork) : IAdminActivityService
{
    private static readonly Dictionary<string, string> KindLabels = new(StringComparer.Ordinal)
    {
        [ActivityKinds.Registration] = "Kayıt",
        [ActivityKinds.Login] = "Giriş",
        [ActivityKinds.Listing] = "İlan",
        [ActivityKinds.Offer] = "Teklif",
        [ActivityKinds.Match] = "Eşleşme",
        [ActivityKinds.Wallet] = "Bakiye hareketi",
        [ActivityKinds.Notification] = "Bildirim",
        [ActivityKinds.Claim] = "Anlaşma bildirimi",
        [ActivityKinds.Admin] = "Admin işlemi",
    };

    public async Task<AdminPagedResultDto<AdminActivityItemDto>> ListAsync(
        Guid userId,
        AdminActivityQueryDto query,
        CancellationToken cancellationToken)
    {
        var profile = await unitOfWork.Profiles.Query()
            .FirstOrDefaultAsync(item => item.Id == userId, cancellationToken)
            ?? throw new DomainException(ErrorKeys.ProfileNotFound);

        var kinds = ParseKinds(query.Kind);
        var requested = AdminPaging.Normalize(query.Page, query.PerPage);
        var window = requested.Page * requested.PerPage;
        var from = query.From;
        var to = query.To;

        var items = new List<AdminActivityItemDto>();
        var total = 0;

        void Add(int count, IEnumerable<AdminActivityItemDto> rows)
        {
            total += count;
            items.AddRange(rows);
        }

        if (kinds.Contains(ActivityKinds.Registration)
            && (from is null || profile.CreatedAt >= from)
            && (to is null || profile.CreatedAt <= to))
        {
            Add(1, [Item(ActivityKinds.Registration, profile.CreatedAt, "Hesap oluşturuldu", RoleLabel(profile.Role), "profile", profile.Id)]);
        }

        var userIdText = userId.ToString();

        if (kinds.Contains(ActivityKinds.Login))
        {
            var logins = unitOfWork.AuditLogs.Query()
                .Where(log => log.ActorType == AuditActorTypes.User && log.Action == AuditActions.UserLogin && log.EntityId == userIdText)
                .Where(log => (from == null || log.CreatedAt >= from) && (to == null || log.CreatedAt <= to));
            Add(
                await logins.CountAsync(cancellationToken),
                (await logins.OrderByDescending(log => log.CreatedAt).Take(window).ToListAsync(cancellationToken))
                    .Select(log => Item(ActivityKinds.Login, log.CreatedAt, "Giriş yaptı", null, null, null)));
        }

        if (kinds.Contains(ActivityKinds.Listing))
        {
            var listings = unitOfWork.Listings.Query()
                .Where(listing => listing.ClientId == userId)
                .Where(listing => (from == null || listing.CreatedAt >= from) && (to == null || listing.CreatedAt <= to));
            Add(
                await listings.CountAsync(cancellationToken),
                (await listings.OrderByDescending(listing => listing.CreatedAt).Take(window).ToListAsync(cancellationToken))
                    .Select(listing => Item(
                        ActivityKinds.Listing,
                        listing.CreatedAt,
                        $"İlan açtı: \"{listing.Title}\"",
                        $"Durum: {listing.Status} · Bütçe: {BudgetLabelFormatter.Format(listing.BudgetMin, listing.BudgetMax)}",
                        "listing",
                        listing.Id)));
        }

        if (kinds.Contains(ActivityKinds.Offer))
        {
            var offers = unitOfWork.Offers.Query()
                .Where(offer => offer.ExpertId == userId || offer.Listing!.ClientId == userId)
                .Where(offer => (from == null || offer.CreatedAt >= from) && (to == null || offer.CreatedAt <= to));
            var rows = await offers
                .OrderByDescending(offer => offer.CreatedAt)
                .Take(window)
                .Select(offer => new
                {
                    offer.Id,
                    offer.ExpertId,
                    offer.CreatedAt,
                    offer.Status,
                    offer.ClosedReason,
                    offer.FeePaidKurus,
                    offer.UsedFreeRight,
                    ListingTitle = offer.Listing!.Title,
                    ExpertName = offer.Expert!.Profile!.FirstName + " " + offer.Expert.Profile.LastName,
                })
                .ToListAsync(cancellationToken);
            Add(
                await offers.CountAsync(cancellationToken),
                rows.Select(offer => offer.ExpertId == userId
                    ? Item(
                        ActivityKinds.Offer,
                        offer.CreatedAt,
                        $"Teklif gönderdi: \"{offer.ListingTitle}\"",
                        $"Ödenen: {MoneyLabel.FeePaid(offer.FeePaidKurus, offer.UsedFreeRight)} · {OfferStatusLabels.For(offer.Status, offer.ClosedReason)}",
                        "offer",
                        offer.Id)
                    : Item(
                        ActivityKinds.Offer,
                        offer.CreatedAt,
                        $"Teklif aldı: \"{offer.ListingTitle}\"",
                        $"Uzman: {offer.ExpertName.Trim()} · {OfferStatusLabels.For(offer.Status, offer.ClosedReason)}",
                        "offer",
                        offer.Id)));
        }

        if (kinds.Contains(ActivityKinds.Match))
        {
            var matches = unitOfWork.Matches.Query()
                .Where(match => match.ClientId == userId || match.ExpertId == userId)
                .Where(match => (from == null || match.CreatedAt >= from) && (to == null || match.CreatedAt <= to));
            var rows = await matches
                .OrderByDescending(match => match.CreatedAt)
                .Take(window)
                .Select(match => new { match.Id, match.CreatedAt, match.Status, ListingTitle = match.Listing!.Title })
                .ToListAsync(cancellationToken);
            Add(
                await matches.CountAsync(cancellationToken),
                rows.Select(match => Item(
                    ActivityKinds.Match,
                    match.CreatedAt,
                    $"Eşleşme: \"{match.ListingTitle}\"",
                    $"Durum: {match.Status}",
                    "match",
                    match.Id)));
        }

        if (kinds.Contains(ActivityKinds.Wallet))
        {
            var ledger = unitOfWork.WalletTransactions.Query()
                .Where(transaction => transaction.ExpertId == userId)
                .Where(transaction => (from == null || transaction.CreatedAt >= from) && (to == null || transaction.CreatedAt <= to));
            var rows = await ledger
                .OrderByDescending(transaction => transaction.CreatedAt)
                .Take(window)
                .Select(transaction => new
                {
                    Transaction = transaction,
                    ListingTitle = transaction.Listing != null ? transaction.Listing.Title : null,
                })
                .ToListAsync(cancellationToken);
            Add(
                await ledger.CountAsync(cancellationToken),
                rows.Select(row => Item(
                    ActivityKinds.Wallet,
                    row.Transaction.CreatedAt,
                    WalletLabels.TypeLabel(row.Transaction.Type),
                    WalletDetail(row.Transaction.AmountKurus, row.Transaction.FreeRightsDelta, row.Transaction.BalanceAfterKurus,
                        row.Transaction.FreeRightsAfter, WalletLabels.Description(row.Transaction.Type, row.ListingTitle, row.Transaction.Reason)),
                    "wallet_transaction",
                    row.Transaction.Id)));
        }

        if (kinds.Contains(ActivityKinds.Notification))
        {
            var notifications = unitOfWork.Notifications.Query()
                .Where(notification => notification.UserId == userId)
                .Where(notification => (from == null || notification.CreatedAt >= from) && (to == null || notification.CreatedAt <= to));
            Add(
                await notifications.CountAsync(cancellationToken),
                (await notifications.OrderByDescending(notification => notification.CreatedAt).Take(window).ToListAsync(cancellationToken))
                    .Select(notification => Item(
                        ActivityKinds.Notification,
                        notification.CreatedAt,
                        notification.Title,
                        $"{notification.Type}{(notification.Read ? " · okundu" : string.Empty)}",
                        "notification",
                        notification.Id)));
        }

        if (kinds.Contains(ActivityKinds.Claim))
        {
            var claims = unitOfWork.ListingClaims.Query()
                .Where(claim => claim.ExpertId == userId || claim.Listing!.ClientId == userId)
                .Where(claim => (from == null || claim.CreatedAt >= from) && (to == null || claim.CreatedAt <= to));
            var rows = await claims
                .OrderByDescending(claim => claim.CreatedAt)
                .Take(window)
                .Select(claim => new
                {
                    claim.Id,
                    claim.ExpertId,
                    claim.CreatedAt,
                    claim.DismissedAt,
                    claim.ResolvedAt,
                    claim.Note,
                    ListingTitle = claim.Listing!.Title,
                })
                .ToListAsync(cancellationToken);
            Add(
                await claims.CountAsync(cancellationToken),
                rows.Select(claim => Item(
                    ActivityKinds.Claim,
                    claim.CreatedAt,
                    claim.ExpertId == userId
                        ? $"\"Danışanla anlaştım\" bildirdi: \"{claim.ListingTitle}\""
                        : $"Uzman anlaşma bildirdi: \"{claim.ListingTitle}\"",
                    ClaimDetail(claim.Note, claim.DismissedAt, claim.ResolvedAt),
                    "listing_claim",
                    claim.Id)));
        }

        if (kinds.Contains(ActivityKinds.Admin))
        {
            var listingIds = await unitOfWork.Listings.Query()
                .Where(listing => listing.ClientId == userId)
                .Select(listing => listing.Id.ToString())
                .ToListAsync(cancellationToken);

            var adminLogs = unitOfWork.AuditLogs.Query()
                .Where(log => log.ActorType == AuditActorTypes.Admin)
                .Where(log => log.EntityId == userIdText || (log.EntityType == "listing" && listingIds.Contains(log.EntityId)))
                .Where(log => (from == null || log.CreatedAt >= from) && (to == null || log.CreatedAt <= to));
            Add(
                await adminLogs.CountAsync(cancellationToken),
                (await adminLogs.OrderByDescending(log => log.CreatedAt).Take(window).ToListAsync(cancellationToken))
                    .Select(log => Item(
                        ActivityKinds.Admin,
                        log.CreatedAt,
                        log.Action,
                        log.Reason,
                        log.EntityType,
                        log.EntityId)));
        }

        var page = items
            .OrderByDescending(item => item.OccurredAt)
            .Skip((requested.Page - 1) * requested.PerPage)
            .Take(requested.PerPage)
            .ToList();

        return new AdminPagedResultDto<AdminActivityItemDto>(page, requested with { Total = total });
    }

    internal static IReadOnlySet<string> ParseKinds(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return ActivityKinds.All.ToHashSet(StringComparer.Ordinal);
        }

        var requested = kind
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => value.ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);

        return requested.All(ActivityKinds.All.Contains)
            ? requested
            : throw new DomainException(ErrorKeys.ValidationFailed, "kind");
    }

    private static AdminActivityItemDto Item(string kind, DateTimeOffset occurredAt, string title, string? detail, string? entityType, object? entityId)
        => new(kind, KindLabels[kind], occurredAt, title, detail, entityType, entityId?.ToString());

    private static string RoleLabel(string role) => role == ProfileRoles.Expert ? "Uzman hesabı" : "Danışan hesabı";

    private static string WalletDetail(long amountKurus, int rightsDelta, long balanceAfter, int rightsAfter, string description)
    {
        var change = string.Join(" ", new[] { MoneyLabel.WithSign(amountKurus), MoneyLabel.Rights(rightsDelta) }.Where(part => part.Length > 0));
        return $"{change} · Bakiye: {MoneyLabel.Try(balanceAfter)}, hak: {rightsAfter} · {description}";
    }

    private static string? ClaimDetail(string? note, DateTimeOffset? dismissedAt, DateTimeOffset? resolvedAt)
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(note))
        {
            parts.Add($"Not: {note}");
        }

        if (dismissedAt is not null)
        {
            parts.Add("Danışan \"Anlaşmadım\" dedi");
        }

        if (resolvedAt is not null)
        {
            parts.Add("Admin çözdü");
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }
}
