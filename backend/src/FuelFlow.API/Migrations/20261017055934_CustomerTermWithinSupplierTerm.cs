using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// Makes the database enforce the rule the design documents already state:
    /// <c>customer_expiration_date &lt;= supplier_expiration_date</c>.
    ///
    /// <para>
    /// <b>Where the rule came from, because the code said otherwise for a long time.</b>
    /// <c>FuelVoucherConfiguration</c> carried a comment saying this constraint was deliberately
    /// absent, on the grounds that <c>ConfirmOperatorRenewalCommandHandler</c> "stamps the promise from
    /// the renewal term, not from the paper's own date", so <c>customer &gt; brand</c> was "a
    /// deliberate business rule". It cited <c>VOUCHER_RENEWAL.md:371-374</c>.
    ///
    /// <para>
    /// Those lines are an open question about whether a <b>replaced</b> voucher hands over its full
    /// paper term - a different operation. The rule actually being violated was the invariant stated a
    /// few pages earlier in that same document: <i>"The provider term never moves. If a rule would push
    /// a customer date past it, that is a replace, not an extension."</i> And that document's own table
    /// of what exists lists the extend ceiling as used at "quote, checkout <b>and fulfilment</b>" -
    /// three paths, with the operator path conspicuously absent, which is exactly where the gap was.
    /// So the citation did not support the claim, and the claim contradicted the stated rule.
    /// </para>
    ///
    /// <para>
    /// <b>Why adding it now is safe.</b> Every other write path already respected the rule, so a
    /// violating row could only have come from the operator path: import stamps both columns from the
    /// same document date, the fulfilment assign clamps with <c>LEAST(...)</c>, and both the extend and
    /// the replace updates carry the comparison in their own <c>WHERE</c>. That path now checks
    /// <c>VoucherRenewalEligibility.CanExtend</c> before it writes.
    /// </para>
    ///
    /// <para>
    /// <b>If this migration fails on an environment, it has earned it.</b> Postgres validates a CHECK
    /// against existing rows, so a failure means that database really does hold a voucher promised
    /// past its supplier's term - real money already promised against paper that will not honour it.
    /// That is the finding, not an obstacle to route around, so this does not go in
    /// <c>NOT VALID</c> and it does not silently clamp anything. Production holds zero
    /// <c>fuel_vouchers</c> rows, so it applies there unconditionally.
    /// </para>
    /// </summary>
    public partial class CustomerTermWithinSupplierTerm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_customer_term_within_supplier_term",
                table: "fuel_vouchers",
                sql: "customer_expiration_date <= supplier_expiration_date");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_customer_term_within_supplier_term",
                table: "fuel_vouchers");
        }
    }
}