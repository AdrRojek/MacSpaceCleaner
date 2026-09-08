using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MacSpaceCleaner.Core;

public static class FinderService
{
    /// <summary>
    /// Opens Finder and selects the file/folder (macOS: open -R).
    /// </summary>
    public static void RevealInFinder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return;

        if (!File.Exists(path) && !Directory.Exists(path))
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "/usr/bin/open",
                ArgumentList = { "-R", path },
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch
        {
            // ignore
        }
    }
}
