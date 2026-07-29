using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class ApplySnakeCaseConvention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_contracts_legal_entities_legal_entity_id",
                table: "contracts");

            migrationBuilder.DropForeignKey(
                name: "FK_contracts_stations_station_id",
                table: "contracts");

            migrationBuilder.DropForeignKey(
                name: "FK_contracts_users_user_id",
                table: "contracts");

            migrationBuilder.DropForeignKey(
                name: "FK_devices_users_user_id",
                table: "devices");

            migrationBuilder.DropForeignKey(
                name: "FK_fuel_vouchers_fuel_types_fuel_type_id",
                table: "fuel_vouchers");

            migrationBuilder.DropForeignKey(
                name: "FK_fuel_vouchers_qr_parameters_qr_parameters_id",
                table: "fuel_vouchers");

            migrationBuilder.DropForeignKey(
                name: "FK_fuel_vouchers_users_assigned_to_user_id",
                table: "fuel_vouchers");

            migrationBuilder.DropForeignKey(
                name: "FK_fuel_vouchers_voucher_imports_import_job_id",
                table: "fuel_vouchers");

            migrationBuilder.DropForeignKey(
                name: "FK_fulfillments_fuel_vouchers_voucher_id",
                table: "fulfillments");

            migrationBuilder.DropForeignKey(
                name: "FK_fulfillments_orders_order_id",
                table: "fulfillments");

            migrationBuilder.DropForeignKey(
                name: "FK_legal_entities_users_user_id",
                table: "legal_entities");

            migrationBuilder.DropForeignKey(
                name: "FK_notifications_users_user_id",
                table: "notifications");

            migrationBuilder.DropForeignKey(
                name: "FK_order_line_items_orders_order_id",
                table: "order_line_items");

            migrationBuilder.DropForeignKey(
                name: "FK_refresh_tokens_users_user_id",
                table: "refresh_tokens");

            migrationBuilder.DropForeignKey(
                name: "FK_station_nodes_stations_station_id",
                table: "station_nodes");

            migrationBuilder.DropForeignKey(
                name: "FK_user_contracts_contracts_contract_id",
                table: "user_contracts");

            migrationBuilder.DropForeignKey(
                name: "FK_user_contracts_users_user_id",
                table: "user_contracts");

            migrationBuilder.DropForeignKey(
                name: "FK_users_roles_role_id",
                table: "users");

            migrationBuilder.DropForeignKey(
                name: "FK_voucher_import_errors_voucher_imports_import_id",
                table: "voucher_import_errors");

            migrationBuilder.DropPrimaryKey(
                name: "PK_voucher_imports",
                table: "voucher_imports");

            migrationBuilder.DropPrimaryKey(
                name: "PK_voucher_import_errors",
                table: "voucher_import_errors");

            migrationBuilder.DropPrimaryKey(
                name: "PK_verification_codes",
                table: "verification_codes");

            migrationBuilder.DropPrimaryKey(
                name: "PK_users",
                table: "users");

            migrationBuilder.DropPrimaryKey(
                name: "PK_user_contracts",
                table: "user_contracts");

            migrationBuilder.DropPrimaryKey(
                name: "PK_stations",
                table: "stations");

            migrationBuilder.DropIndex(
                name: "IX_stations_name",
                table: "stations");

            migrationBuilder.DropIndex(
                name: "IX_stations_station_type",
                table: "stations");

            migrationBuilder.DropPrimaryKey(
                name: "PK_station_nodes",
                table: "station_nodes");

            migrationBuilder.DropPrimaryKey(
                name: "PK_roles",
                table: "roles");

            migrationBuilder.DropPrimaryKey(
                name: "PK_refresh_tokens",
                table: "refresh_tokens");

            migrationBuilder.DropPrimaryKey(
                name: "PK_qr_parameters",
                table: "qr_parameters");

            migrationBuilder.DropPrimaryKey(
                name: "PK_provider_event_outbox",
                table: "provider_event_outbox");

            migrationBuilder.DropPrimaryKey(
                name: "PK_outbox_events",
                table: "outbox_events");

            migrationBuilder.DropPrimaryKey(
                name: "PK_orders",
                table: "orders");

            migrationBuilder.DropPrimaryKey(
                name: "PK_order_line_items",
                table: "order_line_items");

            migrationBuilder.DropIndex(
                name: "IX_order_line_items_fuel_type_id",
                table: "order_line_items");

            migrationBuilder.DropIndex(
                name: "IX_order_line_items_provider",
                table: "order_line_items");

            migrationBuilder.DropPrimaryKey(
                name: "PK_notifications",
                table: "notifications");

            migrationBuilder.DropPrimaryKey(
                name: "PK_legal_entities",
                table: "legal_entities");

            migrationBuilder.DropPrimaryKey(
                name: "PK_fulfillments",
                table: "fulfillments");

            migrationBuilder.DropPrimaryKey(
                name: "PK_fuel_vouchers",
                table: "fuel_vouchers");

            migrationBuilder.DropPrimaryKey(
                name: "PK_fuel_types",
                table: "fuel_types");

            migrationBuilder.DropIndex(
                name: "IX_fuel_types_name",
                table: "fuel_types");

            migrationBuilder.DropIndex(
                name: "IX_fuel_types_station_id_name",
                table: "fuel_types");

            migrationBuilder.DropPrimaryKey(
                name: "PK_fuel_packages",
                table: "fuel_packages");

            migrationBuilder.DropIndex(
                name: "IX_fuel_packages_station_id_fuel_type_id",
                table: "fuel_packages");

            migrationBuilder.DropPrimaryKey(
                name: "PK_devices",
                table: "devices");

            migrationBuilder.DropPrimaryKey(
                name: "PK_contracts",
                table: "contracts");

            migrationBuilder.RenameIndex(
                name: "IX_voucher_imports_status",
                table: "voucher_imports",
                newName: "ix_voucher_imports_status");

            migrationBuilder.RenameIndex(
                name: "IX_voucher_imports_started_at_utc",
                table: "voucher_imports",
                newName: "ix_voucher_imports_started_at_utc");

            migrationBuilder.RenameIndex(
                name: "IX_voucher_import_errors_import_id",
                table: "voucher_import_errors",
                newName: "ix_voucher_import_errors_import_id");

            migrationBuilder.RenameIndex(
                name: "IX_verification_codes_phone_number_is_used_expires_at_utc",
                table: "verification_codes",
                newName: "ix_verification_codes_phone_number_is_used_expires_at_utc");

            migrationBuilder.RenameIndex(
                name: "IX_verification_codes_phone_number",
                table: "verification_codes",
                newName: "ix_verification_codes_phone_number");

            migrationBuilder.RenameIndex(
                name: "IX_verification_codes_expires_at_utc",
                table: "verification_codes",
                newName: "ix_verification_codes_expires_at_utc");

            migrationBuilder.RenameIndex(
                name: "IX_users_role_id",
                table: "users",
                newName: "ix_users_role_id");

            migrationBuilder.RenameIndex(
                name: "IX_users_referral_code",
                table: "users",
                newName: "ix_users_referral_code");

            migrationBuilder.RenameIndex(
                name: "IX_users_phone_number",
                table: "users",
                newName: "ix_users_phone_number");

            migrationBuilder.RenameIndex(
                name: "IX_user_contracts_user_id",
                table: "user_contracts",
                newName: "ix_user_contracts_user_id");

            migrationBuilder.RenameIndex(
                name: "IX_user_contracts_contract_id",
                table: "user_contracts",
                newName: "ix_user_contracts_contract_id");

            migrationBuilder.RenameIndex(
                name: "IX_station_nodes_station_id",
                table: "station_nodes",
                newName: "ix_station_nodes_station_id");

            migrationBuilder.RenameIndex(
                name: "IX_roles_name",
                table: "roles",
                newName: "ix_roles_name");

            migrationBuilder.RenameIndex(
                name: "IX_refresh_tokens_user_id_is_revoked",
                table: "refresh_tokens",
                newName: "ix_refresh_tokens_user_id_is_revoked");

            migrationBuilder.RenameIndex(
                name: "IX_refresh_tokens_user_id",
                table: "refresh_tokens",
                newName: "ix_refresh_tokens_user_id");

            migrationBuilder.RenameIndex(
                name: "IX_refresh_tokens_token",
                table: "refresh_tokens",
                newName: "ix_refresh_tokens_token");

            migrationBuilder.RenameIndex(
                name: "IX_qr_parameters_ecc_level_version_mask_pattern_encoding_mode",
                table: "qr_parameters",
                newName: "ix_qr_parameters_ecc_level_version_mask_pattern_encoding_mode");

            migrationBuilder.RenameIndex(
                name: "IX_provider_event_outbox_changed_at_utc",
                table: "provider_event_outbox",
                newName: "ix_provider_event_outbox_changed_at_utc");

            migrationBuilder.RenameIndex(
                name: "IX_provider_event_outbox_aggregate_id",
                table: "provider_event_outbox",
                newName: "ix_provider_event_outbox_aggregate_id");

            migrationBuilder.RenameIndex(
                name: "IX_outbox_events_processed",
                table: "outbox_events",
                newName: "ix_outbox_events_processed");

            migrationBuilder.RenameIndex(
                name: "IX_outbox_events_created_at_utc",
                table: "outbox_events",
                newName: "ix_outbox_events_created_at_utc");

            migrationBuilder.RenameIndex(
                name: "IX_orders_user_id_status",
                table: "orders",
                newName: "ix_orders_user_id_status");

            migrationBuilder.RenameIndex(
                name: "IX_orders_user_id_created_at_utc",
                table: "orders",
                newName: "ix_orders_user_id_created_at_utc");

            migrationBuilder.RenameIndex(
                name: "IX_orders_user_id",
                table: "orders",
                newName: "ix_orders_user_id");

            migrationBuilder.RenameIndex(
                name: "IX_orders_status",
                table: "orders",
                newName: "ix_orders_status");

            migrationBuilder.RenameIndex(
                name: "IX_orders_idempotency_key",
                table: "orders",
                newName: "ix_orders_idempotency_key");

            migrationBuilder.RenameIndex(
                name: "IX_orders_created_at_utc",
                table: "orders",
                newName: "ix_orders_created_at_utc");

            migrationBuilder.RenameIndex(
                name: "IX_order_line_items_order_id",
                table: "order_line_items",
                newName: "ix_order_line_items_order_id");

            migrationBuilder.RenameIndex(
                name: "IX_notifications_user_id_is_read",
                table: "notifications",
                newName: "ix_notifications_user_id_is_read");

            migrationBuilder.RenameIndex(
                name: "IX_notifications_user_id",
                table: "notifications",
                newName: "ix_notifications_user_id");

            migrationBuilder.RenameIndex(
                name: "IX_legal_entities_user_id",
                table: "legal_entities",
                newName: "ix_legal_entities_user_id");

            migrationBuilder.RenameIndex(
                name: "IX_legal_entities_edrpou",
                table: "legal_entities",
                newName: "ix_legal_entities_edrpou");

            migrationBuilder.RenameIndex(
                name: "IX_fulfillments_voucher_id",
                table: "fulfillments",
                newName: "ix_fulfillments_voucher_id");

            migrationBuilder.RenameIndex(
                name: "IX_fulfillments_order_id",
                table: "fulfillments",
                newName: "ix_fulfillments_order_id");

            migrationBuilder.RenameIndex(
                name: "IX_fuel_vouchers_voucher_number",
                table: "fuel_vouchers",
                newName: "ix_fuel_vouchers_voucher_number");

            migrationBuilder.RenameIndex(
                name: "IX_fuel_vouchers_status",
                table: "fuel_vouchers",
                newName: "ix_fuel_vouchers_status");

            migrationBuilder.RenameIndex(
                name: "IX_fuel_vouchers_qr_payload",
                table: "fuel_vouchers",
                newName: "ix_fuel_vouchers_qr_payload");

            migrationBuilder.RenameIndex(
                name: "IX_fuel_vouchers_qr_parameters_id",
                table: "fuel_vouchers",
                newName: "ix_fuel_vouchers_qr_parameters_id");

            migrationBuilder.RenameIndex(
                name: "IX_fuel_vouchers_provider_fuel_type_id_liters_status",
                table: "fuel_vouchers",
                newName: "ix_fuel_vouchers_provider_fuel_type_id_liters_status");

            migrationBuilder.RenameIndex(
                name: "IX_fuel_vouchers_provider",
                table: "fuel_vouchers",
                newName: "ix_fuel_vouchers_provider");

            migrationBuilder.RenameIndex(
                name: "IX_fuel_vouchers_import_job_id",
                table: "fuel_vouchers",
                newName: "ix_fuel_vouchers_import_job_id");

            migrationBuilder.RenameIndex(
                name: "IX_fuel_vouchers_fuel_type_id",
                table: "fuel_vouchers",
                newName: "ix_fuel_vouchers_fuel_type_id");

            migrationBuilder.RenameIndex(
                name: "IX_fuel_vouchers_external_id",
                table: "fuel_vouchers",
                newName: "ix_fuel_vouchers_external_id");

            migrationBuilder.RenameIndex(
                name: "IX_fuel_vouchers_expiration_date",
                table: "fuel_vouchers",
                newName: "ix_fuel_vouchers_expiration_date");

            migrationBuilder.RenameIndex(
                name: "IX_fuel_vouchers_assigned_to_user_id_status",
                table: "fuel_vouchers",
                newName: "ix_fuel_vouchers_assigned_to_user_id_status");

            migrationBuilder.RenameIndex(
                name: "IX_fuel_vouchers_assigned_to_user_id",
                table: "fuel_vouchers",
                newName: "ix_fuel_vouchers_assigned_to_user_id");

            migrationBuilder.RenameIndex(
                name: "IX_fuel_types_station_id",
                table: "fuel_types",
                newName: "ix_fuel_types_station_id");

            migrationBuilder.RenameIndex(
                name: "IX_fuel_packages_station_id",
                table: "fuel_packages",
                newName: "ix_fuel_packages_station_id");

            migrationBuilder.RenameIndex(
                name: "IX_fuel_packages_fuel_type_id",
                table: "fuel_packages",
                newName: "ix_fuel_packages_fuel_type_id");

            migrationBuilder.RenameIndex(
                name: "IX_devices_user_id",
                table: "devices",
                newName: "ix_devices_user_id");

            migrationBuilder.RenameIndex(
                name: "IX_devices_device_id",
                table: "devices",
                newName: "ix_devices_device_id");

            migrationBuilder.RenameIndex(
                name: "IX_contracts_user_id",
                table: "contracts",
                newName: "ix_contracts_user_id");

            migrationBuilder.RenameIndex(
                name: "IX_contracts_station_id",
                table: "contracts",
                newName: "ix_contracts_station_id");

            migrationBuilder.RenameIndex(
                name: "IX_contracts_legal_entity_id",
                table: "contracts",
                newName: "ix_contracts_legal_entity_id");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "orders",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "monobank_status",
                table: "orders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "is_deleted",
                table: "orders",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "fuel_vouchers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddPrimaryKey(
                name: "pk_voucher_imports",
                table: "voucher_imports",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_voucher_import_errors",
                table: "voucher_import_errors",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_verification_codes",
                table: "verification_codes",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_users",
                table: "users",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_user_contracts",
                table: "user_contracts",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_stations",
                table: "stations",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_station_nodes",
                table: "station_nodes",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_roles",
                table: "roles",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_refresh_tokens",
                table: "refresh_tokens",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_qr_parameters",
                table: "qr_parameters",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_provider_event_outbox",
                table: "provider_event_outbox",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_outbox_events",
                table: "outbox_events",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_orders",
                table: "orders",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_order_line_items",
                table: "order_line_items",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_notifications",
                table: "notifications",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_legal_entities",
                table: "legal_entities",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_fulfillments",
                table: "fulfillments",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_fuel_vouchers",
                table: "fuel_vouchers",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_fuel_types",
                table: "fuel_types",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_fuel_packages",
                table: "fuel_packages",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_devices",
                table: "devices",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_contracts",
                table: "contracts",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_contracts_legal_entities_legal_entity_id",
                table: "contracts",
                column: "legal_entity_id",
                principalTable: "legal_entities",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_contracts_stations_station_id",
                table: "contracts",
                column: "station_id",
                principalTable: "stations",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_contracts_users_user_id",
                table: "contracts",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_devices_users_user_id",
                table: "devices",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_fuel_vouchers_fuel_types_fuel_type_id",
                table: "fuel_vouchers",
                column: "fuel_type_id",
                principalTable: "fuel_types",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_fuel_vouchers_qr_parameters_qr_parameters_id",
                table: "fuel_vouchers",
                column: "qr_parameters_id",
                principalTable: "qr_parameters",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_fuel_vouchers_users_assigned_to_user_id",
                table: "fuel_vouchers",
                column: "assigned_to_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_fuel_vouchers_voucher_imports_import_job_id",
                table: "fuel_vouchers",
                column: "import_job_id",
                principalTable: "voucher_imports",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_fulfillments_fuel_vouchers_voucher_id",
                table: "fulfillments",
                column: "voucher_id",
                principalTable: "fuel_vouchers",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_fulfillments_orders_order_id",
                table: "fulfillments",
                column: "order_id",
                principalTable: "orders",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_legal_entities_users_user_id",
                table: "legal_entities",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_notifications_users_user_id",
                table: "notifications",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_order_line_items_orders_order_id",
                table: "order_line_items",
                column: "order_id",
                principalTable: "orders",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_refresh_tokens_users_user_id",
                table: "refresh_tokens",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_station_nodes_stations_station_id",
                table: "station_nodes",
                column: "station_id",
                principalTable: "stations",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_user_contracts_contracts_contract_id",
                table: "user_contracts",
                column: "contract_id",
                principalTable: "contracts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_user_contracts_users_user_id",
                table: "user_contracts",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_users_roles_role_id",
                table: "users",
                column: "role_id",
                principalTable: "roles",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_voucher_import_errors_voucher_imports_import_id",
                table: "voucher_import_errors",
                column: "import_id",
                principalTable: "voucher_imports",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_contracts_legal_entities_legal_entity_id",
                table: "contracts");

            migrationBuilder.DropForeignKey(
                name: "fk_contracts_stations_station_id",
                table: "contracts");

            migrationBuilder.DropForeignKey(
                name: "fk_contracts_users_user_id",
                table: "contracts");

            migrationBuilder.DropForeignKey(
                name: "fk_devices_users_user_id",
                table: "devices");

            migrationBuilder.DropForeignKey(
                name: "fk_fuel_vouchers_fuel_types_fuel_type_id",
                table: "fuel_vouchers");

            migrationBuilder.DropForeignKey(
                name: "fk_fuel_vouchers_qr_parameters_qr_parameters_id",
                table: "fuel_vouchers");

            migrationBuilder.DropForeignKey(
                name: "fk_fuel_vouchers_users_assigned_to_user_id",
                table: "fuel_vouchers");

            migrationBuilder.DropForeignKey(
                name: "fk_fuel_vouchers_voucher_imports_import_job_id",
                table: "fuel_vouchers");

            migrationBuilder.DropForeignKey(
                name: "fk_fulfillments_fuel_vouchers_voucher_id",
                table: "fulfillments");

            migrationBuilder.DropForeignKey(
                name: "fk_fulfillments_orders_order_id",
                table: "fulfillments");

            migrationBuilder.DropForeignKey(
                name: "fk_legal_entities_users_user_id",
                table: "legal_entities");

            migrationBuilder.DropForeignKey(
                name: "fk_notifications_users_user_id",
                table: "notifications");

            migrationBuilder.DropForeignKey(
                name: "fk_order_line_items_orders_order_id",
                table: "order_line_items");

            migrationBuilder.DropForeignKey(
                name: "fk_refresh_tokens_users_user_id",
                table: "refresh_tokens");

            migrationBuilder.DropForeignKey(
                name: "fk_station_nodes_stations_station_id",
                table: "station_nodes");

            migrationBuilder.DropForeignKey(
                name: "fk_user_contracts_contracts_contract_id",
                table: "user_contracts");

            migrationBuilder.DropForeignKey(
                name: "fk_user_contracts_users_user_id",
                table: "user_contracts");

            migrationBuilder.DropForeignKey(
                name: "fk_users_roles_role_id",
                table: "users");

            migrationBuilder.DropForeignKey(
                name: "fk_voucher_import_errors_voucher_imports_import_id",
                table: "voucher_import_errors");

            migrationBuilder.DropPrimaryKey(
                name: "pk_voucher_imports",
                table: "voucher_imports");

            migrationBuilder.DropPrimaryKey(
                name: "pk_voucher_import_errors",
                table: "voucher_import_errors");

            migrationBuilder.DropPrimaryKey(
                name: "pk_verification_codes",
                table: "verification_codes");

            migrationBuilder.DropPrimaryKey(
                name: "pk_users",
                table: "users");

            migrationBuilder.DropPrimaryKey(
                name: "pk_user_contracts",
                table: "user_contracts");

            migrationBuilder.DropPrimaryKey(
                name: "pk_stations",
                table: "stations");

            migrationBuilder.DropPrimaryKey(
                name: "pk_station_nodes",
                table: "station_nodes");

            migrationBuilder.DropPrimaryKey(
                name: "pk_roles",
                table: "roles");

            migrationBuilder.DropPrimaryKey(
                name: "pk_refresh_tokens",
                table: "refresh_tokens");

            migrationBuilder.DropPrimaryKey(
                name: "pk_qr_parameters",
                table: "qr_parameters");

            migrationBuilder.DropPrimaryKey(
                name: "pk_provider_event_outbox",
                table: "provider_event_outbox");

            migrationBuilder.DropPrimaryKey(
                name: "pk_outbox_events",
                table: "outbox_events");

            migrationBuilder.DropPrimaryKey(
                name: "pk_orders",
                table: "orders");

            migrationBuilder.DropPrimaryKey(
                name: "pk_order_line_items",
                table: "order_line_items");

            migrationBuilder.DropPrimaryKey(
                name: "pk_notifications",
                table: "notifications");

            migrationBuilder.DropPrimaryKey(
                name: "pk_legal_entities",
                table: "legal_entities");

            migrationBuilder.DropPrimaryKey(
                name: "pk_fulfillments",
                table: "fulfillments");

            migrationBuilder.DropPrimaryKey(
                name: "pk_fuel_vouchers",
                table: "fuel_vouchers");

            migrationBuilder.DropPrimaryKey(
                name: "pk_fuel_types",
                table: "fuel_types");

            migrationBuilder.DropPrimaryKey(
                name: "pk_fuel_packages",
                table: "fuel_packages");

            migrationBuilder.DropPrimaryKey(
                name: "pk_devices",
                table: "devices");

            migrationBuilder.DropPrimaryKey(
                name: "pk_contracts",
                table: "contracts");

            migrationBuilder.RenameIndex(
                name: "ix_voucher_imports_status",
                table: "voucher_imports",
                newName: "IX_voucher_imports_status");

            migrationBuilder.RenameIndex(
                name: "ix_voucher_imports_started_at_utc",
                table: "voucher_imports",
                newName: "IX_voucher_imports_started_at_utc");

            migrationBuilder.RenameIndex(
                name: "ix_voucher_import_errors_import_id",
                table: "voucher_import_errors",
                newName: "IX_voucher_import_errors_import_id");

            migrationBuilder.RenameIndex(
                name: "ix_verification_codes_phone_number_is_used_expires_at_utc",
                table: "verification_codes",
                newName: "IX_verification_codes_phone_number_is_used_expires_at_utc");

            migrationBuilder.RenameIndex(
                name: "ix_verification_codes_phone_number",
                table: "verification_codes",
                newName: "IX_verification_codes_phone_number");

            migrationBuilder.RenameIndex(
                name: "ix_verification_codes_expires_at_utc",
                table: "verification_codes",
                newName: "IX_verification_codes_expires_at_utc");

            migrationBuilder.RenameIndex(
                name: "ix_users_role_id",
                table: "users",
                newName: "IX_users_role_id");

            migrationBuilder.RenameIndex(
                name: "ix_users_referral_code",
                table: "users",
                newName: "IX_users_referral_code");

            migrationBuilder.RenameIndex(
                name: "ix_users_phone_number",
                table: "users",
                newName: "IX_users_phone_number");

            migrationBuilder.RenameIndex(
                name: "ix_user_contracts_user_id",
                table: "user_contracts",
                newName: "IX_user_contracts_user_id");

            migrationBuilder.RenameIndex(
                name: "ix_user_contracts_contract_id",
                table: "user_contracts",
                newName: "IX_user_contracts_contract_id");

            migrationBuilder.RenameIndex(
                name: "ix_station_nodes_station_id",
                table: "station_nodes",
                newName: "IX_station_nodes_station_id");

            migrationBuilder.RenameIndex(
                name: "ix_roles_name",
                table: "roles",
                newName: "IX_roles_name");

            migrationBuilder.RenameIndex(
                name: "ix_refresh_tokens_user_id_is_revoked",
                table: "refresh_tokens",
                newName: "IX_refresh_tokens_user_id_is_revoked");

            migrationBuilder.RenameIndex(
                name: "ix_refresh_tokens_user_id",
                table: "refresh_tokens",
                newName: "IX_refresh_tokens_user_id");

            migrationBuilder.RenameIndex(
                name: "ix_refresh_tokens_token",
                table: "refresh_tokens",
                newName: "IX_refresh_tokens_token");

            migrationBuilder.RenameIndex(
                name: "ix_qr_parameters_ecc_level_version_mask_pattern_encoding_mode",
                table: "qr_parameters",
                newName: "IX_qr_parameters_ecc_level_version_mask_pattern_encoding_mode");

            migrationBuilder.RenameIndex(
                name: "ix_provider_event_outbox_changed_at_utc",
                table: "provider_event_outbox",
                newName: "IX_provider_event_outbox_changed_at_utc");

            migrationBuilder.RenameIndex(
                name: "ix_provider_event_outbox_aggregate_id",
                table: "provider_event_outbox",
                newName: "IX_provider_event_outbox_aggregate_id");

            migrationBuilder.RenameIndex(
                name: "ix_outbox_events_processed",
                table: "outbox_events",
                newName: "IX_outbox_events_processed");

            migrationBuilder.RenameIndex(
                name: "ix_outbox_events_created_at_utc",
                table: "outbox_events",
                newName: "IX_outbox_events_created_at_utc");

            migrationBuilder.RenameIndex(
                name: "ix_orders_user_id_status",
                table: "orders",
                newName: "IX_orders_user_id_status");

            migrationBuilder.RenameIndex(
                name: "ix_orders_user_id_created_at_utc",
                table: "orders",
                newName: "IX_orders_user_id_created_at_utc");

            migrationBuilder.RenameIndex(
                name: "ix_orders_user_id",
                table: "orders",
                newName: "IX_orders_user_id");

            migrationBuilder.RenameIndex(
                name: "ix_orders_status",
                table: "orders",
                newName: "IX_orders_status");

            migrationBuilder.RenameIndex(
                name: "ix_orders_idempotency_key",
                table: "orders",
                newName: "IX_orders_idempotency_key");

            migrationBuilder.RenameIndex(
                name: "ix_orders_created_at_utc",
                table: "orders",
                newName: "IX_orders_created_at_utc");

            migrationBuilder.RenameIndex(
                name: "ix_order_line_items_order_id",
                table: "order_line_items",
                newName: "IX_order_line_items_order_id");

            migrationBuilder.RenameIndex(
                name: "ix_notifications_user_id_is_read",
                table: "notifications",
                newName: "IX_notifications_user_id_is_read");

            migrationBuilder.RenameIndex(
                name: "ix_notifications_user_id",
                table: "notifications",
                newName: "IX_notifications_user_id");

            migrationBuilder.RenameIndex(
                name: "ix_legal_entities_user_id",
                table: "legal_entities",
                newName: "IX_legal_entities_user_id");

            migrationBuilder.RenameIndex(
                name: "ix_legal_entities_edrpou",
                table: "legal_entities",
                newName: "IX_legal_entities_edrpou");

            migrationBuilder.RenameIndex(
                name: "ix_fulfillments_voucher_id",
                table: "fulfillments",
                newName: "IX_fulfillments_voucher_id");

            migrationBuilder.RenameIndex(
                name: "ix_fulfillments_order_id",
                table: "fulfillments",
                newName: "IX_fulfillments_order_id");

            migrationBuilder.RenameIndex(
                name: "ix_fuel_vouchers_voucher_number",
                table: "fuel_vouchers",
                newName: "IX_fuel_vouchers_voucher_number");

            migrationBuilder.RenameIndex(
                name: "ix_fuel_vouchers_status",
                table: "fuel_vouchers",
                newName: "IX_fuel_vouchers_status");

            migrationBuilder.RenameIndex(
                name: "ix_fuel_vouchers_qr_payload",
                table: "fuel_vouchers",
                newName: "IX_fuel_vouchers_qr_payload");

            migrationBuilder.RenameIndex(
                name: "ix_fuel_vouchers_qr_parameters_id",
                table: "fuel_vouchers",
                newName: "IX_fuel_vouchers_qr_parameters_id");

            migrationBuilder.RenameIndex(
                name: "ix_fuel_vouchers_provider_fuel_type_id_liters_status",
                table: "fuel_vouchers",
                newName: "IX_fuel_vouchers_provider_fuel_type_id_liters_status");

            migrationBuilder.RenameIndex(
                name: "ix_fuel_vouchers_provider",
                table: "fuel_vouchers",
                newName: "IX_fuel_vouchers_provider");

            migrationBuilder.RenameIndex(
                name: "ix_fuel_vouchers_import_job_id",
                table: "fuel_vouchers",
                newName: "IX_fuel_vouchers_import_job_id");

            migrationBuilder.RenameIndex(
                name: "ix_fuel_vouchers_fuel_type_id",
                table: "fuel_vouchers",
                newName: "IX_fuel_vouchers_fuel_type_id");

            migrationBuilder.RenameIndex(
                name: "ix_fuel_vouchers_external_id",
                table: "fuel_vouchers",
                newName: "IX_fuel_vouchers_external_id");

            migrationBuilder.RenameIndex(
                name: "ix_fuel_vouchers_expiration_date",
                table: "fuel_vouchers",
                newName: "IX_fuel_vouchers_expiration_date");

            migrationBuilder.RenameIndex(
                name: "ix_fuel_vouchers_assigned_to_user_id_status",
                table: "fuel_vouchers",
                newName: "IX_fuel_vouchers_assigned_to_user_id_status");

            migrationBuilder.RenameIndex(
                name: "ix_fuel_vouchers_assigned_to_user_id",
                table: "fuel_vouchers",
                newName: "IX_fuel_vouchers_assigned_to_user_id");

            migrationBuilder.RenameIndex(
                name: "ix_fuel_types_station_id",
                table: "fuel_types",
                newName: "IX_fuel_types_station_id");

            migrationBuilder.RenameIndex(
                name: "ix_fuel_packages_station_id",
                table: "fuel_packages",
                newName: "IX_fuel_packages_station_id");

            migrationBuilder.RenameIndex(
                name: "ix_fuel_packages_fuel_type_id",
                table: "fuel_packages",
                newName: "IX_fuel_packages_fuel_type_id");

            migrationBuilder.RenameIndex(
                name: "ix_devices_user_id",
                table: "devices",
                newName: "IX_devices_user_id");

            migrationBuilder.RenameIndex(
                name: "ix_devices_device_id",
                table: "devices",
                newName: "IX_devices_device_id");

            migrationBuilder.RenameIndex(
                name: "ix_contracts_user_id",
                table: "contracts",
                newName: "IX_contracts_user_id");

            migrationBuilder.RenameIndex(
                name: "ix_contracts_station_id",
                table: "contracts",
                newName: "IX_contracts_station_id");

            migrationBuilder.RenameIndex(
                name: "ix_contracts_legal_entity_id",
                table: "contracts",
                newName: "IX_contracts_legal_entity_id");

            migrationBuilder.AlterColumn<int>(
                name: "status",
                table: "orders",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<int>(
                name: "monobank_status",
                table: "orders",
                type: "integer",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "is_deleted",
                table: "orders",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<int>(
                name: "status",
                table: "fuel_vouchers",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AddPrimaryKey(
                name: "PK_voucher_imports",
                table: "voucher_imports",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_voucher_import_errors",
                table: "voucher_import_errors",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_verification_codes",
                table: "verification_codes",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_users",
                table: "users",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_user_contracts",
                table: "user_contracts",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_stations",
                table: "stations",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_station_nodes",
                table: "station_nodes",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_roles",
                table: "roles",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_refresh_tokens",
                table: "refresh_tokens",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_qr_parameters",
                table: "qr_parameters",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_provider_event_outbox",
                table: "provider_event_outbox",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_outbox_events",
                table: "outbox_events",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_orders",
                table: "orders",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_order_line_items",
                table: "order_line_items",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_notifications",
                table: "notifications",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_legal_entities",
                table: "legal_entities",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_fulfillments",
                table: "fulfillments",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_fuel_vouchers",
                table: "fuel_vouchers",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_fuel_types",
                table: "fuel_types",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_fuel_packages",
                table: "fuel_packages",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_devices",
                table: "devices",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_contracts",
                table: "contracts",
                column: "id");

            migrationBuilder.CreateIndex(
                name: "IX_stations_name",
                table: "stations",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "IX_stations_station_type",
                table: "stations",
                column: "station_type");

            migrationBuilder.CreateIndex(
                name: "IX_order_line_items_fuel_type_id",
                table: "order_line_items",
                column: "fuel_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_order_line_items_provider",
                table: "order_line_items",
                column: "provider");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_types_name",
                table: "fuel_types",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_types_station_id_name",
                table: "fuel_types",
                columns: new[] { "station_id", "name" });

            migrationBuilder.CreateIndex(
                name: "IX_fuel_packages_station_id_fuel_type_id",
                table: "fuel_packages",
                columns: new[] { "station_id", "fuel_type_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_contracts_legal_entities_legal_entity_id",
                table: "contracts",
                column: "legal_entity_id",
                principalTable: "legal_entities",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_contracts_stations_station_id",
                table: "contracts",
                column: "station_id",
                principalTable: "stations",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_contracts_users_user_id",
                table: "contracts",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_devices_users_user_id",
                table: "devices",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_fuel_vouchers_fuel_types_fuel_type_id",
                table: "fuel_vouchers",
                column: "fuel_type_id",
                principalTable: "fuel_types",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_fuel_vouchers_qr_parameters_qr_parameters_id",
                table: "fuel_vouchers",
                column: "qr_parameters_id",
                principalTable: "qr_parameters",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_fuel_vouchers_users_assigned_to_user_id",
                table: "fuel_vouchers",
                column: "assigned_to_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_fuel_vouchers_voucher_imports_import_job_id",
                table: "fuel_vouchers",
                column: "import_job_id",
                principalTable: "voucher_imports",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_fulfillments_fuel_vouchers_voucher_id",
                table: "fulfillments",
                column: "voucher_id",
                principalTable: "fuel_vouchers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_fulfillments_orders_order_id",
                table: "fulfillments",
                column: "order_id",
                principalTable: "orders",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_legal_entities_users_user_id",
                table: "legal_entities",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_notifications_users_user_id",
                table: "notifications",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_order_line_items_orders_order_id",
                table: "order_line_items",
                column: "order_id",
                principalTable: "orders",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_refresh_tokens_users_user_id",
                table: "refresh_tokens",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_station_nodes_stations_station_id",
                table: "station_nodes",
                column: "station_id",
                principalTable: "stations",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_user_contracts_contracts_contract_id",
                table: "user_contracts",
                column: "contract_id",
                principalTable: "contracts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_user_contracts_users_user_id",
                table: "user_contracts",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_users_roles_role_id",
                table: "users",
                column: "role_id",
                principalTable: "roles",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_voucher_import_errors_voucher_imports_import_id",
                table: "voucher_import_errors",
                column: "import_id",
                principalTable: "voucher_imports",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
