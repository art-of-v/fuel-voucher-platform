using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.API.Features.Orders.CreateCheckout.Models;
using FuelFlow.Features.Company.GetMembers;
using FuelFlow.Features.Company.GetMyInvitations;
using FuelFlow.Features.Company.GetMyMemberships;
using FuelFlow.Features.Contracts;
using FuelFlow.Features.Contracts.GetCustomerContracts;
using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.GetUserVouchers;
using FuelFlow.Features.Vouchers.GetWorkerUsageReport;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The whole legal-entity (B2B) flow, end to end, over HTTP and against real Postgres.
/// </summary>
/// <remarks>
/// <para>
/// The company feature is covered handler by handler - <see cref="CompanyMembershipIntegrationTests"/>,
/// <see cref="CompanyFiredWorkerFuelIntegrationTests"/>, <see cref="CompanyVoucherBlockingIntegrationTests"/>,
/// <see cref="MyCompanyMembershipsIntegrationTests"/> - and every contract in the chain is covered on
/// its own. What none of them cover is the chain itself: registration, the roster, a purchase that
/// lands in the company's pool, the handover, the worker's receipt and usage report, the freeze, the
/// way back to the pool, and the firing. A suite per handler proves each link works; it does not prove
/// the links fit together, and the seams between them (the legal entity id on the order, the worker
/// link on the voucher, the membership the endpoints resolve from) are exactly where a company flow
/// breaks.
/// </para>
/// <para>
/// So this file is the journey, in order, with the assertions at the points a client would notice:
/// the roster, the pool, the receipt, the totals, the state after an off-boarding. Where a claim in
/// <c>docs/testing/multi-company-103.md</c> was only ever checked on a device, the API half of it is
/// pinned here.
/// </para>
/// <para>
/// The one test-only deviation is the role: the owner is made an <c>Admin</c>, because
/// <c>POST /api/purchases/simulate</c> is a money-movement endpoint and the platform has no
/// customer-facing way to mark an order paid without an acquirer. Everything the flow itself does is
/// the production wiring.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class CompanyLegalEntityFlowIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public CompanyLegalEntityFlowIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    // ---------------------------------------------------------------------------------------------
    // S3a - an invitation becomes a roster entry
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_invited_worker_becomes_a_member_of_the_company_and_only_of_it()
    {
        var owner = await RegisterAsync(Staff: true);
        var worker = await RegisterAsync(Staff: false);
        var companyId = await CreateCompanyAsync(owner, "ТОВ Ростер");
        var otherCompanyId = await CreateCompanyAsync(owner, "ТОВ Ростер Друга");

        var invitationId = await InviteAsync(owner, companyId, worker.Phone);

        // The worker's side of the two-sided consent: the invitation they see names the company.
        var pending = await GetAsync<List<MyCompanyInvitationDto>>(worker.Client, "/api/company/my-invitations");
        pending.Should().ContainSingle(x => x.Id == invitationId)
            .Which.LegalEntityId.Should().Be(companyId);

        // Until they accept, they are not a member and the roster is empty.
        (await MembersAsync(owner, companyId)).Should().BeEmpty("nobody has accepted yet");
        (await GetAsync<List<MyCompanyMembershipDto>>(worker.Client, "/api/company/my-memberships"))
            .Should().BeEmpty();

        await AcceptAsync(worker, invitationId);

        var roster = await MembersAsync(owner, companyId);
        roster.Should().ContainSingle().Which.WorkerUserId.Should().Be(worker.UserId);

        // The company they work for is a worker context; the one they were not invited to is not, and
        // the membership is not a duplicate of an ownership row (isOwner is false here).
        var memberships = await GetAsync<List<MyCompanyMembershipDto>>(worker.Client, "/api/company/my-memberships");
        memberships.Should().ContainSingle().Which.LegalEntityId.Should().Be(companyId);
        memberships.Single().IsOwner.Should().BeFalse();
        memberships.Should().NotContain(x => x.LegalEntityId == otherCompanyId);

        // An accepted invitation is no longer pending, and the same worker cannot be invited twice.
        (await GetAsync<List<MyCompanyInvitationDto>>(worker.Client, "/api/company/my-invitations"))
            .Should().BeEmpty();

        var duplicate = await owner.Client.PostAsJsonAsync(
            "/api/company/invitations",
            new { workerPhoneNumber = worker.Phone, legalEntityId = companyId });
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict, "the worker is already a member");
    }

    [Fact]
    public async Task A_company_is_registered_under_its_owner_and_only_its_owner_can_edit_it()
    {
        var owner = await RegisterAsync(Staff: true);
        var stranger = await RegisterAsync(Staff: true);

        var companyId = await CreateCompanyAsync(owner, "ТОВ Володіння");

        (await GetAsync<List<LegalEntityDto>>(owner.Client, "/api/legal-entity/mine"))
            .Should().ContainSingle(x => x.Id == companyId);
        (await GetAsync<List<LegalEntityDto>>(stranger.Client, "/api/legal-entity/mine"))
            .Should().BeEmpty();

        var edit = await stranger.Client.PutAsJsonAsync(
            $"/api/legal-entity/{companyId}",
            new { name = "ТОВ Чуже", edrpou = NewEdrpou() });
        edit.StatusCode.Should().Be(HttpStatusCode.NotFound, "a company you do not own is not yours to edit");

        (await GetAsync<List<LegalEntityDto>>(owner.Client, "/api/legal-entity/mine"))
            .Single(x => x.Id == companyId).Name.Should().Be("ТОВ Володіння");
    }

    // ---------------------------------------------------------------------------------------------
    // S4 - a purchase in the company context belongs to the company
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_purchase_made_in_the_company_context_lands_in_the_company_pool()
    {
        var stack = await CompanyWithWorkerAsync("okko-dp");
        SeedSellableStock("okko", "okko-dp", 40m);

        var orderId = await BuyAsync(stack.Owner, stack.CompanyId, "okko-dp", 40m);
        await FulfilAsync(orderId);

        var wallet = await WalletAsync(stack.Owner);

        // The fuel the company bought is the company's: stamped with the entity, held by the
        // purchasing user, and belonging to no worker yet - that is the pool.
        var bought = wallet.Should().ContainSingle(v => v.FuelType == ToUuid("okko-dp").ToString()).Subject;
        bought.LegalEntityId.Should().Be(stack.CompanyId);
        bought.WorkerUserId.Should().BeNull();
        bought.Status.Should().Be(nameof(VoucherStatus.Assigned));
        bought.Amount.Should().Be(40m);

        // The order itself carries the entity, so the hub can group purchases by brand per company.
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = await context.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
        order.LegalEntityId.Should().Be(stack.CompanyId);
        order.Kind.Should().Be(OrderKind.Purchase, "buying for a company is still a sale");

        // And nobody else can buy into this company's wallet.
        var stranger = await RegisterAsync(Staff: true);
        var hijack = await stranger.Client.PostAsJsonAsync("/api/purchases", new
        {
            legalEntityId = stack.CompanyId,
            fuelTypeId = ToUuid("okko-dp"),
            liters = 40m,
            quantity = 1,
            price = 2000m,
            brandId = BrandIds.Okko
        });
        hijack.IsSuccessStatusCode.Should().BeFalse("a company only its owner owns is not a context a stranger can buy into");
    }

    // ---------------------------------------------------------------------------------------------
    // #169 - a handover is a receipt, and the worker can account for it
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Fuel_handed_to_a_worker_becomes_a_receipt_the_worker_can_redeem_and_account_for()
    {
        var stack = await CompanyWithWorkerAsync("okko-95");
        SeedSellableStock("okko", "okko-95", 30m);
        var orderId = await BuyAsync(stack.Owner, stack.CompanyId, "okko-95", 30m);
        await FulfilAsync(orderId);

        var pool = (await WalletAsync(stack.Owner))
            .Single(v => v.LegalEntityId == stack.CompanyId && v.WorkerUserId is null);

        var issuance = await GiftAsync(stack.Owner, stack.CompanyId, stack.Worker.UserId, pool.Id);
        issuance.GiftedCount.Should().Be(1);

        // The handover is a real order of its own: kind ReceivedFromCompany, no money, linked back to
        // the purchase the fuel came from (#169 INV-A / I-1).
        var issuanceOrder = await OrderAsync(issuance.IssuanceOrderId);
        issuanceOrder.Kind.Should().Be(OrderKind.ReceivedFromCompany);
        issuanceOrder.Price.Should().Be(0m, "distributing fuel inside the company is not a sale");
        issuanceOrder.LegalEntityId.Should().Be(stack.CompanyId);
        issuanceOrder.SourceOrderId.Should().Be(orderId);

        // The worker's wallet carries it as gifted, and the roster counts it.
        var workerWallet = await WalletAsync(stack.Worker);
        var issued = workerWallet.Should().ContainSingle(v => v.Id == pool.Id).Subject;
        issued.Source.Should().Be("gifted");
        issued.Status.Should().Be(nameof(VoucherStatus.Assigned));
        (await MembersAsync(stack.Owner, stack.CompanyId))
            .Single(m => m.WorkerUserId == stack.Worker.UserId)
            .GiftedVoucherCount.Should().Be(1);

        // ...and as a RECEIPT: the issuance is an order of the worker's own, which is what makes
        // the hub's worker branch read real receipts instead of a client-side arrangement (I-3).
        // Read as JSON rather than through PurchaseDto: the receipt carries a per-voucher history
        // whose shape is the client's concern, and this test is about the receipt, not about it.
        var receipts = await GetAsync<List<JsonElement>>(stack.Worker.Client, "/api/purchases/my");
        var receipt = receipts.Should().ContainSingle(r => r.GetProperty("id").GetGuid() == issuance.IssuanceOrderId).Subject;
        receipt.GetProperty("kind").GetString().Should().Be(nameof(OrderKind.ReceivedFromCompany));
        receipt.GetProperty("price").GetDecimal().Should().Be(0m, "the worker paid nothing for it");
        receipt.GetProperty("legalEntityId").GetGuid().Should().Be(stack.CompanyId);
        receipt.GetProperty("vouchers").EnumerateArray()
            .Should().Contain(v => v.GetProperty("id").GetGuid() == pool.Id);

        // The owner's own list does not grow a receipt for fuel they gave away: it is the worker's
        // handover, not their purchase.
        (await GetAsync<List<JsonElement>>(stack.Owner.Client, "/api/purchases/my"))
            .Should().NotContain(r => r.GetProperty("id").GetGuid() == issuance.IssuanceOrderId);

        // The usage report is the worker's own accounting: received, used, remaining.
        var before = await UsageAsync(stack.Worker, stack.CompanyId);
        before.Totals.Count.Should().Be(1);
        before.Totals.LitersReceived.Should().Be(30m);
        before.Totals.LitersUsed.Should().Be(0m);
        before.Totals.LitersRemaining.Should().Be(30m);
        before.Items.Single().IsUsed.Should().BeFalse();
        before.Items.Single().IssuedAtUtc.Should().NotBeNull("the issue date is stamped on the handover");

        // Redemption is the worker's, needs nobody's permission, and moves the report.
        //
        // What it moves it to is "reported": the platform records the claim and waits for the
        // supplier to confirm that fuel actually left the pump (the reconciliation epic), so the
        // voucher is neither spendable nor revenue yet, and the report says so in its own bucket.
        var markUsed = await stack.Worker.Client.PatchAsync($"/api/vouchers/{pool.Id}/mark-used", content: null);
        markUsed.EnsureSuccessStatusCode();

        (await VoucherAsync(pool.Id)).Status.Should().Be(VoucherStatus.PendingReconciliation);

        var reported = await UsageAsync(stack.Worker, stack.CompanyId);
        reported.Totals.LitersPending.Should().Be(30m, "the claim is awaiting the supplier's confirmation");
        reported.Totals.LitersUsed.Should().Be(0m);
        reported.Totals.LitersRemaining.Should().Be(0m, "a reported voucher is not fuel the worker still holds");
        reported.Totals.CountPending.Should().Be(1);
        reported.Items.Single().IsUsed.Should().BeFalse();

        // The supplier's confirmation is an operator action, and it is what turns the claim into
        // spent fuel for both the worker's report and the voucher.
        var confirm = await stack.Owner.Client.PostAsJsonAsync(
            $"/api/admin/redemptions/{pool.Id}/decision",
            new { action = "Confirm" });
        confirm.EnsureSuccessStatusCode();

        var after = await UsageAsync(stack.Worker, stack.CompanyId);
        after.Totals.LitersUsed.Should().Be(30m);
        after.Totals.LitersPending.Should().Be(0m);
        after.Totals.LitersRemaining.Should().Be(0m);
        after.Totals.CountUsed.Should().Be(1);
        after.Items.Single().IsUsed.Should().BeTrue();

        (await VoucherAsync(pool.Id)).Status.Should().Be(VoucherStatus.Used);
    }

    [Fact]
    public async Task The_owner_of_the_company_cannot_mark_the_workers_voucher_as_used()
    {
        // The holder rule, over HTTP: the owner still matches AssignedToUserId but the paper is the
        // worker's, so the owner must not be able to spend it from their own session.
        var stack = await CompanyWithWorkerAsync("wog-dp");
        SeedSellableStock("wog", "wog-dp", 20m);
        var orderId = await BuyAsync(stack.Owner, stack.CompanyId, "wog-dp", 20m);
        await FulfilAsync(orderId);

        var pool = (await WalletAsync(stack.Owner))
            .Single(v => v.LegalEntityId == stack.CompanyId && v.WorkerUserId is null);
        await GiftAsync(stack.Owner, stack.CompanyId, stack.Worker.UserId, pool.Id);

        var ownerRedeem = await stack.Owner.Client.PatchAsync($"/api/vouchers/{pool.Id}/mark-used", content: null);
        ownerRedeem.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await VoucherAsync(pool.Id)).Status.Should().Be(VoucherStatus.Assigned);
    }

    // ---------------------------------------------------------------------------------------------
    // S3b + #558 - freeze, then the way back to the pool
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_frozen_voucher_goes_back_to_the_pool_and_can_be_handed_out_again()
    {
        var stack = await CompanyWithWorkerAsync("wog-95");
        SeedSellableStock("wog", "wog-95", 25m, count: 2);
        var orderId = await BuyAsync(stack.Owner, stack.CompanyId, "wog-95", 25m, quantity: 2);
        await FulfilAsync(orderId);
        var pool = (await WalletAsync(stack.Owner))
            .Where(v => v.LegalEntityId == stack.CompanyId && v.WorkerUserId is null)
            .Select(v => v.Id)
            .ToList();
        pool.Should().HaveCount(2);
        var handover = await GiftAsync(stack.Owner, stack.CompanyId, stack.Worker.UserId, pool.ToArray());

        // The recall: the fuel comes back to the pool, the worker link is cleared, and the voucher
        // is filed under the purchase again - while the issuance order stays as history.
        var recall = await stack.Owner.Client.PostAsync(
            $"/api/company/vouchers/recall/{pool[0]}?legalEntityId={stack.CompanyId}", content: null);
        recall.EnsureSuccessStatusCode();

        var recalled = await VoucherAsync(pool[0]);
        recalled.Status.Should().Be(VoucherStatus.Assigned);
        recalled.WorkerUserId.Should().BeNull();
        recalled.OrderId.Should().Be(orderId);

        (await MembersAsync(stack.Owner, stack.CompanyId))
            .Single(m => m.WorkerUserId == stack.Worker.UserId)
            .GiftedVoucherCount.Should().Be(1, "the recalled voucher is back in the pool");

        // The freeze, on the voucher the worker still holds.
        var freeze = await stack.Owner.Client.PostAsync(
            $"/api/company/vouchers/block/{pool[1]}?legalEntityId={stack.CompanyId}", content: null);
        freeze.EnsureSuccessStatusCode();

        // Frozen means unusable for the worker, and gone from their list - S5-7's API half. The
        // freeze does not rewrite the handover: a frozen voucher is still the worker's, which is
        // what lets the owner thaw it back to them (I-6).
        (await VoucherAsync(pool[1])).Status.Should().Be(VoucherStatus.Blocked);
        (await VoucherAsync(pool[1])).WorkerUserId.Should().Be(stack.Worker.UserId);
        (await VoucherAsync(pool[1])).OrderId.Should().Be(handover.IssuanceOrderId);
        (await WalletAsync(stack.Worker)).Should().NotContain(v => v.Id == pool[1]);

        // The way back to the pool in one step: no thaw, no re-issue to the person holding it.
        var returned = await stack.Owner.Client.PostAsync(
            $"/api/company/vouchers/return-to-pool/{pool[1]}?legalEntityId={stack.CompanyId}", content: null);
        returned.EnsureSuccessStatusCode();

        var back = await VoucherAsync(pool[1]);
        back.Status.Should().Be(VoucherStatus.Assigned);
        back.WorkerUserId.Should().BeNull();
        back.OrderId.Should().Be(orderId, "the fuel is filed back under the purchase it arrived from");

        // Company stock again: the pool can hand it out, which is the whole point of the return.
        var again = await GiftAsync(stack.Owner, stack.CompanyId, stack.Worker.UserId, pool[1]);
        again.GiftedCount.Should().Be(1);
        (await VoucherAsync(pool[1])).WorkerUserId.Should().Be(stack.Worker.UserId);
    }

    // ---------------------------------------------------------------------------------------------
    // I-2 - each handover is its own receipt
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Two_handovers_are_two_receipts()
    {
        var stack = await CompanyWithWorkerAsync("wog-95");
        SeedSellableStock("wog", "wog-95", 5m, count: 2);
        var orderId = await BuyAsync(stack.Owner, stack.CompanyId, "wog-95", 5m, quantity: 2);
        await FulfilAsync(orderId);

        var pool = (await WalletAsync(stack.Owner))
            .Where(v => v.LegalEntityId == stack.CompanyId && v.WorkerUserId is null)
            .Select(v => v.Id)
            .ToList();
        pool.Should().HaveCount(2);

        var first = await GiftAsync(stack.Owner, stack.CompanyId, stack.Worker.UserId, pool[0]);
        var second = await GiftAsync(stack.Owner, stack.CompanyId, stack.Worker.UserId, pool[1]);

        // Two separate actions are two orders: "when did they get this fuel" has an exact answer,
        // and batches are never merged into an earlier handover.
        second.IssuanceOrderId.Should().NotBe(first.IssuanceOrderId);
        (await OrderAsync(first.IssuanceOrderId)).LineItems.Should().ContainSingle();
        (await OrderAsync(second.IssuanceOrderId)).LineItems.Should().ContainSingle();

        var receipts = await GetAsync<List<JsonElement>>(stack.Worker.Client, "/api/purchases/my");
        receipts.Where(r => r.GetProperty("kind").GetString() == nameof(OrderKind.ReceivedFromCompany))
            .Select(r => r.GetProperty("id").GetGuid())
            .Should().BeEquivalentTo([first.IssuanceOrderId, second.IssuanceOrderId]);
    }

    // ---------------------------------------------------------------------------------------------
    // S3a-9 + S5-8 - firing takes the context and the fuel
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Firing_a_worker_takes_the_company_away_and_puts_the_fuel_back()
    {
        var stack = await CompanyWithWorkerAsync("okko-gas");
        SeedSellableStock("okko", "okko-gas", 10m, count: 3);
        var orderId = await BuyAsync(stack.Owner, stack.CompanyId, "okko-gas", 10m, quantity: 3);
        await FulfilAsync(orderId);

        var pool = (await WalletAsync(stack.Owner))
            .Where(v => v.LegalEntityId == stack.CompanyId && v.WorkerUserId is null)
            .Select(v => v.Id)
            .ToList();
        pool.Should().HaveCount(3);
        var issuance = await GiftAsync(stack.Owner, stack.CompanyId, stack.Worker.UserId, pool.ToArray());
        issuance.GiftedCount.Should().Be(3);

        // One handover is one receipt: three line items in a single order of kind
        // ReceivedFromCompany, not three orders and not a merged one (I-1).
        var issuanceOrder = await OrderAsync(issuance.IssuanceOrderId);
        issuanceOrder.Kind.Should().Be(OrderKind.ReceivedFromCompany);
        issuanceOrder.LineItems.Should().HaveCount(3);
        foreach (var voucherId in pool)
        {
            (await VoucherAsync(voucherId)).OrderId.Should().Be(issuance.IssuanceOrderId);
        }

        // The worker sees the whole handover as ONE receipt, with its three vouchers inside.
        var receipts = await GetAsync<List<JsonElement>>(stack.Worker.Client, "/api/purchases/my");
        var receipt = receipts.Should().ContainSingle(r => r.GetProperty("id").GetGuid() == issuance.IssuanceOrderId).Subject;
        receipt.GetProperty("vouchers").GetArrayLength().Should().Be(3);

        var fire = await stack.Owner.Client.DeleteAsync(
            $"/api/company/members/{stack.MemberId}?legalEntityId={stack.CompanyId}");
        fire.EnsureSuccessStatusCode();

        // The membership is gone, so the worker context is gone with it (S5-8 / S5-13).
        (await MembersAsync(stack.Owner, stack.CompanyId)).Should().BeEmpty();
        (await GetAsync<List<MyCompanyMembershipDto>>(stack.Worker.Client, "/api/company/my-memberships"))
            .Should().BeEmpty();

        // The fuel that left with the worker is company fuel again - blocked, unassigned, and filed
        // back under the purchase it came from.
        foreach (var voucherId in pool)
        {
            var voucher = await VoucherAsync(voucherId);
            voucher.Status.Should().Be(VoucherStatus.Blocked);
            voucher.WorkerUserId.Should().BeNull();
            voucher.OrderId.Should().Be(orderId);
        }

        // And the worker can no longer spend it.
        var redeem = await stack.Worker.Client.PatchAsync($"/api/vouchers/{pool[0]}/mark-used", content: null);
        redeem.IsSuccessStatusCode.Should().BeFalse("a fired worker does not redeem the employer's fuel");
        (await VoucherAsync(pool[0])).Status.Should().Be(VoucherStatus.Blocked);
    }

    // ---------------------------------------------------------------------------------------------
    // INV-2 - no cross-context movement
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Nobody_outside_the_company_can_move_its_fuel()
    {
        var stack = await CompanyWithWorkerAsync("wog-100");
        SeedSellableStock("wog", "wog-100", 15m);
        var orderId = await BuyAsync(stack.Owner, stack.CompanyId, "wog-100", 15m);
        await FulfilAsync(orderId);
        var pool = (await WalletAsync(stack.Owner))
            .Single(v => v.LegalEntityId == stack.CompanyId && v.WorkerUserId is null);
        await GiftAsync(stack.Owner, stack.CompanyId, stack.Worker.UserId, pool.Id);

        var stranger = await RegisterAsync(Staff: true);
        var strangerCompany = await CreateCompanyAsync(stranger, "ТОВ Чужа");

        // A crafted gift that names the voucher but the stranger's own company: the voucher is not in
        // that company's pool, so it is not a voucher they can hand out.
        var gift = await stranger.Client.PostAsJsonAsync("/api/company/vouchers/gift", new
        {
            workerUserId = stack.Worker.UserId,
            voucherIds = new[] { pool.Id },
            legalEntityId = strangerCompany
        });
        gift.IsSuccessStatusCode.Should().BeFalse();

        // Targeting the company directly is refused as "not yours" - not silently done.
        var block = await stranger.Client.PostAsync(
            $"/api/company/vouchers/block/{pool.Id}?legalEntityId={stack.CompanyId}", content: null);
        block.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var recall = await stranger.Client.PostAsync(
            $"/api/company/vouchers/recall/{pool.Id}?legalEntityId={stack.CompanyId}", content: null);
        recall.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // The worker is not an owner: the owner surface is closed to them even for their own company.
        var workerGift = await stack.Worker.Client.PostAsJsonAsync("/api/company/vouchers/gift", new
        {
            workerUserId = stack.Worker.UserId,
            voucherIds = new[] { pool.Id },
            legalEntityId = stack.CompanyId
        });
        workerGift.IsSuccessStatusCode.Should().BeFalse("authority stays with the owner");

        // Nothing moved: still the worker's, still spendable, still under the issuance order.
        var voucher = await VoucherAsync(pool.Id);
        voucher.WorkerUserId.Should().Be(stack.Worker.UserId);
        voucher.Status.Should().Be(VoucherStatus.Assigned);
        voucher.LegalEntityId.Should().Be(stack.CompanyId);
    }

    // ---------------------------------------------------------------------------------------------
    // S5-11 - one person, two employers
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_worker_can_work_for_two_companies_and_the_two_pools_stay_apart()
    {
        // W1 shipped the second membership; nothing proven at the HTTP level that the two contexts
        // do not mix, which is the reason the constraint was relaxed in the first place.
        var worker = await RegisterAsync(Staff: false);

        var first = await CompanyWithWorkerAsync("okko-p95", worker);
        var second = await CompanyWithWorkerAsync("wog-95-euro", worker);

        SeedSellableStock("okko", "okko-p95", 20m);
        SeedSellableStock("wog", "wog-95-euro", 45m);

        var firstOrder = await BuyAsync(first.Owner, first.CompanyId, "okko-p95", 20m);
        var secondOrder = await BuyAsync(second.Owner, second.CompanyId, "wog-95-euro", 45m);
        await FulfilAsync(firstOrder);
        await FulfilAsync(secondOrder);

        var firstPool = (await WalletAsync(first.Owner))
            .Single(v => v.LegalEntityId == first.CompanyId && v.WorkerUserId is null);
        var secondPool = (await WalletAsync(second.Owner))
            .Single(v => v.LegalEntityId == second.CompanyId && v.WorkerUserId is null);

        await GiftAsync(first.Owner, first.CompanyId, worker.UserId, firstPool.Id);
        await GiftAsync(second.Owner, second.CompanyId, worker.UserId, secondPool.Id);

        // Both employers are in the switcher, and each is a different company.
        var memberships = await GetAsync<List<MyCompanyMembershipDto>>(worker.Client, "/api/company/my-memberships");
        memberships.Select(m => m.LegalEntityId).Should().BeEquivalentTo([first.CompanyId, second.CompanyId]);
        memberships.Should().OnlyContain(m => !m.IsOwner);

        // Each report counts only its own company's fuel, in litres - the number a worker would
        // check against their own tank, not a count that happens to differ.
        var atFirst = await UsageAsync(worker, first.CompanyId);
        atFirst.Totals.LitersReceived.Should().Be(20m);
        atFirst.Items.Should().ContainSingle().Which.Id.Should().Be(firstPool.Id);

        var atSecond = await UsageAsync(worker, second.CompanyId);
        atSecond.Totals.LitersReceived.Should().Be(45m);
        atSecond.Items.Should().ContainSingle().Which.Id.Should().Be(secondPool.Id);

        // The wallet holds both, each stamped with the company it came from.
        var wallet = await WalletAsync(worker);
        wallet.Single(v => v.Id == firstPool.Id).LegalEntityId.Should().Be(first.CompanyId);
        wallet.Single(v => v.Id == secondPool.Id).LegalEntityId.Should().Be(second.CompanyId);

        // Leaving one employer does not touch the other's fuel.
        (await first.Owner.Client.DeleteAsync(
                $"/api/company/members/{first.MemberId}?legalEntityId={first.CompanyId}"))
            .EnsureSuccessStatusCode();

        (await VoucherAsync(firstPool.Id)).Status.Should().Be(VoucherStatus.Blocked);

        var kept = await VoucherAsync(secondPool.Id);
        kept.Status.Should().Be(VoucherStatus.Assigned);
        kept.WorkerUserId.Should().Be(worker.UserId);

        (await GetAsync<List<MyCompanyMembershipDto>>(worker.Client, "/api/company/my-memberships"))
            .Should().ContainSingle().Which.LegalEntityId.Should().Be(second.CompanyId);
    }

    // ---------------------------------------------------------------------------------------------
    // A firing can be undone without the history coming with it
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_fired_worker_can_be_invited_again_and_the_roster_stays_one_row()
    {
        // The defect this guards is the one that started the hub work: firing somebody and inviting
        // them back produced two contradictory rows for the same person. The membership is a soft
        // delete and the uniqueness is a filtered index, so "invited again" has to be exactly one row.
        var stack = await CompanyWithWorkerAsync("okko-pulls-dp");
        SeedSellableStock("okko", "okko-pulls-dp", 35m);
        var orderId = await BuyAsync(stack.Owner, stack.CompanyId, "okko-pulls-dp", 35m);
        await FulfilAsync(orderId);

        var pool = (await WalletAsync(stack.Owner))
            .Single(v => v.LegalEntityId == stack.CompanyId && v.WorkerUserId is null);
        await GiftAsync(stack.Owner, stack.CompanyId, stack.Worker.UserId, pool.Id);

        (await stack.Owner.Client.DeleteAsync(
                $"/api/company/members/{stack.MemberId}?legalEntityId={stack.CompanyId}"))
            .EnsureSuccessStatusCode();

        var reinvite = await InviteAsync(stack.Owner, stack.CompanyId, stack.Worker.Phone);
        await AcceptAsync(stack.Worker, reinvite);

        var roster = await MembersAsync(stack.Owner, stack.CompanyId);
        roster.Should().ContainSingle("the former membership is history, not a second row");
        roster.Single().WorkerUserId.Should().Be(stack.Worker.UserId);
        roster.Single().GiftedVoucherCount.Should().Be(0, "the fuel they had before did not come back with them");

        // The fuel from the first spell is still the company's, and the company can hand out fresh
        // fuel to the person who has just rejoined.
        (await VoucherAsync(pool.Id)).Status.Should().Be(VoucherStatus.Blocked);
        (await VoucherAsync(pool.Id)).WorkerUserId.Should().BeNull();

        SeedSellableStock("okko", "okko-pulls-dp", 35m);
        var secondOrder = await BuyAsync(stack.Owner, stack.CompanyId, "okko-pulls-dp", 35m);
        await FulfilAsync(secondOrder);

        // The blocked voucher from the first spell is company stock but NOT the pool - the pool is
        // the Assigned, worker-less fuel a handover can draw from.
        var fresh = (await WalletAsync(stack.Owner))
            .Single(v => v.LegalEntityId == stack.CompanyId
                         && v.WorkerUserId is null
                         && v.Status == nameof(VoucherStatus.Assigned)
                         && v.Id != pool.Id);
        await GiftAsync(stack.Owner, stack.CompanyId, stack.Worker.UserId, fresh.Id);

        (await MembersAsync(stack.Owner, stack.CompanyId)).Single().GiftedVoucherCount.Should().Be(1);
    }

    // ---------------------------------------------------------------------------------------------
    // The documents a company is created with
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_published_provider_text_gives_a_company_a_contract_its_owner_can_sign()
    {
        // The B2B half of the legal-entity flow: terms live once per provider, a company is created
        // with a contract for each provider whose text exists, and the owner signs it. The writer runs
        // on create, so a publish that happens AFTER the company exists is its backfill path (#103 /
        // the owner's 2026-10-21 decision).
        var owner = await RegisterAsync(Staff: true);
        var companyId = await CreateCompanyAsync(owner, "ТОВ Документи");

        var publish = await owner.Client.PostAsJsonAsync("/api/admin/contract-versions", new
        {
            brandId = BrandIds.Okko,
            body = "Умови співпраці з ТОВ Документи: паливо видається за талонами, термін дії — рік.",
            termDays = 365
        });
        publish.EnsureSuccessStatusCode();

        using (var document = JsonDocument.Parse(await publish.Content.ReadAsStringAsync()))
        {
            document.RootElement.GetProperty("contractsCreated").GetInt32()
                .Should().BeGreaterThanOrEqualTo(1, "the publish is the backfill for companies that already exist");
        }

        var contracts = await GetAsync<List<CustomerContractDto>>(owner.Client, "/api/legal-entity/contracts");
        var contract = contracts.Should().ContainSingle(c => c.LegalEntityId == companyId).Subject;
        contract.BrandId.Should().Be(BrandIds.Okko);
        contract.HasText.Should().BeTrue("a contract with no text behind it is a document nobody can read");
        contract.TermsText.Should().Contain("ТОВ Документи");
        contract.Expired.Should().BeFalse();

        var sign = await owner.Client.PostAsJsonAsync("/api/legal-entity/sign", new
        {
            contractId = contract.Id,
            signatureData = "data:image/png;base64,iVBORw0KGgo="
        });
        sign.EnsureSuccessStatusCode();

        var signed = await GetAsync<List<JsonElement>>(owner.Client, "/api/legal-entity/contracts/signed");
        signed.Should().Contain(row => row.GetProperty("contractId").GetGuid() == contract.Id);

        // Signing the same document twice is refused rather than silently re-stamped.
        var again = await owner.Client.PostAsJsonAsync("/api/legal-entity/sign", new
        {
            contractId = contract.Id,
            signatureData = "data:image/png;base64,iVBORw0KGgo="
        });
        again.IsSuccessStatusCode.Should().BeFalse();
    }

    // ---------------------------------------------------------------------------------------------
    // The test stack: two signed-in people, one company, one membership
    // ---------------------------------------------------------------------------------------------

    private sealed record Account(HttpClient Client, Guid UserId, string Phone);

    private sealed record CompanyStack(
        Account Owner,
        Account Worker,
        Guid CompanyId,
        Guid MemberId,
        string FuelTypeId);

    private async Task<CompanyStack> CompanyWithWorkerAsync(string fuelTypeId, Account? existingWorker = null)
    {
        var owner = await RegisterAsync(Staff: true);
        var worker = existingWorker ?? await RegisterAsync(Staff: false);
        var companyId = await CreateCompanyAsync(owner, "ТОВ Потік");
        var invitationId = await InviteAsync(owner, companyId, worker.Phone);
        await AcceptAsync(worker, invitationId);

        var member = (await MembersAsync(owner, companyId))
            .Single(m => m.WorkerUserId == worker.UserId);

        return new CompanyStack(owner, worker, companyId, member.Id, fuelTypeId);
    }

    // ---------------------------------------------------------------------------------------------
    // Identity
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Signs a fresh user up through the real OTP endpoints, then performs the two staff actions a
    /// test needs: activation, and (for the owner) a role that may simulate a payment.
    /// </summary>
    /// <remarks>
    /// The promotion is the only test-only step in this file. The flow itself then runs through the
    /// handlers, the database and the constraints the production host wires.
    /// </remarks>
    private async Task<Account> RegisterAsync(bool Staff)
    {
        var phone = "+380" + TestClock.UniqueDigits(9);
        var client = _fixture.CreateClient();

        await SignInAsync(client, phone);

        Guid userId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var user = await context.Users.AsTracking().SingleAsync(u => u.PhoneNumber == phone);
            user.IsActive = true;

            if (Staff)
            {
                var adminRole = await context.Roles.FirstOrDefaultAsync(r => r.Name == SeedRoles.AdminName);
                if (adminRole is null)
                {
                    adminRole = new Role
                    {
                        Id = Guid.NewGuid(),
                        Name = SeedRoles.AdminName,
                        CreatedAtUtc = TestClock.UtcNow
                    };
                    context.Roles.Add(adminRole);
                }

                user.RoleId = adminRole.Id;
            }

            user.UpdatedAtUtc = TestClock.UtcNow;
            context.Update(user);
            await context.SaveChangesAsync();
            userId = user.Id;
        }

        // A fresh token, because the role and the active flag travel in the claims.
        await SignInAsync(client, phone);

        return new Account(client, userId, phone);
    }

    private static async Task SignInAsync(HttpClient client, string phone)
    {
        (await client.PostAsJsonAsync("/api/auth/send-code", new { phoneNumber = phone }))
            .EnsureSuccessStatusCode();

        var verify = await client.PostAsJsonAsync("/api/auth/verify",
            new { phoneNumber = phone, code = "000000" });
        verify.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await verify.Content.ReadAsStringAsync());
        var token = document.RootElement.GetProperty("accessToken").GetString();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    // ---------------------------------------------------------------------------------------------
    // The company
    // ---------------------------------------------------------------------------------------------

    private static async Task<Guid> CreateCompanyAsync(Account owner, string name)
    {
        var response = await owner.Client.PostAsJsonAsync("/api/legal-entity",
            new { name, edrpou = NewEdrpou() });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<LegalEntityDto>())!.Id;
    }

    private static async Task<Guid> InviteAsync(Account owner, Guid companyId, string workerPhone)
    {
        var response = await owner.Client.PostAsJsonAsync("/api/company/invitations",
            new { workerPhoneNumber = workerPhone, legalEntityId = companyId });
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("invitationId").GetGuid();
    }

    private static async Task AcceptAsync(Account worker, Guid invitationId)
    {
        (await worker.Client.PostAsync($"/api/company/invitations/{invitationId}/accept", content: null))
            .EnsureSuccessStatusCode();
    }

    private static Task<List<CompanyMemberDto>> MembersAsync(Account owner, Guid companyId)
        => GetAsync<List<CompanyMemberDto>>(owner.Client, $"/api/company/members?legalEntityId={companyId}");

    // ---------------------------------------------------------------------------------------------
    // Buying, handing out, moving
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Buys in the company's context and pays through the development simulation.
    /// </summary>
    private async Task<Guid> BuyAsync(
        Account owner,
        Guid companyId,
        string fuelTypeId,
        decimal liters,
        int quantity = 1)
    {
        var checkout = await owner.Client.PostAsJsonAsync("/api/purchases", new
        {
            legalEntityId = companyId,
            fuelTypeId = ToUuid(fuelTypeId),
            liters,
            quantity,
            price = 2000m * quantity,
            brandId = BrandIdFor(fuelTypeId)
        });
        checkout.EnsureSuccessStatusCode();

        var order = (await checkout.Content.ReadFromJsonAsync<CreateCheckoutResponse>())!;

        // The simulation settles the order and queues fulfilment through Hangfire; the test then
        // runs the fulfilment itself so the assertions do not race an asynchronous worker.
        var simulate = await owner.Client.PostAsJsonAsync("/api/purchases/simulate",
            new { orderId = order.OrderId, scenario = "success" });
        simulate.EnsureSuccessStatusCode();

        return order.OrderId;
    }

    private async Task FulfilAsync(Guid orderId)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // The queued job may already have run; either way the order leaves PendingFulfillment once
        // vouchers exist, so waiting on the state is deterministic and the call is idempotent.
        var worked = await WaitForAsync(
            async () =>
            {
                var pending = await context.Orders.AsNoTracking()
                    .SingleAsync(o => o.Id == orderId);
                return pending.Status != OrderStatus.PendingFulfillment;
            },
            TimeSpan.FromSeconds(5));

        if (worked)
        {
            return;
        }

        var fulfilment = scope.ServiceProvider.GetRequiredService<FulfillmentService>();
        await fulfilment.ProcessPendingOrdersAsync(CancellationToken.None);

        var status = (await context.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId)).Status;
        status.Should().NotBe(OrderStatus.PendingFulfillment, "a paid order must hand out its vouchers");
    }

    private static async Task<(int GiftedCount, Guid IssuanceOrderId)> GiftAsync(
        Account owner,
        Guid companyId,
        Guid workerUserId,
        params Guid[] voucherIds)
    {
        var response = await owner.Client.PostAsJsonAsync("/api/company/vouchers/gift", new
        {
            workerUserId,
            voucherIds,
            legalEntityId = companyId
        });
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (
            document.RootElement.GetProperty("giftedCount").GetInt32(),
            document.RootElement.GetProperty("issuanceOrderId").GetGuid());
    }

    private static Task<List<FuelFlow.Features.Vouchers.GetUserVouchers.VoucherDto>> WalletAsync(Account account)
        => GetAsync<List<FuelFlow.Features.Vouchers.GetUserVouchers.VoucherDto>>(account.Client, "/api/vouchers/my");

    private static Task<GetWorkerUsageReportResponse> UsageAsync(Account worker, Guid companyId)
        => GetAsync<GetWorkerUsageReportResponse>(
            worker.Client, $"/api/company/worker-usage?legalEntityId={companyId}");

    // ---------------------------------------------------------------------------------------------
    // Reading the database, for the facts no endpoint exposes
    // ---------------------------------------------------------------------------------------------

    private async Task<FuelVoucher> VoucherAsync(Guid voucherId)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.FuelVouchers.AsNoTracking().SingleAsync(v => v.Id == voucherId);
    }

    private async Task<Order> OrderAsync(Guid orderId)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Orders.AsNoTracking().Include(o => o.LineItems).SingleAsync(o => o.Id == orderId);
    }

    /// <summary>
    /// Puts sellable stock in the operator pool so a checkout has something to fulfil from.
    /// </summary>
    /// <remarks>
    /// The brand and the fuel are the seeded catalogue's, and the nominal has to exist for the
    /// checkout to recognise the size as sellable. The vouchers are <c>Available</c>, which is
    /// operator stock - the state that is exempt from the "held fuel belongs to an order" rule.
    /// </remarks>
    private void SeedSellableStock(string brandSlug, string fuelTypeId, decimal liters, int count = 1)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var brandId = context.Brands.First(b => b.Slug == brandSlug).Id;

        if (!context.FuelTypes.Any(f => f.Id == ToUuid(fuelTypeId)))
        {
            context.FuelTypes.Add(new FuelTypeEntity
            {
                Id = ToUuid(fuelTypeId),
                Name = fuelTypeId,
                BrandId = brandId,
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            });
        }

        if (!context.FuelNominals.Any(n => n.FuelTypeId == ToUuid(fuelTypeId) && n.Liters == liters))
        {
            context.FuelNominals.Add(new FuelNominal
            {
                FuelTypeId = ToUuid(fuelTypeId),
                Liters = liters,
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            });
        }

        var expiry = DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(6));
        for (var i = 0; i < count; i++)
        {
            context.FuelVouchers.Add(new FuelVoucher
            {
                Id = Guid.CreateVersion7(),
                BrandId = BrandIds.ForSlug(brandSlug),
                Brand = brandSlug,
                FuelTypeId = ToUuid(fuelTypeId),
                Liters = liters,
                SupplierExpirationDate = expiry,
                CustomerExpirationDate = expiry,
                VoucherNumber = $"COMPANY-{fuelTypeId}-{liters}-{Guid.NewGuid().ToString()[..8]}",
                QrPayload = Guid.NewGuid().ToString(),
                Status = VoucherStatus.Available,
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            });
        }

        context.SaveChanges();
    }

    // ---------------------------------------------------------------------------------------------
    // Small helpers
    // ---------------------------------------------------------------------------------------------

    private static async Task<T> GetAsync<T>(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<T>();
        return body!;
    }

    /// <summary>
    /// Waits for a condition against the wall clock, because the application's own clock is frozen.
    /// </summary>
    /// <remarks>
    /// The suite reads <see cref="TestClock"/> as "now", so a deadline built from it never passes and
    /// a wait would hang a CI run instead of failing it. A <see cref="Stopwatch"/> measures the wait
    /// without asking the suite what day it is.
    /// </remarks>
    private static async Task<bool> WaitForAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (await condition())
            {
                return true;
            }

            await Task.Delay(100);
        }

        return false;
    }

    private static Guid BrandIdFor(string fuelTypeId) => fuelTypeId.StartsWith("wog", StringComparison.Ordinal)
        ? BrandIds.Wog
        : BrandIds.Okko;

    /// <summary>
    /// A registry code the platform will accept.
    /// </summary>
    /// <remarks>
    /// The eighth digit is a checksum over the first seven, so a random eight-digit number is almost
    /// always refused (<c>LegalEntityIdentifier.HasValidControlDigit</c>). The test derives the digit
    /// from the same rule the server enforces rather than guessing, and asks the server's own gate
    /// whether the result is acceptable - which also means a change to that rule shows up here.
    /// </remarks>
    private static string NewEdrpou()
    {
        while (true)
        {
            var registryNumber = "8" + TestClock.UniqueDigits(6);

            for (var control = 0; control <= 9; control++)
            {
                var candidate = registryNumber + control;
                if (LegalEntityIdentifier.IsValidEdrpou(candidate))
                {
                    return candidate;
                }
            }
        }
    }
}
