using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using VoiceMagic;

class UiTests
{
    static void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); }
    static void CaptureComponents(Form form, string path)
    {
        // Render actual WinForms controls without showing a window on the user's desktop.
        using (var bitmap = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(bitmap, form.ClientRectangle);
            using (var graphics = Graphics.FromImage(bitmap))
                foreach (Control child in form.Controls)
                    using (var content = new Bitmap(child.Width, child.Height))
                    { child.DrawToBitmap(content, child.ClientRectangle); graphics.DrawImageUnscaled(content, child.Location); }
            bitmap.Save(path, ImageFormat.Png);
        }
    }
    static void Render()
    {
        string registrationKey = @"Software\VoiceMagic\StartupTests\" + Guid.NewGuid().ToString("N");
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(registrationKey))
            {
                key.SetValue("Unrelated", "keep");
                StartupManager.WriteRegistration(key, true, @"F:\我的 工具\VoiceMagic.exe");
                Check((string)key.GetValue(StartupManager.ValueName) == "\"F:\\我的 工具\\VoiceMagic.exe\" --startup", "startup command quotes spaced and Unicode paths");
                Check(key.GetValueKind(StartupManager.ValueName) == RegistryValueKind.String, "startup registration uses REG_SZ");
                StartupManager.WriteRegistration(key, false, "unused");
                StartupManager.WriteRegistration(key, false, "unused");
                Check(key.GetValue(StartupManager.ValueName) == null && (string)key.GetValue("Unrelated") == "keep", "disabling startup removes only VoiceMagic registration");
            }
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(registrationKey, false); }
        string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "qa"); Directory.CreateDirectory(folder);
        foreach (float scale in new[] { 1f, 1.5f, 2f })
            using (var panel = new VolumePopup())
            {
                panel.SetUiScale(scale);
                panel.Bind(new PanelState { Percent = 1000, Ready = true, Running = true, Status = "正在放大", Level = .38f });
                panel.CreateControl();
                CaptureComponents(panel, Path.Combine(folder, "volume-" + (int)(scale * 100) + ".png"));
                Check(panel.ClientSize == new Size((int)(360 * scale), (int)(166 * scale)), "popup dimensions at " + scale);
                Check(panel.Controls.Cast<Control>().All(c => panel.ClientRectangle.Contains(c.Bounds)), "all controls stay inside popup at " + scale);
                var percent = panel.Controls.Cast<Control>().First(c => c.AccessibleName == "当前微信音量");
                var measured = TextRenderer.MeasureText("1000%", percent.Font, Size.Empty, TextFormatFlags.NoPadding);
                Check(measured.Width + 4 * scale <= percent.Width, "1000% is fully visible at " + scale);
                var slider = panel.Controls.OfType<GainSlider>().Single();
                Check(slider.AccessibilityObject.Value == "1000%", "accessible gain value at " + scale);
            }
        foreach (float scale in new[] { 1f, 1.5f, 2f })
        {
            var size = new Size((int)(360 * scale), (int)(166 * scale));
            var screen = new Rectangle(-1920, 0, 1920, 1080);
            foreach (Rectangle work in new[] { new Rectangle(-1920, 0, 1920, 1032), new Rectangle(-1920, 48, 1920, 1032), new Rectangle(-1872, 0, 1872, 1080), new Rectangle(-1920, 0, 1872, 1080) })
            {
                Point position = PopupPlacement.Calculate(size, work, screen, new Rectangle(-16, 1050, 24, 24), (int)(8 * scale));
                Check(work.Contains(new Rectangle(position, size)), "popup stays in working area at " + scale + " " + work);
            }
        }
    }
    static int failure;
    static async Task StartupLifecycle(VoiceMagicContext context, VolumePopup panel)
    {
        try
        {
            for (int i = 0; i < 100 && !context.Running; i++) await Task.Delay(100);
            Check(context.Running, "startup mode automatically enables audio");
            Check(!panel.Visible, "startup mode keeps popup hidden");
            await Task.Delay(2300);
            Check(context.Running && !panel.Visible, "startup remains in background after routing checks");
            Check(await context.StopAsync(true), "startup mode can be stopped normally");
            await Task.Delay(1100);
            Check(!context.Running, "explicit stop prevents automatic re-enabling");
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); failure = 1; }
        await context.ExitAsync();
    }
    static async Task Lifecycle(VoiceMagicContext context, VolumePopup panel)
    {
        int savedPercent = context.State.Percent;
        try
        {
            Check(context.Ready, "audio components ready for lifecycle test");
            context.SetGain(100); await context.ToggleAsync();
            Check(context.Running && File.Exists(Routing.Marker), "application owns running audio engine");
            int publications = 0; context.Changed += delegate { publications++; };
            panel.Hide(); await Task.Delay(2300);
            Check(context.Running && publications >= 5, "hidden popup leaves audio and application timer running");
            IntPtr panelHandle = panel.Handle; panel.Close();
            Check(!panel.IsDisposed && context.Running, "closing the popup hides it without releasing audio");
            using (var settings = new SettingsForm(context)) { settings.Close(); }
            Check(context.Running, "closing settings leaves audio running");
            await context.ReconnectAsync(); Check(context.Running, "reconnect restarts audio independently of popup");
            Check(await context.StopAsync(true) && !context.Running && !File.Exists(Routing.Marker), "stop restores routing and releases audio");
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); failure = 1; }
        finally { context.SetGain(savedPercent); }
        await context.ExitAsync();
    }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            bool startupMode = args.Contains("--startup-lifecycle");
            if (!args.Contains("--lifecycle") && !startupMode) { Render(); return 0; }
            using (var show = new EventWaitHandle(false, EventResetMode.AutoReset))
            using (var restore = new EventWaitHandle(false, EventResetMode.AutoReset))
            using (var panel = new VolumePopup())
            using (var context = new VoiceMagicContext(show, restore, panel, false, startupMode))
            using (var dispatch = new Control())
            {
                IntPtr handle = dispatch.Handle;
                dispatch.BeginInvoke(new Action(async delegate { if (startupMode) await StartupLifecycle(context, panel); else await Lifecycle(context, panel); }));
                Application.Run(context);
            }
            return failure;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
