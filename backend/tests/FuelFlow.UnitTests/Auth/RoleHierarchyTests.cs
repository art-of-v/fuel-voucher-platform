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
    public void CanAssignRole_ShouldNeverAllowAssigningProductOwner(string actorRole)
    {
        // No actor - not even a ProductOwner - may mint a second ProductOwner.
        RoleHierarchy.CanAssignRole(actorRole, SeedRoles.UserName, SeedRoles.ProductOwnerName)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(SeedRoles.ProductOwnerName)]
    [InlineData(SeedRoles.AdminName)]
    public void CanAssignRole_ShouldNeverAllowChangingAProductOwnersRole(string actorRole)
    {
        // The PO is untouchable: nobody can change an existing ProductOwner's role.
        RoleHierarchy.CanAssignRole(actorRole, SeedRoles.ProductOwnerName, SeedRoles.AdminName)
            .Should().BeFalse();
    }

    [Fact]
    public void CanAssignRole_ProductOwner_MayAssignAdminManagerUser()
    {
        RoleHierarchy.CanAssignRole(SeedRoles.ProductOwnerName, SeedRoles.UserName, SeedRoles.AdminName).Should().BeTrue();
        RoleHierarchy.CanAssignRole(SeedRoles.ProductOwnerName, SeedRoles.UserName, SeedRoles.ManagerName).Should().BeTrue();
        RoleHierarchy.CanAssignRole(SeedRoles.ProductOwnerName, SeedRoles.ManagerName, SeedRoles.UserName).Should().BeTrue();
    }

    [Fact]
    public void CanAssignRole_Admin_MayAssignUserOrManager_ButNotAdmin()
    {
        // Admins are created by the ProductOwner only.
        RoleHierarchy.CanAssignRole(SeedRoles.AdminName, SeedRoles.UserName, SeedRoles.ManagerName).Should().BeTrue();
        RoleHierarchy.CanAssignRole(SeedRoles.AdminName, SeedRoles.ManagerName, SeedRoles.UserName).Should().BeTrue();
        RoleHierarchy.CanAssignRole(SeedRoles.AdminName, SeedRoles.UserName, SeedRoles.AdminName).Should().BeFalse();
    }

    [Fact]
    public void CanAssignRole_Manager_MayNotChangeRoles()
    {
        RoleHierarchy.CanAssignRole(SeedRoles.ManagerName, SeedRoles.UserName, SeedRoles.ManagerName).Should().BeFalse();
    }
}
