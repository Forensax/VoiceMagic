using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace VoiceMagic
{
    public sealed class SettingsForm : Form
    {
        readonly VoiceMagicContext context;
        readonly ComboBox outputs = new ComboBox();
        readonly Label heading = new Label(), component = new Label(), note = new Label(), error = new Label();
        readonly Button refresh = new Button(), install = new Button(), restore = new Button(), windows = new Button();
        bool binding;
        string deviceSignature;
        float scale;
        public SettingsForm(VoiceMagicContext context)
        {
            this.context = context;
            Text = "VoiceMagic · 设置"; Icon = UiTheme.AppIcon; BackColor = UiTheme.Background; ForeColor = UiTheme.Text;
            AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            heading.Text = "播放设备";
            outputs.DropDownStyle = ComboBoxStyle.DropDownList; outputs.AccessibleName = "放大后的播放设备";
            outputs.SelectedIndexChanged += async delegate { if (!binding && outputs.SelectedItem != null) await context.SetOutputAsync(((DeviceChoice)outputs.SelectedItem).Id); };
            refresh.Text = "刷新设备"; refresh.Click += delegate { context.RefreshDevices(); };
            install.Text = "安装音频组件"; install.Click += delegate { context.InstallDriver(); };
            restore.Text = "恢复默认输出"; restore.Click += async delegate { await context.StopAsync(true); };
            windows.Text = "Windows 音量设置"; windows.Click += delegate { Process.Start(new ProcessStartInfo("ms-settings:apps-volume") { UseShellExecute = true }); };
            note.Text = "首次安装后，按安装程序提示重启。"; note.ForeColor = UiTheme.Muted;
            error.ForeColor = Color.FromArgb(161, 64, 48); error.AutoEllipsis = true;
            foreach (Button button in new[] { refresh, install, restore, windows })
            {
                button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderColor = UiTheme.Border;
                button.BackColor = Color.FromArgb(250, 250, 250); button.Cursor = Cursors.Hand;
            }
            Controls.AddRange(new Control[] { heading, outputs, component, refresh, install, note, error, restore, windows });
            ApplyScale(NativeUi.ScaleAt(Cursor.Position));
            context.Changed += UpdateState; UpdateState();
        }
        void ApplyScale(float value)
        {
            scale = value; ClientSize = new Size(P(420), P(308)); Font = UiTheme.Font(scale);
            heading.SetBounds(P(20), P(20), P(380), P(24));
            outputs.SetBounds(P(20), P(52), P(380), P(30));
            component.SetBounds(P(20), P(102), P(380), P(24));
            refresh.SetBounds(P(20), P(138), P(116), P(34));
            install.SetBounds(P(148), P(138), P(144), P(34));
            note.SetBounds(P(20), P(186), P(380), P(24));
            error.SetBounds(P(20), P(213), P(380), P(36));
            restore.SetBounds(P(20), P(254), P(184), P(34));
            windows.SetBounds(P(216), P(254), P(184), P(34));
        }
        int P(int value) { return (int)Math.Round(value * scale); }
        void UpdateState()
        {
            if (IsDisposed) return;
            binding = true;
            try
            {
                string signature = string.Join("|", context.Devices.Select(d => d.Id));
                if (signature != deviceSignature)
                {
                    outputs.Items.Clear(); foreach (var device in context.Devices) outputs.Items.Add(device); deviceSignature = signature;
                }
                for (int i = 0; i < outputs.Items.Count; i++) if (((DeviceChoice)outputs.Items[i]).Id == context.OutputId) outputs.SelectedIndex = i;
                outputs.Enabled = refresh.Enabled = restore.Enabled = !context.Busy;
                install.Enabled = !context.Busy && !context.Running;
                component.Text = context.Ready ? "音频组件已就绪" : "需要安装音频组件";
                error.Text = context.ErrorDetail ?? "";
            }
            finally { binding = false; }
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x2e0) ApplyScale((int)(message.WParam.ToInt64() & 0xffff) / 96f);
            base.WndProc(ref message);
        }
        protected override void Dispose(bool disposing) { if (disposing) context.Changed -= UpdateState; base.Dispose(disposing); }
    }
}
