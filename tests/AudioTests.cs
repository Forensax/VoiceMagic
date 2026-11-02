using System;
using System.Linq;
using NAudio.CoreAudioApi;
using NAudio.Wave;
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
    static int Main()
    {
        try
        {
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
