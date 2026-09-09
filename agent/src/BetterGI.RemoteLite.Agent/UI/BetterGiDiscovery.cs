using System.Diagnostics;
using BetterGI.RemoteLite.BetterGi;

namespace BetterGI.RemoteLite.Agent.UI;

internal static class BetterGiDiscovery
{
    private static readonly string[] RelativeCandidates =
    [
        @"BetterGI\BetterGI.exe",
        @"Games\BetterGI\BetterGI.exe",
        @"Game\BetterGI\BetterGI.exe",
        @"Tools\BetterGI\BetterGI.exe",
        @"Program Files\BetterGI\BetterGI.exe",
    ];

    public static string? FindSupportedExecutable(string? preferred = null)
    {
        foreach (var candidate in Candidates(preferred).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (BetterGiVersionPolicy.Check(candidate).Supported)
            {
                return Path.GetFullPath(candidate);
            }
        }
        return null;
    }

    private static IEnumerable<string> Candidates(string? preferred)
    {
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            yield return preferred;
        }

        foreach (var process in Process.GetProcessesByName("BetterGI"))
        {
            using (process)
            {
                string? path = null;
                try
                {
                    path = process.MainModule?.FileName;
                }
                catch
                {
                }
                if (!string.IsNullOrWhiteSpace(path))
                {
                    yield return path;
                }
            }
        }

        foreach (var drive in DriveInfo.GetDrives().Where(value => value.IsReady && value.DriveType is DriveType.Fixed or DriveType.Removable))
        {
            foreach (var relative in RelativeCandidates)
            {
                yield return Path.Combine(drive.RootDirectory.FullName, relative);
            }

            IEnumerable<string> topLevel;
            try
            {
                topLevel = Directory.EnumerateDirectories(drive.RootDirectory.FullName, "*BetterGI*", SearchOption.TopDirectoryOnly).Take(20).ToArray();
            }
            catch
            {
                continue;
            }
            foreach (var directory in topLevel)
            {
                yield return Path.Combine(directory, "BetterGI.exe");
            }
        }
    }
}
