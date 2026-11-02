using System;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace VoiceMagic
{
    // Linked stereo limiter: one envelope per frame preserves the stereo image.
    public sealed class GainProvider : ISampleProvider
    {
        readonly ISampleProvider source;
        double envelope = 1, currentGain = 1;
        readonly double release, smoothing;
        public volatile float Gain = 2;
        public volatile bool Muted;
        public volatile float Peak;
        public volatile float InputPeak;
        public volatile bool Limiting;
        public WaveFormat WaveFormat { get { return source.WaveFormat; } }
        public GainProvider(ISampleProvider source)
        {
            this.source = source;
            release = Math.Exp(-1.0 / (source.WaveFormat.SampleRate * .12));
            smoothing = Math.Exp(-1.0 / (source.WaveFormat.SampleRate * .015));
        }
        public int Read(float[] buffer, int offset, int count)
        {
            int read = source.Read(buffer, offset, count), channels = WaveFormat.Channels;
            float target = Muted ? 0 : Math.Max(0, Math.Min(10, Gain));
            float peak = 0, inputPeak = 0; bool limiting = false;
            for (int i = offset; i < offset + read; i += channels)
            {
                currentGain = smoothing * currentGain + (1 - smoothing) * target;
                double framePeak = 0;
                for (int ch = 0; ch < channels && i + ch < offset + read; ch++)
                {
                    float sample = buffer[i + ch];
                    if (float.IsNaN(sample) || float.IsInfinity(sample)) sample = 0;
                    buffer[i + ch] = sample;
                    inputPeak = Math.Max(inputPeak, Math.Abs(sample));
                    framePeak = Math.Max(framePeak, Math.Abs(sample * currentGain));
                }
                double needed = framePeak > .94 ? .94 / framePeak : 1;
                envelope = needed < envelope ? needed : release * envelope + (1 - release) * needed;
                limiting |= envelope < .99;
                for (int ch = 0; ch < channels && i + ch < offset + read; ch++)
                {
                    float value = (float)(buffer[i + ch] * currentGain * envelope);
                    buffer[i + ch] = value;
                    peak = Math.Max(peak, Math.Abs(value));
                }
            }
            InputPeak = inputPeak; Peak = peak; Limiting = limiting;
            return read;
        }
    }

    public sealed class AudioEngine : IDisposable
    {
        WasapiCapture capture;
        WasapiOut output;
        BufferedWaveProvider buffer;
        MMDevice inputDevice, outputDevice;
        public GainProvider Gain;
        public volatile string Failure;
        volatile bool stopping;
        public void Start(string inputId, string outputId, float gain)
        {
            try
            {
                using (var devices = new MMDeviceEnumerator())
                {
                    inputDevice = devices.GetDevice(inputId);
                    outputDevice = devices.GetDevice(outputId);
                }
                capture = new WasapiCapture(inputDevice, true, 30);
                buffer = new BufferedWaveProvider(capture.WaveFormat) {
                    BufferDuration = TimeSpan.FromMilliseconds(400), DiscardOnBufferOverflow = true, ReadFully = true };
                Gain = new GainProvider(buffer.ToSampleProvider()) { Gain = gain };
                output = new WasapiOut(outputDevice, AudioClientShareMode.Shared, true, 60);
                output.Init(new SampleToWaveProvider(Gain));
                capture.DataAvailable += OnData;
                capture.RecordingStopped += delegate(object sender, StoppedEventArgs e) {
                    if (!stopping) Failure = e.Exception == null ? "输入设备已停止" : e.Exception.Message;
                };
                output.PlaybackStopped += delegate(object sender, StoppedEventArgs e) {
                    if (!stopping) Failure = e.Exception == null ? "输出设备已停止" : e.Exception.Message;
                };
                output.Play();
                capture.StartRecording();
            }
            catch { Dispose(); throw; }
        }
        void OnData(object sender, WaveInEventArgs e)
        {
            if (stopping) return;
            try
            {
                // Drop stale samples instead of letting latency accumulate indefinitely.
                if (buffer.BufferedDuration.TotalMilliseconds > 180) buffer.ClearBuffer();
                buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
            }
            catch (Exception ex) { Failure = ex.Message; }
        }
        public void Dispose()
        {
            stopping = true;
            try { if (capture != null) capture.Dispose(); }
            finally
            {
                try { if (output != null) output.Dispose(); }
                finally
                {
                    if (inputDevice != null) inputDevice.Dispose();
                    if (outputDevice != null) outputDevice.Dispose();
                    capture = null; output = null; inputDevice = null; outputDevice = null;
                }
            }
        }
    }
}
