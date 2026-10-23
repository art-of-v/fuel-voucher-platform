using FluentAssertions;
using Npgsql;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// What the database lets the application's own roles do - asked of the database, as those
/// roles, over a real connection.
/// </summary>
/// <remarks>
/// <para>
/// The rest of the suite connects as the container's superuser, so it cannot tell a privilege
/// that was never revoked from one that was. This class connects as the roles production
/// connects as: <c>fuelflow_app</c> for the traffic and <c>fuelflow_maintenance</c> for the
/// retention purge. The grants it asserts are the ones
/// <c>deploy/database-privileges.tsv</c> states, so the matrix is the specification and this
/// is the proof that the file and the database agree.
/// </para>
/// <para>
/// Every probe that is expected to fail says <c>WHERE false</c>: it is a privilege check, not
/// a data operation - no row is read, written or locked, and the test leaves nothing behind.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class AppRolePrivilegeGuardTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public AppRolePrivilegeGuardTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task The_traffic_connection_is_the_traffic_role()
    {
        await using var connection = await OpenAsync(_fixture.AppConnectionString);

        (await ScalarAsync(connection, "SELECT current_user")).Should().Be(
            "fuelflow_app",
            "the application's traffic connects as the role the deploy script creates");
    }

    [Fact]
    public async Task The_maintenance_connection_is_the_maintenance_role()
    {
        await using var connection = await OpenAsync(_fixture.MaintenanceConnectionString);

        (await ScalarAsync(connection, "SELECT current_user")).Should().Be(
            "fuelflow_maintenance",
            "the retention purge connects as the role the deploy script creates");
    }

    [Fact]
    public async Task The_traffic_role_may_not_rewrite_the_voucher_audit_trail()
    {
        await using var connection = await OpenAsync(_fixture.AppConnectionString);

        await DeniedAsync(
            connection,
            "UPDATE public.voucher_events SET occurred_at_utc = occurred_at_utc WHERE false",
            "the voucher trail is written once: a redemption nobody can rewrite is a redemption "
            + "the money reconciliation can trust");
    }

    [Fact]
    public async Task The_traffic_role_may_not_delete_a_payment()
    {
        await using var connection = await OpenAsync(_fixture.AppConnectionString);

        await DeniedAsync(
            connection,
            "DELETE FROM public.payments WHERE false",
            "a payment a defect can erase is a payment the reconciliation cannot trust");
    }

    [Fact]
    public async Task The_traffic_role_may_not_delete_the_provider_audit_trail()
    {
        await using var connection = await OpenAsync(_fixture.AppConnectionString);

        await DeniedAsync(
            connection,
            "DELETE FROM public.provider_event_outbox WHERE false",
            "the admin change audit trail is purged by the retention job, not by the traffic role");
    }

    [Fact]
    public async Task The_traffic_role_may_close_an_outbox_event_and_may_not_delete_it()
    {
        await using var connection = await OpenAsync(_fixture.AppConnectionString);

        await AllowedAsync(
            connection,
            "UPDATE public.outbox_events SET processed = true, processed_at_utc = now() WHERE false",
            "the dispatcher closes an event exactly this way");

        await AllowedAsync(
            connection,
            "UPDATE public.outbox_events SET event_type = event_type WHERE false",
            "EF Core's Update(entity) writes every column of the row when the dispatcher closes it, "
            + "so the matrix grants the update on the table and not on two columns - a column-level "
            + "grant would refuse the dispatcher's own save");

        await DeniedAsync(
            connection,
            "DELETE FROM public.outbox_events WHERE false",
            "the purge belongs to the maintenance role");
    }

    [Fact]
    public async Task The_traffic_role_may_settle_a_webhook_delivery_and_may_not_rewrite_it()
    {
        await using var connection = await OpenAsync(_fixture.AppConnectionString);

        await AllowedAsync(
            connection,
            "UPDATE public.webhook_events SET status = 'Processed', processed_at_utc = now() WHERE false",
            "the recorder settles a delivery by writing exactly the outcome columns");

        await DeniedAsync(
            connection,
            "UPDATE public.webhook_events SET payload = payload WHERE false",
            "the body exactly as received is the evidence the delivery is worth: a role that may "
            + "rewrite it may rewrite the evidence");
    }

    [Fact]
    public async Task The_maintenance_role_may_purge_and_may_not_write()
    {
        await using var connection = await OpenAsync(_fixture.MaintenanceConnectionString);

        await AllowedAsync(
            connection,
            "DELETE FROM public.outbox_events WHERE false",
            "the retention job empties the outbox, and this is the role it does it as");

        await DeniedAsync(
            connection,
            "UPDATE public.outbox_events SET processed = true WHERE false",
            "the maintenance role holds the destructive half of the lifecycle only");

        await DeniedAsync(
            connection,
            "INSERT INTO public.outbox_events SELECT * FROM public.outbox_events WHERE false",
            "a role that may erase the outbox must not be able to forge one in it");
    }

    [Fact]
    public async Task The_maintenance_role_may_not_touch_the_tables_the_matrix_leaves_it()
    {
        await using var connection = await OpenAsync(_fixture.MaintenanceConnectionString);

        await DeniedAsync(
            connection,
            "SELECT count(*) FROM public.brands",
            "the catalogue is product data: the purge has no business reading it");

        await DeniedAsync(
            connection,
            "DELETE FROM public.payments WHERE false",
            "payments are not a retention table - no window was ever agreed for them");
    }

    [Fact]
    public async Task The_traffic_role_may_not_change_the_schema()
    {
        await using var connection = await OpenAsync(_fixture.AppConnectionString);

        await DeniedAsync(
            connection,
            "CREATE TABLE public.app_role_guard_probe(id integer)",
            "the schema belongs to the migration role");

        await DeniedAsync(
            connection,
            "CREATE ROLE app_role_guard_probe LOGIN",
            "a role that may create a role may create itself a superuser");
    }

    private static async Task<NpgsqlConnection> OpenAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(connectionString)
        {
            Pooling = false,
        }.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<string?> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (string?)await command.ExecuteScalarAsync();
    }

    private static async Task AllowedAsync(NpgsqlConnection connection, string sql, string because)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var act = async () => await command.ExecuteNonQueryAsync();
        await act.Should().NotThrowAsync(because);
    }

    private static async Task DeniedAsync(NpgsqlConnection connection, string sql, string because)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var act = async () => await command.ExecuteNonQueryAsync();
        await act.Should().ThrowAsync<PostgresException>(because);
    }
}
