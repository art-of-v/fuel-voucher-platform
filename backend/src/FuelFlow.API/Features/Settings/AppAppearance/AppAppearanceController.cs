using FuelFlow.Features.Settings.SharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Settings.AppAppearance;

/// <summary>
/// What the mobile app needs to paint itself before it knows who is holding it.
/// </summary>
/// <remarks>
/// <para>
/// Anonymous on purpose: the theme is the first thing the phone draws — the splash, the sign-in
/// screen, the app lock — and waiting for a token to learn it would mean those screens paint in the
/// default palette and then jump to the configured one. The answer carries a palette id and nothing
/// else, so there is nothing behind it to leak.
/// </para>
/// <para>
/// Cached for a short window, briefly in proportion to how rarely it changes: a manager flipping
/// the default is a decision made a handful of times, not a control someone drags. The cache is not
/// part of correctness either way — <see cref="RuntimeSettingsService.GetDefaultAppThemeAsync"/>
/// reads the row fresh, and the app falls back to the default if this endpoint is unreachable.
/// </para>
/// </remarks>
[ApiController]
[AllowAnonymous]
[ResponseCache(Duration = 60)]
[Route("api/app/appearance")]
public sealed class AppAppearanceController : ControllerBase
{
    private readonly RuntimeSettingsService _settings;

    public AppAppearanceController(RuntimeSettingsService settings) => _settings = settings;

    /// <summary>
    /// The installation's default theme, always a known id. It is a default, not an order: the app
    /// applies it only where nobody has chosen a theme on that device.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
        => Ok(new AppAppearanceDto
        {
            DefaultTheme = await _settings.GetDefaultAppThemeAsync(cancellationToken)
        });
}

/// <summary>The wire shape: one id, camelCased by the JSON policy.</summary>
public sealed class AppAppearanceDto
{
    /// <summary>A theme id from <see cref="AppThemes"/>, e.g. <c>obsidian</c>.</summary>
    public string DefaultTheme { get; set; } = AppThemes.Default;
}
