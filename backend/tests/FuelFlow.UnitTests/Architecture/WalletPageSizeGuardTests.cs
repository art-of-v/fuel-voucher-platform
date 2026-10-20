using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FuelFlow.UnitTests.Architecture;

/// <summary>
/// Architecture guard for the customer's wallet's default page size.
/// </summary>
/// <remarks>
/// The wallet is paged now, and the page size the caller gets when it does not ask for one
/// is the MAXIMUM - not a smaller number, and not the page size the app asks for.
///
/// The reason is a deployment order. The mobile app is the one client that cannot be
/// changed in the same deploy as the backend: it reaches its users through the store and
/// through an OTA, and the backend reaches them now. The app that is in everybody's hands
/// today asks for no page at all, so whatever the default is, it is what that app gets.
///
/// A smaller default would therefore truncate a customer's wallet for as long as the old
/// app is in use - and the truncation would be SILENT, because the old app does not read
/// the count header. A wallet that shows fewer vouchers than the customer owns is a worse
/// failure than a slow response: a slow response is visible, and a missing voucher is not.
///
/// The app that does ask for pages asks for 50, and it is the one that pays for a small
/// page - one request, one page, one set of rendered QR images.
/// </remarks>
public sealed class WalletPageSizeGuardTests
{
    [Fact]
    public void The_Wallet_Default_Page_Size_Should_Be_The_Maximum()
    {
        var source = ReadController();

        source.Should().MatchRegex(
            @"pageSize\s*=\s*PageLimits\.MaxPageSize",
            "the default page size is the maximum on purpose: the app in everybody's hands "
            + "today asks for no page, and a smaller default would silently truncate a "
            + "customer's wallet for as long as that app is in use");

        // The clamp itself lives in PagedRequest's constructor, and the controller has to
        // go through it - so "?pageSize=2000000000" is clamped to the maximum and not
        // taken as a promise.
        source.Should().Contain("new PagedRequest(",
            "a client-supplied page size is clamped by PagedRequest, and the controller has "
            + "to go through it, or the default is a bound and not a promise");
    }

    /// <summary>
    /// The comments are stripped first: this file's own comment explains the change by
    /// naming the number, and a guard that read the comment would forbid the explanation.
    /// </summary>
    private static string WithoutComments(string source)
    {
        var withoutBlockComments = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        return Regex.Replace(withoutBlockComments, @"//[^\n]*", " ");
    }

    private static string ReadController()
    {
        var source = WithoutComments(ReadFile());
        return source;
    }

    private static string ReadFile([CallerFilePath] string thisFile = "")
    {
        // thisFile = .../backend/tests/FuelFlow.UnitTests/Architecture/<this>.cs
        var backend = Directory.GetParent(thisFile)!.Parent!.Parent!.Parent!.FullName;
        var path = Path.Combine(backend, "src", "FuelFlow.API", "Features", "Vouchers", "VoucherController.cs");
        if (File.Exists(path)) return File.ReadAllText(path);

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "FuelFlow.API", "Features", "Vouchers", "VoucherController.cs");
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException("VoucherController.cs was not found from the test assembly.");
    }
}
