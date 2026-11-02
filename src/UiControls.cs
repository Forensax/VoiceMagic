using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Windows.Forms;

namespace VoiceMagic
{
    public static class UiTheme
    {
        public static readonly Color Background = Color.FromArgb(244, 244, 244), Footer = Color.FromArgb(236, 236, 236),
            Text = Color.FromArgb(38, 38, 38), Muted = Color.FromArgb(104, 104, 104), Accent = Color.FromArgb(25, 116, 134), Border = Color.FromArgb(204, 204, 204);
        static readonly Dictionary<string, Bitmap> icons = new Dictionary<string, Bitmap>();
        static readonly Dictionary<float, Font> fonts = new Dictionary<float, Font>();
        static Icon appIcon;
        public static Font Font(float scale)
        {
            if (!fonts.ContainsKey(scale)) fonts[scale] = new Font("Microsoft YaHei UI", 13 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            return fonts[scale];
        }
        public static Icon AppIcon
        {
            get { if (appIcon == null) using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("VoiceMagic.Icons.VoiceMagic.ico")) appIcon = new Icon(stream); return appIcon; }
        }
        public static Bitmap Image(string name)
        {
            if (!icons.ContainsKey(name))
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("VoiceMagic.Icons." + name + ".png"))
                using (var image = System.Drawing.Image.FromStream(stream)) icons[name] = new Bitmap(image);
            return icons[name];
        }
        public static GraphicsPath Rounded(RectangleF rect, float radius)
        {
            var path = new GraphicsPath(); float d = radius * 2;
            path.AddArc(rect.Left, rect.Top, d, d, 180, 90); path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90); path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
        }
    }
    public class IconButton : Button
    {
        string iconName;
        bool hover;
        public float UiScale = 1;
        public bool Selected;
        public string IconName { get { return iconName; } set { if (iconName != value) { iconName = value; Invalidate(); } } }
        public IconButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand; TabStop = true;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(BackColor);
            if (hover && Enabled) using (var brush = new SolidBrush(Color.FromArgb(223, 225, 226)))
                using (var path = UiTheme.Rounded(new RectangleF(0, 0, Width - 1, Height - 1), 4 * UiScale)) g.FillPath(brush, path);
            if (iconName != null)
            {
                int side = (int)Math.Round(20 * UiScale);
                var target = new Rectangle((Width - side) / 2, (Height - side) / 2, side, side);
                var color = !Enabled ? Color.FromArgb(156, 156, 156) : Selected ? UiTheme.Accent : UiTheme.Text;
                using (var attributes = new ImageAttributes())
                {
                    attributes.SetColorMatrix(new ColorMatrix(new float[][] {
                        new float[] {0,0,0,0,0}, new float[] {0,0,0,0,0}, new float[] {0,0,0,0,0},
                        new float[] {0,0,0,1,0}, new float[] {color.R / 255f,color.G / 255f,color.B / 255f,0,1} }));
                    Bitmap image = UiTheme.Image(iconName); g.DrawImage(image, target, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
                }
            }
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, new Rectangle(2, 2, Width - 5, Height - 5));
        }
    }
    public sealed class GainSlider : Control
    {
        int value;
        public float UiScale = 1;
        public event Action<int> ValueChanged;
        public int Value { get { return value; } set { int next = Math.Max(0, Math.Min(1000, value)); if (this.value != next) { this.value = next; Invalidate(); if (ValueChanged != null) ValueChanged(next); } } }
        public GainSlider()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true; Cursor = Cursors.Hand; AccessibleName = "微信音量百分比"; AccessibleRole = AccessibleRole.Slider;
        }
        void SetFromX(int x) { float inset = 9 * UiScale; Value = (int)Math.Round((x - inset) / Math.Max(1, Width - 2 * inset) * 1000); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { Focus(); Capture = true; SetFromX(e.X); } }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (Capture) SetFromX(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (e.Button == MouseButtons.Left) Capture = false; }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Value += Math.Sign(e.Delta) * 10; }
        protected override bool IsInputKey(Keys key) { return key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down || key == Keys.Home || key == Keys.End || base.IsInputKey(key); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down) Value -= 10;
            else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up) Value += 10;
            else if (e.KeyCode == Keys.PageDown) Value -= 50;
            else if (e.KeyCode == Keys.PageUp) Value += 50;
            else if (e.KeyCode == Keys.Home) Value = 0;
            else if (e.KeyCode == Keys.End) Value = 1000;
            else { base.OnKeyDown(e); return; }
            e.Handled = true; e.SuppressKeyPress = true;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            float inset = 9 * UiScale, y = Height / 2f, x = inset + (Width - 2 * inset) * value / 1000f;
            using (var pen = new Pen(Color.FromArgb(142, 142, 142), 3 * UiScale) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(pen, inset, y, Width - inset, y);
            using (var pen = new Pen(UiTheme.Accent, 3 * UiScale) { StartCap = LineCap.Round, EndCap = LineCap.Round }) if (value > 0) g.DrawLine(pen, inset, y, x, y);
            float r = 8 * UiScale;
            g.FillEllipse(Brushes.White, x - r, y - r, r * 2, r * 2);
            using (var brush = new SolidBrush(UiTheme.Accent)) g.FillEllipse(brush, x - 5 * UiScale, y - 5 * UiScale, 10 * UiScale, 10 * UiScale);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, new Rectangle(0, 0, Width - 1, Height - 1));
        }
        protected override AccessibleObject CreateAccessibilityInstance() { return new SliderAccessible(this); }
        sealed class SliderAccessible : ControlAccessibleObject
        {
            readonly GainSlider slider;
            public SliderAccessible(GainSlider owner) : base(owner) { slider = owner; }
            public override string Value { get { return slider.Value + "%"; } set { int parsed; if (int.TryParse(value.TrimEnd('%'), out parsed)) slider.Value = parsed; } }
        }
    }
    public sealed class PowerToggle : CheckBox
    {
        bool hover;
        public float UiScale = 1;
        public PowerToggle() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); Cursor = Cursors.Hand; AccessibleName = "放大开关"; }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            float x = 0, y = (Height - 18 * UiScale) / 2;
            Color fill = Enabled ? Checked ? UiTheme.Accent : Color.FromArgb(154, 154, 154) : UiTheme.Border;
            if (hover && Enabled) fill = ControlPaint.Light(fill, .15f);
            using (var path = UiTheme.Rounded(new RectangleF(x, y, 34 * UiScale, 18 * UiScale), 9 * UiScale))
            using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
            g.FillEllipse(Brushes.White, (Checked ? 18 : 2) * UiScale, y + 2 * UiScale, 14 * UiScale, 14 * UiScale);
            TextRenderer.DrawText(g, "放大", Font, new Rectangle((int)(44 * UiScale), 0, Width - (int)(44 * UiScale), Height), UiTheme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, ClientRectangle);
        }
    }
    public sealed class LevelLine : Control
    {
        float level;
        public float Level { get { return level; } set { level = Math.Max(0, Math.Min(1, value)); Invalidate(); } }
        public LevelLine() { DoubleBuffered = true; AccessibleName = "微信声音电平"; AccessibleRole = AccessibleRole.ProgressBar; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Color.FromArgb(218, 220, 221));
            using (var brush = new SolidBrush(UiTheme.Accent)) e.Graphics.FillRectangle(brush, 0, 0, Width * level, Height);
        }
    }
}
