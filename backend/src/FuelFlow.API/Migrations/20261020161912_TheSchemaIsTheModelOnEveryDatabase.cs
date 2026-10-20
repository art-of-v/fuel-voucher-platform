using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class TheSchemaIsTheModelOnEveryDatabase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_webhook_events_outstanding",
                table: "webhook_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_webhook_events_processed",
                table: "webhook_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_webhook_events_status",
                table: "webhook_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_term_pricing_scheme",
                table: "voucher_term_pricing");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_term_pricing_term_code",
                table: "voucher_term_pricing");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_movements_actor_kind",
                table: "voucher_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_movements_kind",
                table: "voucher_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_movements_locations",
                table: "voucher_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_imports_status",
                table: "voucher_imports");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_events_actor_kind",
                table: "voucher_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_events_statuses",
                table: "voucher_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_events_type",
                table: "voucher_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_suppliers_acquisition_source",
                table: "suppliers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_settlement_messages_outcome",
                table: "settlement_messages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_qr_parameters_ecc_level",
                table: "qr_parameters");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_kind",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_status",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_parties_party_type",
                table: "parties");

            migrationBuilder.DropCheckConstraint(
                name: "ck_outbox_event_type",
                table: "outbox_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_order_kind",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_order_status",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_order_line_renewal_branch",
                table: "order_line_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_order_events_event_type",
                table: "order_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_operator_renewal_branch",
                table: "operator_voucher_renewals");

            migrationBuilder.DropCheckConstraint(
                name: "ck_operator_legal_entities_tax_regime",
                table: "operator_legal_entities");

            migrationBuilder.DropCheckConstraint(
                name: "ck_movement_batches_kind",
                table: "movement_batches");

            migrationBuilder.DropCheckConstraint(
                name: "ck_movement_batches_status",
                table: "movement_batches");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_mapping_rules_side",
                table: "ledger_mapping_rules");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_mapping_rules_source_type",
                table: "ledger_mapping_rules");

            migrationBuilder.DropCheckConstraint(
                name: "ck_journal_entries_source_type",
                table: "journal_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_journal_entries_status",
                table: "journal_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_vouchers_acquisition_source",
                table: "fuel_vouchers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_vouchers_data_kind",
                table: "fuel_vouchers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_held_has_order",
                table: "fuel_vouchers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_status",
                table: "fuel_vouchers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_price_history_data_kind",
                table: "fuel_price_history");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_price_history_reason",
                table: "fuel_price_history");

            migrationBuilder.DropCheckConstraint(
                name: "ck_feedback_message_category",
                table: "feedback_messages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_feedback_message_language",
                table: "feedback_messages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_feedback_message_severity",
                table: "feedback_messages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_feedback_message_status",
                table: "feedback_messages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_feedback_message_verdict",
                table: "feedback_messages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_device_status",
                table: "devices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_company_invitation_status",
                table: "company_invitations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_chart_of_accounts_account_type",
                table: "chart_of_accounts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_chart_of_accounts_normal_balance",
                table: "chart_of_accounts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_accounting_periods_status",
                table: "accounting_periods");

            migrationBuilder.AlterColumn<Guid>(
                name: "family_id",
                table: "refresh_tokens",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldDefaultValueSql: "'00000000-0000-0000-0000-000000000000'::uuid");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_events_outstanding",
                table: "webhook_events",
                column: "received_at_utc",
                filter: "status IN ('Received','Failed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_webhook_events_processed",
                table: "webhook_events",
                sql: "status NOT IN ('Processed','Ignored') OR processed_at_utc IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_webhook_events_status",
                table: "webhook_events",
                sql: "status IN ('Received','Processed','Ignored','Failed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_term_pricing_scheme",
                table: "voucher_term_pricing",
                sql: "scheme IN ('renewal_rate','purchase_term_discount')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_term_pricing_term_code",
                table: "voucher_term_pricing",
                sql: "term_code IN ('1w','2w','1m','2m','3m','4m','5m','6m')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_movements_actor_kind",
                table: "voucher_movements",
                sql: "actor_kind IN ('user','system')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_movements_kind",
                table: "voucher_movements",
                sql: "kind IN ('deposit','issued','returned','replace_out','exchange_out','exchange_in','gift','recall')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_movements_locations",
                table: "voucher_movements",
                sql: "from_location IN ('pool','customer','supplier','company','worker') AND to_location IN ('pool','customer','supplier','company','worker')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_imports_status",
                table: "voucher_imports",
                sql: "status IN ('Started','Completed','Failed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_events_actor_kind",
                table: "voucher_events",
                sql: "actor_kind IN ('user','system')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_events_statuses",
                table: "voucher_events",
                sql: "(from_status IS NULL OR from_status IN ('Imported','VerifiedWithWarnings','VerificationFailed','Available','Assigned','Used','Expired','Blocked','Deactivated')) AND (to_status IS NULL OR to_status IN ('Imported','VerifiedWithWarnings','VerificationFailed','Available','Assigned','Used','Expired','Blocked','Deactivated'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_events_type",
                table: "voucher_events",
                sql: "event_type IN ('Imported','StatusChanged','Extended','Archived','Unarchived')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_suppliers_acquisition_source",
                table: "suppliers",
                sql: "acquisition_source IN ('direct', 'partner')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_settlement_messages_outcome",
                table: "settlement_messages",
                sql: "outcome IN ('accepted','unallocated','unparseable','rejected_sender','rejected_chat')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_qr_parameters_ecc_level",
                table: "qr_parameters",
                sql: "ecc_level IN ('L','M','Q','H')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_kind",
                table: "payments",
                sql: "kind IN ('Capture','Refund')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_status",
                table: "payments",
                sql: "status IN ('Pending','Succeeded','Failed','Cancelled','Expired')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_parties_party_type",
                table: "parties",
                sql: "party_type IN ('Natural','Legal')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_outbox_event_type",
                table: "outbox_events",
                sql: "event_type IN ('OrderCreated', 'OrderFulfilled', 'OrderCancelled',\n               'PaymentCompleted', 'VoucherExpired', 'VoucherUsed', 'VoucherActivated',\n               'VoucherIssued')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_order_kind",
                table: "orders",
                sql: "kind IN ('Purchase', 'Renewal', 'ReceivedFromCompany')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_order_status",
                table: "orders",
                sql: "status IN ('PendingPayment', 'Paid', 'PendingFulfillment', 'PartiallyFulfilled',\n           'Fulfilled', 'PartiallyRefunded', 'Refunded', 'Cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_order_line_renewal_branch",
                table: "order_line_items",
                sql: "renewal_branch IS NULL OR renewal_branch IN ('extend','replace')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_order_events_event_type",
                table: "order_events",
                sql: "event_type IN ('Created', 'StatusChanged', 'InvoiceCreated', 'PaymentCaptured', 'Fulfilled', 'PartiallyFulfilled', 'Refunded', 'Cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_operator_renewal_branch",
                table: "operator_voucher_renewals",
                sql: "branch IN ('extend', 'replace')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_operator_legal_entities_tax_regime",
                table: "operator_legal_entities",
                sql: "tax_regime IN ('Vat','Simplified')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_movement_batches_kind",
                table: "movement_batches",
                sql: "kind IN ('voucher_exchange','bulk_issuance')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_movement_batches_status",
                table: "movement_batches",
                sql: "status IN ('draft','confirmed','rejected','cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_mapping_rules_side",
                table: "ledger_mapping_rules",
                sql: "side IN ('Debit','Credit')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_mapping_rules_source_type",
                table: "ledger_mapping_rules",
                sql: "source_type IN ('Payment','Refund','Fulfillment','Cost','Movement','Adjustment','Fiscalization','Reversal')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_journal_entries_source_type",
                table: "journal_entries",
                sql: "source_type IN ('Payment','Refund','Fulfillment','Cost','Movement','Adjustment','Fiscalization','Reversal')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_journal_entries_status",
                table: "journal_entries",
                sql: "status IN ('Draft','Posted','Reversed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_vouchers_acquisition_source",
                table: "fuel_vouchers",
                sql: "acquisition_source IN ('direct', 'partner')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_vouchers_data_kind",
                table: "fuel_vouchers",
                sql: "data_kind IN ('real', 'qa')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_held_has_order",
                table: "fuel_vouchers",
                sql: "status NOT IN ('Assigned', 'Used', 'Blocked') OR order_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_status",
                table: "fuel_vouchers",
                sql: "status IN ('Imported', 'VerifiedWithWarnings', 'VerificationFailed', 'Available',\n           'Assigned', 'Used', 'Expired', 'Blocked', 'Deactivated')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_price_history_data_kind",
                table: "fuel_price_history",
                sql: "data_kind IN ('real', 'qa')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_price_history_reason",
                table: "fuel_price_history",
                sql: "reason IN ('operator', 'pump-import', 'catalogue-import', 'batch-cost', 'exchange-release', 'backfill')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_feedback_message_category",
                table: "feedback_messages",
                sql: "category IS NULL OR category IN ('Bug', 'Idea', 'Complaint', 'Ux', 'Question', 'Praise', 'Other')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_feedback_message_language",
                table: "feedback_messages",
                sql: "language IS NULL OR language IN ('Uk', 'Ru', 'En', 'Other')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_feedback_message_severity",
                table: "feedback_messages",
                sql: "severity IS NULL OR severity IN ('Low', 'Medium', 'High')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_feedback_message_status",
                table: "feedback_messages",
                sql: "status IN ('New', 'Triaged', 'Handled', 'Rejected')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_feedback_message_verdict",
                table: "feedback_messages",
                sql: "verdict IS NULL OR verdict IN ('Junk', 'Kept')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_status",
                table: "devices",
                sql: "status IN ('Active', 'Revoked')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_company_invitation_status",
                table: "company_invitations",
                sql: "status IN ('Pending', 'Accepted', 'Declined', 'Cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_chart_of_accounts_account_type",
                table: "chart_of_accounts",
                sql: "account_type IN ('Asset','Liability','Equity','Revenue','Expense','Vat')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_chart_of_accounts_normal_balance",
                table: "chart_of_accounts",
                sql: "normal_balance IN ('Debit','Credit')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_accounting_periods_status",
                table: "accounting_periods",
                sql: "status IN ('Open','Closed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_webhook_events_outstanding",
                table: "webhook_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_webhook_events_processed",
                table: "webhook_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_webhook_events_status",
                table: "webhook_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_term_pricing_scheme",
                table: "voucher_term_pricing");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_term_pricing_term_code",
                table: "voucher_term_pricing");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_movements_actor_kind",
                table: "voucher_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_movements_kind",
                table: "voucher_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_movements_locations",
                table: "voucher_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_imports_status",
                table: "voucher_imports");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_events_actor_kind",
                table: "voucher_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_events_statuses",
                table: "voucher_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_events_type",
                table: "voucher_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_suppliers_acquisition_source",
                table: "suppliers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_settlement_messages_outcome",
                table: "settlement_messages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_qr_parameters_ecc_level",
                table: "qr_parameters");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_kind",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_status",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_parties_party_type",
                table: "parties");

            migrationBuilder.DropCheckConstraint(
                name: "ck_outbox_event_type",
                table: "outbox_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_order_kind",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_order_status",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_order_line_renewal_branch",
                table: "order_line_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_order_events_event_type",
                table: "order_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_operator_renewal_branch",
                table: "operator_voucher_renewals");

            migrationBuilder.DropCheckConstraint(
                name: "ck_operator_legal_entities_tax_regime",
                table: "operator_legal_entities");

            migrationBuilder.DropCheckConstraint(
                name: "ck_movement_batches_kind",
                table: "movement_batches");

            migrationBuilder.DropCheckConstraint(
                name: "ck_movement_batches_status",
                table: "movement_batches");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_mapping_rules_side",
                table: "ledger_mapping_rules");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_mapping_rules_source_type",
                table: "ledger_mapping_rules");

            migrationBuilder.DropCheckConstraint(
                name: "ck_journal_entries_source_type",
                table: "journal_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_journal_entries_status",
                table: "journal_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_vouchers_acquisition_source",
                table: "fuel_vouchers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_vouchers_data_kind",
                table: "fuel_vouchers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_held_has_order",
                table: "fuel_vouchers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_status",
                table: "fuel_vouchers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_price_history_data_kind",
                table: "fuel_price_history");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_price_history_reason",
                table: "fuel_price_history");

            migrationBuilder.DropCheckConstraint(
                name: "ck_feedback_message_category",
                table: "feedback_messages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_feedback_message_language",
                table: "feedback_messages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_feedback_message_severity",
                table: "feedback_messages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_feedback_message_status",
                table: "feedback_messages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_feedback_message_verdict",
                table: "feedback_messages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_device_status",
                table: "devices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_company_invitation_status",
                table: "company_invitations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_chart_of_accounts_account_type",
                table: "chart_of_accounts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_chart_of_accounts_normal_balance",
                table: "chart_of_accounts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_accounting_periods_status",
                table: "accounting_periods");

            migrationBuilder.AlterColumn<Guid>(
                name: "family_id",
                table: "refresh_tokens",
                type: "uuid",
                nullable: false,
                defaultValueSql: "'00000000-0000-0000-0000-000000000000'::uuid",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_events_outstanding",
                table: "webhook_events",
                column: "received_at_utc",
                filter: "status IN ('Received','Failed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_webhook_events_processed",
                table: "webhook_events",
                sql: "status NOT IN ('Processed','Ignored') OR processed_at_utc IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_webhook_events_status",
                table: "webhook_events",
                sql: "status IN ('Received','Processed','Ignored','Failed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_term_pricing_scheme",
                table: "voucher_term_pricing",
                sql: "scheme IN ('renewal_rate','purchase_term_discount')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_term_pricing_term_code",
                table: "voucher_term_pricing",
                sql: "term_code IN ('1w','2w','1m','2m','3m','4m','5m','6m')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_movements_actor_kind",
                table: "voucher_movements",
                sql: "actor_kind IN ('user','system')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_movements_kind",
                table: "voucher_movements",
                sql: "kind IN ('deposit','issued','returned','replace_out','exchange_out','exchange_in','gift','recall')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_movements_locations",
                table: "voucher_movements",
                sql: "from_location IN ('pool','customer','supplier','company','worker') AND to_location IN ('pool','customer','supplier','company','worker')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_imports_status",
                table: "voucher_imports",
                sql: "status IN ('Started','Completed','Failed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_events_actor_kind",
                table: "voucher_events",
                sql: "actor_kind IN ('user','system')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_events_statuses",
                table: "voucher_events",
                sql: "(from_status IS NULL OR from_status IN ('Imported','VerifiedWithWarnings','VerificationFailed','Available','Assigned','Used','Expired','Blocked','Deactivated')) AND (to_status IS NULL OR to_status IN ('Imported','VerifiedWithWarnings','VerificationFailed','Available','Assigned','Used','Expired','Blocked','Deactivated'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_events_type",
                table: "voucher_events",
                sql: "event_type IN ('Imported','StatusChanged','Extended','Archived','Unarchived')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_suppliers_acquisition_source",
                table: "suppliers",
                sql: "acquisition_source IN ('direct', 'partner')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_settlement_messages_outcome",
                table: "settlement_messages",
                sql: "outcome IN ('accepted','unallocated','unparseable','rejected_sender','rejected_chat')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_qr_parameters_ecc_level",
                table: "qr_parameters",
                sql: "ecc_level IN ('L','M','Q','H')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_kind",
                table: "payments",
                sql: "kind IN ('Capture','Refund')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_status",
                table: "payments",
                sql: "status IN ('Pending','Succeeded','Failed','Cancelled','Expired')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_parties_party_type",
                table: "parties",
                sql: "party_type IN ('Natural','Legal')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_outbox_event_type",
                table: "outbox_events",
                sql: "event_type IN ('OrderCreated', 'OrderFulfilled', 'OrderCancelled',\n               'PaymentCompleted', 'VoucherExpired', 'VoucherUsed', 'VoucherActivated',\n               'VoucherIssued')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_order_kind",
                table: "orders",
                sql: "kind IN ('Purchase', 'Renewal', 'ReceivedFromCompany')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_order_status",
                table: "orders",
                sql: "status IN ('PendingPayment', 'Paid', 'PendingFulfillment', 'PartiallyFulfilled',\n           'Fulfilled', 'PartiallyRefunded', 'Refunded', 'Cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_order_line_renewal_branch",
                table: "order_line_items",
                sql: "renewal_branch IS NULL OR renewal_branch IN ('extend','replace')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_order_events_event_type",
                table: "order_events",
                sql: "event_type IN ('Created', 'StatusChanged', 'InvoiceCreated', 'PaymentCaptured', 'Fulfilled', 'PartiallyFulfilled', 'Refunded', 'Cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_operator_renewal_branch",
                table: "operator_voucher_renewals",
                sql: "branch IN ('extend', 'replace')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_operator_legal_entities_tax_regime",
                table: "operator_legal_entities",
                sql: "tax_regime IN ('Vat','Simplified')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_movement_batches_kind",
                table: "movement_batches",
                sql: "kind IN ('voucher_exchange','bulk_issuance')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_movement_batches_status",
                table: "movement_batches",
                sql: "status IN ('draft','confirmed','rejected','cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_mapping_rules_side",
                table: "ledger_mapping_rules",
                sql: "side IN ('Debit','Credit')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_mapping_rules_source_type",
                table: "ledger_mapping_rules",
                sql: "source_type IN ('Payment','Refund','Fulfillment','Cost','Movement','Adjustment','Fiscalization','Reversal')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_journal_entries_source_type",
                table: "journal_entries",
                sql: "source_type IN ('Payment','Refund','Fulfillment','Cost','Movement','Adjustment','Fiscalization','Reversal')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_journal_entries_status",
                table: "journal_entries",
                sql: "status IN ('Draft','Posted','Reversed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_vouchers_acquisition_source",
                table: "fuel_vouchers",
                sql: "acquisition_source IN ('direct', 'partner')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_vouchers_data_kind",
                table: "fuel_vouchers",
                sql: "data_kind IN ('real', 'qa')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_held_has_order",
                table: "fuel_vouchers",
                sql: "status NOT IN ('Assigned', 'Used', 'Blocked') OR order_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_status",
                table: "fuel_vouchers",
                sql: "status IN ('Imported', 'VerifiedWithWarnings', 'VerificationFailed', 'Available',\n           'Assigned', 'Used', 'Expired', 'Blocked', 'Deactivated')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_price_history_data_kind",
                table: "fuel_price_history",
                sql: "data_kind IN ('real', 'qa')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_price_history_reason",
                table: "fuel_price_history",
                sql: "reason IN ('operator', 'pump-import', 'catalogue-import', 'batch-cost', 'exchange-release', 'backfill')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_feedback_message_category",
                table: "feedback_messages",
                sql: "category IS NULL OR category IN ('Bug', 'Idea', 'Complaint', 'Ux', 'Question', 'Praise', 'Other')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_feedback_message_language",
                table: "feedback_messages",
                sql: "language IS NULL OR language IN ('Uk', 'Ru', 'En', 'Other')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_feedback_message_severity",
                table: "feedback_messages",
                sql: "severity IS NULL OR severity IN ('Low', 'Medium', 'High')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_feedback_message_status",
                table: "feedback_messages",
                sql: "status IN ('New', 'Triaged', 'Handled', 'Rejected')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_feedback_message_verdict",
                table: "feedback_messages",
                sql: "verdict IS NULL OR verdict IN ('Junk', 'Kept')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_status",
                table: "devices",
                sql: "status IN ('Active', 'Revoked')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_company_invitation_status",
                table: "company_invitations",
                sql: "status IN ('Pending', 'Accepted', 'Declined', 'Cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_chart_of_accounts_account_type",
                table: "chart_of_accounts",
                sql: "account_type IN ('Asset','Liability','Equity','Revenue','Expense','Vat')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_chart_of_accounts_normal_balance",
                table: "chart_of_accounts",
                sql: "normal_balance IN ('Debit','Credit')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_accounting_periods_status",
                table: "accounting_periods",
                sql: "status IN ('Open','Closed')");
        }
    }
}
