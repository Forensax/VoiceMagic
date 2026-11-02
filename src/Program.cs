using System;
using System.Threading;
using System.Windows.Forms;
namespace VoiceMagic
{
    static class Program
    {
        public const string ShowEventName = "Local\\VoiceMagic.ShowPanel";
        public const string RestoreEventName = "Local\\VoiceMagic.RestoreOutput";
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--watch") { Routing.Watch(int.Parse(args[1])); return 0; }
            bool restore = args.Length == 1 && args[0] == "--restore";
            using (var mutex = new Mutex(false, "Local\\VoiceMagic.Gui"))
            {
                bool owner;
                try { owner = mutex.WaitOne(0); } catch (AbandonedMutexException) { owner = true; }
                if (!owner)
                {
                    try { using (var signal = EventWaitHandle.OpenExisting(restore ? RestoreEventName : ShowEventName)) signal.Set(); return 0; }
                    catch (WaitHandleCannotBeOpenedException) { MessageBox.Show("请从托盘打开当前 VoiceMagic。", "VoiceMagic"); return 1; }
                }
                try
                {
                    if (restore) { Routing.Restore(true); return 0; }
                    if (System.IO.File.Exists(Routing.Marker)) Routing.Restore();
                    Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                    SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                    using (var show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
                    using (var reset = new EventWaitHandle(false, EventResetMode.AutoReset, RestoreEventName))
                    using (var context = new VoiceMagicContext(show, reset)) Application.Run(context);
                    return 0;
                }
                catch (Exception ex) { Routing.Log(ex); MessageBox.Show(ex.Message, "VoiceMagic"); return 1; }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }
}
