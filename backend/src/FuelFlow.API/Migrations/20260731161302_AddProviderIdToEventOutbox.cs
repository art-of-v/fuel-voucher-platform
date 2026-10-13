using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderIdToEventOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "provider_id",
                table: "provider_event_outbox",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE provider_event_outbox
                SET provider_id = aggregate_id
                WHERE aggregate_type IN ('Provider', 'Nominal');
                """);

            migrationBuilder.Sql("""
                UPDATE provider_event_outbox e
                SET provider_id = f.station_id
                FROM fuel_types f
                WHERE e.aggregate_type = 'Fuel' AND e.aggregate_id = f.id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "provider_id",
                table: "provider_event_outbox");
        }
    }
}
