using System.Reflection;

namespace ShutdownGuard.Services;

public static class AppVersion
{
    public static Version Current { get; } = ReadCurrent();

    public static string Display => Current.ToString();

    /// <summary>
    /// Parses tags like "v1.2.3", "1.2.3", "1.2.3-beta" → Version(1,2,3).
    /// Returns false when the string cannot yield a usable version.
    /// </summary>
    public static bool TryParse(string? raw, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var s = raw.Trim();
        if (s.StartsWith('v') || s.StartsWith('V'))
            s = s[1..];

        var plus = s.IndexOf('+');
        if (plus >= 0)
            s = s[..plus];

        var dash = s.IndexOf('-');
        if (dash >= 0)
            s = s[..dash];

        return Version.TryParse(s, out version!);
    }

    public static bool IsNewerThan(Version candidate, Version current)
        => candidate > current;

    private static Version ReadCurrent()
    {
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (TryParse(info, out var fromInfo) && fromInfo > new Version(0, 0, 0))
            return fromInfo;

        var name = asm.GetName().Version;
        if (name is not null)
            return new Version(name.Major, name.Minor, Math.Max(0, name.Build));

        return new Version(0, 0, 0);
    }
}
