using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace FuelFlow.UnitTests.Infrastructure;

public class ConfigReloadTests
{
    [Fact]
    public void HostBuilder_ShouldNotRegisterReloadWatchers_WhenReloadConfigOnChangeIsFalse()
    {
        // Guards the Render inotify crash fix: Program.cs sets
        // DOTNET_hostbuilder__reloadConfigOnChange=false before building the
        // host. This test proves the setting is actually honored by .NET 10
        // (the previously deployed AppContext switch silently wasn't), so the
        // default appsettings*.json sources must come out with
        // ReloadOnChange == false and no FileSystemWatcher gets created.
        Environment.SetEnvironmentVariable("DOTNET_hostbuilder__reloadConfigOnChange", "false");
        try
        {
            var builder = Host.CreateApplicationBuilder();

            var fileSources = ((IConfigurationBuilder)builder.Configuration)
                .Sources
                .OfType<FileConfigurationSource>()
                .ToList();

            fileSources.Should().NotBeEmpty("the default host loads appsettings*.json");
            fileSources.Should().OnlyContain(
                s => !s.ReloadOnChange,
                "reload must be disabled so no inotify watchers are registered");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_hostbuilder__reloadConfigOnChange", null);
        }
    }
}
