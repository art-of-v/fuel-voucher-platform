namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// A row that is written once and never rewritten: an audit or custody record whose value is that
/// it still says what it said when it was written.
/// </summary>
/// <remarks>
/// <para>
/// The marker is read by <c>PrivilegeMatrixCoversTheModelTests</c>, which holds
/// <c>deploy/database-privileges.tsv</c> to it: an append-only entity's table grants the traffic
/// role SELECT and INSERT, and no UPDATE or DELETE at all. It has no runtime effect - the database
/// grant is the enforcement - and it exists because the grant alone cannot tell a table that is
/// deliberately append-only from one whose write path the matrix forgot, which is planning #725.
/// </para>
/// <para>
/// A row that is closed in place rather than appended to - a webhook delivery that moves from
/// Received to Processed - is not append-only: it declares the few columns a later step may write
/// with <see cref="MutableAfterInsertAttribute"/> instead.
/// </para>
/// </remarks>
public interface IAppendOnlyEntity
{
}
