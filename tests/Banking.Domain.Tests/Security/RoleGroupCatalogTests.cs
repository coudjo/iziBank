using Banking.Domain.Lookups;
using Banking.Domain.Security;

namespace Banking.Domain.Tests.Security;

public sealed class RoleGroupCatalogTests
{
    [Fact]
    public void Group_codes_are_unique_and_fit_varchar_thirty()
    {
        var codes = RoleGroupCatalog.All.Select(static group => group.Code).ToArray();

        codes.Distinct(StringComparer.Ordinal).Count().ShouldBe(codes.Length);
        codes.ShouldAllBe(static code => code.Length <= 30);
        codes.ShouldBe(
            ["TELLER", "FIELD_OFFICER", "KYC_OFFICER", "SUPERVISOR", "ADMINISTRATOR", "AUDITOR", "ACCOUNTANT"],
            ignoreOrder: true);
    }

    [Fact]
    public void Every_grant_points_at_a_module_and_sub_module_in_the_catalog()
    {
        foreach (var grant in RoleGroupCatalog.All.SelectMany(static group => group.Permissions))
        {
            var module = ModuleCatalog.All.SingleOrDefault(candidate => candidate.Code == grant.ModuleCode);
            module.ShouldNotBeNull($"Unknown module {grant.ModuleCode}");

            if (grant.SubModuleCode is not null)
            {
                module.SubModules.ShouldContain(
                    subModule => subModule.Code == grant.SubModuleCode,
                    $"{grant.SubModuleCode} does not belong to {grant.ModuleCode}");
            }
        }
    }

    [Fact]
    public void A_group_has_at_most_one_row_per_module_and_sub_module()
    {
        foreach (var group in RoleGroupCatalog.All)
        {
            var keys = group.Permissions.Select(static grant => (grant.ModuleCode, grant.SubModuleCode)).ToArray();
            keys.Distinct().Count().ShouldBe(keys.Length, group.Code);
        }
    }

    [Fact]
    public void Any_write_or_supervise_flag_implies_view()
    {
        foreach (var grant in RoleGroupCatalog.All.SelectMany(static group => group.Permissions))
        {
            if (grant.AllowAdd || grant.AllowEdit || grant.AllowDelete || grant.AllowSupervise)
            {
                grant.AllowView.ShouldBeTrue($"{grant.ModuleCode}/{grant.SubModuleCode}");
            }
        }
    }

    [Fact]
    public void Bootstrap_administrator_groups_cover_the_last_administrator_guard()
    {
        var grants = RoleGroupCatalog.All
            .Where(static group => RoleGroupCatalog.BootstrapAdministratorGroups.Contains(group.Code))
            .SelectMany(static group => group.Permissions)
            .ToArray();

        bool Has(string module, string subModule, Func<PermissionGrant, bool> flag) =>
            grants.Any(grant =>
                grant.ModuleCode == module && (grant.SubModuleCode is null || grant.SubModuleCode == subModule) && flag(grant));

        Has(ModuleCatalog.Security, "USERS", static grant => grant.AllowEdit).ShouldBeTrue();
        Has(ModuleCatalog.Security, "PERMISSIONS", static grant => grant.AllowEdit).ShouldBeTrue();
        Has(ModuleCatalog.Party, "KYC", static grant => grant.AllowSupervise).ShouldBeTrue();
    }

    [Fact]
    public void Role_group_changes_can_be_supervised_by_an_administrator()
    {
        // R10: role group changes need supervision, so SEC/PERMISSIONS must carry a supervise flag somewhere.
        RoleGroupCatalog.All
            .SelectMany(static group => group.Permissions)
            .ShouldContain(static grant =>
                grant.ModuleCode == ModuleCatalog.Security && grant.SubModuleCode == "PERMISSIONS" && grant.AllowSupervise);
    }

    [Fact]
    public void Kyc_officer_supervises_party_and_kyc_but_cannot_add_customers()
    {
        var kyc = RoleGroupCatalog.All.Single(static group => group.Code == RoleGroupCatalog.KycOfficer);

        kyc.Permissions.ShouldContain(static grant =>
            grant.SubModuleCode == "KYC" && grant.AllowSupervise);
        kyc.Permissions.ShouldContain(static grant =>
            grant.SubModuleCode == "PARTY" && grant.AllowSupervise);
        kyc.Permissions.ShouldNotContain(static grant => grant.AllowAdd);
    }
}
