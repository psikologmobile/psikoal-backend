using System.Net;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PsikoAl.Api.Authorization;
using PsikoAl.Common.Constants;
using PsikoAl.Common.Dtos.Admin;
using PsikoAl.Data.Entities;
using PsikoAl.Services.Abstractions;

namespace PsikoAl.Api.ContractTests;

public sealed class AdminAuthorizationContractTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // Rol → yetki haritası burada sabitlenir. Bir controller'ın policy'si değişirse
    // bu tablo da bilinçli olarak güncellenmeli; sessiz yetki genişlemesi kırmızı olur.
    public static TheoryData<string, string> ExpectedPolicies => new()
    {
        { "AdminMeController", AdminPolicies.Any },
        { "AdminDashboardController", AdminPolicies.Any },
        { "AdminUsersController", AdminPolicies.Moderation },
        { "AdminExpertsController", AdminPolicies.Moderation },
        { "AdminListingsController", AdminPolicies.Moderation },
        { "AdminReviewsController", AdminPolicies.Moderation },
        { "AdminMatchesController", AdminPolicies.Moderation },
        { "AdminCategoriesController", AdminPolicies.Content },
        { "AdminAssessmentsController", AdminPolicies.Content },
        { "AdminBlogsController", AdminPolicies.Content },
        { "AdminNotificationsController", AdminPolicies.SuperAdmin },
        { "AdminNotificationTemplatesController", AdminPolicies.SuperAdmin },
        { "AdminSystemSettingsController", AdminPolicies.SuperAdmin },
        { "AdminAuditLogsController", AdminPolicies.SuperAdmin },
        { "AdminWalletController", AdminPolicies.Finance },
        { "AdminPricingPeriodsController", AdminPolicies.Finance },
        { "AdminReferralsController", AdminPolicies.Finance },
        { "AdminClaimsController", AdminPolicies.Moderation },
    };

    [Theory]
    [MemberData(nameof(ExpectedPolicies))]
    public void Admin_controllers_declare_their_role_scoped_policy(string controllerName, string expectedPolicy)
    {
        var controller = AdminControllerTypes().SingleOrDefault(type => type.Name == controllerName);

        Assert.NotNull(controller);
        var authorize = controller.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal(expectedPolicy, authorize.Policy);
    }

    [Fact]
    public void Every_admin_route_controller_is_covered_by_the_policy_map()
    {
        var expected = ExpectedPolicies.Select(row => (string)row[0]).ToHashSet(StringComparer.Ordinal);

        var uncovered = AdminControllerTypes().Select(type => type.Name).Where(name => !expected.Contains(name)).ToList();

        Assert.Empty(uncovered);
    }

    [Fact]
    public void Super_admin_is_allowed_by_every_policy()
    {
        foreach (var roles in AdminPolicies.RolesByPolicy.Values)
        {
            Assert.Contains(AdminRoles.SuperAdmin, roles);
        }
    }

    [Theory]
    [InlineData(AdminRoles.SuperAdmin, AdminPolicies.SuperAdmin, true)]
    [InlineData(AdminRoles.SuperAdmin, AdminPolicies.Moderation, true)]
    [InlineData(AdminRoles.SuperAdmin, AdminPolicies.Content, true)]
    [InlineData(AdminRoles.Moderator, AdminPolicies.Moderation, true)]
    [InlineData(AdminRoles.Moderator, AdminPolicies.SuperAdmin, false)]
    [InlineData(AdminRoles.Moderator, AdminPolicies.Content, false)]
    [InlineData(AdminRoles.ContentEditor, AdminPolicies.Content, true)]
    [InlineData(AdminRoles.ContentEditor, AdminPolicies.Moderation, false)]
    [InlineData(AdminRoles.ContentEditor, AdminPolicies.SuperAdmin, false)]
    [InlineData(AdminRoles.Finance, AdminPolicies.Any, true)]
    [InlineData(AdminRoles.Finance, AdminPolicies.Moderation, false)]
    [InlineData(AdminRoles.Finance, AdminPolicies.Finance, true)]
    [InlineData(AdminRoles.Finance, AdminPolicies.Content, false)]
    [InlineData(AdminRoles.Finance, AdminPolicies.SuperAdmin, false)]
    [InlineData(AdminRoles.SuperAdmin, AdminPolicies.Finance, true)]
    [InlineData(AdminRoles.Moderator, AdminPolicies.Finance, false)]
    [InlineData(AdminRoles.ContentEditor, AdminPolicies.Finance, false)]
    public async Task Handler_grants_access_only_to_roles_in_the_policy(string role, string policy, bool expected)
    {
        var authUserId = Guid.NewGuid();
        var guard = new FakeAdminGuard(new AdminUser { AuthUserId = authUserId, DisplayName = "Test", Role = role });

        var succeeded = await EvaluateAsync(guard, authUserId, policy);

        Assert.Equal(expected, succeeded);
    }

    [Fact]
    public async Task Handler_rejects_users_without_an_active_admin_row()
    {
        var succeeded = await EvaluateAsync(new FakeAdminGuard(admin: null), Guid.NewGuid(), AdminPolicies.Any);

        Assert.False(succeeded);
    }

    [Fact]
    public async Task Admin_session_and_audit_endpoints_require_authentication()
    {
        var client = factory.CreateClient();

        var me = await client.GetAsync("/admin/me", CancellationToken.None);
        var audit = await client.GetAsync("/admin/audit-logs", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, audit.StatusCode);
    }

    private static IEnumerable<Type> AdminControllerTypes()
        => typeof(AdminPolicies).Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type))
            .Where(type => type.GetCustomAttribute<RouteAttribute>()?.Template.StartsWith("admin/", StringComparison.Ordinal) == true);

    private static async Task<bool> EvaluateAsync(IAdminGuard guard, Guid authUserId, string policy)
    {
        var requirement = new AdminRequirement(AdminPolicies.RolesByPolicy[policy]);
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, authUserId.ToString())],
            authenticationType: "test"));
        var context = new AuthorizationHandlerContext([requirement], user, resource: null);

        await new AdminRequirementHandler(guard).HandleAsync(context);
        return context.HasSucceeded;
    }

    private sealed class FakeAdminGuard(AdminUser? admin) : IAdminGuard
    {
        public Task<AdminUser?> GetActiveAdminAsync(Guid authUserId, CancellationToken cancellationToken)
            => Task.FromResult(admin is not null && admin.AuthUserId == authUserId ? admin : null);

        public Task<AdminMeDto> GetCurrentAsync(Guid authUserId, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
