using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PsikoAl.Api.Authorization;
using PsikoAl.Api.Extensions;
using PsikoAl.Common.Dtos.Admin;
using PsikoAl.Services.Abstractions;

namespace PsikoAl.Api.Controllers;

[ApiController]
[Route("admin/users")]
[Authorize(Policy = AdminPolicies.Moderation)]
public sealed class AdminUsersController(
    IAdminUserService adminUserService,
    IAdminActivityService adminActivityService) : ControllerBase
{
    // REVIZE-2 §9: kişi bazlı işlem kaydı, en yeni önce. kind: ActivityKinds (virgülle birden çok).
    [HttpGet("{id:guid}/activity")]
    public Task<AdminPagedResultDto<AdminActivityItemDto>> Activity(
        Guid id,
        [FromQuery] AdminActivityQueryDto query,
        CancellationToken cancellationToken)
        => adminActivityService.ListAsync(id, query, cancellationToken);

    [HttpGet]
    public Task<AdminPagedResultDto<AdminUserListItemDto>> List(
        [FromQuery] AdminUserListQueryDto query,
        CancellationToken cancellationToken)
        => adminUserService.ListUsersAsync(query, cancellationToken);

    [HttpPost("{id:guid}/freeze")]
    public async Task<IActionResult> Freeze(Guid id, [FromBody] AdminActionReasonDto? body, CancellationToken cancellationToken)
    {
        await adminUserService.FreezeUserAsync(this.CurrentUserId(), id, body?.Reason, cancellationToken);
        return Ok(new { success = true });
    }

    [HttpPost("{id:guid}/unfreeze")]
    public async Task<IActionResult> Unfreeze(Guid id, [FromBody] AdminActionReasonDto? body, CancellationToken cancellationToken)
    {
        await adminUserService.UnfreezeUserAsync(this.CurrentUserId(), id, body?.Reason, cancellationToken);
        return Ok(new { success = true });
    }
}
