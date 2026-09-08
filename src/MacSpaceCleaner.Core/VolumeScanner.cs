using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using MacSpaceCleaner.Core.Models;

namespace MacSpaceCleaner.Core;

/// <summary>
/// Discovers all meaningful mounted volumes for the current Mac user
/// (system Data volume + every entry under /Volumes, not a single vendor disk).
/// </summary>
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
        "/System/Volumes/Data/home",
        "/private/var/vm"
    };

    public IReadOnlyList<VolumeInfo> Scan()
    {
        var merged = new Dictionary<string, VolumeInfo>(StringComparer.Ordinal);

        foreach (var v in ScanViaDf())
            merged[v.MountPoint] = v;

        foreach (var v in ScanViaDriveInfo())
        {
            if (!merged.ContainsKey(v.MountPoint))
                merged[v.MountPoint] = v;
        }

        foreach (var v in ScanVolumesDirectory())
        {
            if (!merged.ContainsKey(v.MountPoint))
                merged[v.MountPoint] = v;
        }

        return Deduplicate(merged.Values.ToList());
    }

    private static List<VolumeInfo> ScanViaDf()
    {
        var result = new List<VolumeInfo>();
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) &&
            !RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return result;

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

                // Skip pseudo / empty mounts (e.g. map auto_home)
                if (totalKb <= 0)
                    continue;

                result.Add(new VolumeInfo
                {
                    Name = ResolveVolumeName(mount),
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
            // ignore — other strategies may still work
        }

        return result;
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
                if (drive.DriveType is DriveType.Ram)
                    continue;

                var root = drive.RootDirectory.FullName.TrimEnd(Path.DirectorySeparatorChar, '/');
                if (string.IsNullOrEmpty(root))
                    root = "/";

                if (ShouldSkipMount(root))
                    continue;
                if (drive.TotalSize <= 0)
                    continue;

                result.Add(new VolumeInfo
                {
                    Name = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? ResolveVolumeName(root) : drive.VolumeLabel,
                    MountPoint = root,
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

        return result;
    }

    /// <summary>
    /// Explicitly pick up anything mounted under /Volumes for the current user
    /// (USB disks, externals, Time Machine volumes, etc.).
    /// </summary>
    private static List<VolumeInfo> ScanVolumesDirectory()
    {
        var result = new List<VolumeInfo>();
        const string volumesRoot = "/Volumes";
        if (!Directory.Exists(volumesRoot))
            return result;

        IEnumerable<string> dirs;
        try
        {
            dirs = Directory.EnumerateDirectories(volumesRoot);
        }
        catch
        {
            return result;
        }

        foreach (var dir in dirs)
        {
            try
            {
                // Skip the Macintosh HD firmlink — already covered by / and Data
                var name = Path.GetFileName(dir);
                if (name.Equals("Macintosh HD", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (ShouldSkipMount(dir))
                    continue;

                var di = new DriveInfo(dir);
                if (!di.IsReady || di.TotalSize <= 0)
                    continue;

                result.Add(new VolumeInfo
                {
                    Name = string.IsNullOrWhiteSpace(di.VolumeLabel) ? name : di.VolumeLabel,
                    MountPoint = dir,
                    TotalBytes = di.TotalSize,
                    UsedBytes = di.TotalSize - di.AvailableFreeSpace,
                    FreeBytes = di.AvailableFreeSpace,
                    IsRoot = false
                });
            }
            catch
            {
                // unreadable volume
            }
        }

        return result;
    }

    private static bool ShouldSkipMount(string mount)
    {
        if (string.IsNullOrWhiteSpace(mount))
            return true;
        if (SkipMounts.Contains(mount))
            return true;
        if (mount.StartsWith("/System/Volumes/", StringComparison.Ordinal) &&
            mount is not "/System/Volumes/Data")
            return true;
        if (mount.Contains("CoreSimulator", StringComparison.OrdinalIgnoreCase))
            return true;
        if (mount.Contains("Cryptex", StringComparison.OrdinalIgnoreCase))
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
            .Where(v => v.TotalBytes > 0)
            .GroupBy(v => v.MountPoint, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(x => x.TotalBytes).First())
            .OrderByDescending(v => v.IsRoot)
            .ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
