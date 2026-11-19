using FluentAssertions;
using FuelFlow.Features.Feedback;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Settings.AppAppearance;
using FuelFlow.Features.Settings.GetAdminSettings;
using FuelFlow.Features.Settings.RegisterFeedbackWebhook;
using FuelFlow.Features.Settings.SharedModels;
using FuelFlow.Features.Settings.UpdateAdminSettings;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Options;
using FuelFlow.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace FuelFlow.UnitTests.Settings;

/// <summary>
/// The installation-wide app theme: what a phone opens in when its owner never chose one.
/// </summary>
/// <remarks>
/// These tests exist because the value is the one setting a client reads anonymously, before it can
/// know who the user is. That makes three things the contract rather than an implementation detail:
/// it always answers with a paintable id, an id that is not a theme is refused at the write, and a
/// stored value that has since stopped being a theme still reads as the default.
/// </remarks>
public sealed class AppAppearanceThemeTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly RuntimeSettingsService _settings;
    private readonly AdminSettingsController _controller;
    private readonly AppAppearanceController _appearance;

    public AppAppearanceThemeTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);
        _settings = new RuntimeSettingsService(_context);

        var feedbackBot = new SettingsTestTelegramFeedbackClient();
        var feedbackOptions = Options.Create(new TelegramFeedbackOptions());

        _controller = new AdminSettingsController(
            new GetAdminSettingsQueryHandler(_settings, _context, feedbackBot, feedbackOptions),
            new UpdateAdminSettingsCommandHandler(_settings),
            new RegisterFeedbackWebhookCommandHandler(feedbackBot, feedbackOptions))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        _appearance = new AppAppearanceController(_settings);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task Unset_ShouldReadAsObsidian_BothToTheAdminAndToTheApp()
    {
        (await _settings.GetDefaultAppThemeAsync()).Should().Be("obsidian")
            .And.Be(AppThemes.Default);

        var body = (await _appearance.Get(CancellationToken.None)).Should().BeOfType<OkObjectResult>().Subject;
        ((AppAppearanceDto)body.Value!).DefaultTheme.Should().Be("obsidian");

        var settings = (await _controller.GetAll(CancellationToken.None))
            .Should().BeOfType<OkObjectResult>().Subject;
        ((SettingsDto)settings.Value!).Appearance.DefaultTheme.Should().Be("obsidian");
    }

    [Fact]
    public async Task Update_ShouldPersistTheTheme_AndTheAppShouldReadItBack()
    {
        var result = await _controller.Update(
            new UpdateSettingsRequest
            {
                Appearance = new AppearanceSettingsDto { DefaultTheme = "lemberg" }
            },
            CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();

        (await _settings.GetDefaultAppThemeAsync()).Should().Be("lemberg");

        var body = (await _appearance.Get(CancellationToken.None)).Should().BeOfType<OkObjectResult>().Subject;
        ((AppAppearanceDto)body.Value!).DefaultTheme.Should().Be("lemberg");
    }

    [Fact]
    public async Task Update_ShouldWriteTheCanonicalCasing_WhateverTheFormSent()
    {
        await _controller.Update(
            new UpdateSettingsRequest
            {
                Appearance = new AppearanceSettingsDto { DefaultTheme = "  OBSIDIAN " }
            },
            CancellationToken.None);

        (await _settings.GetDefaultAppThemeAsync()).Should().Be("obsidian");
    }

    [Theory]
    [InlineData("purple")]
    [InlineData("")]
    [InlineData("obsidian-forge")] // an admin-panel theme: the phone has no palette for it
    public async Task Update_UnknownTheme_ShouldBeRefusedAndChangeNothing(string theme)
    {
        await _controller.Update(
            new UpdateSettingsRequest
            {
                Appearance = new AppearanceSettingsDto { DefaultTheme = "lemberg" }
            },
            CancellationToken.None);

        var refused = await _controller.Update(
            new UpdateSettingsRequest
            {
                Appearance = new AppearanceSettingsDto { DefaultTheme = theme }
            },
            CancellationToken.None);

        var badRequest = refused.Should().BeOfType<BadRequestObjectResult>().Subject;
        var problem = badRequest.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Extensions["code"].Should().Be("settings_unknown_theme");

        // The previous value stands: a refused save must not half-apply, and the screen sends every
        // section in one request.
        (await _settings.GetDefaultAppThemeAsync()).Should().Be("lemberg");
    }

    [Fact]
    public async Task StoredValueThatIsNoLongerATheme_ShouldReadAsTheDefault()
    {
        // A row an older release wrote with an id that has since been retired. The app must still get
        // a palette rather than an id it would have to guess about.
        await _settings.UpsertAsync(AppSettingKeys.AppearanceDefaultTheme, "sunset");

        (await _settings.GetDefaultAppThemeAsync()).Should().Be(AppThemes.Default);
    }

    [Fact]
    public async Task AppearanceAlone_ShouldBeEnoughForASave()
    {
        // The other sections are all null, so this is the "nothing supplied" guard's opposite: one
        // section is a save.
        var result = await _controller.Update(
            new UpdateSettingsRequest
            {
                Appearance = new AppearanceSettingsDto { DefaultTheme = "nova" }
            },
            CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task AppearanceWrite_ShouldNotDisturbTheOtherSections()
    {
        await _controller.Update(
            new UpdateSettingsRequest
            {
                FeedbackBot = new FeedbackBotSettingsDto { Enabled = true }
            },
            CancellationToken.None);

        await _controller.Update(
            new UpdateSettingsRequest
            {
                Appearance = new AppearanceSettingsDto { DefaultTheme = "blue" }
            },
            CancellationToken.None);

        (await _settings.IsFeedbackBotEnabledAsync()).Should().BeTrue();
        (await _settings.GetDefaultAppThemeAsync()).Should().Be("blue");
    }
}
