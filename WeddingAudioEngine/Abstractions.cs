using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace WeddingAudioEngine;

/// <summary>
/// Creates <see cref="IWavePlayer"/> instances for a deck role.
/// Abstracted so unit tests can inject fakes instead of real WASAPI endpoints.
/// </summary>
public interface IWavePlayerFactory
{
    IWavePlayer Create(DeckRole role);
}

/// <summary>
/// A disposable, seekable audio source. Abstracted so tests can synthesize
/// audio without touching the filesystem.
/// </summary>
public interface IAudioSource : IDisposable
{
    ISampleProvider SampleProvider { get; }
    WaveFormat WaveFormat { get; }
    TimeSpan TotalTime { get; }
}

public interface IAudioSourceFactory
{
    IAudioSource Open(string path);
}

/// <summary>Production WASAPI factory: Master → default render endpoint, Cue → a chosen secondary device.</summary>
public sealed class WasapiPlayerFactory : IWavePlayerFactory
{
    private readonly string? _cueDeviceId;

    /// <param name="cueDeviceId">
    /// The <see cref="MMDevice.ID"/> of the headphone/PFL device.
    /// When null, the cue deck falls back to the default device.
    /// </param>
    public WasapiPlayerFactory(string? cueDeviceId = null) => _cueDeviceId = cueDeviceId;

    public static IReadOnlyList<(string Id, string FriendlyName)> EnumerateRenderDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                         .Select(d => (d.ID, d.FriendlyName))
                         .ToList();
    }

    public IWavePlayer Create(DeckRole role)
    {
        using var enumerator = new MMDeviceEnumerator();

        MMDevice device = role == DeckRole.Cue && _cueDeviceId is not null
            ? enumerator.GetDevice(_cueDeviceId)
            : enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

        // Shared mode, event-driven, 100 ms latency — safe default for playback decks.
        return new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: true, latency: 100);
    }
}

/// <summary>Production source factory backed by <see cref="AudioFileReader"/>.</summary>
public sealed class AudioFileSourceFactory : IAudioSourceFactory
{
    // MF_E_UNSUPPORTED_BYTESTREAM_TYPE: Media Foundation could not recognize the
    // container/codec of the source (unsupported format, or a corrupt/mislabeled file).
    private const uint MF_E_UNSUPPORTED_BYTESTREAM_TYPE = 0xC00D36C4;

    public IAudioSource Open(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("No audio source path was resolved.", nameof(path));

        // AudioFileReader expects a local file path, not a remote URL. A non-file URI
        // would be routed to MediaFoundationReader and fail with an opaque COMException.
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && !uri.IsFile)
            throw new NotSupportedException(
                $"Cannot open remote source '{path}'. Ensure the track is cached locally before playback.");

        if (!File.Exists(path))
            throw new FileNotFoundException("Audio file not found.", path);

        try
        {
            return new AudioFileSource(path);
        }
        catch (COMException ex) when ((uint)ex.HResult == MF_E_UNSUPPORTED_BYTESTREAM_TYPE)
        {
            throw new NotSupportedException(
                $"The audio format of '{path}' is not supported. The file may be an unsupported " +
                "codec/container or a corrupt/incomplete download.", ex);
        }
    }

    private sealed class AudioFileSource : IAudioSource
    {
        private readonly AudioFileReader _reader;
        public AudioFileSource(string path) => _reader = new AudioFileReader(path);

        public ISampleProvider SampleProvider => _reader;
        public WaveFormat WaveFormat => _reader.WaveFormat;
        public TimeSpan TotalTime => _reader.TotalTime;
        public void Dispose() => _reader.Dispose();
    }
}
