using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace FuelFlow.UnitTests.Infrastructure;

/// <summary>
/// The store links in <c>AppVersion</c> are the only thing standing between a
/// user and the real listing: the app's forced-update wall and the marketing
/// site's download button/QR both resolve to them. They shipped as the
/// placeholder <c>.../app/fuelflow/id1234567890</c>, which is a 404 - a user
/// tapping "update" got a dead page and no diagnostic anywhere.
///
/// These assert the shipped configuration is a well-formed link to the real
/// stores, which is the property that broke. They deliberately do not pin the
/// exact URL: the listing id only changes when a new app is submitted, and a
/// test that hardcoded it would fail on a legitimate change while still passing
/// for any other plausible-looking-but-wrong id.
/// </summary>
public sealed class AppVersionStoreUrlTests
{
    private const string AppSettingsRelativePath = "src/FuelFlow.API/appsettings.json";
    private const string SectionName = "AppVersion";

    private static string ReadSetting(string key)
    {
        var candidate = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && candidate is not null; i++, candidate = Path.GetDirectoryName(candidate))
        {
            var path = Path.Combine(candidate, AppSettingsRelativePath);
            if (!File.Exists(path))
                continue;

            // The real configuration provider, so the JSON-with-comments this file
            // actually uses parses exactly as it does at runtime.
            var config = new ConfigurationBuilder().AddJsonFile(path, optional: false).Build();
            return config[$"{SectionName}:{key}"]
                   ?? throw new InvalidOperationException($"{SectionName}:{key} is missing from {AppSettingsRelativePath}");
        }

        throw new FileNotFoundException(
            $"Could not locate {AppSettingsRelativePath} walking up from {AppContext.BaseDirectory}; " +
            "if the layout moved, update AppSettingsRelativePath");
    }

    [Fact]
    public void IosStoreUrl_PointsAtTheRealAppStoreListing()
    {
        var url = ReadSetting("IosStoreUrl");

        url.Should().NotBeNullOrWhiteSpace();
        url.Should().MatchRegex(@"^https://apps\.apple\.com/[\w-]+/app/[\w.-]+/id\d+$",
            "Apple rejects anything that is not a canonical /<storefront>/app/<slug>/id<numeric id> URL");
        url.Should().NotContain("1234567890", "that was the placeholder id that shipped as a 404");
    }

    [Fact]
    public void AndroidStoreUrl_PointsAtTheRealPlayListing()
    {
        var url = ReadSetting("AndroidStoreUrl");

        url.Should().NotBeNullOrWhiteSpace();
        url.Should().MatchRegex(@"^https://play\.google\.com/store/apps/details\?id=[A-Za-z][A-Za-z0-9_.]*$",
            "the app hands this to Linking.openURL, so it has to be the canonical details URL");
    }
}