using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// Deletes outright the two fuels <see cref="ArchiveTheImportedDuplicatesOfOneGrade"/> archived an
    /// hour earlier, at the operator's instruction: a row that exists only to be ignored is still a row
    /// every future query has to filter, and its 14 nominals and 2 price-history rows are the only thing
    /// it still carries.
    /// <para>
    /// <b>The archive rule (#217) does not defend these rows.</b> It exists because deleting a catalogue
    /// entry cascades into vouchers — real, paid-for paper — and destroys the record of what a customer
    /// bought. There are <b>zero</b> vouchers against either id; they were born from a defective import,
    /// were never sold, and the grade they held is carried by a fuel that was never touched. So the
    /// cascade that rule guards against cannot happen here, and the row's remaining value is only the
    /// confusion of its own existence.
    /// </para>
    /// <para>
    /// Deletion order is the foreign keys, which were read off the live schema rather than assumed:
    /// <c>fuel_vouchers.fuel_type_id</c>, <c>fuel_offer_prices.offer_id</c> and
    /// <c>fuel_offer_volumes.offer_id</c> are the only inbound references in the database, and none of
    /// them cascades. The audit trail is not among them, so the record that these fuels were created and
    /// archived survives the rows — which is what an audit trail is for.
    /// </para>
    /// <para>
    /// <b>Guarded three ways</b>, so a stale data fix is a no-op rather than a second accident: the rows
    /// must still be archived, must still carry one of the two codes, and must have no vouchers. If
    /// anybody sells against them between this being merged and this running, nothing is deleted.
    /// </para>
    /// <para>
    /// Not reversible by design: <see cref="Down"/> raises rather than resurrecting rows it cannot
    /// reconstruct. The archived state and their nominals are recoverable from the database's own history
    /// if that ever matters, which is a different and much slower promise than a <c>DELETE</c>.
    /// </para>
    /// </summary>
    public partial class HardDeleteTheImportedDuplicateFuels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM fuel_offer_prices
                 WHERE offer_id IN (
                       SELECT id FROM fuel_offers
                        WHERE fuel_type_id IN ('01a14a6e-687d-76d1-8ab3-d36676ac582e',
                                              '01a14a6e-68d1-7ea1-a485-bb217afacbb2'));

                DELETE FROM fuel_offer_volumes
                 WHERE offer_id IN (
                       SELECT id FROM fuel_offers
                        WHERE fuel_type_id IN ('01a14a6e-687d-76d1-8ab3-d36676ac582e',
                                              '01a14a6e-68d1-7ea1-a485-bb217afacbb2'));

                DELETE FROM fuel_offers
                 WHERE fuel_type_id IN ('01a14a6e-687d-76d1-8ab3-d36676ac582e',
                                       '01a14a6e-68d1-7ea1-a485-bb217afacbb2');

                DELETE FROM fuel_types
                 WHERE id IN ('01a14a6e-687d-76d1-8ab3-d36676ac582e',
                              '01a14a6e-68d1-7ea1-a485-bb217afacbb2')
                   AND is_deleted
                   AND brand_fuel_code IN ('Pulls Diesel', 'Pulls 95')
                   AND NOT EXISTS (SELECT 1 FROM fuel_vouchers v WHERE v.fuel_type_id = fuel_types.id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "These rows were deleted outright and cannot be reconstructed from a migration: their "
                + "ids, names, creation timestamps and nominals existed only in the database. Recreating "
                + "them would fabricate catalogue history that never happened to any price that was ever "
                + "charged.");
        }
    }
}
