using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

/// <summary>
/// Gives the double-entry journal the three things it needed to actually be usable. The tables
/// themselves arrived in <c>20261017193621_DoubleEntryJournal</c>, complete down to the triggers
/// that refuse an unbalanced posting and freeze a posted one - but nothing could ever be written,
/// because no code path produced an entry.
/// </summary>
/// <remarks>
/// <para>
/// <b>source_event</b> is the reason this migration exists. <c>journal_entries</c> recorded the
/// operational row that caused an entry, but not <em>which</em> fact about that row. One payment
/// legitimately produces three postings - revenue, VAT, cost of sales - so keying idempotency on
/// the source row alone would let the second posting be swallowed as a duplicate of the first.
/// Storing the event on the entry makes <c>(source_event, source_type, source_id)</c> both correct
/// and sufficient as a uniqueness key.
/// </para>
/// <para>
/// The column is non-null with a default of <c>''</c> because the table is empty. If it were not,
/// the default would be a lie and the check constraint added below would reject every existing row.
/// </para>
/// <para>
/// <b>document_sequences</b> is a table the code had already promised and never created:
/// <c>JournalEntry.EntryNo</c> documented itself as "allocated from document_sequences". Allocating
/// by <c>MAX(entry_no) + 1</c> instead would hand the same number to two concurrent posts and turn
/// the existing unique index on <c>(period_id, entry_no)</c> into an unhandled error.
/// </para>
/// </remarks>
namespace FuelFlow.API.Migrations
{    /// <inheritdoc />
    public partial class DoubleEntryPostingEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "source_event",
                table: "journal_entries",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "document_sequences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    last_value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_sequences", x => x.id);
                    table.CheckConstraint("ck_document_sequences_non_negative", "last_value >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_source_event_source_type_source_id",
                table: "journal_entries",
                columns: new[] { "source_event", "source_type", "source_id" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_journal_entries_source_event_not_blank",
                table: "journal_entries",
                sql: "btrim(source_event) <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_document_sequences_name",
                table: "document_sequences",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_sequences");

            migrationBuilder.DropIndex(
                name: "IX_journal_entries_source_event_source_type_source_id",
                table: "journal_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_journal_entries_source_event_not_blank",
                table: "journal_entries");

            migrationBuilder.DropColumn(
                name: "source_event",
                table: "journal_entries");
        }
    }
}
