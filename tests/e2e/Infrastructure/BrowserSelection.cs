using System.Runtime.CompilerServices;

namespace E2E.Infrastructure;

/// <summary>
/// Pins the tests to Chromium. Playwright's test adapter picks its browser from the <c>BROWSER</c> environment
/// variable, which many Linux desktops set to the user's own browser launcher (Omarchy: <c>omarchy-launch-browser</c>)
/// and which Playwright then rejects. Only Chromium is installed (<c>just e2e-setup</c>), so it is the only choice.
/// </summary>
internal static class BrowserSelection
{
    // Runs when the test assembly loads, before any test reads the setting, however the tests are started:
    // `just e2e`, `dotnet test` or an IDE's test explorer.
#pragma warning disable CA2255 // The test assembly is an executable, not a library others load.
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void UseChromium() => Environment.SetEnvironmentVariable("BROWSER", "chromium");
}
