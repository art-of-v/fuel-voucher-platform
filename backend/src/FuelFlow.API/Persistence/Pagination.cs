using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Persistence;

/// <summary>
/// Server-side bounds for any client-supplied page size or row limit.
///
/// Every list endpoint took its page size straight from the query string, so
/// "?pageSize=2000000000" made the server materialize the entire table into one
/// response. On a single-droplet deployment that is an availability lever with a
/// trivial payload, and on the voucher tables it also turns a paged inventory view
/// into a one-request dump of every redeemable voucher number. Clamp centrally so
/// new endpoints inherit the bound instead of each re-deciding it.
/// </summary>
public static class PageLimits
{
    public const int MaxPageSize = 200;

    /// <summary>Clamps a client-supplied page size / limit into [1, MaxPageSize].</summary>
    public static int ClampPageSize(int pageSize) => Math.Clamp(pageSize, 1, MaxPageSize);

    /// <summary>Clamps a client-supplied 1-based page number to at least 1.</summary>
    public static int ClampPage(int page) => page < 1 ? 1 : page;

    /// <summary>Clamps a client-supplied 0-based row offset to at least 0.</summary>
    public static int ClampOffset(int offset) => offset < 0 ? 0 : offset;

    /// <summary>
    /// Computes a non-negative OFFSET for a 1-based page number. Both inputs must
    /// already be clamped. The multiplication is done in 64-bit because
    /// (int.MaxValue - 1) * MaxPageSize overflows int and would wrap to a negative
    /// offset, which Postgres rejects outright with a 500.
    /// </summary>
    public static int SkipFor(int page, int pageSize)
    {
        var skip = (long)(page - 1) * pageSize;
        return skip > int.MaxValue ? int.MaxValue : (int)skip;
    }
}

public record PagedRequest
{
    // Parameter names stay PascalCase: callers and PaginationTests use named arguments
    // (new PagedRequest(Page: 2, PageSize: 10)), which the original positional record
    // exposed. `this.` is required because the parameters shadow the properties.
    public PagedRequest(int Page = 1, int PageSize = 50)
    {
        this.Page = PageLimits.ClampPage(Page);
        this.PageSize = PageLimits.ClampPageSize(PageSize);
    }

    // Get-only, not `init`: an init accessor would let `new PagedRequest() with
    // { PageSize = 1_000_000 }` write past the clamp, reopening the hole the constructor
    // closes. Nothing in the codebase uses `with` on this type.
    public int Page { get; }
    public int PageSize { get; }

    public int Skip => PageLimits.SkipFor(Page, PageSize);
}

public class PagedResult<T>
{
    public List<T> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;
}

public static class PagingExtensions
{
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> source, PagedRequest request, CancellationToken ct = default)
    {
        var totalCount = await source.CountAsync(ct);
        var items = await source
            .Skip(request.Skip)
            .Take(request.PageSize)
            .ToListAsync(ct);

        return new PagedResult<T>
        {
            Items = items,
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize
        };
    }
}
