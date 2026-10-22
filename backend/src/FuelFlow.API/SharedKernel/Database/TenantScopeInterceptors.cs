using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FuelFlow.SharedKernel.Database;

/// <summary>
/// Writes the tenant scope into the PostgreSQL session before the first statement can read a
/// protected table.
/// </summary>
/// <remarks>
/// The pool runs with <c>No Reset On Close=true</c> (see <see cref="DatabaseConnectionPool"/>),
/// so a returned connection keeps the last scope it was given. That is safe only because every
/// open overwrites both variables - including the anonymous and system cases, which write empty
/// values rather than leaving the previous request's identity behind.
/// </remarks>
public sealed class TenantConnectionInterceptor : DbConnectionInterceptor
{
    private readonly TenantScopeAccessor _scopeAccessor;

    public TenantConnectionInterceptor(TenantScopeAccessor scopeAccessor)
    {
        _scopeAccessor = scopeAccessor;
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        TenantSessionVariables.Apply(
            connection,
            transaction: null,
            _scopeAccessor.Current,
            isLocal: false);
    }

    public override Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        return TenantSessionVariables.ApplyAsync(
            connection,
            transaction: null,
            _scopeAccessor.Current,
            isLocal: false,
            cancellationToken);
    }
}

/// <summary>
/// Re-asserts the tenant scope as transaction-local state.
/// </summary>
/// <remarks>
/// The connection-level value is the one non-transactional reads use, but it can be older than
/// the transaction: a connection opened by middleware that ran before the tenant was resolved
/// keeps that older value until it is reopened. Writing the scope with <c>is_local: true</c> at
/// the start of every transaction removes that window for the money paths, where a wrong scope
/// is worst.
/// </remarks>
public sealed class TenantTransactionInterceptor : DbTransactionInterceptor
{
    private readonly TenantScopeAccessor _scopeAccessor;

    public TenantTransactionInterceptor(TenantScopeAccessor scopeAccessor)
    {
        _scopeAccessor = scopeAccessor;
    }

    public override DbTransaction TransactionStarted(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result)
    {
        TenantSessionVariables.Apply(connection, result, _scopeAccessor.Current, isLocal: true);
        return result;
    }

    public override async ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        await TenantSessionVariables.ApplyAsync(
            connection,
            result,
            _scopeAccessor.Current,
            isLocal: true,
            cancellationToken);

        return result;
    }
}

internal static class TenantSessionVariables
{
    internal const string UserIdSetting = "app.user_id";

    internal const string IsSystemSetting = "app.is_system";

    internal const string Sql =
        "SELECT set_config('" + UserIdSetting + "', @user_id, @is_local), " +
        "set_config('" + IsSystemSetting + "', @is_system, @is_local)";

    internal static void Apply(
        DbConnection connection,
        DbTransaction? transaction,
        TenantScope scope,
        bool isLocal)
    {
        using var command = Create(connection, transaction, scope, isLocal);
        command.ExecuteNonQuery();
    }

    internal static async Task ApplyAsync(
        DbConnection connection,
        DbTransaction? transaction,
        TenantScope scope,
        bool isLocal,
        CancellationToken cancellationToken)
    {
        await using var command = Create(connection, transaction, scope, isLocal);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static DbCommand Create(
        DbConnection connection,
        DbTransaction? transaction,
        TenantScope scope,
        bool isLocal)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = Sql;

        Add(command, "user_id", scope.Kind == TenantScopeKind.User
            ? scope.UserId?.ToString("D") ?? ""
            : "");

        Add(command, "is_system", scope.Kind == TenantScopeKind.System ? "on" : "off");
        Add(command, "is_local", isLocal);

        return command;
    }

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
