using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PsikoAl.Api.Authorization;
using PsikoAl.Api.Extensions;
using PsikoAl.Common.Dtos.Admin;
using PsikoAl.Services.Abstractions;

namespace PsikoAl.Api.Controllers;

// Fiyatlandırma dönemleri (REVIZE-2 §1, §9): para ile ilgili ayar — finans rolü.
[ApiController]
[Route("admin/pricing-periods")]
[Authorize(Policy = AdminPolicies.Finance)]
public sealed class AdminPricingPeriodsController(IAdminPricingService pricingService) : ControllerBase
{
    [HttpGet]
    public Task<AdminPagedResultDto<AdminPricingPeriodDto>> List(
        [FromQuery] int? page,
        [FromQuery] int? perPage,
        CancellationToken cancellationToken)
        => pricingService.ListAsync(page, perPage, cancellationToken);

    [HttpPost]
    public Task<AdminPricingPeriodDto> Create(CreatePricingPeriodDto request, CancellationToken cancellationToken)
        => pricingService.CreateAsync(this.CurrentUserId(), request, cancellationToken);

    [HttpPost("preview")]
    public Task<PricingPreviewResultDto> Preview(PricingPreviewRequestDto request, CancellationToken cancellationToken)
        => pricingService.PreviewAsync(request, cancellationToken);
}
