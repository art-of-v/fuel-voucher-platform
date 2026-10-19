using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.API.Features.Orders.CreateCheckout.Models;
using FuelFlow.Features.Orders.CreateCheckout;
using FuelFlow.Features.Orders.SimulatePayment;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The outbox is dispatched when the row is written, not when a poll next looks for it.
/// </summary>
/// <remarks>
/// <para>
/// The outbox writes a domain event in the same transaction as the state change that caused it,
/// and something has to pick it up. For most of this system's life that something was three
/// recurring jobs that looked for unprocessed rows once a minute each - and a customer could wait
/// two minutes for the notification of a purchase they had already paid for, because the first
/// poll took the <c>OrderCreated</c> event and the second one took the <c>OrderFulfilled</c> event
/// the first one produced.
/// </para>
/// <para>
/// Now the row is dispatched by a job the outbox interceptor queues when the row is written. This
/// test is what proves it, and it proves the thing the unit tests cannot: that the interceptor is
/// actually attached to the context the application uses, and that the job it queues really
/// processes the event. Each half is tested elsewhere - the interceptor queues a job, and the
/// safety net does not race it - but neither test uses the wiring the production host builds.
/// </para>
/// <para>
/// <b>Why the timing is the assertion.</b> The safety net only claims events older than a claim
/// window, so an event processed well inside that window can only have been processed by the fast
/// path. A test that merely asserted "the event eventually got processed" would pass even with the
/// whole change reverted, one safety-net cycle later.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class OutboxFastPathIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;
    private readonly HttpClient _client;
    private string? _accessToken;

    public OutboxFastPathIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateClient();
    }

    [Fact]
    public async Task An_Event_Written_By_The_Application_Is_Dispatched_Without_Waiting_For_A_Poll()
    {
        // Arrange - a user, a sellable package, and the stock behind it.
        await AuthenticateAsync("+380991234711");
        SeedSellableStock("okko", "okko-dp", 50m, 1);

        var createResponse = await _client.PostAsJsonAsync("/api/purchases", new CreateCheckoutCommand
        {
            FuelTypeId = "okko-dp",
            Liters = 50,
            Quantity = 1,
            Price = 2000,
            BrandId = BrandIds.Okko
        });
        createResponse.EnsureSuccessStatusCode();
        var order = await createResponse.Content.ReadFromJsonAsync<CreateCheckoutResponse>();
        Assert.NotNull(order);

        // Act - pay. This is the write that puts an OrderCreated event in the outbox, through the
        // application's own DbContext, which is the one the interceptor is attached to.
        var simulateResponse = await _client.PostAsJsonAsync("/api/purchases/simulate", new SimulatePaymentCommand
        {
            OrderId = order!.OrderId,
            Scenario = "success"
        });
        simulateResponse.EnsureSuccessStatusCode();

        // Assert - the event is written, and it is processed.
        var written = await WaitForOutboxEventAsync("OrderCreated", TimeSpan.FromSeconds(90));
        Assert.NotNull(written);

        var processed = await WaitForProcessedAsync(written!.Value.Id, TimeSpan.FromSeconds(90));
        Assert.NotNull(processed);

        var lag = processed!.Value - written!.Value.CreatedAt;
        Assert.True(
            lag < OutboxDispatchJob.SafetyNetClaimWindow,
            $"the event was processed {lag.TotalSeconds:F1}s after it was written, which is inside the "
            + $"claim window of {OutboxDispatchJob.SafetyNetClaimWindow.TotalSeconds:F0}s - so the safety net "
            + "cannot have claimed it, and the fast path did");
    }

    [Fact]
    public async Task A_Dispatched_Event_Is_Marked_Processed_And_Not_Left_Behind()
    {
        // The counterpart: a dispatched event is marked processed, not merely written. An event that
        // is written and never processed is a silent loss, and the safety net would eventually
        // hide it - which is exactly why this is worth asserting on its own.
        await AuthenticateAsync("+380991234712");
        SeedSellableStock("okko", "okko-95", 50m, 1);

        var createResponse = await _client.PostAsJsonAsync("/api/purchases", new CreateCheckoutCommand
        {
            FuelTypeId = "okko-95",
            Liters = 50,
            Quantity = 1,
            Price = 2000,
            BrandId = BrandIds.Okko
        });
        createResponse.EnsureSuccessStatusCode();
        var order = await createResponse.Content.ReadFromJsonAsync<CreateCheckoutResponse>();

        var simulateResponse = await _client.PostAsJsonAsync("/api/purchases/simulate", new SimulatePaymentCommand
        {
            OrderId = order!.OrderId,
            Scenario = "success"
        });
        simulateResponse.EnsureSuccessStatusCode();

        var written = await WaitForOutboxEventAsync("OrderCreated", TimeSpan.FromSeconds(90));
        Assert.NotNull(written);

        var processed = await WaitForProcessedAsync(written!.Value.Id, TimeSpan.FromSeconds(90));
        Assert.NotNull(processed);
    }

    // ---------------------------------------------------------------- helpers

    private async Task AuthenticateAsync(string phoneNumber)
    {
        if (_accessToken != null) return;

        var sendCodeResponse = await _client.PostAsJsonAsync("/api/auth/send-code", new { phoneNumber });
        sendCodeResponse.EnsureSuccessStatusCode();

        var verifyResponse = await _client.PostAsJsonAsync("/api/auth/verify", new
        {
            phoneNumber,
            code = "000000"
        });
        verifyResponse.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await verifyResponse.Content.ReadAsStringAsync());
        _accessToken = document.RootElement.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrEmpty(_accessToken));

        // OTP verify registers a user as is_active=false, and activation is a staff-only
        // operation. Payment endpoints reject inactive accounts with 403, so a purchase test has
        // to perform that activation step explicitly - the same one SetUserActiveCommandHandler
        // would - and the default authorization policy wants a role above the plain User one.
        using (var scope = _fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var adminRole = await context.Roles.FirstOrDefaultAsync(r => r.Name == "Admin");
            if (adminRole is null)
            {
                adminRole = new Role
                {
                    Id = Guid.NewGuid(),
                    Name = "Admin",
                    CreatedAtUtc = DateTime.UtcNow
                };
                context.Roles.Add(adminRole);
                await context.SaveChangesAsync();
            }

            var user = await context.Users.FirstAsync(u => u.PhoneNumber == phoneNumber);
            user.RoleId = adminRole.Id;
            user.IsActive = true;
            user.UpdatedAtUtc = DateTime.UtcNow;
            context.Update(user);
            await context.SaveChangesAsync();
        }

        // Re-login after activation so the token carries the active claim.
        _accessToken = null;
        var resend = await _client.PostAsJsonAsync("/api/auth/send-code", new { phoneNumber });
        resend.EnsureSuccessStatusCode();
        var reverify = await _client.PostAsJsonAsync("/api/auth/verify", new
        {
            phoneNumber,
            code = "000000"
        });
        reverify.EnsureSuccessStatusCode();
        using var second = JsonDocument.Parse(await reverify.Content.ReadAsStringAsync());
        _accessToken = second.RootElement.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrEmpty(_accessToken));

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
    }

    private void SeedSellableStock(string brandSlug, string fuelTypeId, decimal liters, int count)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var brandId = context.Brands.First(b => b.Slug == brandSlug).Id;

        if (!context.FuelTypes.Any(f => f.Id == fuelTypeId))
        {
            context.FuelTypes.Add(new FuelTypeEntity
            {
                Id = fuelTypeId,
                Name = fuelTypeId,
                BrandId = brandId,
                BasePrice = 50m,
                DiscountPrice = 50m,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }

        if (!context.FuelNominals.Any(n => n.FuelTypeId == fuelTypeId && n.Liters == liters))
        {
            context.FuelNominals.Add(new FuelNominal
            {
                FuelTypeId = fuelTypeId,
                Liters = liters,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }

        var expiry = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6));
        for (var i = 0; i < count; i++)
        {
            context.FuelVouchers.Add(new FuelVoucher
            {
                Id = Guid.CreateVersion7(),
                Brand = brandSlug.ToUpperInvariant(),
                FuelTypeId = fuelTypeId,
                Liters = liters,
                SupplierExpirationDate = expiry,
                CustomerExpirationDate = expiry,
                VoucherNumber = $"FASTPATH-{fuelTypeId}-{liters}-{Guid.NewGuid().ToString()[..8]}",
                QrPayload = Guid.NewGuid().ToString(),
                Status = VoucherStatus.Available,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }

        context.SaveChanges();
    }

    private async Task<(int Id, DateTime CreatedAt)?> WaitForOutboxEventAsync(string eventType, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            using var scope = _fixture.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var created = await context.OutboxEvents
                .Where(e => e.EventType.ToString() == eventType)
                .OrderByDescending(e => e.Id)
                .Select(e => new { e.Id, e.CreatedAtUtc })
                .FirstOrDefaultAsync();
            if (created is not null)
                return (created.Id, created.CreatedAtUtc);

            await Task.Delay(250);
        }

        return null;
    }

    private async Task<DateTime?> WaitForProcessedAsync(int eventId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            using var scope = _fixture.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var processed = await context.OutboxEvents
                .Where(e => e.Id == eventId)
                .Select(e => e.Processed ? e.ProcessedAtUtc : null)
                .FirstOrDefaultAsync();
            if (processed is not null)
                return processed;

            await Task.Delay(250);
        }

        return null;
    }
}