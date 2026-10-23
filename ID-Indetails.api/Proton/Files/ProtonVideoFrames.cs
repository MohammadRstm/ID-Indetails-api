using System.Diagnostics;
using System.Globalization;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Proton.Tools;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Proton.Files;

/// <summary>Pictures taken evenly through a video, oldest first, and how long it is (null when ffprobe couldn't say).</summary>
public sealed record VideoFrames(IReadOnlyList<byte[]> Pictures, double? Duration);

/// <summary>
/// Takes pictures through a video with ffmpeg, so the model can see what it shows (it can't take a video, and the sound
/// isn't read). The file is someone's upload, so ffmpeg is held in: only MP4/MOV, Matroska/WebM and AVI files, known by
/// their first bytes, read with that one format forced, local files only (a playlist or a reference inside can't reach
/// anything else), a time limit, and a temporary folder that is always deleted.
/// </summary>
public sealed class ProtonVideoFrames(IOptions<ProtonOptions> options, ILogger<ProtonVideoFrames> log)
{
    /// <summary>The longest side of each picture: enough to see what a video shows, at a few hundred tokens each.</summary>
    private const int Edge = 768;

    private ProtonFileOptions Limits => options.Value.Files;

    /// <summary>The format ffmpeg is told the file is, from its first bytes; null for anything else.</summary>
    public static string? FormatOf(ReadOnlySpan<byte> start)
    {
        if (start.Length >= 12 && start[4..8].SequenceEqual("ftyp"u8))
        {
            return "mov";
        }

        if (start.Length >= 4 && start[..4].SequenceEqual(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }))
        {
            return "matroska";
        }

        return start.Length >= 12 && start[..4].SequenceEqual("RIFF"u8) && start[8..12].SequenceEqual("AVI "u8) ? "avi" : null;
    }

    public async Task<VideoFrames> TakeAsync(byte[] video, string fileName, int count, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Limits.FfmpegPath))
        {
            throw new ProtonToolInputException("Reading videos isn't set up on this server.");
        }

        var format = FormatOf(video) ?? throw new ProtonToolInputException($"{fileName} isn't a video Proton can read (MP4, MOV, WebM, MKV or AVI).");
        var folder = Directory.CreateTempSubdirectory("proton-video-");
        try
        {
            var input = Path.Combine(folder.FullName, "in");
            await File.WriteAllBytesAsync(input, video, cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Limits.VideoTimeoutSeconds));

            var duration = await DurationAsync(input, format, timeout.Token);
            // evenly through the video; without its length, one a second from the start
            var rate = duration is > 0 ? (count / duration.Value).ToString("0.######", CultureInfo.InvariantCulture) : "1";
            var (exit, error) = await RunAsync(Limits.FfmpegPath,
            [
                "-nostdin", "-hide_banner", "-loglevel", "error", "-protocol_whitelist", "file", "-f", format, "-i", input,
                "-an", "-sn", "-dn", "-vf", $"fps={rate},scale='min({Edge},iw)':'min({Edge},ih)':force_original_aspect_ratio=decrease",
                "-frames:v", count.ToString(CultureInfo.InvariantCulture), "-q:v", "5", Path.Combine(folder.FullName, "frame-%03d.jpg"),
            ], timeout.Token);
            var pictures = folder.GetFiles("frame-*.jpg").OrderBy(f => f.Name, StringComparer.Ordinal).Take(count)
                .Select(f => File.ReadAllBytes(f.FullName)).ToList();
            if (pictures.Count == 0)
            {
                log.LogWarning("ffmpeg took no pictures from a {Format} video (exit {Exit}): {Error}", format, exit, error);
                throw new ProtonToolInputException($"{fileName} couldn't be read: it may be damaged, or use a kind of video this server can't open.");
            }

            return new VideoFrames(pictures, duration);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ProtonToolInputException($"{fileName} took too long to read.");
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            log.LogWarning(exception, "ffmpeg or ffprobe couldn't be started.");
            throw new ProtonToolInputException("Reading videos isn't set up on this server.");
        }
        finally
        {
            try
            {
                folder.Delete(recursive: true);
            }
            catch (IOException exception)
            {
                log.LogWarning(exception, "A video's temporary folder couldn't be deleted.");
            }
        }
    }

    private async Task<double?> DurationAsync(string input, string format, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Limits.FfprobePath))
        {
            return null;
        }

        var (exit, output) = await RunAsync(Limits.FfprobePath,
            ["-v", "error", "-protocol_whitelist", "file", "-f", format, "-show_entries", "format=duration", "-of", "csv=p=0", input], cancellationToken, stdout: true);
        return exit == 0 && double.TryParse(output.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0 ? seconds : null;
    }

    /// <summary>Runs a program with its arguments as they are (no shell), and kills it when the time is up.</summary>
    private static async Task<(int Exit, string Output)> RunAsync(string program, IReadOnlyList<string> arguments, CancellationToken cancellationToken, bool stdout = false)
    {
        var start = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{program} didn't start.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return (process.ExitCode, stdout ? await output : Shorten(await error));
    }

    private static string Shorten(string text) => text.Length > 500 ? text[..500] : text;
}
