using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;
using NAudio.CoreAudioApi;

namespace VoiceMagic
{
    public class Settings
    {
        public int Percent = 200;
        public string OutputId = "";
        public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceMagic");
        static string FileName { get { return Path.Combine(Folder, "settings.xml"); } }
        public static Settings Load()
        {
            try { using (var s = File.OpenRead(FileName)) return (Settings)new XmlSerializer(typeof(Settings)).Deserialize(s); }
            catch { return new Settings(); }
        }
        public void Save()
        {
            Directory.CreateDirectory(Folder);
            using (var s = File.Create(FileName + ".tmp")) new XmlSerializer(typeof(Settings)).Serialize(s, this);
            File.Copy(FileName + ".tmp", FileName, true); File.Delete(FileName + ".tmp");
        }
    }
    public sealed class DeviceChoice
    {
        public string Id, Name;
        public override string ToString() { return Name; }
    }
    public sealed class RouteSession
    {
        public int ProcessId;
        public string DeviceId;
        public bool Active;
    }
    public sealed class RouteReport
    {
        public int Detected, ActiveOnCable, ActiveOutside;
    }
    public static class Routing
    {
        public static readonly string[] Processes = { "Weixin", "WeChat", "WeChatAppEx" };
        static string Tool { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "SoundVolumeView.exe"); } }
        public static string Marker { get { return Path.Combine(Settings.Folder, "route-active.txt"); } }
        public static bool Available { get { return File.Exists(Tool); } }
        static void Run(string device, string process)
        {
            using (var p = Process.Start(new ProcessStartInfo(Tool, "/SetAppDefault \"" + device + "\" all \"" + process + "\"") {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden }))
            {
                if (!p.WaitForExit(8000)) { p.Kill(); throw new IOException("微信输出切换超时"); }
                if (p.ExitCode != 0) throw new IOException("微信输出切换失败：" + p.ExitCode);
            }
        }
        public static void Begin()
        {
            if (!Available) throw new IOException("缺少路由组件，请重新运行构建脚本");
            Directory.CreateDirectory(Settings.Folder);
            File.WriteAllText(Marker, "停止或退出时，将微信输出恢复为跟随系统默认设备。");
        }
        public static bool IsWeChatProcess(string name, string path)
        {
            if (string.Equals(name, "Weixin", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "WeChat", StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.Equals(name, "WeChatAppEx", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(path)) return false;
            return new[] { "\\xwechat\\", "\\WeChat\\", "\\Weixin\\" }.Any(folder => path.IndexOf(folder, StringComparison.OrdinalIgnoreCase) >= 0);
        }
        public static List<RouteSession> GetSessions()
        {
            var result = new List<RouteSession>();
            using (var devices = new MMDeviceEnumerator())
                foreach (var device in devices.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                    using (device)
                    {
                        var sessions = device.AudioSessionManager.Sessions;
                        for (int i = 0; i < sessions.Count; i++)
                            using (var session = sessions[i])
                            {
                                if (session.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateExpired) continue;
                                int pid = (int)session.GetProcessID;
                                if (pid == 0) continue;
                                try
                                {
                                    using (var p = Process.GetProcessById(pid))
                                    {
                                        string path = p.ProcessName.Equals("WeChatAppEx", StringComparison.OrdinalIgnoreCase) ? p.MainModule.FileName : "";
                                        if (!IsWeChatProcess(p.ProcessName, path)) continue;
                                        result.Add(new RouteSession { ProcessId = pid, DeviceId = device.ID,
                                            Active = session.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive });
                                    }
                                }
                                catch (ArgumentException) { } // The process can exit during enumeration.
                                catch (System.ComponentModel.Win32Exception) { } // An inaccessible plugin is not assumed to belong to WeChat.
                            }
                    }
            return result;
        }
        public static int[] Plan(IEnumerable<RouteSession> sessions, string cableId)
        {
            // A process can retain an inactive CABLE session while its new stream plays elsewhere.
            // Decisions are based on the current streams, with no lifetime PID cache.
            return sessions.GroupBy(s => s.ProcessId)
                .Where(group => group.Any(s => s.DeviceId != cableId && s.Active) || !group.Any(s => s.DeviceId == cableId))
                .Select(group => group.Key).ToArray();
        }
        public static RouteReport Apply(string id)
        {
            var sessions = GetSessions();
            int[] targets = Plan(sessions, id);
            foreach (int pid in targets) Run(id, pid.ToString());
            if (targets.Length > 0) sessions = GetSessions();
            return new RouteReport {
                Detected = sessions.Select(s => s.ProcessId).Distinct().Count(),
                ActiveOnCable = sessions.Count(s => s.DeviceId == id && s.Active),
                ActiveOutside = sessions.Count(s => s.DeviceId != id && s.Active) };
        }
        public static void Restore(bool force = false)
        {
            if (!force && !File.Exists(Marker)) return;
            foreach (string name in new[] { "Weixin", "WeChat" }) Run("DefaultRenderDevice", name + ".exe");
            foreach (var p in Process.GetProcessesByName("WeChatAppEx"))
                using (p)
                    try { if (IsWeChatProcess(p.ProcessName, p.MainModule.FileName)) Run("DefaultRenderDevice", p.Id.ToString()); }
                    catch (ArgumentException) { }
                    catch (System.ComponentModel.Win32Exception) { }
            File.Delete(Marker);
        }
        public static void Watch(int pid)
        {
            try { using (var p = Process.GetProcessById(pid)) p.WaitForExit(); } catch (ArgumentException) { }
            // Wait for the GUI's normal exit cleanup before attempting recovery.
            Thread.Sleep(700);
            try { using (var mutex = new Mutex(false, "Local\\VoiceMagic.Gui"))
            {
                bool acquired = false;
                try
                {
                    try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                    if (acquired) Restore();
                }
                finally { if (acquired) mutex.ReleaseMutex(); }
            } }
            catch (Exception ex) { Log(ex); }
        }
        public static void Log(Exception ex)
        {
            try { Directory.CreateDirectory(Settings.Folder); File.AppendAllText(Path.Combine(Settings.Folder, "error.log"), DateTime.Now + " " + ex + Environment.NewLine); } catch { }
        }
    }

}
