using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PsikoAl.Api.Extensions;
using PsikoAl.Common.Dtos.Claim;
using PsikoAl.Common.Dtos.Listing;
using PsikoAl.Common.Dtos.Listing.Create;
using PsikoAl.Services.Abstractions;

namespace PsikoAl.Api.Controllers;

[ApiController]
[Route("listings")]
[Authorize]
public sealed class ListingsController(
    IListingService listingService,
    IOfferService offerService,
    IListingClaimService claimService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> ListFeed(
        [FromQuery] string[]? specialization,
        [FromQuery] string[]? sessionType,
        [FromQuery] decimal? budgetMin,
        [FromQuery] decimal? budgetMax,
        [FromQuery] string? sort,
        [FromQuery] string? city,
        [FromQuery] bool? invitedOnly,
        CancellationToken cancellationToken)
    {
        var filters = new ListingFeedFilters(specialization, sessionType, budgetMin, budgetMax, sort, city, invitedOnly == true);
        var result = await listingService.ListFeedAsync(this.CurrentUserId(), filters, cancellationToken);
        return Ok(new { data = result.Data, meta = new { page = 1, total = result.Total, perPage = result.Total } });
    }

    [HttpGet("rules")]
    public async Task<IActionResult> GetRules(CancellationToken cancellationToken)
        => Ok(new { data = await listingService.GetRulesAsync(cancellationToken) });

    [HttpGet("my")]
    public async Task<IActionResult> ListMy([FromQuery] string? status, CancellationToken cancellationToken)
    {
        var result = await listingService.ListMyAsync(this.CurrentUserId(), status, cancellationToken);
        return Ok(new
        {
            data = result.Data,
            meta = new { page = 1, total = result.Total, perPage = result.Total, activeCount = result.ActiveCount },
        });
    }

    [HttpGet("{id:guid}")]
    public Task<ListingDto> GetById(Guid id, CancellationToken cancellationToken)
        => listingService.GetByIdAsync(id, this.CurrentUserId(), cancellationToken);

    [HttpPost]
    public Task<ListingDto> Create(CreateListingDto request, CancellationToken cancellationToken)
        => listingService.CreateAsync(this.CurrentUserId(), request, cancellationToken);

    [HttpPost("{id:guid}/close")]
    public Task<ListingDto> Close(Guid id, CancellationToken cancellationToken)
        => listingService.CloseAsync(this.CurrentUserId(), id, cancellationToken);

    // REVIZE-2 §3.4: danışan ilanını "X uzmanla eşleşti" işaretler (tek transaction).
    [HttpPost("{id:guid}/match")]
    public async Task<IActionResult> MarkMatched(Guid id, MarkMatchedDto request, CancellationToken cancellationToken)
        => Ok(new { data = await listingService.MarkMatchedAsync(this.CurrentUserId(), id, request.OfferId, cancellationToken) });

    // REVIZE-2 §3.5: uzmanın "Danışanla anlaştım" bildirimi. Gövde isteğe bağlı.
    [HttpPost("{id:guid}/claims")]
    public async Task<IActionResult> CreateClaim(
        Guid id,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] CreateClaimDto? request,
        CancellationToken cancellationToken)
    {
        var claim = await claimService.CreateAsync(this.CurrentUserId(), id, request?.Note, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, new { data = claim });
    }

    // Danışanın "Anlaşmadım" yanıtı.
    [HttpPost("{id:guid}/claims/{expertId:guid}/dismiss")]
    public async Task<IActionResult> DismissClaim(Guid id, Guid expertId, CancellationToken cancellationToken)
        => Ok(new { data = await claimService.DismissAsync(this.CurrentUserId(), id, expertId, cancellationToken) });

    [HttpGet("{id:guid}/offers")]
    public async Task<IActionResult> ListOffers(Guid id, CancellationToken cancellationToken)
    {
        var result = await offerService.ListForListingAsync(id, this.CurrentUserId(), cancellationToken);
        return Ok(new { data = result.Data, meta = new { page = 1, total = result.Total, perPage = result.Total } });
    }
}
