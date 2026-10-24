namespace KyzWheel.App.Shell;

public static class Log
{
    public static string Dir { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "kyzWheel");

    private static readonly Lock Gate = new();

    public static void Write(string file, string line)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(Path.Combine(Dir, file), $"{DateTimeOffset.Now:O} {line}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
        }
    }

    public static void Error(Exception e) => Write("app.log", e.ToString());
}
