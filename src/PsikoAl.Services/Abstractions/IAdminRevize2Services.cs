using PsikoAl.Common.Dtos.Admin;
using PsikoAl.Common.Dtos.Wallet;

namespace PsikoAl.Services.Abstractions;

public interface IAdminPricingService
{
    Task<AdminPagedResultDto<AdminPricingPeriodDto>> ListAsync(int? page, int? perPage, CancellationToken cancellationToken);

    Task<AdminPricingPeriodDto> CreateAsync(Guid actorAuthUserId, CreatePricingPeriodDto request, CancellationToken cancellationToken);

    Task<PricingPreviewResultDto> PreviewAsync(PricingPreviewRequestDto request, CancellationToken cancellationToken);
}

public interface IAdminWalletService
{
    Task<AdminPagedResultDto<AdminWalletPaymentDto>> ListPaymentsAsync(AdminWalletPaymentQueryDto query, CancellationToken cancellationToken);

    Task<AdminWalletPaymentDto> UploadInvoiceAsync(
        Guid actorAuthUserId,
        Guid paymentId,
        Stream content,
        string contentType,
        string? fileName,
        CancellationToken cancellationToken);

    Task<InvoiceUrlDto> GetInvoiceUrlAsync(Guid paymentId, CancellationToken cancellationToken);

    Task<AdminExpertWalletDto> GetExpertWalletAsync(Guid expertId, int? page, int? perPage, CancellationToken cancellationToken);

    Task<AdminExpertWalletDto> GiftAsync(Guid actorAuthUserId, Guid expertId, AdminWalletGiftDto request, CancellationToken cancellationToken);

    Task<AdminExpertWalletDto> AdjustAsync(Guid actorAuthUserId, Guid expertId, AdminWalletAdjustDto request, CancellationToken cancellationToken);
}

public interface IAdminClaimService
{
    Task<AdminPagedResultDto<AdminClaimDto>> ListAsync(AdminClaimQueryDto query, CancellationToken cancellationToken);

    Task ResolveAsync(Guid actorAuthUserId, Guid claimId, string? note, CancellationToken cancellationToken);
}

public interface IAdminReferralService
{
    Task<AdminPagedResultDto<AdminReferralDto>> ListAsync(AdminReferralQueryDto query, CancellationToken cancellationToken);
}

public interface IAdminActivityService
{
    Task<AdminPagedResultDto<AdminActivityItemDto>> ListAsync(Guid userId, AdminActivityQueryDto query, CancellationToken cancellationToken);
}
