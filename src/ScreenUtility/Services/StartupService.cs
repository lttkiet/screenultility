using System.Reflection;
using Microsoft.Win32;

namespace ScreenUtility.Services;

public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ScreenUtility";

    public bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
                return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
            }
            catch
            {
                return false;
            }
        }
    }

    public bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true) ??
                            Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
            if (key is null)
            {
                return false;
            }

            if (enabled)
            {
                key.SetValue(ValueName, BuildCommand(), RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildCommand()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            var fileName = System.IO.Path.GetFileNameWithoutExtension(processPath);
            if (!fileName.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                return $"\"{processPath}\" --startup";
            }
        }

        var assemblyPath = Assembly.GetEntryAssembly()?.Location;
        if (!string.IsNullOrWhiteSpace(assemblyPath))
        {
            return $"\"{processPath ?? "dotnet"}\" \"{assemblyPath}\" --startup";
        }

        return $"\"{processPath ?? "ScreenUtility.exe"}\" --startup";
    }
}
