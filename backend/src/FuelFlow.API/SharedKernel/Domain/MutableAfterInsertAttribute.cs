namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// Marks a property an otherwise write-once row is allowed to move when the row is closed in place:
/// the webhook delivery that becomes Processed, the redemption a supplier confirms.
/// </summary>
/// <remarks>
/// <para>
/// <c>deploy/database-privileges.tsv</c> grants the traffic role a column-level UPDATE for exactly
/// these properties, and <c>PrivilegeMatrixCoversTheModelTests</c> fails when the two disagree in
/// either direction: a marked column the matrix does not grant is a production <c>42501</c> waiting
/// for its first write, and a granted column nothing marks is a privilege wider than the model.
/// </para>
/// <para>
/// A property with this attribute on a fully mutable entity is reported too: that entity's table
/// already carries a table-level UPDATE, so the narrower declaration contradicts it.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class MutableAfterInsertAttribute : Attribute
{
}
