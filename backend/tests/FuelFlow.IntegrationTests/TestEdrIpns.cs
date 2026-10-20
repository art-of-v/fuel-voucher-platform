using System.Threading;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// A registry code for a test fixture.
/// <para>
/// The supplier's registry code became unique in the database on 2026-10-20, and the
/// integration suite shares one database per test class - so a fixture that hard-codes
/// the same code twice collides with itself the second time the class runs. The codes
/// are therefore handed out from one counter, and each is different.
/// </para>
/// <para>
/// The values are 10 digits so they satisfy the same format check as a real one: a
/// fixture that could not satisfy the constraint would leave the test database unable
/// to take the migration at all.
/// </para>
/// </summary>
public static class TestEdrIpns
{
    private static int _next = 900000000;

    public static string Next() => Interlocked.Increment(ref _next).ToString();
}
