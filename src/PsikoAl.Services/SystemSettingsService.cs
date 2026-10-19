using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PsikoAl.Common.Constants;
using PsikoAl.Common.Dtos.Admin;
using PsikoAl.Common.Exceptions;
using PsikoAl.Data.Entities;
using PsikoAl.Data.Repositories.Abstractions;
using PsikoAl.Services.Abstractions;
using PsikoAl.Services.Wallet;

namespace PsikoAl.Services;

public sealed class SystemSettingsService(
    IUnitOfWork unitOfWork,
    IAdminGuard adminGuard) : ISystemSettingsService
{
    public async Task<IReadOnlyList<SystemSettingDto>> ListAsync(CancellationToken cancellationToken)
        => await unitOfWork.SystemSettings.Query()
            .OrderBy(setting => setting.Key)
            .Select(setting => new SystemSettingDto(setting.Key, setting.Value, setting.Description, setting.UpdatedAt))
            .ToListAsync(cancellationToken);

    public async Task<SystemSettingDto> UpdateAsync(
        Guid actorAuthUserId,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        var actor = await adminGuard.GetActiveAdminAsync(actorAuthUserId, cancellationToken)
            ?? throw new DomainException(ErrorKeys.AdminUserNotFound);

        var setting = await unitOfWork.SystemSettings.GetAsync(key, cancellationToken)
            ?? throw new DomainException(ErrorKeys.SystemSettingNotFound, key);

        EnsureValidValue(key, value);

        var oldValue = setting.Value;
        setting.Value = value;
        setting.UpdatedBy = actor.Id;
        setting.UpdatedAt = DateTimeOffset.UtcNow;

        await unitOfWork.AuditLogs.AddAsync(
            new AuditLog
            {
                AdminUserId = actor.Id,
                ActorType = AuditActorTypes.Admin,
                Action = "admin.system_setting_update",
                EntityType = "system_setting",
                EntityId = key,
                OldValue = JsonSerializer.Serialize(new { value = oldValue }),
                NewValue = JsonSerializer.Serialize(new { value }),
            },
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new SystemSettingDto(setting.Key, setting.Value, setting.Description, setting.UpdatedAt);
    }

    // Revize 2 ayarları tipli doğrulanır: bozuk bir değer akışta sessizce varsayılana
    // düşerdi (SettingValues) ve panelde girilenle uygulanan farklı olurdu.
    internal static void EnsureValidValue(string key, string value)
    {
        var valid = key switch
        {
            SystemSettingKeys.TopupPresetsTry => SettingValues.TryParsePresets(value, out _),
            SystemSettingKeys.TopupMinTry or SystemSettingKeys.TopupMaxTry
                => SettingValues.TryParseNonNegativeLong(value, out var amount) && amount is > 0 and <= 1_000_000,
            SystemSettingKeys.WelcomeFreeRights or SystemSettingKeys.ReferralFreeRights
                => SettingValues.TryParseNonNegativeLong(value, out var rights) && rights <= WalletFieldLimits.AdminMaxFreeRights,
            SystemSettingKeys.ClaimAdminReviewDays
                => SettingValues.TryParseNonNegativeLong(value, out var days) && days <= 365,
            SystemSettingKeys.ClaimsEnabled => bool.TryParse(value, out _),
            SystemSettingKeys.VatNote => !string.IsNullOrWhiteSpace(value) && value.Length <= 100,
            _ => true,
        };

        if (!valid)
        {
            throw new DomainException(ErrorKeys.ValidationFailed, "value");
        }
    }
}
