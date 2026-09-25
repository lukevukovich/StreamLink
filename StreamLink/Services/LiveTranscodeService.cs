using System.ComponentModel;
using System.Diagnostics;

namespace StreamLink.Services;

// Converts a provider's continuous TS stream into browser-friendly H.264/AAC TS.
// FFmpeg receives the upstream stream on stdin; credentials are never passed on its command line.
public sealed class LiveTranscodeService(IStreamProxyService proxy, IConfiguration configuration, ILogger<LiveTranscodeService> logger)
{
    private static readonly SemaphoreSlim Slots = new(2);

    private static string? FindFfmpeg(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath)) return configuredPath;
        var executable = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var path = string.Join(Path.PathSeparator, new[]
        {
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User)
        }.Where(x => !string.IsNullOrEmpty(x)));
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim('"'), executable);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { }
        }
        return null;
    }

    public async Task StreamAsync(HttpContext context, string token)
    {
        var cancellationToken = context.RequestAborted;
        var ffmpeg = FindFfmpeg(configuration["FFmpeg:Path"]);
        if (ffmpeg is null)
        {
            logger.LogWarning("FFmpeg not found; set FFmpeg:Path or install FFmpeg and restart the app.");
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }
        if (!await Slots.WaitAsync(0, cancellationToken))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        try
        {
            await using var live = await proxy.GetLiveTsAsync(token, cancellationToken);
            if (live is null)
            {
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                return;
            }

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpeg,
                    Arguments = "-hide_banner -loglevel error -nostdin -fflags +genpts -i pipe:0 -map 0:v:0? -map 0:a:0? -c:v libx264 -preset superfast -crf 18 -maxrate 10000k -bufsize 20000k -pix_fmt yuv420p -c:a aac -b:a 192k -ar 48000 -ac 2 -muxdelay 0 -f mpegts pipe:1",
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            try { process.Start(); }
            catch (Win32Exception)
            {
                logger.LogWarning("FFmpeg is not available on the server PATH; transcoded playback is unavailable.");
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return;
            }

            using var registration = cancellationToken.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            });
            var errors = process.StandardError.ReadToEndAsync();
            using var inputCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var input = FeedInputAsync(live.Response.Content, process.StandardInput.BaseStream, inputCancellation.Token);
            try
            {
                context.Response.ContentType = "video/mp2t";
                context.Response.Headers.CacheControl = "no-store";
                await process.StandardOutput.BaseStream.CopyToAsync(context.Response.Body, cancellationToken);
                if (!context.Response.HasStarted && !cancellationToken.IsCancellationRequested)
                    context.Response.StatusCode = StatusCodes.Status502BadGateway;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (IOException) when (cancellationToken.IsCancellationRequested) { }
            finally
            {
                inputCancellation.Cancel();
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                await process.WaitForExitAsync(CancellationToken.None);
                try { await input; }
                catch (OperationCanceledException) when (inputCancellation.IsCancellationRequested) { }
                var diagnostic = await errors;
                if (process.ExitCode != 0 && !cancellationToken.IsCancellationRequested)
                    logger.LogWarning("FFmpeg live transcode exited with code {ExitCode} ({ErrorType})", process.ExitCode, string.IsNullOrWhiteSpace(diagnostic) ? "no output" : "input or codec error");
            }
        }
        finally { Slots.Release(); }
    }

    private static async Task FeedInputAsync(HttpContent content, Stream destination, CancellationToken cancellationToken)
    {
        try
        {
            await using var source = await content.ReadAsStreamAsync(cancellationToken);
            await source.CopyToAsync(destination, cancellationToken);
        }
        catch (IOException) { /* FFmpeg closed stdin or the provider disconnected. */ }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally { await destination.DisposeAsync(); }
    }
}