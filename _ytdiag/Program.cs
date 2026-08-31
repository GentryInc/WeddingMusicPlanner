using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

var yt = new YoutubeClient();
try
{
    Console.WriteLine("Searching...");
    await foreach (var r in yt.Search.GetVideosAsync("Wild Love"))
    {
        Console.WriteLine($"HIT: {r.Id} {r.Title}");
        Console.WriteLine("Getting manifest...");
        var manifest = await yt.Videos.Streams.GetManifestAsync(r.Id);
        var audio = manifest.GetAudioOnlyStreams();
        foreach (var s in audio)
            Console.WriteLine($"  stream: {s.Container.Name} {s.Bitrate}");
        var mp4 = audio.Where(s => s.Container == Container.Mp4).OrderByDescending(s => s.Bitrate).FirstOrDefault();
        Console.WriteLine(mp4 is null ? "NO MP4 AUDIO" : $"MP4 chosen: {mp4.Bitrate}");
        if (mp4 is not null)
        {
            Console.WriteLine("Downloading...");
            await yt.Videos.Streams.DownloadAsync(mp4, "test.m4a");
            var fi = new FileInfo("test.m4a");
            Console.WriteLine($"Downloaded {fi.Length} bytes");
        }
        break;
    }
    Console.WriteLine("DONE OK");
}
catch (Exception ex)
{
    Console.WriteLine("ERROR: " + ex);
}
