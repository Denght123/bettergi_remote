namespace BetterGI.RemoteLite.Agent;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var mutexName = "BetterGIRemoteLiteAgent";
        var testIndex = Array.IndexOf(args, "--local-test-profile");
        if (testIndex >= 0)
        {
            if (testIndex + 1 >= args.Length) return;
            var profile = Path.GetFullPath(args[testIndex + 1]);
            Environment.SetEnvironmentVariable("BGRL_DATA_DIRECTORY", profile);
            Environment.SetEnvironmentVariable("BGRL_LOCAL_TEST", "1");
            var relayIndex = Array.IndexOf(args, "--local-test-relay");
            if (relayIndex >= 0 && relayIndex + 1 < args.Length &&
                Uri.TryCreate(args[relayIndex + 1], UriKind.Absolute, out var relay) && relay.IsLoopback)
                Environment.SetEnvironmentVariable("BGRL_DEFAULT_RELAY_BASE_URL", relay.GetLeftPart(UriPartial.Authority));
            var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(profile.ToUpperInvariant()));
            mutexName += "-local-" + Convert.ToHexString(hash)[..16];
        }
        using var singleInstance = new Mutex(true, mutexName, out var ownsMutex);
        if (!ownsMutex)
        {
            MessageBox.Show("BetterGI Remote 已经在后台运行。请查看任务栏右下角托盘。", "BetterGI Remote", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new AgentApplicationContext(args.Contains("--setup", StringComparer.OrdinalIgnoreCase)));
    }
}
