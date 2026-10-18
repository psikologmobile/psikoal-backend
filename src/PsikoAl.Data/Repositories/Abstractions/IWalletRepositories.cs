using PsikoAl.Data.Entities;

namespace PsikoAl.Data.Repositories.Abstractions;

public interface IPricingPeriodRepository : IRepository<PricingPeriod, Guid>
{
    // Geçerli dönem: effective_from <= now olan en yeni satır.
    Task<PricingPeriod?> GetCurrentAsync(DateTimeOffset now, CancellationToken cancellationToken);
}

public interface IWalletPaymentRepository : IRepository<WalletPayment, Guid>
{
    Task<WalletPayment?> GetByMerchantOidAsync(string merchantOid, CancellationToken cancellationToken);

    // PENDING'den TEK geçiş: yalnız hâlâ PENDING ise yazar ve true döner (callback idempotency'si).
    Task<bool> TryCompleteAsync(
        Guid paymentId,
        string status,
        string? providerPayload,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken);
}

public interface IWalletTransactionRepository : IRepository<WalletTransaction, Guid>
{
    // En yeni önce.
    IQueryable<WalletTransaction> QueryForExpert(Guid expertId);
}

public sealed record WalletState(long BalanceKurus, int FreeOfferRights);

public interface IExpertWalletRepository
{
    Task<WalletState?> GetAsync(Guid expertId, CancellationToken cancellationToken);

    // Bakiyeye ve hakka işaretli değişikliği TEK koşullu UPDATE ile uygular. Sonuç
    // eksiye düşecekse hiçbir şey yazılmaz ve null döner. Satır kilidi aynı uzmanın
    // eşzamanlı işlemlerini sıraya sokar; çağıran transaction içinde olmalıdır.
    Task<WalletState?> TryApplyAsync(Guid expertId, long deltaKurus, int deltaRights, CancellationToken cancellationToken);
}

public interface IListingClaimRepository : IRepository<ListingClaim, Guid>;
