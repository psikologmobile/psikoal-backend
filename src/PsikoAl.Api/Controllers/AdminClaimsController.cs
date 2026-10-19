using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PsikoAl.Api.Authorization;
using PsikoAl.Api.Extensions;
using PsikoAl.Common.Dtos.Admin;
using PsikoAl.Services.Abstractions;

namespace PsikoAl.Api.Controllers;

// "Anlaşma bildirimleri" bir uyuşmazlık incelemesidir — moderasyon rolü.
[ApiController]
[Route("admin/claims")]
[Authorize(Policy = AdminPolicies.Moderation)]
public sealed class AdminClaimsController(IAdminClaimService adminClaimService) : ControllerBase
{
    [HttpGet]
    public Task<AdminPagedResultDto<AdminClaimDto>> List([FromQuery] AdminClaimQueryDto query, CancellationToken cancellationToken)
        => adminClaimService.ListAsync(query, cancellationToken);

    [HttpPost("{id:guid}/resolve")]
    public async Task<IActionResult> Resolve(Guid id, AdminResolveClaimDto body, CancellationToken cancellationToken)
    {
        await adminClaimService.ResolveAsync(this.CurrentUserId(), id, body.Note, cancellationToken);
        return Ok(new { success = true });
    }
}
