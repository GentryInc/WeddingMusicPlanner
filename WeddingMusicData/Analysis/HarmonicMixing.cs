using System.Text.RegularExpressions;

namespace WeddingMusicData.Analysis;

/// <summary>
/// Utilities for DJ-style harmonic ("Camelot wheel") mixing. Converts musical
/// keys such as "Am", "F#", "8B" or "C minor" into Camelot codes and determines
/// which keys make a harmonically smooth transition.
/// </summary>
/// <remarks>
/// The Camelot wheel numbers keys 1–12 around a circle with a letter suffix:
/// 'A' = minor, 'B' = major. Two keys mix well when they are the same code,
/// share the number (relative major/minor), or are ±1 on the wheel with the
/// same letter. Enharmonic spellings (e.g. C#/Db) are normalised.
/// </remarks>
public static partial class HarmonicMixing
{
    // Maps a normalised "<pitch><A|B>" identity to its Camelot code.
    // A = minor, B = major.
    private static readonly Dictionary<string, string> ToCamelot = new(StringComparer.Ordinal)
    {
        // Minor keys (A)
        ["G#m"] = "1A",  ["D#m"] = "2A",  ["A#m"] = "3A",  ["Fm"]  = "4A",
        ["Cm"]  = "5A",  ["Gm"]  = "6A",  ["Dm"]  = "7A",  ["Am"]  = "8A",
        ["Em"]  = "9A",  ["Bm"]  = "10A", ["F#m"] = "11A", ["C#m"] = "12A",
        // Major keys (B)
        ["B"]   = "1B",  ["F#"]  = "2B",  ["C#"]  = "3B",  ["G#"]  = "4B",
        ["D#"]  = "5B",  ["A#"]  = "6B",  ["F"]   = "7B",  ["C"]   = "8B",
        ["G"]   = "9B",  ["D"]   = "10B", ["A"]   = "11B", ["E"]   = "12B",
    };

    // Enharmonic normalisation of flats/uncommon spellings to the sharps used above.
    private static readonly Dictionary<string, string> EnharmonicPitch = new(StringComparer.Ordinal)
    {
        ["Ab"] = "G#", ["Bb"] = "A#", ["Cb"] = "B",  ["Db"] = "C#",
        ["Eb"] = "D#", ["Fb"] = "E",  ["Gb"] = "F#", ["E#"] = "F", ["B#"] = "C",
    };

    [GeneratedRegex(@"^\s*(?<pitch>[A-Ga-g](?:#|b|♯|♭)?)\s*(?<qual>m|min|minor|maj|major)?\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();

    [GeneratedRegex(@"^\s*(?<num>[1-9]|1[0-2])\s*(?<let>[ABab])\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex CamelotPattern();

    /// <summary>
    /// Parses an arbitrary key string into a Camelot code (e.g. "8A"), or null
    /// when the key cannot be interpreted. Already-Camelot inputs pass through.
    /// </summary>
    public static string? ToCamelotCode(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        var trimmed = key.Trim();

        // Already a Camelot code?
        var cam = CamelotPattern().Match(trimmed);
        if (cam.Success)
            return cam.Groups["num"].Value + char.ToUpperInvariant(cam.Groups["let"].Value[0]);

        var m = KeyPattern().Match(trimmed);
        if (!m.Success) return null;

        var pitch = NormalizePitch(m.Groups["pitch"].Value);
        var qual = m.Groups["qual"].Value.ToLowerInvariant();
        bool isMinor = qual is "m" or "min" or "minor";

        var identity = isMinor ? pitch + "m" : pitch;
        return ToCamelot.TryGetValue(identity, out var code) ? code : null;
    }

    /// <summary>
    /// Returns true when two keys make a harmonically compatible transition:
    /// identical code, relative major/minor (same number), or an adjacent
    /// number (±1, wrapping) with the same letter.
    /// </summary>
    public static bool AreCompatible(string? keyA, string? keyB)
    {
        var a = ToCamelotCode(keyA);
        var b = ToCamelotCode(keyB);
        if (a is null || b is null) return false;
        if (a == b) return true;

        var (numA, letA) = SplitCode(a);
        var (numB, letB) = SplitCode(b);

        // Relative major/minor: same number, different letter.
        if (numA == numB) return true;

        // Adjacent on the wheel with same letter (±1, wrapping 1..12).
        if (letA == letB)
        {
            int diff = Math.Abs(numA - numB);
            return diff == 1 || diff == 11;
        }

        return false;
    }

    /// <summary>
    /// Enumerates the Camelot codes considered harmonically compatible with the
    /// given key (inclusive of the key itself). Empty when the key is unparseable.
    /// </summary>
    public static IReadOnlyList<string> CompatibleCodes(string? key)
    {
        var code = ToCamelotCode(key);
        if (code is null) return Array.Empty<string>();

        var (num, let) = SplitCode(code);
        char other = let == 'A' ? 'B' : 'A';

        return new[]
        {
            code,                                   // same key
            $"{num}{other}",                        // relative major/minor
            $"{Wrap(num - 1)}{let}",                // one step down the wheel
            $"{Wrap(num + 1)}{let}",                // one step up the wheel
        };
    }

    private static string NormalizePitch(string pitch)
    {
        // Capitalise the note letter and normalise unicode accidentals to ASCII.
        var note = char.ToUpperInvariant(pitch[0]).ToString();
        var accidental = pitch.Length > 1
            ? pitch[1] switch { '♯' => "#", '♭' => "b", var c => c.ToString() }
            : string.Empty;

        var raw = note + accidental;
        return EnharmonicPitch.TryGetValue(raw, out var norm) ? norm : raw;
    }

    private static (int Num, char Let) SplitCode(string code)
    {
        char let = char.ToUpperInvariant(code[^1]);
        int num = int.Parse(code[..^1]);
        return (num, let);
    }

    // Wrap a wheel position into the 1..12 range.
    private static int Wrap(int n) => ((n - 1 + 12) % 12) + 1;
}
