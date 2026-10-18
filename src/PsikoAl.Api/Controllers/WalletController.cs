using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PsikoAl.Api.Extensions;
using PsikoAl.Services.Abstractions;

namespace PsikoAl.Api.Controllers;

// Uzman cüzdanı (REVIZE-2 §2.2). Mobil uygulama bakiye, hak, hareket ve faturaları
// GÖSTERİR; yükleme yolu yalnız web sitesindedir (App Store / Play kuralı).
[ApiController]
[Route("wallet")]
public sealed class WalletController(IWalletService walletService) : ControllerBase
{
    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken)
        => Ok(new { data = await walletService.GetMineAsync(this.CurrentUserId(), cancellationToken) });

    [Authorize]
    [HttpGet("transactions")]
    public async Task<IActionResult> ListTransactions(
        [FromQuery] int? page,
        [FromQuery] int? perPage,
        [FromQuery] string? type,
        CancellationToken cancellationToken)
    {
        var result = await walletService.ListMyTransactionsAsync(this.CurrentUserId(), page, perPage, type, cancellationToken);
        return Ok(new { data = result.Data, meta = new { page = result.Page, total = result.Total, perPage = result.PerPage } });
    }

    [Authorize]
    [HttpGet("payments")]
    public async Task<IActionResult> ListPayments(
        [FromQuery] int? page,
        [FromQuery] int? perPage,
        CancellationToken cancellationToken)
    {
        var result = await walletService.ListMyPaymentsAsync(this.CurrentUserId(), page, perPage, cancellationToken);
        return Ok(new { data = result.Data, meta = new { page = result.Page, total = result.Total, perPage = result.PerPage } });
    }

    [Authorize]
    [HttpGet("payments/{id:guid}/invoice")]
    public async Task<IActionResult> GetInvoice(Guid id, CancellationToken cancellationToken)
        => Ok(new { data = await walletService.GetInvoiceUrlAsync(this.CurrentUserId(), id, cancellationToken) });

    [AllowAnonymous]
    [HttpGet("topup-options")]
    public async Task<IActionResult> GetTopupOptions(CancellationToken cancellationToken)
        => Ok(new { data = await walletService.GetTopupOptionsAsync(cancellationToken) });
}
