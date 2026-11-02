namespace InitiativeTracker.Tests;

// Discovery-time skips are visible in TRX and the test runner, with reasons.
public sealed class LinuxFactAttribute : FactAttribute
{
    public LinuxFactAttribute(string reason)
    {
        if (!OperatingSystem.IsLinux()) Skip = reason;
    }
}

public sealed class LinuxTheoryAttribute : TheoryAttribute
{
    public LinuxTheoryAttribute(string reason)
    {
        if (!OperatingSystem.IsLinux()) Skip = reason;
    }
}

public sealed class NonLinuxFactAttribute : FactAttribute
{
    public NonLinuxFactAttribute()
    {
        if (OperatingSystem.IsLinux()) Skip = "Verifies production fail-closed behavior on unsupported non-Linux platforms.";
    }
}

public sealed class SymbolicLinkFactAttribute : FactAttribute
{
    public SymbolicLinkFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "symlink-capability-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var target = Path.Combine(root, "target");
            File.WriteAllText(target, "capability probe");
            File.CreateSymbolicLink(Path.Combine(root, "link"), target);
        }
        catch (IOException error) when ((error.HResult & 0xffff) == 1314)
        {
            Skip = "Windows process lacks symbolic-link creation privilege (Win32 error 1314); enable Developer Mode or run with that privilege to execute this security assertion.";
        }
        finally { Directory.Delete(root, true); }
    }
}
