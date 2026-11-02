using System;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VoiceMagic
{
    public static class StartupManager
    {
        public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string ValueName = "VoiceMagic";
        public static string LauncherPath
        {
            get
            {
                string supplied = Environment.GetEnvironmentVariable("VOICEMAGIC_LAUNCHER");
                if (!string.IsNullOrEmpty(supplied) && File.Exists(supplied)) return Path.GetFullPath(supplied);
                string installed = Path.Combine(Settings.Folder, "VoiceMagic.exe");
                return File.Exists(installed) ? installed : Application.ExecutablePath;
            }
        }
        public static string BuildCommand(string executable)
        {
            if (string.IsNullOrWhiteSpace(executable) || executable.IndexOf('"') >= 0) throw new ArgumentException("启动程序路径无效");
            string command = "\"" + Path.GetFullPath(executable) + "\" --startup";
            if (command.Length > 260) throw new ArgumentException("启动路径过长，请将程序放到更短的目录中");
            return command;
        }
        public static bool Enabled
        {
            get { using (var key = Registry.CurrentUser.OpenSubKey(RunKey)) return key != null && !string.IsNullOrEmpty(key.GetValue(ValueName) as string); }
        }
        public static void SetEnabled(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey)) WriteRegistration(key, enabled, LauncherPath);
        }
        // The key is injectable so tests never modify the user's actual startup entries.
        public static void WriteRegistration(RegistryKey key, bool enabled, string executable)
        {
            if (enabled) key.SetValue(ValueName, BuildCommand(executable), RegistryValueKind.String);
            else key.DeleteValue(ValueName, false);
        }
    }
}
