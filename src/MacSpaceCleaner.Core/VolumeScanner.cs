using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using MacSpaceCleaner.Core.Models;

namespace MacSpaceCleaner.Core;

public sealed class VolumeScanner
{
    private static readonly HashSet<string> SkipMounts = new(StringComparer.OrdinalIgnoreCase)
    {
        "/dev",
        "/System/Volumes/xarts",
        "/System/Volumes/iSCPreboot",
        "/System/Volumes/Hardware",
        "/System/Volumes/Update",
        "/System/Volumes/VM",
        "/System/Volumes/Preboot",
        "/private/var/vm"
    };

    public IReadOnlyList<VolumeInfo> Scan()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return ScanViaDriveInfo();

        var fromDf = ScanViaDf();
        return fromDf.Count > 0 ? fromDf : ScanViaDriveInfo();
    }

    private static List<VolumeInfo> ScanViaDf()
    {
        var result = new List<VolumeInfo>();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/bin/df",
                Arguments = "-kP",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
                return result;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(10_000);

            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var line in lines.Skip(1))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 6)
                    continue;

                var mount = parts[^1];
                if (ShouldSkipMount(mount))
                    continue;

                if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var totalKb))
                    continue;
                if (!long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var usedKb))
                    continue;
                if (!long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var availKb))
                    continue;

                var name = ResolveVolumeName(mount);
                result.Add(new VolumeInfo
                {
                    Name = name,
                    MountPoint = mount,
                    TotalBytes = totalKb * 1024,
                    UsedBytes = usedKb * 1024,
                    FreeBytes = availKb * 1024,
                    IsRoot = mount is "/" or "/System/Volumes/Data"
                });
            }
        }
        catch
        {
            // fall through to DriveInfo
        }

        return Deduplicate(result);
    }

    private static List<VolumeInfo> ScanViaDriveInfo()
    {
        var result = new List<VolumeInfo>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady)
                    continue;
                if (drive.DriveType is DriveType.Ram or DriveType.Network)
                    continue;

                var root = drive.RootDirectory.FullName.TrimEnd('/');
                if (string.IsNullOrEmpty(root))
                    root = "/";

                if (ShouldSkipMount(root))
                    continue;

                result.Add(new VolumeInfo
                {
                    Name = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? root : drive.VolumeLabel,
                    MountPoint = root == "" ? "/" : root,
                    TotalBytes = drive.TotalSize,
                    UsedBytes = drive.TotalSize - drive.AvailableFreeSpace,
                    FreeBytes = drive.AvailableFreeSpace,
                    IsRoot = root is "/" or "/System/Volumes/Data"
                });
            }
            catch
            {
                // ignore inaccessible drives
            }
        }

        return Deduplicate(result);
    }

    private static bool ShouldSkipMount(string mount)
    {
        if (SkipMounts.Contains(mount))
            return true;
        if (mount.StartsWith("/System/Volumes/", StringComparison.Ordinal) &&
            mount is not "/System/Volumes/Data")
            return true;
        if (mount.Contains("CoreSimulator", StringComparison.OrdinalIgnoreCase))
            return true;
        if (mount.StartsWith("/dev", StringComparison.Ordinal))
            return true;
        return false;
    }

    private static string ResolveVolumeName(string mount)
    {
        if (mount == "/")
            return "Macintosh HD";
        if (mount == "/System/Volumes/Data")
            return "Macintosh HD — Data";
        if (mount.StartsWith("/Volumes/", StringComparison.Ordinal))
            return mount["/Volumes/".Length..];
        return mount;
    }

    private static List<VolumeInfo> Deduplicate(List<VolumeInfo> volumes)
    {
        return volumes
            .GroupBy(v => v.MountPoint, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderByDescending(v => v.IsRoot)
            .ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
