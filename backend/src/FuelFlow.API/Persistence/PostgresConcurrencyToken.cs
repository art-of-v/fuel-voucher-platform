using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Persistence;

/// <summary>
/// Concurrency token backed by Postgres' own <c>xmin</c> system column.
/// <para>
/// <c>xmin</c> changes on every row update, so EF adds <c>AND xmin = @original</c> to the UPDATE's
/// WHERE clause. Two writers that both read the same row and then write it: exactly one wins, and
/// the other raises <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/> instead
/// of silently overwriting. It needs no schema change - <c>xmin</c> is a system column that is
/// already there - and it cannot drift, because nothing maintains it.
/// </para>
/// <para>
/// <b>Which tables can carry this.</b> Only tables whose rows are written exclusively through the
/// change tracker. A table that is also written with raw SQL is disqualified: the raw UPDATE bumps
/// <c>xmin</c> behind EF's back, and the tracked entity's own save then fails its predicate - not
/// because anything raced, but because EF's snapshot went stale the moment the raw statement ran.
/// <c>fuel_vouchers</c> (7 raw UPDATE sites) and <c>orders</c> (2) are written that way and are
/// deliberately excluded. Their raw statements have to become conditional UPDATEs first; those sites
/// are already protected by status predicates and advisory locks in the meantime.
/// </para>
/// <para>
/// This was landed as a <b>detector</b>, not as a fix. Two attempts to find a live lost-update by
/// reading the code both came back wrong - the handlers re-read fresh, or prefer an already-tracked
/// entity, so the stale-snapshot window they appeared to have was not reachable. The token exists to
/// produce evidence about which handlers genuinely race in production, instead of guessing from the
/// source. Every hit is logged with its entity type and key; nothing is swallowed.
/// </para>
/// </summary>
internal static class PostgresConcurrencyToken
{
    /// <summary>
    /// The shadow property's CLR name. It is a shadow property so no entity has to carry a member
    /// that means nothing to the domain, and so no model class has to change to add the token.
    /// </summary>
    internal const string PropertyName = "xmin";

    /// <summary>
    /// Adds the token to <typeparamref name="TEntity"/>. Safe to call once per entity type; the
    /// shadow property is named after <c>xmin</c> so EF maps it onto the system column.
    /// </summary>
    internal static EntityTypeBuilder<TEntity> UseXminAsConcurrencyToken<TEntity>(
        this EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        // uint, not byte[]: Npgsql maps `xid` to uint and refuses byte[] outright - the model fails
        // to build with "the database brand does not support mapping 'byte[]' properties to
        // 'xid' columns", which takes the whole context down at startup and at design time. The
        // widely-copied byte[] version of this pattern does not compile against this brand.
        builder.Property<uint>(PropertyName)
            .HasColumnName(PropertyName)
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        return builder;
    }
}