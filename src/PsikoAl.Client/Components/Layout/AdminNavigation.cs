using PsikoAl.Common.Constants;

namespace PsikoAl.Client.Components.Layout;

public sealed record AdminNavItem(string Href, string Label, string Icon, IReadOnlyList<string> AllowedRoles);

public static class AdminNavigation
{
    public static IReadOnlyList<AdminNavItem> Items { get; } =
    [
        new("/", "Genel Bakış", "dashboard", AdminRoleGroups.Any),
        new("/users", "Kullanıcılar", "users", AdminRoleGroups.Moderation),
        new("/experts", "Uzmanlar", "expert", AdminRoleGroups.Moderation),
        new("/listings", "İlanlar", "listing", AdminRoleGroups.Moderation),
        new("/reviews", "Yorumlar", "review", AdminRoleGroups.Moderation),
        new("/matches", "Eşleşmeler", "match", AdminRoleGroups.Moderation),
        new("/categories", "Kategoriler", "category", AdminRoleGroups.Content),
        new("/assessments", "Testler", "assessment", AdminRoleGroups.Content),
        new("/blogs", "Blog", "blog", AdminRoleGroups.Content),
        new("/notifications", "Bildirimler", "notification", AdminRoleGroups.SuperAdminOnly),
        new("/settings", "Ayarlar", "settings", AdminRoleGroups.SuperAdminOnly),
        new("/audit-logs", "Denetim Kaydı", "audit", AdminRoleGroups.SuperAdminOnly),
    ];

    public static AdminNavItem? Find(string relativePath)
    {
        var normalized = "/" + relativePath.Trim('/');
        return Items.FirstOrDefault(item => string.Equals(item.Href, normalized, StringComparison.OrdinalIgnoreCase));
    }

    public static string TitleFor(string relativePath) => Find(relativePath)?.Label ?? "PsikoAl Admin";
}
