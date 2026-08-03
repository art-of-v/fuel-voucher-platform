extern alias JobsWorker;

using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Providers;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FuelFlow.IntegrationTests;

public sealed class FulfillmentConcurrencyIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public FulfillmentConcurrencyIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ConcurrentFulfillmentRuns_AssignExactlyTheOrderedVouchers()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        const string provider = "OKKO";
        const string fuelTypeId = "okko-95";
        const decimal liters = 50m;
        const int quantity = 3;
        const int availableCount = 200;

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();

            seed.Users.Add(new User
            {
                Id = userId,
                PhoneNumber = $"+38{userId:N}"[..20],
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });

            seed.Orders.Add(new Order
            {
                Id = orderId,
                UserId = userId,
                Price = quantity * 2500,
                Status = OrderStatus.PendingFulfillment,
                CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
                UpdatedAtUtc = DateTime.UtcNow.AddDays(-1),
                LineItems = new List<OrderLineItem>
                {
                    new OrderLineItem
                    {
                        Id = Guid.NewGuid(),
                        OrderId = orderId,
                        Provider = provider,
                        FuelTypeId = fuelTypeId,
                        Liters = liters,
                        Quantity = quantity,
                        UnitPrice = 2500,
                        LineTotal = quantity * 2500
                    }
                }
            });

            for (var i = 0; i < availableCount; i++)
            {
                seed.FuelVouchers.Add(new FuelVoucher
                {
                    Id = Guid.NewGuid(),
                    Provider = provider,
                    FuelTypeId = fuelTypeId,
                    Liters = liters,
                    ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
                    VoucherNumber = $"OKKO-{orderId:N}-{i:D3}",
                    QrPayload = $"payload-{orderId:N}-{i:D3}",
                    Status = VoucherStatus.Available,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                });
            }

            await seed.SaveChangesAsync();
        }

        var barrier = new DualBarrier();
        var monobankMock = new Mock<IMonobankClient>();
        monobankMock.Setup(m => m.CancelInvoiceAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonobankCancelResponse { Status = "processing" });

        using (var serviceA = CreateService(barrier, monobankMock.Object))
        using (var serviceB = CreateService(barrier, monobankMock.Object))
        {
            await Task.WhenAll(
                serviceA.ProcessPendingOrdersAsync(),
                serviceB.ProcessPendingOrdersAsync());
        }

        using var verify = CreateContext();
        var fulfillments = await verify.Fulfillments
            .Where(f => f.OrderId == orderId)
            .ToListAsync();
        fulfillments.Should().HaveCount(quantity);

        var order = await verify.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId);
        order.Should().NotBeNull();
        order!.Status.Should().Be(OrderStatus.Fulfilled);

        var assignedVouchers = await verify.FuelVouchers
            .AsNoTracking()
            .Where(v => v.AssignedToUserId == userId && v.Status == VoucherStatus.Assigned)
            .ToListAsync();
        assignedVouchers.Should().HaveCount(quantity);

        var assignedVoucherIds = assignedVouchers.Select(v => v.Id).ToHashSet();
        var fulfilledVoucherIds = fulfillments.Select(f => f.VoucherId).ToHashSet();
        assignedVoucherIds.Should().BeEquivalentTo(fulfilledVoucherIds);
    }

    [Fact]
    public async Task ConcurrentJobsWorkerRuns_AssignExactlyTheOrderedVouchers()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        const string provider = "OKKO";
        const string fuelTypeId = "okko-95";
        const decimal liters = 50m;
        const int quantity = 3;
        const int availableCount = 200;

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();

            seed.Users.Add(new User
            {
                Id = userId,
                PhoneNumber = $"+38{userId:N}"[..20],
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });

            seed.Orders.Add(new Order
            {
                Id = orderId,
                UserId = userId,
                Price = quantity * 2500,
                Status = OrderStatus.PendingFulfillment,
                CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
                UpdatedAtUtc = DateTime.UtcNow.AddDays(-1),
                LineItems = new List<OrderLineItem>
                {
                    new OrderLineItem
                    {
                        Id = Guid.NewGuid(),
                        OrderId = orderId,
                        Provider = provider,
                        FuelTypeId = fuelTypeId,
                        Liters = liters,
                        Quantity = quantity,
                        UnitPrice = 2500,
                        LineTotal = quantity * 2500
                    }
                }
            });

            for (var i = 0; i < availableCount; i++)
            {
                seed.FuelVouchers.Add(new FuelVoucher
                {
                    Id = Guid.NewGuid(),
                    Provider = provider,
                    FuelTypeId = fuelTypeId,
                    Liters = liters,
                    ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
                    VoucherNumber = $"JW-{orderId:N}-{i:D3}",
                    QrPayload = $"payload-{orderId:N}-{i:D3}",
                    Status = VoucherStatus.Available,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                });
            }

            await seed.SaveChangesAsync();
        }

        var barrier = new DualBarrier();

        using (var serviceA = CreateJobsWorkerService(barrier))
        using (var serviceB = CreateJobsWorkerService(barrier))
        {
            await Task.WhenAll(
                serviceA.ProcessPendingOrdersAsync(),
                serviceB.ProcessPendingOrdersAsync());
        }

        using var verify = CreateContext();
        var fulfillments = await verify.Fulfillments
            .Where(f => f.OrderId == orderId)
            .ToListAsync();
        fulfillments.Should().HaveCount(quantity);

        var order = await verify.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId);
        order.Should().NotBeNull();
        order!.Status.Should().Be(OrderStatus.Fulfilled);

        var assignedVouchers = await verify.FuelVouchers
            .AsNoTracking()
            .Where(v => v.AssignedToUserId == userId && v.Status == VoucherStatus.Assigned)
            .ToListAsync();
        assignedVouchers.Should().HaveCount(quantity);

        var assignedVoucherIds = assignedVouchers.Select(v => v.Id).ToHashSet();
        var fulfilledVoucherIds = fulfillments.Select(f => f.VoucherId).ToHashSet();
        assignedVoucherIds.Should().BeEquivalentTo(fulfilledVoucherIds);
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.DbContainer.GetConnectionString())
            .Options);

    private ConcurrentFulfillmentService CreateService(DualBarrier barrier, IMonobankClient monobankClient)
    {
        var context = CreateContext();
        var refundHandler = new RefundOrderCommandHandler(
            context,
            monobankClient,
            new ProviderEventService(context));
        return new ConcurrentFulfillmentService(
            context,
            barrier,
            NullLogger<FuelFlow.API.BackgroundJobs.FulfillmentService>.Instance,
            refundHandler);
    }

    private ConcurrentJobsWorkerFulfillmentService CreateJobsWorkerService(DualBarrier barrier)
        => new(
            CreateContext(),
            barrier,
            NullLogger<JobsWorker::FuelFlow.JobsWorker.Services.FulfillmentService>.Instance);

    /// <summary>
    /// Hooks the first voucher-claim so two service instances are guaranteed to both be
    /// "in flight" (past the already-assigned count read) before either assigns. Without the
    /// per-order lock this deterministically over-assigns; with it the second instance blocks
    /// on the advisory lock and never reaches the hook.
    /// </summary>
    private sealed class ConcurrentFulfillmentService : FuelFlow.API.BackgroundJobs.FulfillmentService, IDisposable
    {
        private readonly DualBarrier _barrier;
        private readonly ApplicationDbContext _context;

        public ConcurrentFulfillmentService(
            ApplicationDbContext context,
            DualBarrier barrier,
            ILogger<FuelFlow.API.BackgroundJobs.FulfillmentService> logger,
            RefundOrderCommandHandler refundHandler)
            : base(context, logger, refundHandler)
        {
            _context = context;
            _barrier = barrier;
        }

        public void Dispose()
        {
            _context.Dispose();
        }

        protected internal override async Task<int> TryAssignVoucherAsync(Guid voucherId, Guid userId, CancellationToken cancellationToken)
        {
            _barrier.SignalArrival();
            await _barrier.WaitForPeerAsync();
            return await base.TryAssignVoucherAsync(voucherId, userId, cancellationToken);
        }
    }

    /// <summary>
    /// JobsWorker copy of the same barrier-hooked service. Proves the cross-process fix
    /// (advisory lock namespace shared with FuelFlow.API.BackgroundJobs.FulfillmentService)
    /// holds even when two different binaries run the assignment concurrently.
    /// </summary>
    private sealed class ConcurrentJobsWorkerFulfillmentService : JobsWorker::FuelFlow.JobsWorker.Services.FulfillmentService, IDisposable
    {
        private readonly DualBarrier _barrier;
        private readonly ApplicationDbContext _context;

        public ConcurrentJobsWorkerFulfillmentService(
            ApplicationDbContext context,
            DualBarrier barrier,
            ILogger<JobsWorker::FuelFlow.JobsWorker.Services.FulfillmentService> logger)
            : base(context, logger)
        {
            _context = context;
            _barrier = barrier;
        }

        public void Dispose()
        {
            _context.Dispose();
        }

        protected internal override async Task<int> TryAssignVoucherAsync(Guid voucherId, Guid userId, CancellationToken cancellationToken)
        {
            _barrier.SignalArrival();
            await _barrier.WaitForPeerAsync();
            return await base.TryAssignVoucherAsync(voucherId, userId, cancellationToken);
        }
    }

    private sealed class DualBarrier
    {
        private readonly TaskCompletionSource _first =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void SignalArrival()
        {
            _first.TrySetResult();
        }

        public Task WaitForPeerAsync() => _first.Task;
    }
}
