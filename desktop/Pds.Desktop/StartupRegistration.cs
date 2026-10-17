using System;
using System.IO;
using Microsoft.Win32;

namespace Pds.Desktop;

internal static class StartupRegistration
{
    internal static string Command(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.Contains('"') || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("실행 파일 경로를 확인해 주세요.");
        return "\"" + path + "\"";
    }
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue("PDSPlanner") is string current && current.Equals(Command(Environment.ProcessPath ?? ""), StringComparison.OrdinalIgnoreCase);
    }
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static void Apply(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true)
            ?? throw new IOException("자동 실행 설정을 열지 못했어요.");
        const string name = "PDSPlanner";
        if (enabled) key.SetValue(name, Command(Environment.ProcessPath ?? throw new IOException("앱 경로를 찾지 못했어요.")), RegistryValueKind.String);
        else key.DeleteValue(name, throwOnMissingValue: false);
    }
}
