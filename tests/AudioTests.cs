using System;
using System.Linq;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VoiceMagic;

class AudioTests
{
    sealed class Signal : ISampleProvider
    {
        public WaveFormat WaveFormat { get { return WaveFormat.CreateIeeeFloatWaveFormat(48000, 2); } }
        public float Level = .05f;
        public int Read(float[] b, int o, int c) { for (int i = o; i < o + c; i += 2) { b[i] = Level; b[i + 1] = -Level / 2; } return c; }
    }
    static void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); }
    static float MeasurePeak(AudioEngine engine)
    {
        Thread.Sleep(400);
        float peak = 0;
        for (int i = 0; i < 100; i++) { peak = Math.Max(peak, engine.Gain.Peak); Thread.Sleep(5); }
        if (engine.Failure != null) throw new Exception(engine.Failure);
        return peak;
    }
    static void Integration()
    {
        using (var devs = new MMDeviceEnumerator())
        {
            string captureId = null;
            MMDevice cable = null;
            foreach (var device in devs.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                using (device) { if (device.FriendlyName.Contains("CABLE Output")) captureId = device.ID; }
            foreach (var device in devs.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                if (device.FriendlyName.Contains("CABLE Input")) cable = device; else device.Dispose();
            if (cable == null || captureId == null) throw new Exception("VB-CABLE is required for integration verification");
            using (cable)
            using (var destination = devs.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
            using (var source = new WasapiOut(cable, AudioClientShareMode.Shared, true, 60))
            {
                Check(!destination.FriendlyName.Contains("CABLE"), "system default remains a physical output");
                source.Init(new SampleToWaveProvider(new SignalGenerator(48000, 2) { Gain = .0001, Frequency = 735 }));
                source.Play();
                for (int round = 1; round <= 2; round++)
                    using (var engine = new AudioEngine())
                    {
                        engine.Start(captureId, destination.ID, 1);
                        float baseline = MeasurePeak(engine);
                        engine.Gain.Gain = 3;
                        float amplified = MeasurePeak(engine);
                        Console.WriteLine("ROUND " + round + " input=" + engine.Gain.InputPeak + " 100%=" + baseline + " 300%=" + amplified);
                        Check(baseline > .000001f, "live VB-CABLE capture has signal, round " + round);
                        Check(amplified / baseline > 2.8f && amplified / baseline < 3.2f, "live output reaches threefold gain, round " + round);
                    }
                source.Stop();
            }
        }
    }
    static int Main(string[] args)
    {
        try
        {
            if (args.Contains("--integration")) { Integration(); return 0; }
            if (args.Contains("--diagnose"))
            {
                foreach (var session in Routing.GetSessions())
                    Console.WriteLine("PID=" + session.ProcessId + " Active=" + session.Active + " Device=" + session.DeviceId);
                return 0;
            }
            var signal = new Signal(); var gain = new GainProvider(signal) { Gain = 3 }; var samples = new float[48000];
            gain.Read(samples, 0, samples.Length);
            Check(Math.Abs(samples[samples.Length - 2] - .15f) < .001, "300% amplifies quiet audio");
            Check(Math.Abs(samples[samples.Length - 1] / samples[samples.Length - 2] + .5f) < .001, "stereo ratio preserved");
            gain.Gain = 10; gain.Read(samples, 0, samples.Length);
            Check(Math.Abs(samples[samples.Length - 2] - .5f) < .001, "1000% amplifies quiet audio tenfold");
            gain.Gain = 100; gain.Read(samples, 0, samples.Length);
            Check(Math.Abs(samples[samples.Length - 2] - .5f) < .001, "gain above 1000% is capped");
            signal.Level = .9f; gain.Gain = 10; gain.Read(samples, 0, samples.Length);
            Check(samples.All(x => Math.Abs(x) <= .94001f) && gain.Limiting, "limiter contains sudden peaks");
            gain.Muted = true; gain.Read(samples, 0, samples.Length);
            Check(Math.Abs(samples[samples.Length - 2]) < .00001f, "mute fades to silence");
            gain.Muted = false; gain.Gain = 0; gain.Read(samples, 0, samples.Length);
            Check(Math.Abs(samples[samples.Length - 2]) < .00001f, "zero gain remains silent");
            signal.Level = float.NaN; gain.Gain = 2; gain.Read(samples, 0, samples.Length);
            Check(samples.All(x => !float.IsNaN(x) && !float.IsInfinity(x)), "invalid input does not propagate");
            var sessions = new[] {
                new RouteSession { ProcessId = 10, DeviceId = "cable", Active = false },
                new RouteSession { ProcessId = 10, DeviceId = "headphones", Active = true } };
            Check(Routing.Plan(sessions, "cable").SequenceEqual(new[] { 10 }), "stale cable session cannot hide a new headphone stream");
            sessions[0].Active = true; sessions[1].Active = false;
            Check(Routing.Plan(sessions, "cable").Length == 0, "inactive old headphone session does not trigger rerouting");
            sessions[1].Active = true;
            Check(Routing.Plan(sessions, "cable").SequenceEqual(new[] { 10 }), "simultaneous active streams outside cable are repaired");
            Check(Routing.Plan(new[] { sessions[1] }, "cable").SequenceEqual(new[] { 10 }), "restarted process is routed from its current session");
            Check(Routing.IsWeChatProcess("WeChatAppEx", @"C:\Tencent\xwechat\plugins\WeChatAppEx.exe"), "WeChat plugin is recognized");
            Check(!Routing.IsWeChatProcess("WeChatAppEx", @"C:\Tencent\QQMusic\runtime\WeChatAppEx.exe"), "QQMusic plugin is excluded");
            using (var devs = new MMDeviceEnumerator())
            {
                int renders = 0, cables = 0;
                foreach (var d in devs.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                    using (d) { renders++; Console.WriteLine("OUTPUT " + d.FriendlyName); }
                foreach (var d in devs.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                    using (d) { if (d.FriendlyName.Contains("CABLE Output")) cables++; }
                Check(renders > 0, "real Windows playback device enumeration");
                Console.WriteLine("CABLE_CAPTURE_DEVICES=" + cables);
            }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
