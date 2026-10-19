using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PsikoAl.Api.Extensions;
using PsikoAl.Services.Abstractions;

namespace PsikoAl.Api.Controllers;

[ApiController]
[Authorize]
public sealed class ReferrerController(IProfileService profileService) : ControllerBase
{
    // REVIZE-2 §6: danışanı davet eden uzman ya da null.
    [HttpGet("/me/referrer")]
    public async Task<IActionResult> GetReferrer(CancellationToken cancellationToken)
        => Ok(new { data = await profileService.GetReferrerAsync(this.CurrentUserId(), cancellationToken) });
}
