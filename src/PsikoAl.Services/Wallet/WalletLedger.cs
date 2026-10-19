using PsikoAl.Data.Entities;
using PsikoAl.Data.Repositories.Abstractions;

namespace PsikoAl.Services.Wallet;

/// <summary>Bir cüzdan olayını bakiye/hak + defter satırı olarak yazar.</summary>
/// <remarks>
/// Tek kural: bakiye/hak değişimi koşullu UPDATE'tir ve defter satırı onun döndürdüğü
/// "sonraki" değerleri taşır. Çağıran açık bir transaction içinde olmalı ve
/// SaveChanges/Commit'i kendisi yapmalı (teklif gibi olaylarda aynı işlemde başka
/// satırlar da yazılıyor). Eksiye düşecekse hiçbir şey yazılmaz ve null döner.
/// </remarks>
public static class WalletLedger
{
    public static async Task<WalletTransaction?> TryRecordAsync(
        IUnitOfWork unitOfWork,
        WalletTransaction entry,
        CancellationToken cancellationToken)
    {
        var after = await unitOfWork.ExpertWallets.TryApplyAsync(
            entry.ExpertId,
            entry.AmountKurus,
            entry.FreeRightsDelta,
            cancellationToken);
        if (after is null)
        {
            return null;
        }

        entry.BalanceAfterKurus = after.BalanceKurus;
        entry.FreeRightsAfter = after.FreeOfferRights;
        await unitOfWork.WalletTransactions.AddAsync(entry, cancellationToken);
        return entry;
    }
}
