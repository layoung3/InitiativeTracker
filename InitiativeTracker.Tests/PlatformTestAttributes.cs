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
