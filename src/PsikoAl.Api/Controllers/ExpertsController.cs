using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PsikoAl.Api.Extensions;
using PsikoAl.Common.Constants;
using PsikoAl.Common.Dtos.Expert;
using PsikoAl.Common.Dtos.Expert.Create;
using PsikoAl.Common.Dtos.Expert.Update;
using PsikoAl.Common.Dtos.Review;
using PsikoAl.Common.Dtos.Review.Create;
using PsikoAl.Common.Exceptions;
using PsikoAl.Services.Abstractions;

namespace PsikoAl.Api.Controllers;

[ApiController]
[Route("experts")]
public sealed class ExpertsController(
    IExpertService expertService,
    IUploadService uploadService,
    IReviewService reviewService) : ControllerBase
{
    [Authorize]
    [HttpPost("profile")]
    public Task<ExpertDto> CreateProfile(CreateExpertProfileDto request, CancellationToken cancellationToken)
        => expertService.CreateProfileAsync(this.CurrentUserId(), request, cancellationToken);

    [Authorize]
    [HttpPatch("profile")]
    public Task<ExpertDto> UpdateProfile(UpdateExpertProfileDto request, CancellationToken cancellationToken)
        => expertService.UpdateProfileAsync(this.CurrentUserId(), request, cancellationToken);

    [Authorize]
    [HttpGet("{id:guid}")]
    public Task<ExpertDto> GetById(Guid id, CancellationToken cancellationToken)
        => expertService.GetByIdAsync(id, this.CurrentUserId(), cancellationToken);

    [Authorize]
    [HttpPost("profile/cv")]
    [EnableRateLimiting(RateLimitPolicies.Upload)]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadCv(IFormFile file, CancellationToken cancellationToken)
    {
        await using var content = OpenValidatedStream(file);
        var cvUrl = await uploadService.UploadCvAsync(this.CurrentUserId(), content, file.ContentType, cancellationToken);
        return Ok(new { cvUrl });
    }

    [Authorize]
    [HttpPost("profile/certificates")]
    [EnableRateLimiting(RateLimitPolicies.Upload)]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadCertificate(IFormFile file, CancellationToken cancellationToken)
    {
        await using var content = OpenValidatedStream(file);
        var certificates = await uploadService.UploadCertificateAsync(this.CurrentUserId(), content, file.ContentType, cancellationToken);
        return Ok(new { certificates });
    }

    // Sertifika, ExpertDto.certificates'te dönen değerin (Storage yolu) aynısıyla seçilir.
    // DELETE gövdesini bazı istemci/proxy'ler düşürdüğü için değer sorgu parametresinde gelir.
    [Authorize]
    [HttpDelete("profile/certificates")]
    [EnableRateLimiting(RateLimitPolicies.Upload)]
    public async Task<IActionResult> DeleteCertificate([FromQuery] string? certificate, CancellationToken cancellationToken)
    {
        var certificates = await uploadService.DeleteCertificateAsync(this.CurrentUserId(), certificate, cancellationToken);
        return Ok(new { certificates });
    }

    [Authorize]
    [HttpGet("{id:guid}/reviews")]
    public Task<IReadOnlyList<ReviewDto>> GetReviews(Guid id, CancellationToken cancellationToken)
        => reviewService.ListApprovedForExpertAsync(id, this.CurrentUserId(), cancellationToken);

    [Authorize]
    [HttpPost("{id:guid}/reviews")]
    public Task<ReviewDto> CreateReview(Guid id, CreateReviewDto request, CancellationToken cancellationToken)
        => reviewService.CreateAsync(this.CurrentUserId(), id, request, cancellationToken);

    private static Stream OpenValidatedStream(IFormFile? file)
        => file is null or { Length: 0 }
            ? throw new DomainException(ErrorKeys.ValidationFailed, "file")
            : file.OpenReadStream();
}
