using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace WeddingAudioEngine;

/// <summary>
/// DJ microphone talkover. Captures a WASAPI input device and renders it live
/// to the default output (the same front-of-house device the master deck uses;
/// Windows mixes the two shared-mode streams). While the mic is live the music
/// is "ducked": the caller-provided duck action lowers the master deck gain and
/// restores it when the mic is turned off.
/// </summary>
public sealed class MicrophoneInput : IDisposable
{
    private WasapiCapture? _capture;
    private WasapiOut? _output;
    private BufferedWaveProvider? _buffer;
    private VolumeSampleProvider? _volume;
    private bool _disposed;

    private float _micGain = 1.0f;

    /// <summary>Mic gain, 0.0 .. 2.0 (values over 1 boost quiet mics).</summary>
    public float Gain
    {
        get => _micGain;
        set
        {
            _micGain = Math.Clamp(value, 0f, 2f);
            var v = _volume;
            if (v is not null) v.Volume = _micGain;
        }
    }

    /// <summary>True while the microphone is being captured and rendered.</summary>
    public bool IsLive { get; private set; }

    /// <summary>Raised when live state changes (UI badge, duck coordination).</summary>
    public event EventHandler<bool>? LiveChanged;

    /// <summary>Active capture (input) devices for the settings/toolbar picker.</summary>
    public static IReadOnlyList<(string Id, string FriendlyName)> EnumerateCaptureDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                         .Select(d => (d.ID, d.FriendlyName))
                         .ToList();
    }

    /// <summary>
    /// Starts capturing the given input device (null = default microphone) and
    /// playing it through the default render device.
    /// </summary>
    public void Start(string? captureDeviceId = null)
    {
        if (IsLive) return;
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var enumerator = new MMDeviceEnumerator();
        var device = captureDeviceId is not null
            ? enumerator.GetDevice(captureDeviceId)
            : enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);

        // Low-latency event-driven capture in the device's mix format.
        _capture = new WasapiCapture(device) { ShareMode = AudioClientShareMode.Shared };
        _buffer = new BufferedWaveProvider(_capture.WaveFormat)
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(2)
        };
        _capture.DataAvailable += (_, e) => _buffer?.AddSamples(e.Buffer, 0, e.BytesRecorded);

        _volume = new VolumeSampleProvider(_buffer.ToSampleProvider()) { Volume = _micGain };

        var render = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        _output = new WasapiOut(render, AudioClientShareMode.Shared, useEventSync: true, latency: 50);
        _output.Init(_volume);

        _capture.StartRecording();
        _output.Play();
        IsLive = true;
        LiveChanged?.Invoke(this, true);
    }

    /// <summary>Stops the mic and releases the capture/render streams.</summary>
    public void Stop()
    {
        if (!IsLive) return;
        IsLive = false;

        try { _capture?.StopRecording(); } catch { /* device may have vanished */ }
        try { _output?.Stop(); } catch { /* ignore */ }
        _capture?.Dispose();
        _output?.Dispose();
        _capture = null;
        _output = null;
        _buffer = null;
        _volume = null;
        LiveChanged?.Invoke(this, false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
