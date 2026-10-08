using System;
using System.IO;
using Microsoft.Win32;

namespace Snapline
{
    internal static class Startup
    {
        private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Name = "Snapline";

        internal static string Command(string executable, string root)
        {
            string data = Path.GetFullPath(root);
            if (data.EndsWith("\\", StringComparison.Ordinal)) data += "\\";
            return "\"" + executable + "\" --data-dir \"" + data + "\"";
        }

        internal static bool Enabled(string executable, string root)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(Key))
                return key != null && string.Equals(key.GetValue(Name) as string, Command(executable, root), StringComparison.OrdinalIgnoreCase);
        }

        internal static void Set(bool enabled, string executable, string root)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(Key))
            {
                if (enabled) key.SetValue(Name, Command(executable, root), RegistryValueKind.String);
                else key.DeleteValue(Name, false);
            }
        }
    }
}
