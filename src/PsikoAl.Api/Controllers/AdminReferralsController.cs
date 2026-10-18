using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PsikoAl.Api.Authorization;
using PsikoAl.Common.Dtos.Admin;
using PsikoAl.Services.Abstractions;

namespace PsikoAl.Api.Controllers;

// Davetler: ödül (ücretsiz teklif hakkı) muhasebesi — finans rolü.
[ApiController]
[Route("admin/referrals")]
[Authorize(Policy = AdminPolicies.Finance)]
public sealed class AdminReferralsController(IAdminReferralService adminReferralService) : ControllerBase
{
    [HttpGet]
    public Task<AdminPagedResultDto<AdminReferralDto>> List([FromQuery] AdminReferralQueryDto query, CancellationToken cancellationToken)
        => adminReferralService.ListAsync(query, cancellationToken);
}
