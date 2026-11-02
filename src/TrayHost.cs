using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VoiceMagic
{
    internal static class NativeUi
    {
        [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct IconIdentifier { public uint Size; public IntPtr Window; public uint Id; public Guid Guid; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct IconData
        {
            public uint Size; public IntPtr Window; public uint Id, Flags, Callback; public IntPtr Icon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
            public uint State, StateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
            public uint Version;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
            public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern bool Shell_NotifyIcon(uint action, ref IconData data);
        [DllImport("shell32.dll")] public static extern int Shell_NotifyIconGetRect(ref IconIdentifier identifier, out Rect rect);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int RegisterWindowMessage(string name);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(Point point, uint flags);
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        public static float ScaleAt(Point point)
        {
            try { uint x, y; if (GetDpiForMonitor(MonitorFromPoint(point, 2), 0, out x, out y) == 0) return x / 96f; } catch (EntryPointNotFoundException) { }
            using (var graphics = Graphics.FromHwnd(IntPtr.Zero)) return graphics.DpiX / 96f;
        }
        public static void RoundWindow(IntPtr window) { try { int corner = 2; DwmSetWindowAttribute(window, 33, ref corner, 4); } catch (DllNotFoundException) { } }
    }

    // Own the native icon ID so positioning can use Shell_NotifyIconGetRect without reflection.
    internal sealed class TrayHost : NativeWindow, IDisposable
    {
        const uint CallbackMessage = 0x8001;
        readonly int taskbarCreated = NativeUi.RegisterWindowMessage("TaskbarCreated");
        readonly Icon icon = UiTheme.AppIcon;
        NativeUi.IconData data;
        string tip = "VoiceMagic · 微信音量";
        bool disposed;
        public readonly ContextMenuStrip Menu = new ContextMenuStrip();
        public event Action Activated, MenuOpening;
        public TrayHost()
        {
            CreateHandle(new CreateParams { Caption = "VoiceMagic.TrayHost", Style = unchecked((int)0x80000000), ExStyle = 0x80 });
            Menu.Font = UiTheme.Font(1);
            Add();
        }
        void Add()
        {
            data = new NativeUi.IconData { Size = (uint)Marshal.SizeOf(typeof(NativeUi.IconData)), Window = Handle, Id = 1,
                Flags = 1 | 2 | 4 | 0x80, Callback = CallbackMessage, Icon = icon.Handle, Tip = tip, Info = "", InfoTitle = "" };
            NativeUi.Shell_NotifyIcon(0, ref data);
            data.Version = 4; NativeUi.Shell_NotifyIcon(4, ref data);
        }
        public Rectangle Bounds
        {
            get
            {
                var id = new NativeUi.IconIdentifier { Size = (uint)Marshal.SizeOf(typeof(NativeUi.IconIdentifier)), Window = Handle, Id = 1 };
                NativeUi.Rect rect;
                if (NativeUi.Shell_NotifyIconGetRect(ref id, out rect) == 0) return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
                return new Rectangle(work.Right - 40, work.Bottom + 1, 24, 24);
            }
        }
        public void SetTip(string value)
        {
            if (tip == value || disposed) return;
            tip = value; data.Tip = value; data.Flags = 4 | 0x80; NativeUi.Shell_NotifyIcon(1, ref data);
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == taskbarCreated && !disposed) Add();
            if (message.Msg == CallbackMessage)
            {
                int action = (int)(message.LParam.ToInt64() & 0xffff);
                if ((action == 0x400 || action == 0x401) && Activated != null) Activated();
                else if (action == 0x7b)
                {
                    if (MenuOpening != null) MenuOpening();
                    NativeUi.SetForegroundWindow(Handle);
                    Menu.Show(Cursor.Position);
                }
            }
            base.WndProc(ref message);
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            NativeUi.Shell_NotifyIcon(2, ref data); Menu.Dispose(); DestroyHandle();
        }
    }

    public static class PopupPlacement
    {
        public static Point Calculate(Size size, Rectangle work, Rectangle screen, Rectangle anchor, int margin)
        {
            int x = anchor.Right - size.Width, y = work.Bottom - size.Height - margin;
            if (work.Left > screen.Left) { x = work.Left + margin; y = anchor.Bottom - size.Height; }
            else if (work.Right < screen.Right) { x = work.Right - size.Width - margin; y = anchor.Bottom - size.Height; }
            else if (work.Top > screen.Top) y = work.Top + margin;
            x = Math.Max(work.Left + margin, Math.Min(x, work.Right - size.Width - margin));
            y = Math.Max(work.Top + margin, Math.Min(y, work.Bottom - size.Height - margin));
            return new Point(x, y);
        }
    }
}
