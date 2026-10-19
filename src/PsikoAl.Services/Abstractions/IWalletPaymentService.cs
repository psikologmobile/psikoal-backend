using PsikoAl.Common.Dtos.Payment;

namespace PsikoAl.Services.Abstractions;

public interface IWalletPaymentService
{
    Task<PayTrInitResultDto> InitPayTrAsync(
        Guid expertUserId,
        long amountKurus,
        string userIp,
        CancellationToken cancellationToken);

    Task<PayTrCallbackOutcome> HandlePayTrCallbackAsync(PayTrCallbackDto callback, CancellationToken cancellationToken);
}
