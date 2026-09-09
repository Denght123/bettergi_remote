namespace BetterGI.RemoteLite.Agent;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var singleInstance = new Mutex(true, "BetterGIRemoteLiteAgent", out var ownsMutex);
        if (!ownsMutex)
        {
            MessageBox.Show("BetterGI Remote 已经在后台运行。请查看任务栏右下角托盘。", "BetterGI Remote", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new AgentApplicationContext(args.Contains("--setup", StringComparer.OrdinalIgnoreCase)));
    }
}
