using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PsikoAl.Api.Authorization;
using PsikoAl.Api.Extensions;
using PsikoAl.Common.Constants;
using PsikoAl.Common.Dtos.Admin;
using PsikoAl.Common.Dtos.Wallet;
using PsikoAl.Common.Exceptions;
using PsikoAl.Services.Abstractions;

namespace PsikoAl.Api.Controllers;

[ApiController]
[Route("admin/wallet")]
[Authorize(Policy = AdminPolicies.Finance)]
public sealed class AdminWalletController(IAdminWalletService adminWalletService) : ControllerBase
{
    // Multipart sarmalayıcısı için pay: dosya sınırı servis katmanında (10 MB) ayrıca ölçülür.
    private const long MaxInvoiceRequestBytes = WalletFieldLimits.InvoiceMaxBytes + (256 * 1024);

    [HttpGet("payments")]
    public Task<AdminPagedResultDto<AdminWalletPaymentDto>> ListPayments(
        [FromQuery] AdminWalletPaymentQueryDto query,
        CancellationToken cancellationToken)
        => adminWalletService.ListPaymentsAsync(query, cancellationToken);

    [HttpPost("payments/{id:guid}/invoice")]
    [EnableRateLimiting(RateLimitPolicies.Upload)]
    [RequestSizeLimit(MaxInvoiceRequestBytes)]
    public async Task<AdminWalletPaymentDto> UploadInvoice(Guid id, IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null or { Length: 0 })
        {
            throw new DomainException(ErrorKeys.ValidationFailed, "file");
        }

        if (file.Length > WalletFieldLimits.InvoiceMaxBytes)
        {
            throw new DomainException(ErrorKeys.FileTooLarge, "file");
        }

        await using var content = file.OpenReadStream();
        return await adminWalletService.UploadInvoiceAsync(
            this.CurrentUserId(), id, content, file.ContentType, file.FileName, cancellationToken);
    }

    // Görüntüleme/indirme: 5 dakikalık imzalı Storage adresi (fatura yoksa 404 INVOICE_NOT_FOUND).
    [HttpGet("payments/{id:guid}/invoice")]
    public Task<InvoiceUrlDto> GetInvoice(Guid id, CancellationToken cancellationToken)
        => adminWalletService.GetInvoiceUrlAsync(id, cancellationToken);

    [HttpGet("experts/{expertId:guid}")]
    public Task<AdminExpertWalletDto> GetExpertWallet(
        Guid expertId,
        [FromQuery] int? page,
        [FromQuery] int? perPage,
        CancellationToken cancellationToken)
        => adminWalletService.GetExpertWalletAsync(expertId, page, perPage, cancellationToken);

    [HttpPost("experts/{expertId:guid}/gift")]
    public Task<AdminExpertWalletDto> Gift(Guid expertId, AdminWalletGiftDto request, CancellationToken cancellationToken)
        => adminWalletService.GiftAsync(this.CurrentUserId(), expertId, request, cancellationToken);

    [HttpPost("experts/{expertId:guid}/adjust")]
    public Task<AdminExpertWalletDto> Adjust(Guid expertId, AdminWalletAdjustDto request, CancellationToken cancellationToken)
        => adminWalletService.AdjustAsync(this.CurrentUserId(), expertId, request, cancellationToken);
}
