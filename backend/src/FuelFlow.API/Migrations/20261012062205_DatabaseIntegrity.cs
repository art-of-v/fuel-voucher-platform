using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// Closes the gaps found by the database review in one pass, before real sales start.
    /// Nothing here changes product behaviour: every edit either makes the database enforce a
    /// rule the application was already trying to enforce, or removes redundancy.
    ///
    /// <para><b>Hand-edited in two places, and re-scaffolding reintroduces both.</b></para>
    /// <list type="bullet">
    /// <item>EF scaffolded <c>ALTER COLUMN merchant</c> for monobank_statement_entries, in both
    /// directions. Stripped: an int-to-string conversion without a USING clause yields "0" and "1",
    /// not "Live" and "Sandbox". The explicit CASE at the end of Up() and Down() replaces it.</item>
    /// <item>The held-voucher CHECK has existed since 20261004203053 as hand-written SQL, so EF
    /// never knew it and would add a second copy on top. The orphan is dropped first.</item>
    /// </list>
    ///
    /// <para><b>Existing data.</b> A CHECK or a foreign key cannot be added over data that already
    /// breaks it, and Postgres reports that as a bare "constraint violation" naming neither the
    /// table nor the row. The raw SQL that opens Up() pre-checks the risky ones before any DDL
    /// runs and raises with the table and the rule named. A non-zero result means existing data is
    /// wrong and worth looking at before it is papered over - deliberately not auto-repaired.</para>
    /// </summary>
    public partial class DatabaseIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Data the constraints below would reject ───────────────────────────────────
            //
            // These run FIRST, before a single ALTER below, and that ordering is the whole
            // point. A CHECK or a foreign key cannot be added over data that already breaks it,
            // and Postgres reports that as a bare "constraint violation" naming neither the table
            // nor the row - so with the DDL ahead of them, these guards would never get to speak
            // and the operator would be left with a mystery message. Each check below raises
            // instead, naming the table and the rule. A non-zero result means existing data is
            // wrong and worth looking at before it is papered over - deliberately not auto-repaired.
            migrationBuilder.Sql("""
                DO $$
                DECLARE bad int;
                BEGIN
                    SELECT count(*) INTO bad FROM fuel_vouchers WHERE liters <= 0;
                    IF bad > 0 THEN RAISE EXCEPTION
                        'ck_voucher_liters_positive: fuel_vouchers has % row(s) with liters <= 0', bad; END IF;

                    SELECT count(*) INTO bad FROM fuel_vouchers WHERE cost_per_liter < 0;
                    IF bad > 0 THEN RAISE EXCEPTION
                        'ck_voucher_cost_non_negative: fuel_vouchers has % row(s) with a negative cost_per_liter', bad; END IF;

                    SELECT count(*) INTO bad FROM fuel_vouchers WHERE status NOT IN (
                        'Imported', 'VerifiedWithWarnings', 'VerificationFailed', 'Available',
                        'Assigned', 'Used', 'Expired', 'Blocked', 'Deactivated');
                    IF bad > 0 THEN RAISE EXCEPTION
                        'ck_voucher_status: fuel_vouchers has % row(s) outside the VoucherStatus set', bad; END IF;

                    SELECT count(*) INTO bad FROM orders WHERE price < 0;
                    IF bad > 0 THEN RAISE EXCEPTION
                        'ck_order_price_non_negative: orders has % row(s) with a negative price', bad; END IF;

                    SELECT count(*) INTO bad FROM orders WHERE status NOT IN (
                        'PendingPayment', 'Paid', 'PendingFulfillment', 'PartiallyFulfilled',
                        'Fulfilled', 'PartiallyRefunded', 'Refunded', 'Cancelled');
                    IF bad > 0 THEN RAISE EXCEPTION
                        'ck_order_status: orders has % row(s) outside the OrderStatus set', bad; END IF;

                    SELECT count(*) INTO bad FROM orders
                        WHERE kind NOT IN ('Purchase', 'Renewal', 'ReceivedFromCompany');
                    IF bad > 0 THEN RAISE EXCEPTION
                        'ck_order_kind: orders has % row(s) outside the OrderKind set', bad; END IF;

                    SELECT count(*) INTO bad FROM order_line_items
                        WHERE quantity <= 0 OR liters <= 0
                           OR unit_price < 0 OR line_total < 0
                           OR (original_line_total IS NOT NULL AND original_line_total < 0);
                    IF bad > 0 THEN RAISE EXCEPTION
                        'ck_order_line_*: order_line_items has % row(s) with a non-positive quantity or litres, or a negative price', bad; END IF;

                    SELECT count(*) INTO bad FROM refunds WHERE amount <= 0;
                    IF bad > 0 THEN RAISE EXCEPTION
                        'ck_refund_amount_positive: refunds has % row(s) with amount <= 0', bad; END IF;

                    SELECT count(*) INTO bad FROM refunds WHERE status NOT IN ('Processing', 'Completed', 'Failed');
                    IF bad > 0 THEN RAISE EXCEPTION
                        'ck_refund_status: refunds has % row(s) outside the RefundStatus set', bad; END IF;

                    SELECT count(*) INTO bad FROM voucher_exchanges WHERE surcharge_uah < 0;
                    IF bad > 0 THEN RAISE EXCEPTION
                        'ck_voucher_exchange_surcharge_non_negative: voucher_exchanges has % row(s) with a negative surcharge', bad; END IF;

                    SELECT count(*) INTO bad FROM voucher_exchanges
                        WHERE old_voucher_id IS NOT DISTINCT FROM new_voucher_id;
                    IF bad > 0 THEN RAISE EXCEPTION
                        'ck_voucher_exchange_not_self: voucher_exchanges has % row(s) pairing a voucher with itself', bad; END IF;

                    SELECT count(*) INTO bad FROM operator_voucher_renewals
                        WHERE branch NOT IN ('extend', 'replace');
                    IF bad > 0 THEN RAISE EXCEPTION
                        'ck_operator_renewal_branch: operator_voucher_renewals has % row(s) with an unknown branch', bad; END IF;

                    SELECT count(*) INTO bad FROM operator_voucher_renewals WHERE surcharge_uah < 0;
                    IF bad > 0 THEN RAISE EXCEPTION
                        'ck_operator_renewal_surcharge_non_negative: operator_voucher_renewals has % row(s) with a negative surcharge', bad; END IF;

                    SELECT count(*) INTO bad FROM voucher_renewal_items WHERE amount_paid < 0;
                    IF bad > 0 THEN RAISE EXCEPTION
                        'ck_voucher_renewal_amount_non_negative: voucher_renewal_items has % row(s) with a negative amount_paid', bad; END IF;

                    SELECT count(*) INTO bad FROM devices WHERE status NOT IN ('Active', 'Revoked');
                    IF bad > 0 THEN RAISE EXCEPTION
                        'ck_device_status: devices has % row(s) outside the DeviceStatus set', bad; END IF;

                    SELECT count(*) INTO bad FROM company_invitations
                        WHERE status NOT IN ('Pending', 'Accepted', 'Declined', 'Cancelled');
                    IF bad > 0 THEN RAISE EXCEPTION
                        'ck_company_invitation_status: company_invitations has % row(s) outside the InvitationStatus set', bad; END IF;

                    SELECT count(*) INTO bad FROM outbox_events WHERE event_type NOT IN (
                        'OrderCreated', 'OrderFulfilled', 'OrderCancelled', 'PaymentCompleted',
                        'VoucherExpired', 'VoucherUsed', 'VoucherActivated');
                    IF bad > 0 THEN RAISE EXCEPTION
                        'ck_outbox_event_type: outbox_events has % row(s) outside the OutboxEventType set', bad; END IF;
                END $$;
                """);

            // Dangling parents for the foreign keys added below. Same reasoning: report, never
            // repair. An FK over an orphan fails at ALTER time, and these messages say which
            // column is orphaned instead of leaving Postgres to say "violates foreign key".
            migrationBuilder.Sql("""
                DO $$
                DECLARE bad int;
                BEGIN
                    SELECT count(*) INTO bad FROM orders o
                        LEFT JOIN users u ON u.id = o.user_id WHERE u.id IS NULL;
                    IF bad > 0 THEN RAISE EXCEPTION
                        'FK_orders_users: orders has % row(s) pointing at a user that does not exist', bad; END IF;

                    SELECT count(*) INTO bad FROM refunds r
                        LEFT JOIN users u ON u.id = r.user_id WHERE u.id IS NULL;
                    IF bad > 0 THEN RAISE EXCEPTION
                        'FK_refunds_users: refunds has % row(s) pointing at a user that does not exist', bad; END IF;

                    SELECT count(*) INTO bad FROM purchase_batches p
                        LEFT JOIN voucher_imports i ON i.id = p.import_job_id WHERE i.id IS NULL;
                    IF bad > 0 THEN RAISE EXCEPTION
                        'FK_purchase_batches_import: purchase_batches has % row(s) pointing at an import that does not exist', bad; END IF;
                END $$;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE fuel_vouchers DROP CONSTRAINT IF EXISTS "ck_voucher_held_has_order";
                """);

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
                name: "FK_fuel_vouchers_fuel_types_fuel_type_id",
                table: "fuel_vouchers");

            migrationBuilder.DropForeignKey(
                name: "FK_station_nodes_stations_station_id",
                table: "station_nodes");

            migrationBuilder.DropForeignKey(
                name: "FK_user_contracts_contracts_contract_id",
                table: "user_contracts");

            migrationBuilder.DropForeignKey(
                name: "FK_user_contracts_users_user_id",
                table: "user_contracts");

            migrationBuilder.DropIndex(
                name: "IX_voucher_exchanges_old_voucher_id",
                table: "voucher_exchanges");

            migrationBuilder.DropIndex(
                name: "IX_users_referral_code",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_orders_status",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "IX_orders_user_id",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "IX_notifications_user_id",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_fuel_vouchers_assigned_to_user_id",
                table: "fuel_vouchers");

            migrationBuilder.DropIndex(
                name: "IX_fuel_vouchers_external_id",
                table: "fuel_vouchers");

            migrationBuilder.DropIndex(
                name: "IX_fuel_vouchers_fuel_type_id",
                table: "fuel_vouchers");

            migrationBuilder.DropIndex(
                name: "IX_fuel_vouchers_fuel_type_id_status",
                table: "fuel_vouchers");

            migrationBuilder.DropIndex(
                name: "IX_fuel_vouchers_legal_entity_id",
                table: "fuel_vouchers");

            migrationBuilder.DropIndex(
                name: "IX_fuel_vouchers_legal_entity_id_worker_user_id_status",
                table: "fuel_vouchers");

            migrationBuilder.DropIndex(
                name: "IX_fuel_vouchers_provider",
                table: "fuel_vouchers");

            migrationBuilder.DropIndex(
                name: "IX_fuel_vouchers_status",
                table: "fuel_vouchers");

            migrationBuilder.DropIndex(
                name: "IX_fuel_vouchers_status_is_test_data",
                table: "fuel_vouchers");

            migrationBuilder.DropColumn(
                name: "external_id",
                table: "fuel_vouchers");

            migrationBuilder.RenameColumn(
                name: "PendingEmailTokenHash",
                table: "users",
                newName: "pending_email_token_hash");

            migrationBuilder.RenameColumn(
                name: "PendingEmailExpiresAtUtc",
                table: "users",
                newName: "pending_email_expires_at_utc");

            migrationBuilder.RenameColumn(
                name: "PendingEmail",
                table: "users",
                newName: "pending_email");

            migrationBuilder.RenameColumn(
                name: "LastWebhookProcessedAtUtc",
                table: "orders",
                newName: "last_webhook_processed_at_utc");

            migrationBuilder.RenameColumn(
                name: "LastWebhookModifiedDateUtc",
                table: "orders",
                newName: "last_webhook_modified_date_utc");

            migrationBuilder.RenameColumn(
                name: "UsedAtUtc",
                table: "fuel_vouchers",
                newName: "used_at_utc");

            migrationBuilder.RenameColumn(
                name: "IssuedAtUtc",
                table: "fuel_vouchers",
                newName: "issued_at_utc");

            migrationBuilder.AlterColumn<decimal>(
                name: "amount_paid",
                table: "voucher_renewal_items",
                type: "numeric(12,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "raw_text",
                table: "voucher_import_errors",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "error_message",
                table: "voucher_import_errors",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "pending_email_token_hash",
                table: "users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "pending_email",
                table: "users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "amount",
                table: "refunds",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "summary",
                table: "provider_event_outbox",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "scope_id",
                table: "provider_event_outbox",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "event_type",
                table: "provider_event_outbox",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "changed_by_user_name",
                table: "provider_event_outbox",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "aggregate_type",
                table: "provider_event_outbox",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "aggregate_id",
                table: "provider_event_outbox",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "fuel_vouchers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "redemption_rules",
                table: "fuel_vouchers",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "qr_payload",
                table: "fuel_vouchers",
                type: "varchar(2000)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "image_url",
                table: "fuel_vouchers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "user_name",
                table: "error_logs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "trace_id",
                table: "error_logs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "source",
                table: "error_logs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "resolved_by_user_name",
                table: "error_logs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "request_path",
                table: "error_logs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "request_method",
                table: "error_logs",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "message",
                table: "error_logs",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "level",
                table: "error_logs",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "exception_type",
                table: "error_logs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "exception_message",
                table: "error_logs",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_renewal_amount_non_negative",
                table: "voucher_renewal_items",
                sql: "amount_paid IS NULL OR amount_paid >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_voucher_exchanges_old_voucher_id",
                table: "voucher_exchanges",
                column: "old_voucher_id",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_exchange_cost_non_negative",
                table: "voucher_exchanges",
                sql: "cost_per_liter_applied IS NULL OR cost_per_liter_applied >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_exchange_not_self",
                table: "voucher_exchanges",
                sql: "old_voucher_id IS DISTINCT FROM new_voucher_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_exchange_surcharge_non_negative",
                table: "voucher_exchanges",
                sql: "surcharge_uah >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_users_referral_code",
                table: "users",
                column: "referral_code",
                unique: true,
                filter: "referral_code IS NOT NULL AND is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "IX_refunds_created_by_user_id",
                table: "refunds",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_refunds_invoice_id",
                table: "refunds",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "IX_refunds_user_id",
                table: "refunds",
                column: "user_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_refund_amount_positive",
                table: "refunds",
                sql: "amount > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_refund_status",
                table: "refunds",
                sql: "status IN ('Processing', 'Completed', 'Failed')");

            migrationBuilder.CreateIndex(
                name: "IX_provider_event_outbox_event_type_changed_at_utc",
                table: "provider_event_outbox",
                columns: new[] { "event_type", "changed_at_utc" },
                descending: new[] { false, true });

            migrationBuilder.AddCheckConstraint(
                name: "ck_outbox_event_type",
                table: "outbox_events",
                sql: "event_type IN ('OrderCreated', 'OrderFulfilled', 'OrderCancelled',\n               'PaymentCompleted', 'VoucherExpired', 'VoucherUsed', 'VoucherActivated')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_order_kind",
                table: "orders",
                sql: "kind IN ('Purchase', 'Renewal', 'ReceivedFromCompany')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_order_price_non_negative",
                table: "orders",
                sql: "price >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_order_status",
                table: "orders",
                sql: "status IN ('PendingPayment', 'Paid', 'PendingFulfillment', 'PartiallyFulfilled',\n           'Fulfilled', 'PartiallyRefunded', 'Refunded', 'Cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_order_line_non_negative_prices",
                table: "order_line_items",
                sql: "unit_price >= 0 AND line_total >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_order_line_original_non_negative",
                table: "order_line_items",
                sql: "original_line_total IS NULL OR original_line_total >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_order_line_positive_liters",
                table: "order_line_items",
                sql: "liters > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_order_line_positive_quantity",
                table: "order_line_items",
                sql: "quantity > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_operator_renewal_branch",
                table: "operator_voucher_renewals",
                sql: "branch IN ('extend', 'replace')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_operator_renewal_expiration_moves_forward",
                table: "operator_voucher_renewals",
                sql: "new_expiration > old_expiration");

            migrationBuilder.AddCheckConstraint(
                name: "ck_operator_renewal_surcharge_non_negative",
                table: "operator_voucher_renewals",
                sql: "surcharge_uah >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_user_id_created_at_utc",
                table: "notifications",
                columns: new[] { "user_id", "created_at_utc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_fuel_type_id_status_is_deleted",
                table: "fuel_vouchers",
                columns: new[] { "fuel_type_id", "status", "is_deleted" });

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_legal_entity_id_worker_user_id_status_is_dele~",
                table: "fuel_vouchers",
                columns: new[] { "legal_entity_id", "worker_user_id", "status", "is_deleted" });

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_status_is_test_data_is_deleted",
                table: "fuel_vouchers",
                columns: new[] { "status", "is_test_data", "is_deleted" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_cost_non_negative",
                table: "fuel_vouchers",
                sql: "cost_per_liter IS NULL OR cost_per_liter >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_held_has_order",
                table: "fuel_vouchers",
                sql: "status NOT IN ('Assigned', 'Used', 'Blocked') OR order_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_liters_positive",
                table: "fuel_vouchers",
                sql: "liters > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_status",
                table: "fuel_vouchers",
                sql: "status IN ('Imported', 'VerifiedWithWarnings', 'VerificationFailed', 'Available',\n           'Assigned', 'Used', 'Expired', 'Blocked', 'Deactivated')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_status",
                table: "devices",
                sql: "status IN ('Active', 'Revoked')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_company_invitation_status",
                table: "company_invitations",
                sql: "status IN ('Pending', 'Accepted', 'Declined', 'Cancelled')");

            migrationBuilder.AddForeignKey(
                name: "FK_contracts_legal_entities_legal_entity_id",
                table: "contracts",
                column: "legal_entity_id",
                principalTable: "legal_entities",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_contracts_stations_station_id",
                table: "contracts",
                column: "station_id",
                principalTable: "stations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_contracts_users_user_id",
                table: "contracts",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_fuel_vouchers_fuel_types_fuel_type_id",
                table: "fuel_vouchers",
                column: "fuel_type_id",
                principalTable: "fuel_types",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_orders_users_user_id",
                table: "orders",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_purchase_batches_voucher_imports_import_job_id",
                table: "purchase_batches",
                column: "import_job_id",
                principalTable: "voucher_imports",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_refunds_users_created_by_user_id",
                table: "refunds",
                column: "created_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_refunds_users_user_id",
                table: "refunds",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_station_nodes_stations_station_id",
                table: "station_nodes",
                column: "station_id",
                principalTable: "stations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_user_contracts_contracts_contract_id",
                table: "user_contracts",
                column: "contract_id",
                principalTable: "contracts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_user_contracts_users_user_id",
                table: "user_contracts",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // monobank_statement_entries.merchant moves from an int to the same string enum
            // orders.monobank_merchant already uses. This is the only conversion in the migration:
            // an int-to-string ALTER without a USING clause yields "0" and "1".
            migrationBuilder.Sql("""
                DO $$
                DECLARE bad int;
                BEGIN
                    SELECT count(*) INTO bad FROM monobank_statement_entries
                        WHERE merchant NOT IN (0, 1);
                    IF bad > 0 THEN RAISE EXCEPTION
                        'monobank_statement_entries.merchant: % row(s) hold a merchant outside the Live/Sandbox enum', bad; END IF;
                END $$;

                ALTER TABLE monobank_statement_entries
                    ALTER COLUMN merchant TYPE character varying(20)
                    USING CASE merchant WHEN 0 THEN 'Live' WHEN 1 THEN 'Sandbox' END;

                ALTER TABLE monobank_statement_entries
                    ALTER COLUMN merchant SET NOT NULL;
                """);

            // qr_parameters.ecc_level holds a sentinel for a QR whose ECC level did not parse -
            // the unreferenced duplicates the import dedup bug created on every run. Safe to drop
            // precisely because nothing points at them; the leak itself is fixed in
            // ImportVouchersCommand.
            migrationBuilder.Sql("""
                DELETE FROM qr_parameters q
                WHERE q.ecc_level = ''
                  AND NOT EXISTS (SELECT 1 FROM fuel_vouchers v WHERE v.qr_parameters_id = q.id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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
                name: "FK_fuel_vouchers_fuel_types_fuel_type_id",
                table: "fuel_vouchers");

            migrationBuilder.DropForeignKey(
                name: "FK_orders_users_user_id",
                table: "orders");

            migrationBuilder.DropForeignKey(
                name: "FK_purchase_batches_voucher_imports_import_job_id",
                table: "purchase_batches");

            migrationBuilder.DropForeignKey(
                name: "FK_refunds_users_created_by_user_id",
                table: "refunds");

            migrationBuilder.DropForeignKey(
                name: "FK_refunds_users_user_id",
                table: "refunds");

            migrationBuilder.DropForeignKey(
                name: "FK_station_nodes_stations_station_id",
                table: "station_nodes");

            migrationBuilder.DropForeignKey(
                name: "FK_user_contracts_contracts_contract_id",
                table: "user_contracts");

            migrationBuilder.DropForeignKey(
                name: "FK_user_contracts_users_user_id",
                table: "user_contracts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_renewal_amount_non_negative",
                table: "voucher_renewal_items");

            migrationBuilder.DropIndex(
                name: "IX_voucher_exchanges_old_voucher_id",
                table: "voucher_exchanges");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_exchange_cost_non_negative",
                table: "voucher_exchanges");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_exchange_not_self",
                table: "voucher_exchanges");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_exchange_surcharge_non_negative",
                table: "voucher_exchanges");

            migrationBuilder.DropIndex(
                name: "IX_users_referral_code",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_refunds_created_by_user_id",
                table: "refunds");

            migrationBuilder.DropIndex(
                name: "IX_refunds_invoice_id",
                table: "refunds");

            migrationBuilder.DropIndex(
                name: "IX_refunds_user_id",
                table: "refunds");

            migrationBuilder.DropCheckConstraint(
                name: "ck_refund_amount_positive",
                table: "refunds");

            migrationBuilder.DropCheckConstraint(
                name: "ck_refund_status",
                table: "refunds");

            migrationBuilder.DropIndex(
                name: "IX_provider_event_outbox_event_type_changed_at_utc",
                table: "provider_event_outbox");

            migrationBuilder.DropCheckConstraint(
                name: "ck_outbox_event_type",
                table: "outbox_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_order_kind",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_order_price_non_negative",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_order_status",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_order_line_non_negative_prices",
                table: "order_line_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_order_line_original_non_negative",
                table: "order_line_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_order_line_positive_liters",
                table: "order_line_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_order_line_positive_quantity",
                table: "order_line_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_operator_renewal_branch",
                table: "operator_voucher_renewals");

            migrationBuilder.DropCheckConstraint(
                name: "ck_operator_renewal_expiration_moves_forward",
                table: "operator_voucher_renewals");

            migrationBuilder.DropCheckConstraint(
                name: "ck_operator_renewal_surcharge_non_negative",
                table: "operator_voucher_renewals");

            migrationBuilder.DropIndex(
                name: "IX_notifications_user_id_created_at_utc",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_fuel_vouchers_fuel_type_id_status_is_deleted",
                table: "fuel_vouchers");

            migrationBuilder.DropIndex(
                name: "IX_fuel_vouchers_legal_entity_id_worker_user_id_status_is_dele~",
                table: "fuel_vouchers");

            migrationBuilder.DropIndex(
                name: "IX_fuel_vouchers_status_is_test_data_is_deleted",
                table: "fuel_vouchers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_cost_non_negative",
                table: "fuel_vouchers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_held_has_order",
                table: "fuel_vouchers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_liters_positive",
                table: "fuel_vouchers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_status",
                table: "fuel_vouchers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_device_status",
                table: "devices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_company_invitation_status",
                table: "company_invitations");

            migrationBuilder.RenameColumn(
                name: "pending_email_token_hash",
                table: "users",
                newName: "PendingEmailTokenHash");

            migrationBuilder.RenameColumn(
                name: "pending_email_expires_at_utc",
                table: "users",
                newName: "PendingEmailExpiresAtUtc");

            migrationBuilder.RenameColumn(
                name: "pending_email",
                table: "users",
                newName: "PendingEmail");

            migrationBuilder.RenameColumn(
                name: "last_webhook_processed_at_utc",
                table: "orders",
                newName: "LastWebhookProcessedAtUtc");

            migrationBuilder.RenameColumn(
                name: "last_webhook_modified_date_utc",
                table: "orders",
                newName: "LastWebhookModifiedDateUtc");

            migrationBuilder.RenameColumn(
                name: "used_at_utc",
                table: "fuel_vouchers",
                newName: "UsedAtUtc");

            migrationBuilder.RenameColumn(
                name: "issued_at_utc",
                table: "fuel_vouchers",
                newName: "IssuedAtUtc");

            migrationBuilder.AlterColumn<decimal>(
                name: "amount_paid",
                table: "voucher_renewal_items",
                type: "numeric(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "raw_text",
                table: "voucher_import_errors",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "error_message",
                table: "voucher_import_errors",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000);

            migrationBuilder.AlterColumn<string>(
                name: "PendingEmailTokenHash",
                table: "users",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PendingEmail",
                table: "users",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "amount",
                table: "refunds",
                type: "integer",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<string>(
                name: "summary",
                table: "provider_event_outbox",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<string>(
                name: "scope_id",
                table: "provider_event_outbox",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "event_type",
                table: "provider_event_outbox",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "changed_by_user_name",
                table: "provider_event_outbox",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "aggregate_type",
                table: "provider_event_outbox",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "aggregate_id",
                table: "provider_event_outbox",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "fuel_vouchers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "redemption_rules",
                table: "fuel_vouchers",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "qr_payload",
                table: "fuel_vouchers",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(2000)");

            migrationBuilder.AlterColumn<string>(
                name: "image_url",
                table: "fuel_vouchers",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_id",
                table: "fuel_vouchers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "user_name",
                table: "error_logs",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "trace_id",
                table: "error_logs",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "source",
                table: "error_logs",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "resolved_by_user_name",
                table: "error_logs",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "request_path",
                table: "error_logs",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "request_method",
                table: "error_logs",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "message",
                table: "error_logs",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000);

            migrationBuilder.AlterColumn<string>(
                name: "level",
                table: "error_logs",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<string>(
                name: "exception_type",
                table: "error_logs",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "exception_message",
                table: "error_logs",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_voucher_exchanges_old_voucher_id",
                table: "voucher_exchanges",
                column: "old_voucher_id");

            migrationBuilder.CreateIndex(
                name: "IX_users_referral_code",
                table: "users",
                column: "referral_code",
                unique: true,
                filter: "referral_code IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_orders_status",
                table: "orders",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_orders_user_id",
                table: "orders",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_user_id",
                table: "notifications",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_assigned_to_user_id",
                table: "fuel_vouchers",
                column: "assigned_to_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_external_id",
                table: "fuel_vouchers",
                column: "external_id",
                unique: true,
                filter: "external_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_fuel_type_id",
                table: "fuel_vouchers",
                column: "fuel_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_fuel_type_id_status",
                table: "fuel_vouchers",
                columns: new[] { "fuel_type_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_legal_entity_id",
                table: "fuel_vouchers",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_legal_entity_id_worker_user_id_status",
                table: "fuel_vouchers",
                columns: new[] { "legal_entity_id", "worker_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_provider",
                table: "fuel_vouchers",
                column: "brand");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_status",
                table: "fuel_vouchers",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_status_is_test_data",
                table: "fuel_vouchers",
                columns: new[] { "status", "is_test_data" });

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
                name: "FK_fuel_vouchers_fuel_types_fuel_type_id",
                table: "fuel_vouchers",
                column: "fuel_type_id",
                principalTable: "fuel_types",
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

            // merchant goes back to int, through the same explicit CASE in reverse. Hand-written
            // for the same reason as Up(): the scaffolded AlterColumn has no USING clause, and
            // without one the conversion reads the column as integer before it has been cast.
            migrationBuilder.Sql("""
                ALTER TABLE monobank_statement_entries
                    ALTER COLUMN merchant TYPE integer
                    USING CASE merchant WHEN 'Live' THEN 0 WHEN 'Sandbox' THEN 1 END;
                """);
        }
    }
}
