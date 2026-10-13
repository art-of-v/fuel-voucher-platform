using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// Gives the seeded QA account a real identity - a name, an email, and a key generated the same
    /// way every other key in the system is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The account was seeded with no name and no email, so it rendered as "No Name" and "N/A"
    /// everywhere it appears: the admin users table, every audit row it is attributed in, the
    /// recipient of a notification, and the payer name on a Monobank receipt. A reviewer's activity
    /// showing up as "No Name" is a poor look for a row a human is watching, and the email is what an
    /// operator would use to find the account again. The name is split across both columns because
    /// the admin renders them separately: "Quality" / "Assurance Engineer" reads as a person there,
    /// where a single long first name reads as a paste error.
    /// </para>
    /// <para>
    /// The id was a hand-written literal from the original seed
    /// (<c>a10c1de5-4a11-4b00-8000-000000000001</c>) and is replaced with a v7 key, matching
    /// <c>perf(db): generate time-ordered UUIDs for entity keys</c>. Every key written since has been
    /// time-ordered; this one was the last hand-written exception.
    /// </para>
    /// <para>
    /// <b>Why the rekey is conditional.</b> Twenty-six columns across the database hold a user id and
    /// only sixteen are foreign keys to <c>users</c>. The rest -
    /// <c>provider_event_outbox.changed_by_user_id</c>, <c>operator_voucher_renewals
    /// .acting_user_id</c>, <c>voucher_exchanges.acting_user_id</c>, <c>purchase_batches
    /// .entered_by_user_id</c>, the feedback and error tables - carry no constraint, so renaming the
    /// parent would orphan them silently: the rows would remain, still naming an id that no longer
    /// exists, and nothing would complain. The list is read from <c>information_schema</c> rather
    /// than written by hand, so a table added later cannot be missed here.
    /// </para>
    /// <para>
    /// So the name and the email are applied unconditionally, and the rekey runs only when nothing
    /// anywhere still names the old key - at which point a single <c>UPDATE users</c> is complete and
    /// there is no second table left to sweep. Once a reviewer has signed in there is a refresh
    /// token; once anything is bought there is an order. Rather than trust this migration to land in
    /// the window before both, it checks, and if the account already has history it applies the
    /// identity and logs what it skipped and why - the outcome a careful operator would pick anyway.
    /// A notice rather than an exception, because a migration that raises blocks every deploy behind
    /// it, and the safe half of this work is worth having either way.
    /// </para>
    /// </remarks>
    public partial class QaAccountIdentity : Migration
    {
        private const string OldId = "a10c1de5-4a11-4b00-8000-000000000001";
        private const string NewId = "01a136cb-e966-79bf-8cd3-99ccf51bd6b0";
        private const string Phone = "+380110010203";
        private const string FirstName = "Quality";
        private const string LastName = "Assurance Engineer";
        private const string Email = "qa@fuelflow.com";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                $"""
                DO $$
                DECLARE
                    uid       UUID;
                    offenders TEXT[] := ARRAY[]::TEXT[];
                    ref       TEXT;
                    n         BIGINT;
                BEGIN
                    SELECT id INTO uid FROM users WHERE phone_number = '{Phone}' AND NOT is_deleted;
                    IF uid IS NULL THEN
                        RAISE NOTICE 'no active QA account for %; nothing to do', '{Phone}';
                        RETURN;
                    END IF;

                    -- Identity first, and unconditionally: this half is correct regardless of history.
                    UPDATE users
                    SET first_name = COALESCE(first_name, '{FirstName}'),
                        last_name  = COALESCE(last_name,  '{LastName}'),
                        email      = COALESCE(email,      '{Email}'),
                        updated_at_utc = now()
                    WHERE id = uid;

                    IF uid <> '{OldId}'::uuid THEN
                        RAISE NOTICE 'QA account is already keyed %; rekey not applicable', uid;
                        RETURN;
                    END IF;

                    -- Anything at all still naming the old key means the rekey is unsafe: the columns
                    -- without a foreign key would keep pointing at an id that no longer exists, and
                    -- nothing in the database would notice.
                    FOR ref IN
                        SELECT c.table_name || '.' || c.column_name
                        FROM information_schema.columns c
                        WHERE c.table_schema = 'public'
                          AND c.column_name LIKE '%user_id%'
                        ORDER BY 1
                    LOOP
                        EXECUTE format('SELECT count(*) FROM %I WHERE %I::text = $1', split_part(ref, '.', 1), split_part(ref, '.', 2))
                            INTO n USING uid::text;
                        IF n > 0 THEN
                            offenders := offenders || ref;
                        END IF;
                    END LOOP;

                    IF array_length(offenders, 1) > 0 THEN
                        RAISE NOTICE 'QA account has history in % - name and email applied, rekey % -> % SKIPPED',
                            array_to_string(offenders, ', '), uid, '{NewId}';
                        RETURN;
                    END IF;

                    UPDATE users SET id = '{NewId}'::uuid WHERE id = uid;
                    RAISE NOTICE 'QA account rekeyed % -> % and identified as % %', uid, '{NewId}', '{FirstName}', '{LastName}';
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                $"""
                DO $$
                DECLARE
                    uid       UUID;
                    offenders TEXT[] := ARRAY[]::TEXT[];
                    ref       TEXT;
                    n         BIGINT;
                BEGIN
                    SELECT id INTO uid FROM users WHERE phone_number = '{Phone}' AND NOT is_deleted;
                    IF uid IS NULL THEN
                        RETURN;
                    END IF;

                    -- Same guard in reverse: putting the old key back while rows name the new one
                    -- would orphan them the same way.
                    FOR ref IN
                        SELECT c.table_name || '.' || c.column_name
                        FROM information_schema.columns c
                        WHERE c.table_schema = 'public'
                          AND c.column_name LIKE '%user_id%'
                        ORDER BY 1
                    LOOP
                        EXECUTE format('SELECT count(*) FROM %I WHERE %I::text = $1', split_part(ref, '.', 1), split_part(ref, '.', 2))
                            INTO n USING uid::text;
                        IF n > 0 THEN
                            offenders := offenders || ref;
                        END IF;
                    END LOOP;

                    IF array_length(offenders, 1) > 0 THEN
                        RAISE NOTICE 'QA account has history in %; rekey back SKIPPED', array_to_string(offenders, ', ');
                        RETURN;
                    END IF;

                    UPDATE users SET id = '{OldId}'::uuid WHERE id = uid;
                END $$;
                """);
        }
    }
}
