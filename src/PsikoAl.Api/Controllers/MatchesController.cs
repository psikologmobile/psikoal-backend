using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PsikoAl.Api.Extensions;
using PsikoAl.Common.Dtos.Match;
using PsikoAl.Services.Abstractions;

namespace PsikoAl.Api.Controllers;

[ApiController]
[Authorize]
public sealed class MatchesController(IMatchService matchService) : ControllerBase
{
    [HttpGet("/match/active")]
    public Task<MatchDto?> GetActive(CancellationToken cancellationToken)
        => matchService.GetActiveAsync(this.CurrentUserId(), cancellationToken);

    [HttpGet("/matches")]
    public async Task<IActionResult> ListMy([FromQuery] string[]? status, CancellationToken cancellationToken)
    {
        var result = await matchService.ListMyAsync(this.CurrentUserId(), status, cancellationToken);
        return Ok(new
        {
            data = result.Data,
            meta = new
            {
                page = 1,
                total = result.Total,
                perPage = result.Total,
                activeCount = result.ActiveCount,
                pastCount = result.PastCount,
            },
        });
    }

    [HttpGet("/matches/{id:guid}")]
    public Task<MatchDto> GetById(Guid id, CancellationToken cancellationToken)
        => matchService.GetByIdAsync(id, this.CurrentUserId(), cancellationToken);
}
