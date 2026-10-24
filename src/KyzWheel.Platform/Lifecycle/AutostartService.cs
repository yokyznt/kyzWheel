using Microsoft.Win32;

namespace KyzWheel.Platform.Lifecycle;

public static class AutostartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "kyzWheel";

    public static void Enable(string guardExePath)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        k.SetValue(Name, $"\"{guardExePath}\" --autostart");
    }

    public static void Disable()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
        k?.DeleteValue(Name, false);
    }

    public static bool IsEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(Name) is string;
    }
}
