using PsikoAl.Common.Dtos.Wallet;

namespace PsikoAl.Services.Abstractions;

public interface IWalletService
{
    Task<WalletDto> GetMineAsync(Guid expertUserId, CancellationToken cancellationToken);

    Task<WalletPageResult<WalletTransactionDto>> ListMyTransactionsAsync(
        Guid expertUserId,
        int? page,
        int? perPage,
        string? type,
        CancellationToken cancellationToken);

    Task<WalletPageResult<WalletPaymentDto>> ListMyPaymentsAsync(
        Guid expertUserId,
        int? page,
        int? perPage,
        CancellationToken cancellationToken);

    Task<InvoiceUrlDto> GetInvoiceUrlAsync(Guid expertUserId, Guid paymentId, CancellationToken cancellationToken);

    Task<TopupOptionsDto> GetTopupOptionsAsync(CancellationToken cancellationToken);

    // Uzman onaylandığında: hoş geldin hakkı (yalnız ilk kez) + davet edene ödül (çift
    // başına bir kez). İkisi de idempotent — aynı uzman tekrar onaylansa yeni hak doğmaz.
    Task GrantApprovalRewardsAsync(Guid expertId, CancellationToken cancellationToken);
}
