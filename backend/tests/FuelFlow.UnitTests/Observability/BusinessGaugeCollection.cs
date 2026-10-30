using Xunit;

namespace FuelFlow.UnitTests.Observability;

/// <summary>
/// Keeps the suites that register or record the business gauges out of each other's runs.
/// <para>
/// An observable gauge lives on a process-wide meter, so a listener sees every registration any test
/// has published while it runs, and <c>RecordObservableInstruments</c> calls all of them. Two suites
/// reading gauges in parallel therefore read each other's hosts: a tagless zero from a registration
/// that returns an empty list, or another test's pack size. That is the shape of the failure -
/// "expected a single item, found 2" - that turned up intermittently on main and in CI.
/// </para>
/// </summary>
[CollectionDefinition(Name)]
public sealed class BusinessGaugeCollection
{
    public const string Name = "business gauges";
}
