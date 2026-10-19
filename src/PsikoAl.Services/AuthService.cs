using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PsikoAl.Common.Constants;
using PsikoAl.Common.Dtos.Auth;
using PsikoAl.Common.Dtos.Auth.Create;
using PsikoAl.Common.Exceptions;
using PsikoAl.Data.Entities;
using PsikoAl.Data.Repositories.Abstractions;
using PsikoAl.Services.Abstractions;
using PsikoAl.Services.Mapping;
using PsikoAl.Services.Options;

namespace PsikoAl.Services;

public sealed class AuthService(
    ISupabaseAuthService supabaseAuth,
    ISupabaseAdminService supabaseAdmin,
    IUnitOfWork unitOfWork,
    IOptions<LegalOptions> legalOptions) : IAuthService
{
    public async Task<LoginResponseDto> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken)
    {
        var session = await supabaseAuth.LoginAsync(request, cancellationToken);
        var profile = await GetActiveProfileAsync(session.UserId, cancellationToken);
        await RecordLoginAsync(profile.Id, cancellationToken);
        return new LoginResponseDto(ProfileMapper.ToAuthUserDto(profile), session.Tokens);
    }

    // Admin "Kullanıcı işlem kaydı"ndaki giriş olayları (REVIZE-2 §9). Yalnız parola ile
    // giriş yazılır; token yenileme (refresh) bir giriş değildir.
    private async Task RecordLoginAsync(Guid userId, CancellationToken cancellationToken)
    {
        await unitOfWork.AuditLogs.AddAsync(
            new AuditLog
            {
                ActorType = AuditActorTypes.User,
                Action = AuditActions.UserLogin,
                EntityType = "profile",
                EntityId = userId.ToString(),
            },
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<LoginResponseDto> RegisterAsync(RegisterRequestDto request, CancellationToken cancellationToken)
    {
        // Signup yerine admin-create (email_confirm) + login: e-posta doğrulama beklemeden
        // frontend'in beklediği session'ı döndürür ve signup e-posta rate limitine takılmaz.
        var userId = await supabaseAdmin.CreateConfirmedUserAsync(
            request.Email,
            request.Password,
            request.FirstName,
            request.LastName,
            request.Role,
            cancellationToken);

        await RecordConsentAsync(userId, request.ReferralCode, cancellationToken);

        return await LoginAsync(new LoginRequestDto(request.Email, request.Password), cancellationToken);
    }

    public async Task ChangePasswordAsync(
        Guid userId,
        string email,
        ChangePasswordRequestDto request,
        CancellationToken cancellationToken)
    {
        if (request.NewPassword != request.ConfirmPassword)
        {
            throw new DomainException(ErrorKeys.ValidationFailed, "confirmPassword");
        }

        // Mevcut şifre, login denemesiyle doğrulanıyor. Hatayı olduğu gibi bırakmak
        // kullanıcıya şifre DEĞİŞTİRME formunda "E-posta veya şifre hatalı" derdi —
        // oysa o formda e-posta alanı yok. Kullanıcı zaten oturum açmış olduğu için
        // hangi alanın hatalı olduğunu söylemek bir bilgi sızıntısı da değil.
        try
        {
            await supabaseAuth.LoginAsync(
                new LoginRequestDto(email, request.CurrentPassword),
                cancellationToken);
        }
        catch (DomainException exception) when (exception.ErrorKey == ErrorKeys.AuthInvalidCredentials)
        {
            throw new DomainException(ErrorKeys.AuthCurrentPasswordInvalid, "currentPassword");
        }

        await supabaseAdmin.SetPasswordAsync(userId, request.NewPassword, cancellationToken);
    }

    public async Task ForgotPasswordAsync(ForgotPasswordRequestDto request, CancellationToken cancellationToken)
    {
        // Hata durumunda da sessiz kalınır: yanıt üzerinden kayıtlı e-posta taraması yapılamamalı.
        try
        {
            await supabaseAuth.SendPasswordRecoveryAsync(request.Email, cancellationToken);
        }
        catch (HttpRequestException)
        {
        }
    }

    // KVKK ispat yükü veri sorumlusundadır: rızanın ne zaman ve hangi metin sürümüne
    // verildiği profilde saklanır. Profil satırını auth.users trigger'ı açıyor.
    //
    // Davet kodu (REVIZE-2 §7) aynı kayıtta işlenir: geçersiz ya da bilinmeyen kod sessizce
    // yok sayılır (kayıt bir davet kodu yüzünden başarısız olmamalı).
    private async Task RecordConsentAsync(Guid userId, string? referralCode, CancellationToken cancellationToken)
    {
        var profile = await unitOfWork.Profiles.GetByIdAsync(userId, cancellationToken)
            ?? throw new DomainException(ErrorKeys.ProfileNotFound);

        var acceptedAt = DateTimeOffset.UtcNow;
        profile.TermsAcceptedAt = acceptedAt;
        profile.HealthDataConsentAt = acceptedAt;
        profile.ConsentVersion = legalOptions.Value.ConsentVersion;
        profile.ReferredByExpertId = await ResolveReferrerAsync(userId, referralCode, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<Guid?> ResolveReferrerAsync(Guid userId, string? referralCode, CancellationToken cancellationToken)
    {
        var code = referralCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code) || code.Length > ReferralCodeMaxLength)
        {
            return null;
        }

        var expertId = await unitOfWork.Experts.Query()
            .Where(expert => expert.ReferralCode == code)
            .Select(expert => (Guid?)expert.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return expertId == userId ? null : expertId;
    }

    private const int ReferralCodeMaxLength = 32;

    private async Task<Profile> GetActiveProfileAsync(Guid userId, CancellationToken cancellationToken)
    {
        var profile = await unitOfWork.Profiles.GetByIdAsync(userId, cancellationToken)
            ?? throw new DomainException(ErrorKeys.ProfileNotFound);

        if (profile.Status != ProfileStatuses.Active)
        {
            throw new DomainException(ErrorKeys.AuthAccountFrozen);
        }

        return profile;
    }
}
