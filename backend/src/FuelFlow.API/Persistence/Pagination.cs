using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Persistence;

public record PagedRequest(int Page = 1, int PageSize = 50)
{
    public int Skip => (Page - 1) * PageSize;
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
