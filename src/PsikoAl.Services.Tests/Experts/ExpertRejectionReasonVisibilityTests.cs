using PsikoAl.Common.Constants;
using PsikoAl.Data.Entities;
using PsikoAl.Services.Mapping;
using PsikoAl.Services.Tests.Support;

namespace PsikoAl.Services.Tests.Experts;

// Red gerekçesi uzmanın "neden reddedildim" sorusunun cevabı; ama admin ile uzman
// arasındaki bir not olduğu için başka bir izleyiciye (danışan, diğer uzman) gitmemeli.
public sealed class ExpertRejectionReasonVisibilityTests : IDisposable
{
    private const string Reason = "Belgeler eksik.";

    private readonly TestDatabase _db = new();

    [Fact]
    public async Task Rejected_expert_sees_the_reason_on_their_own_profile()
    {
        var expert = await AddExpertAsync(ExpertStatuses.Rejected);

        var dto = await Service().GetByIdAsync(expert.Id, expert.Id, CancellationToken.None);

        Assert.Equal(Reason, dto.RejectionReason);
    }

    [Fact]
    public async Task Other_viewers_never_receive_the_reason()
    {
        // Revizyon reddi: profil onaylı kalır ama RejectionReason dolu — herkese görünen tek durum.
        var expert = await AddExpertAsync(ExpertStatuses.Approved);
        var otherExpert = await _db.AddProfileAsync(role: ProfileRoles.Expert);

        var dto = await Service().GetByIdAsync(expert.Id, otherExpert.Id, CancellationToken.None);

        Assert.Null(dto.RejectionReason);
    }

    [Fact]
    public void Mapper_hides_the_reason_unless_it_is_the_own_profile()
    {
        var expert = new Expert { Id = Guid.NewGuid(), Title = "Psikolog", RejectionReason = Reason };
        var profile = new Profile { Id = expert.Id, Email = "uzman@example.test", FirstName = "A", LastName = "B" };

        Assert.Null(ExpertMapper.ToExpertDto(expert, profile, 0, 0, isOwnProfile: false).RejectionReason);
        Assert.Equal(Reason, ExpertMapper.ToExpertDto(expert, profile, 0, 0, isOwnProfile: true).RejectionReason);
    }

    public void Dispose() => _db.Dispose();

    private ExpertService Service() => new(_db.UnitOfWork, new AllCategoriesExistStub());

    private async Task<Expert> AddExpertAsync(string status)
    {
        var profile = await _db.AddProfileAsync(role: ProfileRoles.Expert);
        var expert = new Expert
        {
            Id = profile.Id,
            Title = "Psikolog",
            Bio = "bio",
            Status = status,
            RejectionReason = Reason,
        };
        _db.Context.Experts.Add(expert);
        await _db.Context.SaveChangesAsync();
        return expert;
    }
}
