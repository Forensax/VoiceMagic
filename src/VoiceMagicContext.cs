using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NAudio.CoreAudioApi;

namespace VoiceMagic
{
    // Audio and routing belong to the application, independently of the visible windows.
    public sealed class VoiceMagicContext : ApplicationContext
    {
        readonly Settings settings = Settings.Load();
        readonly Control dispatcher = new Control();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly TrayHost tray;
        readonly VolumePopup popup;
        readonly bool desktopIntegration;
        readonly RegisteredWaitHandle showWait, restoreWait;
        readonly List<DeviceChoice> devices = new List<DeviceChoice>();
        SettingsForm settingsWindow;
        AudioEngine engine;
        RouteReport report = new RouteReport();
        string cableInput, cableOutput, actualOutput, status = "未开启";
        bool busy, watcherStarted, exiting, exitRequested, savePending, startupPending;
        int ticks, gainChangedAt, startupTicks;
        public event Action Changed;
        public bool Running { get { return engine != null; } }
        public bool Busy { get { return busy; } }
        public bool Ready { get { return cableInput != null && cableOutput != null && Routing.Available; } }
        public string ErrorDetail { get; private set; }
        public string OutputId { get { return settings.OutputId; } }
        public IList<DeviceChoice> Devices { get { return devices.AsReadOnly(); } }
        public PanelState State
        {
            get
            {
                string text = status;
                if (!string.IsNullOrEmpty(ErrorDetail)) text = "连接异常";
                else if (Running && !busy)
                {
                    if (engine.Gain.Muted) text = "已静音";
                    else if (engine.Gain.Limiting) text = "峰值保护";
                    else if (report.ActiveOutside > 0) text = "微信未接入";
                    else if (engine.Gain.InputPeak > .00001f) text = "正在放大";
                    else text = "等待播放";
                }
                return new PanelState { Percent = settings.Percent, Running = Running, Busy = busy, Ready = Ready,
                    Muted = Running && engine.Gain.Muted, Status = text, Level = Running ? engine.Gain.Peak : 0 };
            }
        }
        public VoiceMagicContext(EventWaitHandle show, EventWaitHandle reset, VolumePopup panel = null, bool desktopIntegration = true, bool startup = false)
        {
            this.desktopIntegration = desktopIntegration;
            startupPending = startup;
            settings.Percent = Math.Max(0, Math.Min(1000, settings.Percent));
            IntPtr dispatchHandle = dispatcher.Handle;
            popup = panel ?? new VolumePopup();
            popup.GainChanged += SetGain;
            popup.ToggleRequested += async delegate { await ToggleAsync(); };
            popup.MuteRequested += ToggleMute;
            popup.ReconnectRequested += async delegate { await ReconnectAsync(); };
            popup.SettingsRequested += OpenSettings;
            if (desktopIntegration)
            {
                tray = new TrayHost();
                tray.Activated += delegate { if (popup.Visible) popup.Hide(); else ShowPopup(); };
                tray.MenuOpening += delegate { popup.Hide(); };
                tray.Menu.Items.Add("音量面板", null, delegate { ShowPopup(); });
                tray.Menu.Items.Add("设置", null, delegate { OpenSettings(); });
                tray.Menu.Items.Add(new ToolStripSeparator());
                tray.Menu.Items.Add("恢复并退出", null, async delegate { await ExitAsync(); });
                popup.TrayBounds = delegate { return tray.Bounds; };
            }
            RefreshDevices();
            showWait = ThreadPool.RegisterWaitForSingleObject(show, delegate { Dispatch(ShowPopup); }, null, -1, false);
            restoreWait = ThreadPool.RegisterWaitForSingleObject(reset, delegate { Dispatch(async delegate { await StopAsync(true); }); }, null, -1, false);
            timer.Interval = 250; timer.Tick += Tick; timer.Start(); Publish();
            if (desktopIntegration || startup) dispatcher.BeginInvoke(new Action(async delegate {
                if (startup) { if (Ready) await StartAsync(); }
                else { ShowPopup(); if (!Ready) OpenSettings(); }
            }));
        }
        void Dispatch(Action action)
        {
            try { if (!exiting) dispatcher.BeginInvoke(action); } catch (InvalidOperationException) { }
        }
        void Publish()
        {
            if (exiting) return;
            PanelState state = State; popup.Bind(state);
            if (tray != null) tray.SetTip("VoiceMagic · " + state.Percent + "% · " + state.Status);
            if (Changed != null) Changed();
        }
        void Save()
        {
            try { settings.Save(); savePending = false; } catch (Exception ex) { Routing.Log(ex); }
        }
        public void SetGain(int percent)
        {
            settings.Percent = Math.Max(0, Math.Min(1000, percent));
            if (Running) engine.Gain.Gain = settings.Percent / 100f;
            savePending = true; gainChangedAt = Environment.TickCount; Publish();
        }
        public void ToggleMute() { if (Running && !busy) { engine.Gain.Muted = !engine.Gain.Muted; Publish(); } }
        public void ShowPopup() { if (!exiting && tray != null) { popup.ShowAt(tray.Bounds); Publish(); } }
        public void OpenSettings()
        {
            if (exiting) return;
            popup.Hide();
            if (settingsWindow == null || settingsWindow.IsDisposed) settingsWindow = new SettingsForm(this);
            if (!settingsWindow.Visible) settingsWindow.Show();
            settingsWindow.WindowState = FormWindowState.Normal; settingsWindow.Activate();
        }
        internal static bool IsCable(MMDevice d)
        {
            return d.FriendlyName.IndexOf("VB-Audio", StringComparison.OrdinalIgnoreCase) >= 0 && d.FriendlyName.IndexOf("CABLE", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        public void RefreshDevices()
        {
            try
            {
                string input = null, capture = null;
                devices.Clear(); devices.Add(new DeviceChoice { Id = "", Name = "跟随系统默认播放设备" });
                using (var enumerator = new MMDeviceEnumerator())
                {
                    foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                        using (device) { if (IsCable(device) && device.FriendlyName.IndexOf("CABLE Output", StringComparison.OrdinalIgnoreCase) >= 0) capture = device.ID; }
                    foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                        using (device)
                        {
                            if (IsCable(device)) { if (device.FriendlyName.IndexOf("CABLE Input", StringComparison.OrdinalIgnoreCase) >= 0) input = device.ID; }
                            else devices.Add(new DeviceChoice { Id = device.ID, Name = device.FriendlyName });
                        }
                }
                if (!Running) { cableInput = input; cableOutput = capture; }
                if (!devices.Any(d => d.Id == settings.OutputId)) { settings.OutputId = ""; Save(); }
                if (!Running) status = Ready ? "未开启" : "需要设置";
            }
            catch (Exception ex) { SetError(ex); }
            Publish();
        }
        string ResolveOutput()
        {
            if (!string.IsNullOrEmpty(settings.OutputId)) return settings.OutputId;
            using (var enumerator = new MMDeviceEnumerator())
                using (var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)) return device.ID;
        }
        public async Task SetOutputAsync(string id)
        {
            if (busy || id == settings.OutputId) return;
            bool restart = Running, muted = Running && engine.Gain.Muted;
            if (restart && !await StopAsync(false)) return;
            settings.OutputId = id; Save();
            if (restart) { await StartAsync(); if (Running) engine.Gain.Muted = muted; }
            Publish();
        }
        public async Task ToggleAsync() { startupPending = false; if (!busy) { if (Running) await StopAsync(false); else await StartAsync(); } }
        public async Task ReconnectAsync()
        {
            if (busy) return;
            bool muted = Running && engine.Gain.Muted;
            if (Running && !await StopAsync(false)) return;
            await StartAsync(); if (Running) engine.Gain.Muted = muted; Publish();
        }
        async Task StartAsync()
        {
            RefreshDevices();
            if (!Ready) { if (!startupPending) OpenSettings(); return; }
            busy = true; status = "正在连接"; ErrorDetail = ""; Publish();
            Exception failure = null;
            try
            {
                using (var enumerator = new MMDeviceEnumerator())
                    foreach (Role role in new[] { Role.Console, Role.Multimedia, Role.Communications })
                        using (var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, role))
                            if (IsCable(device)) throw new InvalidOperationException("请将 Windows 默认输出设为耳机或扬声器，勿设为 CABLE Input。");
                actualOutput = ResolveOutput(); engine = new AudioEngine(); engine.Start(cableOutput, actualOutput, settings.Percent / 100f);
                Routing.Begin();
                if (!watcherStarted && desktopIntegration)
                {
                    Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--watch " + Process.GetCurrentProcess().Id) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
                    watcherStarted = true;
                }
                report = await Task.Run(delegate { return Routing.Apply(cableInput); }); Save();
                startupPending = false;
            }
            catch (Exception ex) { failure = ex; }
            try
            {
                if (failure == null) return;
                try { await Task.Run(delegate { Routing.Restore(); }); } catch (Exception restoreError) { Routing.Log(restoreError); }
                if (engine != null) { engine.Dispose(); engine = null; } SetError(failure);
            }
            finally { busy = false; Publish(); }
        }
        public async Task<bool> StopAsync(bool forceRestore)
        {
            if (busy) return false;
            startupPending = false;
            busy = true; status = "正在停止"; Publish();
            try
            {
                await Task.Run(delegate { Routing.Restore(forceRestore); });
                if (engine != null) { engine.Dispose(); engine = null; }
                actualOutput = null; report = new RouteReport(); status = Ready ? "未开启" : "需要设置"; ErrorDetail = "";
                Save(); return true;
            }
            catch (Exception ex) { SetError(ex); return false; }
            finally { busy = false; Publish(); }
        }
        async void Tick(object sender, EventArgs e)
        {
            if (exitRequested && !busy) { await ExitAsync(); return; }
            if (savePending && unchecked(Environment.TickCount - gainChangedAt) >= 500) Save();
            Publish();
            if (startupPending && !Running && !busy && ++startupTicks >= 40)
            {
                startupTicks = 0; RefreshDevices(); if (Ready) await StartAsync(); return;
            }
            if (!Running || busy || ++ticks % 8 != 0) return;
            busy = true;
            Exception failure = null;
            try
            {
                string desired = ResolveOutput();
                if (desired != actualOutput || engine.Failure != null)
                {
                    using (var enumerator = new MMDeviceEnumerator())
                        using (var device = enumerator.GetDevice(desired))
                            if (IsCable(device)) throw new InvalidOperationException("系统默认播放设备不能是 CABLE Input。");
                    bool muted = engine.Gain.Muted;
                    engine.Dispose(); engine = new AudioEngine(); engine.Start(cableOutput, desired, settings.Percent / 100f);
                    engine.Gain.Muted = muted; actualOutput = desired;
                }
                report = await Task.Run(delegate { return Routing.Apply(cableInput); });
            }
            catch (Exception ex) { failure = ex; }
            finally { busy = false; Publish(); }
            if (failure != null) { await StopAsync(false); SetError(failure); Publish(); }
        }
        void SetError(Exception ex) { Routing.Log(ex); ErrorDetail = ex.Message; status = "连接异常"; }
        public void InstallDriver()
        {
            string installer = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "driver", "VBCABLE_Setup_x64.exe");
            try
            {
                if (!File.Exists(installer)) throw new IOException("缺少音频组件安装文件，请重新构建程序。");
                Process.Start(new ProcessStartInfo(installer) { UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(installer) });
            }
            catch (Exception ex) { SetError(ex); Publish(); }
        }
        public async Task ExitAsync()
        {
            if (busy) { exitRequested = true; return; }
            exitRequested = false;
            if (!await StopAsync(false)) { OpenSettings(); return; }
            exiting = true; timer.Stop(); Save(); ExitThread();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                exiting = true; timer.Dispose();
                if (showWait != null) showWait.Unregister(null); if (restoreWait != null) restoreWait.Unregister(null);
                if (engine != null) engine.Dispose(); if (settingsWindow != null) settingsWindow.Dispose();
                popup.AllowClose = true; popup.Dispose(); if (tray != null) tray.Dispose(); dispatcher.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
