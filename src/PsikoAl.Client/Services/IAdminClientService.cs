using PsikoAl.Common.Dtos.Admin;
using PsikoAl.Common.Dtos.Blog.Create;
using PsikoAl.Common.Dtos.Blog.Update;
using PsikoAl.Common.Dtos.Category;
using PsikoAl.Common.Dtos.Category.Create;
using PsikoAl.Common.Dtos.Category.Update;

namespace PsikoAl.Client.Services;

public interface IAdminClientService
{
    Task<AdminApiResult<AdminDashboardStatsDto>> GetDashboardStatsAsync(CancellationToken cancellationToken);

    Task<AdminApiResult<AdminPagedResultDto<AdminUserListItemDto>>> ListUsersAsync(
        AdminUserListQueryDto query,
        CancellationToken cancellationToken);

    Task<AdminApiResult> FreezeUserAsync(Guid userId, string? reason, CancellationToken cancellationToken);

    Task<AdminApiResult> UnfreezeUserAsync(Guid userId, string? reason, CancellationToken cancellationToken);

    Task<AdminApiResult<IReadOnlyList<AdminExpertListItemDto>>> ListExpertsAsync(string? status, CancellationToken cancellationToken);

    Task<AdminApiResult<AdminExpertDetailDto>> GetExpertDetailAsync(Guid expertId, CancellationToken cancellationToken);

    Task<AdminApiResult> ApproveExpertAsync(Guid expertId, CancellationToken cancellationToken);

    Task<AdminApiResult> RejectExpertAsync(Guid expertId, string reason, CancellationToken cancellationToken);

    Task<AdminApiResult> SetExpertVerifiedAsync(Guid expertId, bool isVerified, CancellationToken cancellationToken);

    Task<AdminApiResult<AdminExpertDocumentUrls>> GetExpertDocumentUrlsAsync(Guid expertId, CancellationToken cancellationToken);

    Task<AdminApiResult<IReadOnlyList<AdminCategoryListItemDto>>> ListCategoriesAsync(CancellationToken cancellationToken);

    Task<AdminApiResult<CategoryDto>> CreateCategoryAsync(CreateCategoryDto request, CancellationToken cancellationToken);

    Task<AdminApiResult<CategoryDto>> UpdateCategoryAsync(Guid categoryId, UpdateCategoryDto request, CancellationToken cancellationToken);

    Task<AdminApiResult<IReadOnlyList<AdminReviewListItemDto>>> ListReviewsAsync(string? status, CancellationToken cancellationToken);

    Task<AdminApiResult> ApproveReviewAsync(Guid reviewId, CancellationToken cancellationToken);

    Task<AdminApiResult> RejectReviewAsync(Guid reviewId, string reason, CancellationToken cancellationToken);

    Task<AdminApiResult<AdminPagedResultDto<AdminListingListItemDto>>> ListListingsAsync(
        AdminListingListQueryDto query,
        CancellationToken cancellationToken);

    Task<AdminApiResult<AdminListingDetailDto>> GetListingDetailAsync(Guid listingId, CancellationToken cancellationToken);

    Task<AdminApiResult> ApproveListingAsync(Guid listingId, CancellationToken cancellationToken);

    Task<AdminApiResult> RejectListingAsync(Guid listingId, string reason, CancellationToken cancellationToken);

    Task<AdminApiResult> CloseListingAsync(Guid listingId, CancellationToken cancellationToken);

    Task<AdminApiResult> ExtendListingAsync(Guid listingId, int additionalDays, CancellationToken cancellationToken);

    Task<AdminApiResult> ReopenListingAsync(Guid listingId, string reason, CancellationToken cancellationToken);

    Task<AdminApiResult<IReadOnlyList<SystemSettingDto>>> ListSystemSettingsAsync(CancellationToken cancellationToken);

    Task<AdminApiResult> UpdateSystemSettingAsync(string key, string value, CancellationToken cancellationToken);

    Task<AdminApiResult<IReadOnlyList<AdminMatchListItemDto>>> ListMatchesAsync(string? status, CancellationToken cancellationToken);

    Task<AdminApiResult<AdminMatchDetailDto>> GetMatchDetailAsync(Guid matchId, CancellationToken cancellationToken);

    Task<AdminApiResult> ForceReleaseMatchAsync(Guid matchId, string targetStatus, string reason, CancellationToken cancellationToken);

    Task<AdminApiResult<IReadOnlyList<AdminNotificationTemplateDto>>> ListNotificationTemplatesAsync(CancellationToken cancellationToken);

    Task<AdminApiResult<AdminNotificationTemplateDto>> UpdateNotificationTemplateAsync(
        string type,
        UpdateAdminNotificationTemplateDto request,
        CancellationToken cancellationToken);

    Task<AdminApiResult<AdminNotificationPreviewDto>> PreviewNotificationAsync(
        AdminSendNotificationDto request,
        CancellationToken cancellationToken);

    Task<AdminApiResult<int>> SendNotificationAsync(AdminSendNotificationDto request, CancellationToken cancellationToken);

    Task<AdminApiResult<IReadOnlyList<AdminAssessmentListItemDto>>> ListAssessmentsAsync(CancellationToken cancellationToken);

    Task<AdminApiResult<AdminAssessmentDetailDto>> GetAssessmentDetailAsync(Guid assessmentId, CancellationToken cancellationToken);

    Task<AdminApiResult> UpdateAssessmentAsync(Guid assessmentId, UpdateAdminAssessmentDto request, CancellationToken cancellationToken);

    Task<AdminApiResult> UpdateScoreRuleAsync(Guid scoreRuleId, UpdateAdminScoreRuleDto request, CancellationToken cancellationToken);

    Task<AdminApiResult<AdminPagedResultDto<AdminAuditLogDto>>> ListAuditLogsAsync(
        AdminAuditLogQueryDto query,
        CancellationToken cancellationToken);

    Task<AdminApiResult<IReadOnlyList<AdminBlogListItemDto>>> ListBlogsAsync(string? status, CancellationToken cancellationToken);

    Task<AdminApiResult<AdminBlogDetailDto>> GetBlogDetailAsync(Guid blogId, CancellationToken cancellationToken);

    Task<AdminApiResult<AdminBlogDetailDto>> CreateBlogAsync(CreateBlogDto request, CancellationToken cancellationToken);

    Task<AdminApiResult<AdminBlogDetailDto>> UpdateBlogAsync(Guid blogId, UpdateBlogDto request, CancellationToken cancellationToken);

    Task<AdminApiResult<AdminBlogDetailDto>> PublishBlogAsync(Guid blogId, CancellationToken cancellationToken);

    Task<AdminApiResult<AdminBlogDetailDto>> UnpublishBlogAsync(Guid blogId, CancellationToken cancellationToken);

    Task<AdminApiResult<AdminBlogDetailDto>> ScheduleBlogAsync(Guid blogId, DateTimeOffset publishAt, CancellationToken cancellationToken);

    Task<AdminApiResult<AdminBlogDetailDto>> UploadBlogCoverAsync(
        Guid blogId,
        byte[] content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken);

    Task<AdminApiResult> DeleteBlogAsync(Guid blogId, CancellationToken cancellationToken);
}

public sealed record AdminExpertDocumentUrls(string? CvUrl, IReadOnlyList<string> CertificateUrls);
