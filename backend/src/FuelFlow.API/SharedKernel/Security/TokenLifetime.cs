using FuelFlow.SharedKernel.Time;

namespace FuelFlow.SharedKernel.Security;
/// <summary>
/// The application's rule for a token's lifetime.
/// </summary>
/// <remarks>
/// <para>
/// The framework's own lifetime check reads the machine's clock. That is right for a library and
/// wrong for this application: the token is minted by <c>JwtTokenService</c> against the
/// application's clock, so judging it by another clock means the two can disagree - and on the
/// test suite's fixed instant they always did, which made every token look expired.
/// </para>
/// <para>
/// One rule, in one place, used by the API when it validates a bearer token and by the test that
/// validates one. If the two ever drift apart, the test stops proving what the application does.
/// </para>
/// </remarks>
public static class TokenLifetime
{
    /// <summary>
    /// True when the token may still be used: it has started, and it has not expired.
    /// </summary>
    /// <param name="notBefore">The token's "not before" instant, if it carries one.</param>
    /// <param name="expires">The token's "expires" instant, if it carries one.</param>
    public static bool IsValid(DateTime? notBefore, DateTime? expires)
    {
        var now = SystemClock.UtcNow;

        if (notBefore.HasValue && now < notBefore.Value)
        {
            return false;
        }

        return !expires.HasValue || now <= expires.Value;
    }
}
