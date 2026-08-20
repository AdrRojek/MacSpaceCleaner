using System.Runtime.InteropServices;
using MacSpaceCleaner.Core;

namespace MacSpaceCleaner.Core;

public static class PathSafety
{
    public static bool IsPathAllowed(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch
        {
            return false;
        }

        if (full is "/" or "/System/Volumes/Data")
            return false;

        if (full.StartsWith("/System", StringComparison.Ordinal) ||
            full.StartsWith("/usr/", StringComparison.Ordinal) ||
            full.Equals("/usr", StringComparison.Ordinal) ||
            full.StartsWith("/bin/", StringComparison.Ordinal) ||
            full.Equals("/bin", StringComparison.Ordinal) ||
            full.StartsWith("/sbin/", StringComparison.Ordinal) ||
            full.Equals("/sbin", StringComparison.Ordinal) ||
            full.StartsWith("/Applications/", StringComparison.Ordinal) ||
            full.Equals("/Applications", StringComparison.Ordinal) ||
            full.StartsWith("/Library/", StringComparison.Ordinal) ||
            full.Equals("/Library", StringComparison.Ordinal) ||
            full.StartsWith("/private/etc", StringComparison.Ordinal) ||
            full.StartsWith("/private/var/db", StringComparison.Ordinal))
        {
            return false;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home) &&
            full.Equals(Path.GetFullPath(home), StringComparison.Ordinal))
            return false;

        return true;
    }

    public static bool IsUnderHomeOrVolumesOrTmp(string path)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch
        {
            return false;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
        {
            var homeFull = Path.GetFullPath(home);
            if (full.StartsWith(homeFull + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                full.StartsWith(homeFull + "/", StringComparison.Ordinal))
                return true;
        }

        if (full.StartsWith("/Volumes/", StringComparison.Ordinal))
            return true;

        if (full.StartsWith("/tmp", StringComparison.Ordinal) ||
            full.StartsWith("/private/tmp", StringComparison.Ordinal) ||
            full.StartsWith("/var/folders", StringComparison.Ordinal) ||
            full.StartsWith("/private/var/folders", StringComparison.Ordinal))
            return true;

        if (full.StartsWith("/Users/", StringComparison.Ordinal))
            return true;

        return false;
    }

    public static bool CanDelete(string path) =>
        IsPathAllowed(path) && IsUnderHomeOrVolumesOrTmp(path);

    public static bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
}
