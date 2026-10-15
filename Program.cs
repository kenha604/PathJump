namespace PathJump;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, @"Local\PathJump.SingleInstance", out bool created);
        if (!created) return; // already running

        ApplicationConfiguration.Initialize();
        Application.Run(new App());
    }
}
