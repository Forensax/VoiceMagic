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

    public sealed class MainForm : Form
    {
        readonly Settings settings = Settings.Load();
        readonly Label status = new Label(), value = new Label(), component = new Label(), protection = new Label();
        readonly TrackBar slider = new TrackBar();
        readonly ProgressBar meter = new ProgressBar();
        readonly Button start = new Button(), mute = new Button();
        readonly ComboBox outputs = new ComboBox();
        readonly TabControl tabs = new TabControl();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        AudioEngine engine;
        string cableInput, cableOutput, actualOutput;
        int ticks;
        bool busy, watcherStarted;
        Button ButtonAt(string text, int x, int y, int width, Control parent, EventHandler click)
        {
            var b = new Button { Text = text, Location = new Point(x, y), Size = new Size(width, 34),
                FlatStyle = FlatStyle.Flat, BackColor = Color.White, Cursor = Cursors.Hand };
            b.FlatAppearance.BorderColor = Color.FromArgb(204, 212, 222);
            b.Click += click; parent.Controls.Add(b); return b;
        }
        Label LabelAt(string text, int x, int y, int width, Control parent)
        {
            var l = new Label { Text = text, Location = new Point(x, y), Size = new Size(width, 26) };
            parent.Controls.Add(l); return l;
        }
        public MainForm()
        {
            Text = "VoiceMagic · 微信音量"; ClientSize = new Size(480, 360);
            Font = new Font("Microsoft YaHei UI", 10); BackColor = Color.White;
            FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen; Icon = SystemIcons.Application;
            tabs.Dock = DockStyle.Fill; Controls.Add(tabs);
            var main = new TabPage("音量") { BackColor = Color.White, Padding = new Padding(14) };
            var setup = new TabPage("设置") { BackColor = Color.White, Padding = new Padding(14) };
            tabs.TabPages.Add(main); tabs.TabPages.Add(setup);
            status.SetBounds(20, 20, 426, 30); status.Text = "正在检查音频组件"; main.Controls.Add(status);
            value.SetBounds(20, 57, 420, 46); value.Font = new Font(Font.FontFamily, 24, FontStyle.Bold); main.Controls.Add(value);
            slider.SetBounds(14, 110, 435, 45); slider.Minimum = 0; slider.Maximum = 1000;
            slider.TickFrequency = 100; slider.SmallChange = 10; slider.LargeChange = 50;
            slider.Value = Math.Max(0, Math.Min(1000, settings.Percent));
            slider.AccessibleName = "微信音量百分比"; main.Controls.Add(slider);
            slider.ValueChanged += delegate { UpdateGain(); };
            ButtonAt("100%", 20, 159, 80, main, delegate { slider.Value = 100; });
            ButtonAt("200%", 110, 159, 80, main, delegate { slider.Value = 200; });
            ButtonAt("300%", 200, 159, 80, main, delegate { slider.Value = 300; });
            mute.Text = "静音"; mute.SetBounds(346, 159, 100, 34); StyleButton(mute); main.Controls.Add(mute);
            mute.Click += delegate { if (engine != null) { engine.Gain.Muted = !engine.Gain.Muted; mute.Text = engine.Gain.Muted ? "取消静音" : "静音"; } };
            meter.SetBounds(20, 213, 426, 12); meter.Maximum = 1000; main.Controls.Add(meter);
            protection.SetBounds(20, 235, 426, 26); protection.Text = "峰值保护已就绪"; protection.ForeColor = Color.DimGray; main.Controls.Add(protection);
            start.SetBounds(20, 276, 130, 34); start.Text = "开启放大"; StyleButton(start); main.Controls.Add(start);
            start.Click += Toggle;
            ButtonAt("恢复默认输出", 164, 276, 150, main, delegate { if (!busy) StopAudio(true); });
            ButtonAt("重新连接", 328, 276, 118, main, async delegate {
                if (busy) return;
                if (engine != null) StopAudio();
                if (engine == null) await StartAudio();
            });
            LabelAt("播放设备", 20, 20, 400, setup);
            outputs.SetBounds(20, 52, 426, 30); outputs.DropDownStyle = ComboBoxStyle.DropDownList;
            outputs.AccessibleName = "放大后的播放设备"; setup.Controls.Add(outputs);
            outputs.SelectedIndexChanged += delegate {
                if (outputs.SelectedItem != null) { settings.OutputId = ((DeviceChoice)outputs.SelectedItem).Id; SaveSettings(); }
            };
            component.SetBounds(20, 101, 426, 28); setup.Controls.Add(component);
            ButtonAt("安装音频组件", 20, 141, 150, setup, InstallDriver);
            ButtonAt("刷新设备", 184, 141, 120, setup, delegate { RefreshDevices(); });
            LabelAt("安装窗口选择 Install Driver，随后按提示重启。", 20, 190, 430, setup);
            LabelAt("停止或退出后，微信恢复跟随系统默认输出。", 20, 222, 430, setup);
            ButtonAt("Windows 音量设置", 20, 270, 180, setup, delegate {
                Process.Start(new ProcessStartInfo("ms-settings:apps-volume") { UseShellExecute = true });
            });
            var menu = new ContextMenuStrip();
            menu.Items.Add("显示窗口", null, delegate { ShowWindow(); });
            menu.Items.Add("恢复并退出", null, delegate { Close(); });
            tray.Icon = Icon; tray.Text = "VoiceMagic · 微信音量"; tray.ContextMenuStrip = menu; tray.Visible = true;
            tray.DoubleClick += delegate { ShowWindow(); };
            Resize += delegate { if (WindowState == FormWindowState.Minimized) Hide(); };
            FormClosing += OnFormClosing;
            timer.Interval = 250; timer.Tick += Tick; timer.Start();
            RefreshDevices(); UpdateGain();
        }
        void StyleButton(Button b)
        {
            b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderColor = Color.FromArgb(204, 212, 222);
            b.BackColor = Color.White; b.Cursor = Cursors.Hand;
        }
        void SaveSettings() { try { settings.Save(); } catch (Exception ex) { Routing.Log(ex); } }
        void ShowWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); }
        void UpdateGain()
        {
            value.Text = slider.Value + "%"; settings.Percent = slider.Value;
            if (engine != null) engine.Gain.Gain = slider.Value / 100f;
            tray.Text = "VoiceMagic · 微信音量 " + slider.Value + "%";
        }
        static bool IsCable(MMDevice d) { return d.FriendlyName.IndexOf("VB-Audio", StringComparison.OrdinalIgnoreCase) >= 0 && d.FriendlyName.IndexOf("CABLE", StringComparison.OrdinalIgnoreCase) >= 0; }
        void RefreshDevices()
        {
            if (engine != null) return;
            string wanted = settings.OutputId; cableInput = cableOutput = null; outputs.Items.Clear();
            outputs.Items.Add(new DeviceChoice { Id = "", Name = "跟随系统默认播放设备" });
            try
            {
                using (var devs = new MMDeviceEnumerator())
                {
                    foreach (var d in devs.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                        using (d) { if (IsCable(d) && d.FriendlyName.IndexOf("CABLE Output", StringComparison.OrdinalIgnoreCase) >= 0) cableOutput = d.ID; }
                    foreach (var d in devs.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                        using (d)
                        {
                            if (IsCable(d)) { if (d.FriendlyName.IndexOf("CABLE Input", StringComparison.OrdinalIgnoreCase) >= 0) cableInput = d.ID; continue; }
                            outputs.Items.Add(new DeviceChoice { Id = d.ID, Name = d.FriendlyName });
                        }
                }
                for (int i = 0; i < outputs.Items.Count; i++) if (((DeviceChoice)outputs.Items[i]).Id == wanted) outputs.SelectedIndex = i;
                if (outputs.SelectedIndex < 0) outputs.SelectedIndex = 0;
                bool ready = cableInput != null && cableOutput != null && Routing.Available;
                component.Text = ready ? "音频组件已就绪" : "尚未安装虚拟音频组件";
                status.Text = ready ? "已就绪，点击开启放大" : "首次使用：请在设置页安装音频组件";
                if (!ready) tabs.SelectedIndex = 1;
            }
            catch (Exception ex) { Error(ex); }
        }
        async void Toggle(object sender, EventArgs args)
        {
            if (busy) return;
            if (engine != null) { StopAudio(); return; }
            await StartAudio();
        }
        string ResolveOutput()
        {
            var chosen = outputs.SelectedItem as DeviceChoice;
            if (chosen == null) throw new InvalidOperationException("请先选择播放设备");
            if (!string.IsNullOrEmpty(chosen.Id)) return chosen.Id;
            using (var devs = new MMDeviceEnumerator())
                using (var device = devs.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)) return device.ID;
        }
        void ShowRouteStatus(RouteReport report)
        {
            if (report.ActiveOutside > 0) status.Text = "微信尚未接入 · 请重播语音或重新连接";
            else if (engine != null && engine.Gain.InputPeak > .00001f) status.Text = "正在放大微信声音";
            else status.Text = report.Detected == 0 ? "等待微信播放声音" : "已连接 · 等待微信声音";
        }
        async System.Threading.Tasks.Task StartAudio()
        {
            RefreshDevices();
            if (cableInput == null || cableOutput == null) { tabs.SelectedIndex = 1; return; }
            busy = true; start.Enabled = false;
            try
            {
                // Reject a global CABLE default, which would capture other applications too.
                using (var devs = new MMDeviceEnumerator())
                    foreach (Role role in new[] { Role.Console, Role.Multimedia, Role.Communications })
                        using (var d = devs.GetDefaultAudioEndpoint(DataFlow.Render, role))
                            if (IsCable(d)) throw new InvalidOperationException("请在 Windows 中把系统默认输出设为耳机或扬声器，勿设为 CABLE Input。");
                actualOutput = ResolveOutput();
                engine = new AudioEngine(); engine.Start(cableOutput, actualOutput, slider.Value / 100f);
                Routing.Begin();
                if (!watcherStarted)
                {
                    Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--watch " + Process.GetCurrentProcess().Id) { UseShellExecute = false, CreateNoWindow = true });
                    watcherStarted = true;
                }
                RouteReport report = await System.Threading.Tasks.Task.Run(delegate { return Routing.Apply(cableInput); });
                start.Text = "停止放大"; outputs.Enabled = false;
                ShowRouteStatus(report); SaveSettings();
            }
            catch (Exception ex) { StopAudio(); Error(ex); }
            finally { busy = false; start.Enabled = true; }
        }
        void StopAudio(bool forceRestore = false)
        {
            try { Routing.Restore(forceRestore); }
            catch (Exception ex) { Error(ex); return; }
            try { if (engine != null) engine.Dispose(); }
            catch (Exception ex) { Routing.Log(ex); }
            engine = null; actualOutput = null; start.Text = "开启放大"; mute.Text = "静音";
            outputs.Enabled = true; meter.Value = 0; status.Text = "已停止 · 微信跟随系统默认输出";
            SaveSettings();
        }
        async void Tick(object sender, EventArgs e)
        {
            if (engine == null || busy) return;
            if (engine.Failure != null) { string failure = engine.Failure; StopAudio(); status.Text = "设备中断：" + failure; return; }
            meter.Value = Math.Max(0, Math.Min(1000, (int)(engine.Gain.Peak * 1000)));
            protection.Text = engine.Gain.Limiting ? "峰值保护中" : "峰值保护已开启";
            if (++ticks % 8 != 0) return;
            busy = true;
            try
            {
                string desiredOutput = ResolveOutput();
                if (desiredOutput != actualOutput)
                {
                    using (var devs = new MMDeviceEnumerator())
                        using (var device = devs.GetDevice(desiredOutput))
                            if (IsCable(device)) throw new InvalidOperationException("系统默认播放设备不能是 CABLE Input。");
                    bool muted = engine.Gain.Muted;
                    engine.Dispose(); engine = new AudioEngine();
                    engine.Start(cableOutput, desiredOutput, slider.Value / 100f);
                    engine.Gain.Muted = muted; actualOutput = desiredOutput;
                }
                RouteReport report = await System.Threading.Tasks.Task.Run(delegate { return Routing.Apply(cableInput); });
                ShowRouteStatus(report);
            }
            catch (Exception ex) { StopAudio(); Error(ex); }
            finally { busy = false; }
        }
        void InstallDriver(object sender, EventArgs e)
        {
            string setup = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "driver", "VBCABLE_Setup_x64.exe");
            if (!File.Exists(setup)) { status.Text = "缺少安装文件，请重新运行构建脚本"; return; }
            try { Process.Start(new ProcessStartInfo(setup) { UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(setup) }); }
            catch (Exception ex) { Error(ex); }
        }
        void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (busy) { e.Cancel = true; status.Text = "正在切换音频，请稍后关闭"; return; }
            StopAudio();
            if (engine != null) { e.Cancel = true; return; }
            timer.Stop(); tray.Visible = false; tray.Dispose();
        }
        void Error(Exception ex) { Routing.Log(ex); status.Text = ex.Message; MessageBox.Show(this, ex.Message, "VoiceMagic", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--watch") { Routing.Watch(int.Parse(args[1])); return 0; }
            using (var mutex = new Mutex(false, "Local\\VoiceMagic.Gui"))
            {
                bool owner;
                try { owner = mutex.WaitOne(0); } catch (AbandonedMutexException) { owner = true; }
                if (!owner) { MessageBox.Show("VoiceMagic 已在运行，可从托盘打开。", "VoiceMagic"); return 1; }
                try
                {
                    if (args.Length == 1 && args[0] == "--restore") { Routing.Restore(true); return 0; }
                    if (File.Exists(Routing.Marker)) Routing.Restore();
                    Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new MainForm()); return 0;
                }
                catch (Exception ex) { Routing.Log(ex); MessageBox.Show(ex.Message, "VoiceMagic"); return 1; }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }
}
