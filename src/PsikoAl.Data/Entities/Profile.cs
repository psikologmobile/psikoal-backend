using PsikoAl.Common.Constants;

namespace PsikoAl.Data.Entities;

public sealed class Profile
{
    public Guid Id { get; set; }

    public required string Email { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string Role { get; set; } = ProfileRoles.Client;

    public bool IsVerified { get; set; }

    public string? AvatarUrl { get; set; }

    public string? Phone { get; set; }

    public string? City { get; set; }

    public bool ShareEmail { get; set; }

    public bool SharePhone { get; set; }

    public bool ShareLocation { get; set; }

    public string Status { get; set; } = ProfileStatuses.Active;

    public DateTimeOffset? TermsAcceptedAt { get; set; }

    public DateTimeOffset? HealthDataConsentAt { get; set; }

    public string? ConsentVersion { get; set; }

    public Guid? ReferredByExpertId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
