using Microsoft.EntityFrameworkCore;

namespace FuelFlow.SharedKernel.Database;

/// <summary>
/// The names of the model-level query filters. Named filters are what make a bypass narrow: an
/// <c>IgnoreQueryFilters()</c> with no names lifts every filter on every entity in the query,
/// including one a future change adds, while these keys lift only the filter they name.
/// </summary>
public static class QueryFilterKeys
{
    /// <summary>The global <c>!IsDeleted</c> filter on the soft-deletable tables.</summary>
    public const string SoftDelete = "SoftDelete";

    /// <summary>The global <c>ReleasedAtUtc == null</c> filter on fulfilments.</summary>
    public const string ReleasedFulfilment = "ReleasedFulfilment";
}

/// <summary>
/// The named query-filter scopes a read path may ask for. Every call site is listed with its
/// reason in <c>docs/QUERY_FILTERS.md</c>, and <c>QueryFilterBypassGuardTests</c> fails the build
/// when a scope is used at a site the inventory does not explain.
/// </summary>
/// <remarks>
/// These bypasses are EF-level read policies only. The tenant boundary is row-level security in
/// the database (<see cref="TenantScope"/>), which no <c>IgnoreQueryFilters</c> can lift - the
/// integration test <c>TenantRowLevelSecurityIntegrationTests.Bypassing_the_model_filters_does_not_bypass_the_policies</c>
/// pins that.
/// </remarks>
public static class QueryFilterScopes
{
    /// <summary>
    /// Reads soft-deleted rows as well as live ones - an admin list, an audit, a restore, or a
    /// background job whose subject is the deleted row itself. Only the soft-delete filter is
    /// lifted; the released-fulfilment filter still applies.
    /// </summary>
    public static IQueryable<T> IncludingSoftDeleted<T>(this IQueryable<T> source) where T : class
        => source.IgnoreQueryFilters([QueryFilterKeys.SoftDelete]);

    /// <summary>
    /// Reads released fulfilments as well as live ones - history, and the done-marker of a renewal
    /// whose delivered voucher was later handed back. Only the released-fulfilment filter is
    /// lifted; soft-deleted rows stay hidden.
    /// </summary>
    public static IQueryable<T> IncludingReleasedFulfilments<T>(this IQueryable<T> source) where T : class
        => source.IgnoreQueryFilters([QueryFilterKeys.ReleasedFulfilment]);

    /// <summary>
    /// Both of the above, for the few reads that span a soft-deleted order and its released
    /// fulfilments together - the admin order detail, the refund, and the webhook that restores a
    /// deleted order.
    /// </summary>
    public static IQueryable<T> IncludingSoftDeletedAndReleasedFulfilments<T>(this IQueryable<T> source)
        where T : class
        => source.IgnoreQueryFilters([QueryFilterKeys.SoftDelete, QueryFilterKeys.ReleasedFulfilment]);
}
