using System.Text.Json;
using PsikoAl.Common.Constants;
using PsikoAl.Common.Dtos.Expert;
using PsikoAl.Common.Dtos.Expert.Create;
using PsikoAl.Common.Dtos.Expert.Update;
using PsikoAl.Common.Exceptions;
using PsikoAl.Data.Entities;
using PsikoAl.Data.Repositories.Abstractions;
using PsikoAl.Services.Abstractions;
using PsikoAl.Services.Mapping;

namespace PsikoAl.Services;

public sealed class ExpertService(IUnitOfWork unitOfWork, ICategoryService categoryService) : IExpertService
{
    private static readonly JsonSerializerOptions RevisionJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ExpertDto> CreateProfileAsync(
        Guid userId,
        CreateExpertProfileDto request,
        CancellationToken cancellationToken)
    {
        var profile = await unitOfWork.Profiles.GetByIdAsync(userId, cancellationToken)
            ?? throw new DomainException(ErrorKeys.ProfileNotFound);

        if (profile.Role != ProfileRoles.Expert)
        {
            throw new DomainException(ErrorKeys.ExpertRoleRequired);
        }

        if (await unitOfWork.Experts.ExistsAsync(userId, cancellationToken))
        {
            throw new DomainException(ErrorKeys.ExpertProfileAlreadyExists);
        }

        await EnsureValidSpecializationsAsync(request.Specializations, cancellationToken);

        // cvUrl/certificates istekte gelse de yok sayılır: belgeler yalnız yükleme uçlarından
        // (POST /experts/profile/cv, /certificates) Storage yolu olarak yazılır. Serbest
        // metin hem admin imzalamasını bozuyordu hem de başka klasörü işaret edebiliyordu.
        var expert = new Expert
        {
            Id = userId,
            Title = request.Title,
            Specializations = [.. request.Specializations],
            ExperienceYears = request.ExperienceYears,
            Bio = request.Bio,
            Education = NullIfEmpty(request.Education),
            PersonalWebsite = NullIfEmpty(request.PersonalWebsite),
        };

        if (!string.IsNullOrWhiteSpace(request.AvatarUrl))
        {
            profile.AvatarUrl = request.AvatarUrl;
        }

        await unitOfWork.Experts.AddAsync(expert, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ExpertMapper.ToExpertDto(expert, profile, rating: 0, reviewCount: 0);
    }

    private async Task<ExpertDto> ToExpertDtoWithRatingAsync(Expert expert, Profile profile, CancellationToken cancellationToken)
    {
        var rating = await unitOfWork.Reviews.GetRatingAsync(expert.Id, cancellationToken);
        var reviewCount = await unitOfWork.Reviews.GetReviewCountAsync(expert.Id, cancellationToken);
        return ExpertMapper.ToExpertDto(expert, profile, rating, reviewCount);
    }

    public async Task<ExpertDto> UpdateProfileAsync(
        Guid userId,
        UpdateExpertProfileDto request,
        CancellationToken cancellationToken)
    {
        var expert = await unitOfWork.Experts.GetWithProfileAsync(userId, cancellationToken)
            ?? throw new DomainException(ErrorKeys.ExpertNotFound);
        var profile = expert.Profile ?? throw new DomainException(ErrorKeys.ProfileNotFound);

        if (request.Specializations is not null)
        {
            await EnsureValidSpecializationsAsync(request.Specializations, cancellationToken);
        }

        if (expert.Status == ExpertStatuses.Approved)
        {
            // Versiyonlu onay: yayındaki profil değişmez, revizyon admin onayına düşer.
            expert.PendingRevision = JsonSerializer.Serialize(
                MergeRevision(expert, request),
                RevisionJsonOptions);
        }
        else
        {
            ApplyUpdate(expert, request);
            expert.Status = ExpertStatuses.Pending;
            expert.RejectionReason = null;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await ToExpertDtoWithRatingAsync(expert, profile, cancellationToken);
    }

    public async Task<ExpertDto> GetByIdAsync(Guid expertId, Guid? viewerUserId, CancellationToken cancellationToken)
    {
        var expert = await unitOfWork.Experts.GetWithProfileAsync(expertId, cancellationToken)
            ?? throw new DomainException(ErrorKeys.ExpertNotFound);
        var profile = expert.Profile ?? throw new DomainException(ErrorKeys.ProfileNotFound);

        if (expert.Status != ExpertStatuses.Approved && viewerUserId != expertId)
        {
            throw new DomainException(ErrorKeys.ExpertNotFound);
        }

        await ExpertVisibility.EnsureVisibleAsync(unitOfWork, expertId, viewerUserId, cancellationToken);
        return await ToExpertDtoWithRatingAsync(expert, profile, cancellationToken);
    }

    internal static void ApplyUpdate(Expert expert, UpdateExpertProfileDto request)
    {
        if (request.Title is not null)
        {
            expert.Title = request.Title;
        }

        if (request.Specializations is not null)
        {
            expert.Specializations = [.. request.Specializations];
        }

        if (request.ExperienceYears.HasValue)
        {
            expert.ExperienceYears = request.ExperienceYears.Value;
        }

        if (request.Bio is not null)
        {
            expert.Bio = request.Bio;
        }

        if (request.Education is not null)
        {
            expert.Education = NullIfEmpty(request.Education);
        }

        if (request.PersonalWebsite is not null)
        {
            expert.PersonalWebsite = NullIfEmpty(request.PersonalWebsite);
        }
    }

    private static UpdateExpertProfileDto MergeRevision(Expert expert, UpdateExpertProfileDto request)
    {
        var existing = expert.PendingRevision is null
            ? null
            : JsonSerializer.Deserialize<UpdateExpertProfileDto>(expert.PendingRevision, RevisionJsonOptions);

        return new UpdateExpertProfileDto(
            request.Title ?? existing?.Title,
            request.Specializations ?? existing?.Specializations,
            request.ExperienceYears ?? existing?.ExperienceYears,
            request.Bio ?? existing?.Bio,
            request.Education ?? existing?.Education,
            CvUrl: null,
            Certificates: null,
            request.PersonalWebsite ?? existing?.PersonalWebsite);
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private async Task EnsureValidSpecializationsAsync(IReadOnlyList<string> specializations, CancellationToken cancellationToken)
    {
        if (!await categoryService.AllActiveNamesExistAsync(specializations, cancellationToken))
        {
            throw new DomainException(ErrorKeys.ValidationFailed, "specializations");
        }
    }
}
