using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fuel_vouchers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    fuel_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    liters = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    expiration_date = table.Column<DateOnly>(type: "date", nullable: false),
                    voucher_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    qr_payload = table.Column<string>(type: "text", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    fuel_subtype = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    redemption_rules = table.Column<string>(type: "text", nullable: true),
                    image_url = table.Column<string>(type: "text", nullable: true),
                    assigned_to_user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    import_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fuel_vouchers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fulfillments",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    voucher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fulfilled_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fulfillments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    product_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    fuel_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    liters = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    price = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    monobank_invoice_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    monobank_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fulfilled_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_orders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_events",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    processed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    processed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "verification_codes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    phone_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_used = table.Column<bool>(type: "boolean", nullable: false),
                    used_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_verification_codes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "voucher_imports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "text", nullable: false),
                    page_count = table.Column<int>(type: "integer", nullable: false),
                    started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    imported_count = table.Column<int>(type: "integer", nullable: false),
                    duplicate_count = table.Column<int>(type: "integer", nullable: false),
                    failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_voucher_imports", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    phone_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_login_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    referral_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    referred_by = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    bonus_balance = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                    table.ForeignKey(
                        name: "FK_users_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "voucher_import_errors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    import_id = table.Column<Guid>(type: "uuid", nullable: false),
                    page_number = table.Column<int>(type: "integer", nullable: false),
                    voucher_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: false),
                    raw_text = table.Column<string>(type: "text", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_voucher_import_errors", x => x.id);
                    table.ForeignKey(
                        name: "FK_voucher_import_errors_voucher_imports_import_id",
                        column: x => x.import_id,
                        principalTable: "voucher_imports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_revoked = table.Column<bool>(type: "boolean", nullable: false),
                    revoked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "FK_refresh_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_assigned_to_user_id",
                table: "fuel_vouchers",
                column: "assigned_to_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_expiration_date",
                table: "fuel_vouchers",
                column: "expiration_date");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_fuel_type",
                table: "fuel_vouchers",
                column: "fuel_type");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_provider",
                table: "fuel_vouchers",
                column: "provider");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_provider_fuel_type_liters_status",
                table: "fuel_vouchers",
                columns: new[] { "provider", "fuel_type", "liters", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_qr_payload",
                table: "fuel_vouchers",
                column: "qr_payload",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_status",
                table: "fuel_vouchers",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_voucher_number",
                table: "fuel_vouchers",
                column: "voucher_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fulfillments_order_id",
                table: "fulfillments",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_fulfillments_voucher_id",
                table: "fulfillments",
                column: "voucher_id");

            migrationBuilder.CreateIndex(
                name: "IX_orders_created_at_utc",
                table: "orders",
                column: "created_at_utc");

            migrationBuilder.CreateIndex(
                name: "IX_orders_idempotency_key",
                table: "orders",
                column: "idempotency_key",
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_orders_status",
                table: "orders",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_orders_user_id",
                table: "orders",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_events_created_at_utc",
                table: "outbox_events",
                column: "created_at_utc");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_events_processed",
                table: "outbox_events",
                column: "processed");

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_token",
                table: "refresh_tokens",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_user_id",
                table: "refresh_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_roles_name",
                table: "roles",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_phone_number",
                table: "users",
                column: "phone_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_referral_code",
                table: "users",
                column: "referral_code",
                unique: true,
                filter: "referral_code IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_users_role_id",
                table: "users",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "IX_verification_codes_expires_at_utc",
                table: "verification_codes",
                column: "expires_at_utc");

            migrationBuilder.CreateIndex(
                name: "IX_verification_codes_phone_number",
                table: "verification_codes",
                column: "phone_number");

            migrationBuilder.CreateIndex(
                name: "IX_voucher_import_errors_import_id",
                table: "voucher_import_errors",
                column: "import_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fuel_vouchers");

            migrationBuilder.DropTable(
                name: "fulfillments");

            migrationBuilder.DropTable(
                name: "orders");

            migrationBuilder.DropTable(
                name: "outbox_events");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "verification_codes");

            migrationBuilder.DropTable(
                name: "voucher_import_errors");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "voucher_imports");

            migrationBuilder.DropTable(
                name: "roles");
        }
    }
}
