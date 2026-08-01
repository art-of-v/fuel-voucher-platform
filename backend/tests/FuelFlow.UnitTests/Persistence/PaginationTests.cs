using FluentAssertions;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.UnitTests.Persistence;

public sealed class PaginationTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    public PaginationTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public void PagedRequest_ShouldComputeSkip()
    {
        new PagedRequest(Page: 2, PageSize: 10).Skip.Should().Be(10);
        new PagedRequest(Page: 1, PageSize: 50).Skip.Should().Be(0);
        new PagedRequest(Page: 3, PageSize: 25).Skip.Should().Be(50);
    }

    [Fact]
    public async Task ToPagedResultAsync_ShouldPaginate()
    {
        await SeedUsersAsync(25);

        var firstPage = await _context.Users
            .OrderBy(u => u.Id)
            .ToPagedResultAsync(new PagedRequest(Page: 1, PageSize: 10));

        firstPage.Items.Should().HaveCount(10);
        firstPage.TotalCount.Should().Be(25);
        firstPage.Page.Should().Be(1);
        firstPage.PageSize.Should().Be(10);
        firstPage.TotalPages.Should().Be(3);
        firstPage.HasPreviousPage.Should().BeFalse();
        firstPage.HasNextPage.Should().BeTrue();

        var lastPage = await _context.Users
            .OrderBy(u => u.Id)
            .ToPagedResultAsync(new PagedRequest(Page: 3, PageSize: 10));

        lastPage.Items.Should().HaveCount(5);
        lastPage.HasNextPage.Should().BeFalse();
        lastPage.HasPreviousPage.Should().BeTrue();
    }

    [Fact]
    public async Task ToPagedResultAsync_ShouldHandleEmptySource()
    {
        var result = await _context.Users.ToPagedResultAsync(new PagedRequest());

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(50);
        result.TotalPages.Should().Be(0);
        result.HasPreviousPage.Should().BeFalse();
        result.HasNextPage.Should().BeFalse();
    }

    [Fact]
    public async Task ToPagedResultAsync_ShouldClampPageBeyondLast()
    {
        await SeedUsersAsync(25);

        var result = await _context.Users
            .OrderBy(u => u.Id)
            .ToPagedResultAsync(new PagedRequest(Page: 99, PageSize: 10));

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(25);
        result.TotalPages.Should().Be(3);
        result.HasNextPage.Should().BeFalse();
    }

    private async Task SeedUsersAsync(int count)
    {
        for (var i = 0; i < count; i++)
        {
            _context.Users.Add(new User
            {
                Id = Guid.NewGuid(),
                PhoneNumber = $"+38099{i:0000000}",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();
    }
}
