using System;
using System.Drawing;
using System.Windows.Forms;

namespace VoiceMagic
{
    public sealed class PanelState
    {
        public int Percent;
        public bool Running, Busy, Muted, Ready;
        public string Status = "未开启";
        public float Level;
    }
    public sealed class VolumePopup : Form
    {
        readonly Label title = new Label(), status = new Label(), percent = new Label();
        readonly PictureBox chat = new PictureBox();
        readonly IconButton mute = new IconButton(), reconnect = new IconButton(), settings = new IconButton();
        readonly GainSlider slider = new GainSlider();
        readonly PowerToggle power = new PowerToggle();
        readonly LevelLine meter = new LevelLine();
        readonly ToolTip tips = new ToolTip();
        float scale = 1;
        bool binding, resizing;
        public bool AllowClose;
        public Func<Rectangle> TrayBounds;
        public event Action<int> GainChanged;
        public event Action ToggleRequested, MuteRequested, ReconnectRequested, SettingsRequested;
        public VolumePopup()
        {
            Text = "VoiceMagic · 微信音量"; BackColor = UiTheme.Background; ForeColor = UiTheme.Text;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            AutoScaleMode = AutoScaleMode.None; StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true; Icon = UiTheme.AppIcon;
            title.Text = "微信"; title.TextAlign = ContentAlignment.MiddleLeft;
            status.TextAlign = ContentAlignment.MiddleRight; status.ForeColor = UiTheme.Muted;
            percent.TextAlign = ContentAlignment.MiddleRight; percent.AccessibleName = "当前微信音量";
            chat.Image = UiTheme.Image("chat"); chat.SizeMode = PictureBoxSizeMode.Zoom;
            mute.IconName = "speaker"; mute.AccessibleName = "静音";
            reconnect.IconName = "reconnect"; reconnect.AccessibleName = "重新连接";
            settings.IconName = "settings"; settings.AccessibleName = "设置";
            mute.Click += delegate { if (MuteRequested != null) MuteRequested(); };
            reconnect.Click += delegate { if (ReconnectRequested != null) ReconnectRequested(); };
            settings.Click += delegate { if (SettingsRequested != null) SettingsRequested(); };
            slider.ValueChanged += delegate(int value) { if (!binding && GainChanged != null) GainChanged(value); };
            power.CheckedChanged += delegate { if (!binding && ToggleRequested != null) ToggleRequested(); };
            tips.SetToolTip(reconnect, "重新连接微信声音"); tips.SetToolTip(settings, "设置");
            tips.SetToolTip(meter, "微信声音电平"); tips.SetToolTip(slider, "0–1000% · 方向键或滚轮微调");
            Controls.AddRange(new Control[] { title, status, percent, chat, mute, slider, meter, power, reconnect, settings });
            SetUiScale(1); Bind(new PanelState());
        }
        protected override CreateParams CreateParams
        {
            get { CreateParams p = base.CreateParams; p.ClassStyle |= 0x20000; p.ExStyle |= 0x80; return p; }
        }
        public void SetUiScale(float value)
        {
            if (resizing) return;
            resizing = true;
            try
            {
                scale = Math.Max(.75f, value); SuspendLayout();
                Size = new Size(P(360), P(166));
                Font = UiTheme.Font(scale);
                title.SetBounds(P(48), P(16), P(90), P(24));
                chat.SetBounds(P(20), P(18), P(20), P(20));
                status.SetBounds(P(154), P(16), P(186), P(24));
                mute.SetBounds(P(14), P(54), P(32), P(32));
                slider.SetBounds(P(54), P(54), P(212), P(32));
                percent.SetBounds(P(272), P(54), P(68), P(32));
                meter.SetBounds(P(63), P(96), P(194), P(3));
                power.SetBounds(P(20), P(123), P(96), P(28));
                reconnect.SetBounds(P(268), P(121), P(32), P(32));
                settings.SetBounds(P(310), P(121), P(32), P(32));
                mute.UiScale = reconnect.UiScale = settings.UiScale = slider.UiScale = power.UiScale = scale;
                reconnect.BackColor = settings.BackColor = power.BackColor = UiTheme.Footer;
                using (var rounded = UiTheme.Rounded(new RectangleF(0, 0, Width, Height), P(5)))
                {
                    Region old = Region; Region = new Region(rounded); if (old != null) old.Dispose();
                }
                ResumeLayout(false); Invalidate(true);
            }
            finally { resizing = false; }
        }
        int P(float logical) { return (int)Math.Round(logical * scale); }
        public void Bind(PanelState state)
        {
            binding = true;
            try
            {
                slider.Value = state.Percent; percent.Text = state.Percent + "%"; status.Text = state.Status;
                power.Checked = state.Running; power.Enabled = !state.Busy;
                mute.Enabled = state.Running && !state.Busy; mute.Selected = state.Muted;
                mute.IconName = state.Muted ? "speaker-mute" : "speaker"; mute.Invalidate();
                mute.AccessibleName = state.Muted ? "取消静音" : "静音";
                reconnect.Enabled = state.Ready && !state.Busy;
                meter.Level = state.Level;
                tips.SetToolTip(mute, state.Muted ? "取消静音" : "静音");
            }
            finally { binding = false; }
        }
        public void ShowAt(Rectangle anchor)
        {
            Screen screen = Screen.FromRectangle(anchor);
            SetUiScale(NativeUi.ScaleAt(new Point(anchor.Left, anchor.Top)));
            Location = PopupPlacement.Calculate(Size, screen.WorkingArea, screen.Bounds, anchor, P(8));
            if (!Visible) Show();
            Activate(); NativeUi.SetForegroundWindow(Handle); slider.Focus();
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); NativeUi.RoundWindow(Handle); }
        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (TrayBounds == null || !TrayBounds().Contains(Cursor.Position)) Hide();
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!AllowClose && e.CloseReason != CloseReason.WindowsShutDown) { e.Cancel = true; Hide(); }
            base.OnFormClosing(e);
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Hide(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x2e0) SetUiScale((int)(message.WParam.ToInt64() & 0xffff) / 96f);
            base.WndProc(ref message);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var brush = new SolidBrush(UiTheme.Footer)) e.Graphics.FillRectangle(brush, 0, P(112), Width, Height - P(112));
            using (var pen = new Pen(UiTheme.Border))
            {
                e.Graphics.DrawLine(pen, 0, P(112), Width, P(112));
                using (var rounded = UiTheme.Rounded(new RectangleF(.5f, .5f, Width - 1, Height - 1), P(5))) e.Graphics.DrawPath(pen, rounded);
            }
        }
        protected override void Dispose(bool disposing) { if (disposing) tips.Dispose(); base.Dispose(disposing); }
    }
}
