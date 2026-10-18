using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class DoubleEntryJournal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounting_periods",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false, defaultValue: "Open"),
                    closed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    closed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_periods", x => x.id);
                    table.CheckConstraint("ck_accounting_periods_closed_names_a_closer", "status <> 'Closed' OR closed_by_user_id IS NOT NULL");
                    table.CheckConstraint("ck_accounting_periods_closure_shape", "(status = 'Closed') = (closed_at_utc IS NOT NULL)");
                    table.CheckConstraint("ck_accounting_periods_range", "period_end >= period_start");
                    table.CheckConstraint("ck_accounting_periods_status", "status IN ('Open','Closed')");
                    table.ForeignKey(
                        name: "FK_accounting_periods_users_closed_by_user_id",
                        column: x => x.closed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "chart_of_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    name_ukr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    account_type = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    normal_balance = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_postable = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chart_of_accounts", x => x.id);
                    table.CheckConstraint("ck_chart_of_accounts_account_type", "account_type IN ('Asset','Liability','Equity','Revenue','Expense','Vat')");
                    table.CheckConstraint("ck_chart_of_accounts_code_is_numeric", "code ~ '^[0-9]+$'");
                    table.CheckConstraint("ck_chart_of_accounts_code_not_blank", "btrim(code) <> ''");
                    table.CheckConstraint("ck_chart_of_accounts_normal_balance", "normal_balance IN ('Debit','Credit')");
                    table.CheckConstraint("ck_chart_of_accounts_not_its_own_parent", "parent_id IS DISTINCT FROM id");
                    table.ForeignKey(
                        name: "FK_chart_of_accounts_chart_of_accounts_parent_id",
                        column: x => x.parent_id,
                        principalTable: "chart_of_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "journal_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_no = table.Column<long>(type: "bigint", nullable: false),
                    entry_date = table.Column<DateOnly>(type: "date", nullable: false),
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "Draft"),
                    posted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    posted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reversed_by_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tamper_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_journal_entries", x => x.id);
                    table.CheckConstraint("ck_journal_entries_description_not_blank", "btrim(description) <> ''");
                    table.CheckConstraint("ck_journal_entries_entry_no_positive", "entry_no > 0");
                    table.CheckConstraint("ck_journal_entries_not_own_reversal", "reversed_by_entry_id IS DISTINCT FROM id");
                    table.CheckConstraint("ck_journal_entries_posted_shape", "(status = 'Draft') OR (posted_at_utc IS NOT NULL AND posted_by_user_id IS NOT NULL)");
                    table.CheckConstraint("ck_journal_entries_reversed_shape", "status <> 'Reversed' OR reversed_by_entry_id IS NOT NULL");
                    table.CheckConstraint("ck_journal_entries_source_type", "source_type IN ('Payment','Refund','Fulfillment','Cost','Movement','Adjustment','Fiscalization','Reversal')");
                    table.CheckConstraint("ck_journal_entries_status", "status IN ('Draft','Posted','Reversed')");
                    table.CheckConstraint("ck_journal_entries_tamper_hash_length", "tamper_hash IS NULL OR tamper_hash ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "FK_journal_entries_accounting_periods_period_id",
                        column: x => x.period_id,
                        principalTable: "accounting_periods",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_journal_entries_journal_entries_reversed_by_entry_id",
                        column: x => x.reversed_by_entry_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_journal_entries_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_journal_entries_users_posted_by_user_id",
                        column: x => x.posted_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ledger_mapping_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    source_event = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    side = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_mapping_rules", x => x.id);
                    table.CheckConstraint("ck_ledger_mapping_rules_event_not_blank", "btrim(source_event) <> ''");
                    table.CheckConstraint("ck_ledger_mapping_rules_side", "side IN ('Debit','Credit')");
                    table.CheckConstraint("ck_ledger_mapping_rules_source_type", "source_type IN ('Payment','Refund','Fulfillment','Cost','Movement','Adjustment','Fiscalization','Reversal')");
                    table.ForeignKey(
                        name: "FK_ledger_mapping_rules_chart_of_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "chart_of_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "journal_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<short>(type: "smallint", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    party_id = table.Column<Guid>(type: "uuid", nullable: true),
                    offer_id = table.Column<string>(type: "text", maxLength: 103, nullable: true),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    voucher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity_liters = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    debit_uah = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false, defaultValue: 0m),
                    credit_uah = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false, defaultValue: 0m)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_journal_lines", x => x.id);
                    table.CheckConstraint("ck_journal_lines_amounts_non_negative", "debit_uah >= 0 AND credit_uah >= 0");
                    table.CheckConstraint("ck_journal_lines_line_no_positive", "line_no > 0");
                    table.CheckConstraint("ck_journal_lines_one_side", "(debit_uah > 0 AND credit_uah = 0) OR (credit_uah > 0 AND debit_uah = 0)");
                    table.CheckConstraint("ck_journal_lines_quantity_positive", "quantity_liters IS NULL OR quantity_liters > 0");
                    table.ForeignKey(
                        name: "FK_journal_lines_chart_of_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "chart_of_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_journal_lines_fuel_offers_offer_id",
                        column: x => x.offer_id,
                        principalTable: "fuel_offers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_journal_lines_fuel_vouchers_voucher_id",
                        column: x => x.voucher_id,
                        principalTable: "fuel_vouchers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_journal_lines_journal_entries_entry_id",
                        column: x => x.entry_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_journal_lines_legal_entities_legal_entity_id",
                        column: x => x.legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_journal_lines_parties_party_id",
                        column: x => x.party_id,
                        principalTable: "parties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_periods_closed_by_user_id",
                table: "accounting_periods",
                column: "closed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_periods_period_start",
                table: "accounting_periods",
                column: "period_start",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_periods_period_start_period_end",
                table: "accounting_periods",
                columns: new[] { "period_start", "period_end" });

            migrationBuilder.CreateIndex(
                name: "IX_chart_of_accounts_code",
                table: "chart_of_accounts",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_chart_of_accounts_parent_id",
                table: "chart_of_accounts",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_created_by_user_id",
                table: "journal_entries",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_period_id_entry_no",
                table: "journal_entries",
                columns: new[] { "period_id", "entry_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_posted_by_user_id",
                table: "journal_entries",
                column: "posted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_reversed_by_entry_id",
                table: "journal_entries",
                column: "reversed_by_entry_id");

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_source_type_source_id",
                table: "journal_entries",
                columns: new[] { "source_type", "source_id" });

            migrationBuilder.CreateIndex(
                name: "IX_journal_lines_account_id",
                table: "journal_lines",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_journal_lines_entry_id_line_no",
                table: "journal_lines",
                columns: new[] { "entry_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_journal_lines_legal_entity_id",
                table: "journal_lines",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "IX_journal_lines_offer_id",
                table: "journal_lines",
                column: "offer_id");

            migrationBuilder.CreateIndex(
                name: "IX_journal_lines_party_id",
                table: "journal_lines",
                column: "party_id");

            migrationBuilder.CreateIndex(
                name: "IX_journal_lines_voucher_id",
                table: "journal_lines",
                column: "voucher_id");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_mapping_rules_account_id",
                table: "ledger_mapping_rules",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_mapping_rules_source_event_side",
                table: "ledger_mapping_rules",
                columns: new[] { "source_event", "side" },
                unique: true);

            // -------------------------------------------------------------------------------
            // D-04 and D-05: the two objects that separate "a table with debit and credit
            // columns" from double-entry accounting. Without them the journal is a convention,
            // and a convention is not an invariant.
            //
            // Nothing writes here yet. These are the guard rails, standing on an empty track.
            // -------------------------------------------------------------------------------

            // D-05 - a posted entry must balance.
            //
            // DEFERRABLE INITIALLY DEFERRED because the guard has to run at COMMIT, not per
            // statement: a bookkeeper composing an entry adds a debit line and a credit line in two
            // statements, and the first of those is unbalanced on its own. The draft guard is what
            // makes that legal - an entry still in Draft may sit unbalanced; posting it may not.
            //
            // It fires on journal_lines because that is where a balance changes. Editing the lines of
            // an already-posted entry also passes through here, which is why
            // trg_journal_lines_posted_is_immutable exists: this trigger would notice the imbalance,
            // not the edit.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION assert_journal_entry_balanced() RETURNS trigger AS $$
                DECLARE
                    v_entry  uuid;
                    v_status varchar(10);
                    v_sum    numeric;
                BEGIN
                    -- Nested IFs, not a CASE over TG_TABLE_NAME: PL/pgSQL prepares the whole
                    -- expression, so a branch naming a column the current table lacks fails even when
                    -- it is not taken. journal_entries has no entry_id of its own.
                    IF TG_OP = 'DELETE' THEN
                        IF TG_TABLE_NAME = 'journal_entries' THEN
                            v_entry := OLD.id;
                        ELSE
                            v_entry := OLD.entry_id;
                        END IF;
                    ELSE
                        IF TG_TABLE_NAME = 'journal_entries' THEN
                            v_entry := NEW.id;
                        ELSE
                            v_entry := NEW.entry_id;
                        END IF;
                    END IF;

                    IF v_entry IS NULL THEN
                        RETURN NULL;
                    END IF;

                    SELECT status INTO v_status FROM journal_entries WHERE id = v_entry;
                    IF NOT FOUND OR v_status = 'Draft' THEN
                        RETURN NULL;   -- drafts may be transiently unbalanced
                    END IF;

                    SELECT COALESCE(SUM(debit_uah), 0) - COALESCE(SUM(credit_uah), 0) INTO v_sum
                      FROM journal_lines
                     WHERE entry_id = v_entry;

                    IF v_sum <> 0 THEN
                        RAISE EXCEPTION 'journal entry % is unbalanced by %', v_entry, v_sum;
                    END IF;

                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;

                CREATE CONSTRAINT TRIGGER trg_journal_entry_balanced
                  AFTER INSERT OR UPDATE OR DELETE ON journal_lines
                  DEFERRABLE INITIALLY DEFERRED
                  FOR EACH ROW
                  EXECUTE FUNCTION assert_journal_entry_balanced();

                -- The same check again, on the other table.
                --
                -- The lines trigger only fires when a LINE changes, so an entry posted by updating
                -- journal_entries alone - lines committed earlier, in their own transaction, while the
                -- entry was still a draft - was never checked at all. That is not a theoretical path:
                -- it is how EF saves a change, one call at a time, and the unbalanced entry committed
                -- silently. Deferred like its twin, so a posting composed over several statements is
                -- still judged as one transaction.
                --
                -- Together they cover both ways an entry can be made unbalanced: editing its lines,
                -- and posting it.
                CREATE CONSTRAINT TRIGGER trg_journal_entry_balanced_on_post
                  AFTER UPDATE ON journal_entries
                  DEFERRABLE INITIALLY DEFERRED
                  FOR EACH ROW
                  WHEN (NEW.status = 'Posted')
                  EXECUTE FUNCTION assert_journal_entry_balanced();
                """);

            // D-04, part one: a posted entry is never edited.
            //
            // Two triggers rather than the one TARGET_SCHEMA.md sketched, because the sketch left the
            // obvious way round. It guarded UPDATE/DELETE on journal_entries only, so an entry already
            // posted into a closed month could have its LINES inserted, changed or deleted - the
            // numbers underneath a closed month would move while the entry row stayed put. That is not
            // an immutability boundary, it is a suggestion.
            //
            // The spec's function also reads NEW.entry_id on journal_entries, which has no entry_id
            // column; it could never have run. Here the entry is identified by its own id.
            //
            // Errors, not policy: this is what "closed periods are immutable" has to mean if it is to
            // survive a hurried operator and a psql session.
            // D-04 in two halves, because they are two different rules and conflating them leaves a hole.
            //
            //   "A posted entry is never edited." Correcting one means posting the opposite entry; the
            //   original stays on the books for the auditor. This holds in an OPEN period too - posting
            //   is what freezes an entry, not closing the month.
            //
            //   "A closed period takes no new postings." That is the other half, and it lives in
            //   assert_posting_is_coherent below, because it is about creating a posting rather than
            //   editing one.
            //
            // The design document sketched a single trigger on journal_entries only, which missed both
            // that the lines underneath a posted entry could still be inserted, changed or deleted, and
            // that its function read NEW.entry_id on a table with no such column - it could never have
            // run.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION assert_lines_frozen() RETURNS trigger AS $$
                DECLARE
                    v_status   varchar(10);
                    v_entry_id uuid := CASE WHEN TG_OP = 'DELETE' THEN OLD.entry_id ELSE NEW.entry_id END;
                BEGIN
                    SELECT status INTO v_status FROM journal_entries WHERE id = v_entry_id;
                    IF NOT FOUND OR v_status = 'Draft' THEN
                        RETURN CASE WHEN TG_OP = 'DELETE' THEN OLD ELSE NEW END;
                    END IF;

                    RAISE EXCEPTION 'journal entry % is posted and its lines are frozen; post a reversal instead', v_entry_id;
                END;
                $$ LANGUAGE plpgsql;

                -- AFTER, not BEFORE, and that is not a style choice.
                --
                -- BEFORE, and the function hands the row back when it allows the write.
                --
                -- A BEFORE trigger that returns NULL cancels the operation SILENTLY: no error, no
                -- exception, and the row is simply not written. A guard written that way does not
                -- guard anything - it swallows every write it sees, allowed ones included, and the
                -- only symptom is an INSERT reporting zero rows affected and throwing nothing. That
                -- was the first version of this trigger. Every path in the function now either raises
                -- or returns the row, and there is no third option.

                -- The money. Once an entry is posted its lines are frozen: no insert, no change, no
                -- delete. A correction is a new, reversing entry - which is why nothing here ever
                -- edits in place.
                CREATE TRIGGER trg_journal_lines_posted_is_immutable
                  BEFORE INSERT OR UPDATE OR DELETE ON journal_lines
                  FOR EACH ROW
                  EXECUTE FUNCTION assert_lines_frozen();
                """);

            // Posting is the moment an entry becomes a fact, so it is where "coherent" is checked: the
            // entry must be dated inside the period it claims, and that period must still be open. A
            // draft may sit outside its period or in a closed one while it is composed - that is what
            // draft is for.
            //
            // The date-within-period rule cannot live in a CHECK: it needs accounting_periods, and
            // PostgreSQL rejects a CHECK containing a subquery.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION assert_posting_is_coherent() RETURNS trigger AS $$
                DECLARE
                    v_start date;
                    v_end   date;
                    v_state varchar(8);
                BEGIN
                    IF NEW.status IS DISTINCT FROM 'Posted' THEN
                        RETURN NEW;
                    END IF;

                    SELECT period_start, period_end, status INTO v_start, v_end, v_state
                      FROM accounting_periods WHERE id = NEW.period_id;

                    IF NOT FOUND THEN
                        RAISE EXCEPTION 'journal entry % names period % which does not exist',
                                        NEW.id, NEW.period_id;
                    END IF;

                    IF v_state <> 'Open' THEN
                        RAISE EXCEPTION 'accounting period % is closed; post a reversal instead', NEW.period_id;
                    END IF;

                    IF NEW.entry_date < v_start OR NEW.entry_date > v_end THEN
                        RAISE EXCEPTION 'journal entry % is dated % which is outside period % (%..%)',
                                        NEW.id, NEW.entry_date, NEW.period_id, v_start, v_end;
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER trg_journal_entry_posted_is_coherent
                  BEFORE INSERT OR UPDATE ON journal_entries
                  FOR EACH ROW
                  EXECUTE FUNCTION assert_posting_is_coherent();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_journal_entry_posted_is_coherent ON journal_entries;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_journal_lines_posted_is_immutable ON journal_lines;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_journal_lines_posted_is_immutable ON journal_lines;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_journal_entry_balanced_on_post ON journal_entries;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_journal_entry_balanced ON journal_lines;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS assert_posting_is_coherent();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS assert_lines_frozen();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS assert_journal_entry_balanced();");
            migrationBuilder.DropTable(
                name: "journal_lines");

            migrationBuilder.DropTable(
                name: "ledger_mapping_rules");

            migrationBuilder.DropTable(
                name: "journal_entries");

            migrationBuilder.DropTable(
                name: "chart_of_accounts");

            migrationBuilder.DropTable(
                name: "accounting_periods");
        }
    }
}
