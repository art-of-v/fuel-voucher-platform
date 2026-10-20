using FluentAssertions;
using FuelFlow.Features.Auth.AdminUser;
using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.UnitTests.Auth;

/// <summary>
/// The ProductOwner-singleton and staff-hierarchy rules enforced by RoleHierarchy.CanAssignRole.
/// The PO is established only via the env bootstrap (Auth:BootstrapProductOwnerPhone), so the role
/// is never assignable through the app and an existing PO is never re-roled.
/// </summary>
public sealed class RoleHierarchyTests
{
    [Theory]
    [InlineData(SeedRoles.ProductOwnerName)]
    [InlineData(SeedRoles.AdminName)]
    [InlineData(SeedRoles.ManagerName)]
    [InlineData(SeedRoles.QaName)]
    public void CanAssignRole_ShouldNeverAllowAssigningProductOwner(string actorRole)
    {
        // No actor - not even a ProductOwner - may mint a second ProductOwner.
        RoleHierarchy.CanAssignRole(actorRole, SeedRoles.UserName, SeedRoles.ProductOwnerName)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(SeedRoles.ProductOwnerName)]
    [InlineData(SeedRoles.AdminName)]
    [InlineData(SeedRoles.QaName)]
    public void CanAssignRole_ShouldNeverAllowChangingAProductOwnersRole(string actorRole)
    {
        // The PO is untouchable: nobody can change an existing ProductOwner's role.
        RoleHierarchy.CanAssignRole(actorRole, SeedRoles.ProductOwnerName, SeedRoles.AdminName)
            .Should().BeFalse();
    }

    [Fact]
    public void CanAssignRole_ProductOwner_MayAssignAdminManagerQaUser()
    {
        RoleHierarchy.CanAssignRole(SeedRoles.ProductOwnerName, SeedRoles.UserName, SeedRoles.AdminName).Should().BeTrue();
        RoleHierarchy.CanAssignRole(SeedRoles.ProductOwnerName, SeedRoles.UserName, SeedRoles.ManagerName).Should().BeTrue();
        RoleHierarchy.CanAssignRole(SeedRoles.ProductOwnerName, SeedRoles.ManagerName, SeedRoles.UserName).Should().BeTrue();
        // The ProductOwner is the only role that can hand out QA - nobody else may mint a tester.
        RoleHierarchy.CanAssignRole(SeedRoles.ProductOwnerName, SeedRoles.UserName, SeedRoles.QaName).Should().BeTrue();
    }

    [Fact]
    public void CanAssignRole_Admin_MayAssignUserOrManager_ButNotAdminOrQa()
    {
        // Admins are created by the ProductOwner only, and a QA tester is a ProductOwner appointment.
        RoleHierarchy.CanAssignRole(SeedRoles.AdminName, SeedRoles.UserName, SeedRoles.ManagerName).Should().BeTrue();
        RoleHierarchy.CanAssignRole(SeedRoles.AdminName, SeedRoles.ManagerName, SeedRoles.UserName).Should().BeTrue();
        RoleHierarchy.CanAssignRole(SeedRoles.AdminName, SeedRoles.UserName, SeedRoles.AdminName).Should().BeFalse();
        RoleHierarchy.CanAssignRole(SeedRoles.AdminName, SeedRoles.UserName, SeedRoles.QaName).Should().BeFalse();
    }

    [Fact]
    public void CanAssignRole_Manager_MayNotChangeRoles()
    {
        RoleHierarchy.CanAssignRole(SeedRoles.ManagerName, SeedRoles.UserName, SeedRoles.ManagerName).Should().BeFalse();
    }

    [Fact]
    public void CanAssignRole_Qa_MayNotChangeAnyRole_IncludingItsOwn()
    {
        // QA flips a User account's active switch to test the flow, but it never mints roles -
        // least of all its own, which would otherwise be privilege escalation in one click.
        RoleHierarchy.CanAssignRole(SeedRoles.QaName, SeedRoles.UserName, SeedRoles.QaName).Should().BeFalse();
        RoleHierarchy.CanAssignRole(SeedRoles.QaName, SeedRoles.UserName, SeedRoles.ManagerName).Should().BeFalse();
        RoleHierarchy.CanAssignRole(SeedRoles.QaName, SeedRoles.UserName, SeedRoles.AdminName).Should().BeFalse();
        RoleHierarchy.CanAssignRole(SeedRoles.QaName, SeedRoles.QaName, SeedRoles.UserName).Should().BeFalse();
    }

    [Theory]
    [InlineData(SeedRoles.ManagerName)]
    [InlineData(SeedRoles.QaName)]
    public void CanActivateDeactivate_EmployeeTier_MayOnlyFlipAUserAccount(string actorRole)
    {
        // Manager and QA share one reach: User accounts. Neither may disable a colleague's own
        // staff account - that is the Admin's privilege, and for QA it would be a way to lock
        // real staff out of a live panel.
        RoleHierarchy.CanActivateDeactivate(actorRole, SeedRoles.UserName).Should().BeTrue();
        RoleHierarchy.CanActivateDeactivate(actorRole, SeedRoles.ManagerName).Should().BeFalse();
        RoleHierarchy.CanActivateDeactivate(actorRole, SeedRoles.QaName).Should().BeFalse();
        RoleHierarchy.CanActivateDeactivate(actorRole, SeedRoles.AdminName).Should().BeFalse();
        RoleHierarchy.CanActivateDeactivate(actorRole, SeedRoles.ProductOwnerName).Should().BeFalse();
    }

    [Theory]
    [InlineData(SeedRoles.QaName)]
    [InlineData(SeedRoles.ManagerName)]
    [InlineData(SeedRoles.AdminName)]
    public void EveryRefusalButTheOwners_IsAlsoRefusedForARolelessTarget(string actorRole)
    {
        // A target with no role is refused to everyone below the ProductOwner: the hierarchy has
        // no place to put it, so nobody in a tier can be said to manage it. The ProductOwner is
        // the exception, and the only way such an account is ever recovered - see planning #299.
        RoleHierarchy.CanActivateDeactivate(actorRole, null).Should().BeFalse();
        RoleHierarchy.CanAssignRole(actorRole, null, SeedRoles.UserName).Should().BeFalse();
        RoleHierarchy.CanBan(actorRole, null).Should().BeFalse();
        RoleHierarchy.CanDelete(actorRole, null).Should().BeFalse();
    }

    [Fact]
    public void The_ProductOwner_Can_Manage_An_Account_That_Has_No_Role()
    {
        // The null guard used to sit before the ProductOwner's short-circuit, so the one actor
        // with absolute rights could not touch an account the hierarchy could not place - and the
        // account was stuck: nobody could activate, deactivate, re-role, email or ban it.
        RoleHierarchy.CanActivateDeactivate(SeedRoles.ProductOwnerName, null).Should().BeTrue();
        RoleHierarchy.CanAssignRole(SeedRoles.ProductOwnerName, null, SeedRoles.UserName).Should().BeTrue();
        RoleHierarchy.CanAssignRole(SeedRoles.ProductOwnerName, null, SeedRoles.AdminName).Should().BeTrue();
        RoleHierarchy.CanBan(SeedRoles.ProductOwnerName, null).Should().BeTrue();
    }

    [Fact]
    public void The_ProductOwner_Cannot_Mint_A_Second_ProductOwner_Through_A_Roleless_Target_Either()
    {
        // The roleless exception does not open the singleton: a ProductOwner is still never
        // assignable through the app.
        RoleHierarchy.CanAssignRole(SeedRoles.ProductOwnerName, null, SeedRoles.ProductOwnerName).Should().BeFalse();
    }

    [Fact]
    public void The_Refusal_Says_Why()
    {
        // The refusal used to be an empty 403, so the reason existed only in the server log.
        RoleHierarchy.DescribeRefusal(SeedRoles.ManagerName, null)
            .Should().Contain("no role").And.Contain("ProductOwner");
        RoleHierarchy.DescribeRefusal(SeedRoles.ManagerName, SeedRoles.AdminName)
            .Should().Contain("Manager").And.Contain("Admin");
        RoleHierarchy.DescribeRefusal(null, SeedRoles.UserName)
            .Should().Contain("staff");
    }

    [Theory]
    [InlineData(SeedRoles.QaName, SeedRoles.ManagerName)]
    [InlineData(SeedRoles.QaName, SeedRoles.AdminName)]
    [InlineData(SeedRoles.QaName, SeedRoles.ProductOwnerName)]
    [InlineData(SeedRoles.QaName, SeedRoles.QaName)]
    public void CanBan_Qa_MayBanNobody(string actorRole, string targetRole)
    {
        // Ban is a hard revocation that kills every session. QA is excluded by design, and the
        // endpoint's AdminOrOwner policy excludes it too - this pins the hierarchy half of that.
        RoleHierarchy.CanBan(actorRole, targetRole).Should().BeFalse();
    }

    [Theory]
    [InlineData(SeedRoles.QaName, SeedRoles.UserName)]
    [InlineData(SeedRoles.QaName, SeedRoles.ManagerName)]
    [InlineData(SeedRoles.QaName, SeedRoles.QaName)]
    public void CanDelete_Qa_MayDeleteNobody(string actorRole, string targetRole)
    {
        RoleHierarchy.CanDelete(actorRole, targetRole).Should().BeFalse();
    }

    [Fact]
    public void IsValidRoleName_AcceptsQa()
    {
        // Without this, QA could not be assigned to anyone at all: CanAssignRole gates on it.
        SeedRoles.IsValidRoleName(SeedRoles.QaName).Should().BeTrue();
        SeedRoles.IsStaff(SeedRoles.QaName).Should().BeTrue();
    }

    [Fact]
    public void IsStaff_ExcludesThePlainUserRole()
    {
        // QA is a staff role despite sitting below Manager; User never is.
        SeedRoles.IsStaff(SeedRoles.UserName).Should().BeFalse();
        SeedRoles.IsValidRoleName(SeedRoles.UserName).Should().BeTrue();
    }
}
