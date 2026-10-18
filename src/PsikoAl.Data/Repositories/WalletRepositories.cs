using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using PsikoAl.Common.Constants;
using PsikoAl.Data.Entities;
using PsikoAl.Data.Repositories.Abstractions;

namespace PsikoAl.Data.Repositories;

public sealed class WalletPaymentRepository(AppDbContext dbContext)
    : Repository<WalletPayment, Guid>(dbContext), IWalletPaymentRepository
{
    public Task<WalletPayment?> GetByMerchantOidAsync(string merchantOid, CancellationToken cancellationToken)
        => DbContext.WalletPayments
            .AsNoTracking()
            .FirstOrDefaultAsync(payment => payment.MerchantOid == merchantOid, cancellationToken);

    public async Task<bool> TryCompleteAsync(
        Guid paymentId,
        string status,
        string? providerPayload,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
        => await DbContext.WalletPayments
            .Where(payment => payment.Id == paymentId && payment.Status == WalletPaymentStatuses.Pending)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(payment => payment.Status, status)
                    .SetProperty(payment => payment.ProviderPayload, providerPayload)
                    .SetProperty(payment => payment.CompletedAt, completedAt),
                cancellationToken) == 1;
}

public sealed class WalletTransactionRepository(AppDbContext dbContext)
    : Repository<WalletTransaction, Guid>(dbContext), IWalletTransactionRepository
{
    public IQueryable<WalletTransaction> QueryForExpert(Guid expertId)
        => DbContext.WalletTransactions
            .AsNoTracking()
            .Where(transaction => transaction.ExpertId == expertId)
            .OrderByDescending(transaction => transaction.CreatedAt)
            .ThenByDescending(transaction => transaction.Id);
}

public sealed class ListingClaimRepository(AppDbContext dbContext)
    : Repository<ListingClaim, Guid>(dbContext), IListingClaimRepository;

// Ham ADO bilinçli: UPDATE ... RETURNING iki kolon döndürüyor; EF'in SqlQuery<T> ad-hoc
// tipleri kolon eşlemesini adlandırma kuralına (snake_case eklentisi) bağlıyor ve bu
// sessizce kırılabilir. Komut açık EF transaction'ına bağlanır; böylece düşüm ile defter
// satırı aynı işlemde kalır.
public sealed class ExpertWalletRepository(AppDbContext dbContext) : IExpertWalletRepository
{
    public async Task<WalletState?> GetAsync(Guid expertId, CancellationToken cancellationToken)
        => await dbContext.Experts
            .AsNoTracking()
            .Where(expert => expert.Id == expertId)
            .Select(expert => new WalletState(expert.BalanceKurus, expert.FreeOfferRights))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<WalletState?> TryApplyAsync(
        Guid expertId,
        long deltaKurus,
        int deltaRights,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await dbContext.Database.OpenConnectionAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText =
            """
            update public.experts
               set balance_kurus = balance_kurus + @delta_kurus,
                   free_offer_rights = free_offer_rights + @delta_rights
             where id = @id
               and balance_kurus + @delta_kurus >= 0
               and free_offer_rights + @delta_rights >= 0
            returning balance_kurus, free_offer_rights
            """;
        AddParameter(command, "id", expertId);
        AddParameter(command, "delta_kurus", deltaKurus);
        AddParameter(command, "delta_rights", deltaRights);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new WalletState(reader.GetInt64(0), reader.GetInt32(1))
            : null;
    }

    private static void AddParameter(DbCommand command, string name, object value)
        => command.Parameters.Add(new NpgsqlParameter(name, value));
}
