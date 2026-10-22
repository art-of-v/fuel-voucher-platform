using FluentAssertions;
using FuelFlow.SharedKernel.Domain;
using Xunit;

namespace FuelFlow.UnitTests.Auth;

/// <summary>
/// The capability lists the authorization policies are built from.
/// </summary>
/// <remarks>
/// The effective membership of every policy is asserted over HTTP in
/// <c>AuthorizePolicySurfaceTests</c> (the integration project), because the policy is what a
/// request actually meets. This is the cheap half: the lists themselves, so a role that is added
/// to one capability by accident is caught without a container.
/// <para>
/// The decision these encode is the owner's, of 2026-10-21 (planning #521, §7.1): money movement
/// belongs to Admin, ProductOwner and Manager; ban, delete and the assignment of a role stay with
/// Admin and ProductOwner; the staff role QA is retired.
/// </para>
/// </remarks>
public class AuthPoliciesTests
{
    [Fact]
    public void The_Read_Surface_Is_The_Three_Staff_Roles_And_The_Retired_QA_Is_Not_One_Of_Them()
    {
        SeedRoles.StaffRoleNames.Should().BeEquivalentTo(
            [SeedRoles.ProductOwnerName, SeedRoles.AdminName, SeedRoles.ManagerName]);

        SeedRoles.IsStaff(SeedRoles.QaName).Should().BeFalse(
            "the QA identity the product needs is the client-side test account, not a staff role");
        SeedRoles.IsValidRoleName(SeedRoles.QaName).Should().BeFalse(
            "a role that grants nothing should not be handed out either");
    }

    [Fact]
    public void Every_Write_Capability_Carries_Admin_ProductOwner_And_Manager()
    {
        var expected = new[] { SeedRoles.ProductOwnerName, SeedRoles.AdminName, SeedRoles.ManagerName };

        SeedRoles.CatalogueWriteRoleNames.Should().BeEquivalentTo(expected);
        SeedRoles.VoucherWriteRoleNames.Should().BeEquivalentTo(expected);
        SeedRoles.MoneyMovementRoleNames.Should().BeEquivalentTo(expected);
        SeedRoles.SettingsWriteRoleNames.Should().BeEquivalentTo(expected);
        SeedRoles.StaffOperationsRoleNames.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void The_Account_Administration_Is_Admin_And_ProductOwner_Only()
    {
        SeedRoles.AdminAndOwnerRoleNames.Should().BeEquivalentTo(
            [SeedRoles.ProductOwnerName, SeedRoles.AdminName]);
    }

    [Fact]
    public void The_Settlement_Code_Is_The_Money_Tier_And_A_List_Of_Its_Own()
    {
        // The settlement code is what the settlement channel resolves money to, and what an
        // operator quotes when they place a customer's payment - so the roles that may read it are
        // the roles that move money. And it is a list of its own: a role that joins the read
        // surface must not thereby be handed every customer's code.
        SeedRoles.SettlementCodeRoleNames.Should().BeEquivalentTo(SeedRoles.MoneyMovementRoleNames);
        SeedRoles.SettlementCodeRoleNames.Should().NotBeSameAs(SeedRoles.StaffRoleNames);
    }

    [Fact]
    public void The_Signature_Is_The_Admin_Tier_And_A_List_Of_Its_Own()
    {
        // A customer's handwritten signature cannot be revoked and cannot be rotated, so it is
        // not a thing the staff read surface hands out: it is a capability of its own, for the
        // Admin tier (planning #622, S-13). And it is a list of its own, for the same reason the
        // voucher payload's is - joining the read surface must not bring every customer's
        // handwriting with it.
        SeedRoles.SignaturePayloadRoleNames.Should().BeEquivalentTo(
            [SeedRoles.ProductOwnerName, SeedRoles.AdminName]);
        SeedRoles.SignaturePayloadRoleNames.Should().NotBeSameAs(SeedRoles.StaffRoleNames);
        SeedRoles.SignaturePayloadRoleNames.Should().NotContain(SeedRoles.ManagerName);
    }

    [Fact]
    public void The_Capability_Lists_Are_Separate_Lists_And_Not_One_Array_Under_Another_Name()
    {
        // The point of separate lists: narrowing one capability must not narrow the others, and a
        // role added to the read surface must not thereby be able to move money.
        SeedRoles.MoneyMovementRoleNames.Should().NotBeSameAs(SeedRoles.StaffRoleNames);
        SeedRoles.CatalogueWriteRoleNames.Should().NotBeSameAs(SeedRoles.MoneyMovementRoleNames);
        SeedRoles.AdminAndOwnerRoleNames.Should().NotBeSameAs(SeedRoles.StaffRoleNames);
    }
}
