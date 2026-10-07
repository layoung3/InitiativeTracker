using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace InitiativeTracker.Web.Services;

public sealed class AppVersionService
{
    private readonly DateTimeOffset
        _startedAtUtc;

    public AppVersionService()
    {
        var process =
            Process.GetCurrentProcess();

        _startedAtUtc =
            process.StartTime
                .ToUniversalTime();
    }


    public string Product =>
        "InitiativeTracker";

    public string Version =>
        GetReleaseVersion();

    public string InformationalVersion =>
        GetInformationalVersion();

    public string AssemblyVersion =>
        typeof(AppVersionService)
            .Assembly
            .GetName()
            .Version?
            .ToString() ??
        "unknown";

    public string Runtime =>
        RuntimeInformation
            .FrameworkDescription;

    public string Architecture =>
        RuntimeInformation
            .ProcessArchitecture
            .ToString()
            .ToLowerInvariant();

    public string OperatingSystem =>
        RuntimeInformation
            .OSDescription;

    public DateTimeOffset StartedAtUtc =>
        _startedAtUtc;

    public TimeSpan Uptime =>
        DateTimeOffset.UtcNow -
        _startedAtUtc;


    private static string
        GetReleaseVersion()
    {
        var informationalVersion =
            GetInformationalVersion();

        var metadataIndex =
            informationalVersion.IndexOf(
                '+');

        return metadataIndex >= 0
            ? informationalVersion[
                ..metadataIndex]
            : informationalVersion;
    }


    private static string
        GetInformationalVersion()
    {
        var assembly =
            typeof(AppVersionService)
                .Assembly;

        var informationalVersion =
            assembly
                .GetCustomAttribute<
                    AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(
                informationalVersion))
        {
            return informationalVersion;
        }

        return assembly
            .GetName()
            .Version?
            .ToString() ??
            "unknown";
    }
}
