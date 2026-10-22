namespace FuelFlow.SharedKernel.Database;

/// <summary>
/// Whose rows the current unit of work may see.
/// </summary>
public enum TenantScopeKind
{
    Anonymous,
    User,
    System
}

/// <summary>
/// The tenant identity a request or a background job runs as.
/// </summary>
public readonly record struct TenantScope(TenantScopeKind Kind, Guid? UserId)
{
    public static readonly TenantScope Anonymous = new(TenantScopeKind.Anonymous, null);

    public static readonly TenantScope System = new(TenantScopeKind.System, null);

    public static TenantScope ForUser(Guid userId) => new(TenantScopeKind.User, userId);
}

/// <summary>
/// Holds the <see cref="TenantScope"/> for the current DI scope and decides what an unscoped
/// unit of work is.
/// </summary>
/// <remarks>
/// A request gets its scope from <see cref="FuelFlow.Middleware.TenantScopeMiddleware"/>. Code
/// outside a request - a Hangfire job, the outbox dispatcher, the product-owner bootstrap - is
/// the system: it is the application itself acting across tenants, not a customer's request.
/// The distinction is made through <see cref="IHttpContextAccessor"/> rather than a default,
/// because "no scope yet" inside a request must mean no rows, not every row.
/// </remarks>
public sealed class TenantScopeAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    private TenantScope? _scope;

    public TenantScopeAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public TenantScope Current => _scope ??= ResolveWithoutRequest();

    public void Set(TenantScope scope) => _scope = scope;

    private TenantScope ResolveWithoutRequest() =>
        _httpContextAccessor.HttpContext is null ? TenantScope.System : TenantScope.Anonymous;
}

/// <summary>
/// Marks an endpoint that acts as the system rather than on behalf of a customer.
/// </summary>
/// <remarks>
/// The Monobank and Telegram settlement webhooks are anonymous by necessity and resolve their
/// own trust from a signature or a secret. They are the two anonymous endpoints that write or
/// read a table the row-level policies protect, so they must run as the system - and the scope
/// has to be a property of the endpoint rather than a list of paths in the middleware, or a new
/// webhook would silently run as an anonymous customer.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class SystemScopeAttribute : Attribute;
