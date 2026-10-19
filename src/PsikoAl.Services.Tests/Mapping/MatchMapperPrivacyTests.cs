using PsikoAl.Data.Entities;
using PsikoAl.Services.Mapping;

namespace PsikoAl.Services.Tests.Mapping;

// Sözleşme kilidi: docs/REVIZE-2.md §5 — "Eşleşme detayında iki taraf da karşı tarafın
// phone ve email'ini görür (uzman profilindeki telefon, auth e-postası)".
//
// Eskiden profildeki "İletişim Paylaşımı" anahtarları eşleşmedeki iletişimi süzüyordu.
// Revize 2 ile danışan iletişim paylaşımına İLAN AÇARKEN açık rıza veriyor ve uzman bu
// bilgiyi teklif gönderdiği anda zaten görüyor; eşleşmede saklamak tutarsız olurdu. Bu
// testler, eski süzmenin geri gelip eşleşmiş iki tarafı birbirine ulaşamaz bırakmasını
// engelliyor.
public sealed class MatchMapperPrivacyTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ToMatchDto_RevealsClientContactRegardlessOfShareFlags(bool shareEmail, bool sharePhone)
    {
        var dto = MatchMapper.ToMatchDto(CreateMatch(shareEmail, sharePhone));

        Assert.Equal("danisan@psikoal.test", dto.Client?.Email);
        Assert.Equal("+905551112233", dto.Client?.Phone);
    }

    [Fact]
    public void ToMatchDto_AlwaysRevealsTheClientFullName()
    {
        var dto = MatchMapper.ToMatchDto(CreateMatch(shareEmail: false, sharePhone: false));

        Assert.Equal("Ayşe Yılmaz", dto.Client?.FullName);
        Assert.Equal("AY", dto.Client?.Initials);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ToMatchDto_RevealsExpertPhoneAndAccountEmail(bool shareEmail, bool sharePhone)
    {
        var match = CreateMatch(shareEmail: false, sharePhone: false);
        match.Expert = new Expert
        {
            Id = match.ExpertId,
            Title = "Klinik Psikolog",
            Profile = new Profile
            {
                Id = match.ExpertId,
                FirstName = "Deniz",
                LastName = "Kaya",
                Email = "uzman@psikoal.test",
                Phone = "+905559998877",
                Role = "expert",
                ShareEmail = shareEmail,
                SharePhone = sharePhone,
                CreatedAt = DateTimeOffset.UtcNow,
            },
        };

        var dto = MatchMapper.ToMatchDto(match);

        Assert.Equal("Deniz Kaya", dto.Expert?.Name);
        Assert.Equal("uzman@psikoal.test", dto.Expert?.Email);
        Assert.Equal("+905559998877", dto.Expert?.Phone);
    }

    private static Match CreateMatch(bool shareEmail, bool sharePhone)
    {
        var clientId = Guid.NewGuid();

        return new Match
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            ExpertId = Guid.NewGuid(),
            ListingId = Guid.NewGuid(),
            Status = "ACTIVE",
            CreatedAt = DateTimeOffset.UtcNow,
            Client = new Profile
            {
                Id = clientId,
                FirstName = "Ayşe",
                LastName = "Yılmaz",
                Email = "danisan@psikoal.test",
                Phone = "+905551112233",
                Role = "client",
                ShareEmail = shareEmail,
                SharePhone = sharePhone,
                CreatedAt = DateTimeOffset.UtcNow,
            },
        };
    }
}
