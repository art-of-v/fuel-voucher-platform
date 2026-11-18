using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessLeads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "business_leads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "The company or fleet asking Fuel Flow to contact it."),
                    contact_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "The person Fuel Flow should ask for when following up."),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, comment: "The contact phone in explicit E.164 form, validated at the public edge."),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true, comment: "Optional reply address supplied by the contact and validated at the public edge."),
                    fleet_size = table.Column<int>(type: "integer", nullable: true, comment: "Optional number of vehicles the contact says the company operates."),
                    message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true, comment: "Optional business need the contact wrote in their own words."),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, comment: "Where the sales enquiry stands: New, Contacted, Qualified, Converted or Rejected."),
                    user_agent = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true, comment: "The submitting browser User-Agent, truncated and retained only for abuse diagnostics."),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true, comment: "The submitting client IP after trusted proxy processing; retained only for abuse diagnostics."),
                    email_sent_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, comment: "NULL until the sales notification was handed to SMTP successfully."),
                    email_send_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true, comment: "The latest sales-notification failure, truncated; NULL after success or when delivery was not attempted."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "When the public business enquiry was accepted."),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "When the lead last moved in the sales pipeline; terminal retention is measured from this time.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_leads", x => x.id);
                    table.CheckConstraint("ck_business_lead_fleet_size", "fleet_size IS NULL OR fleet_size > 0");
                    table.CheckConstraint("ck_business_lead_status", "status IN ('New', 'Contacted', 'Qualified', 'Converted', 'Rejected')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_business_leads_status_created_at_utc_id",
                table: "business_leads",
                columns: new[] { "status", "created_at_utc", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_business_leads_status_updated_at_utc",
                table: "business_leads",
                columns: new[] { "status", "updated_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "business_leads");
        }
    }
}
