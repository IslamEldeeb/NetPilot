using System.Globalization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;
using NetPilot.Core.Backup;
using NetPilot.Core.Usage;

namespace NetPilot.Web;

public sealed class BackupOptions
{
    public string? DataDirectory { get; set; }
}

public static class BackupEndpoints
{
    private static readonly SemaphoreSlim DownloadGate = new(1, 1);

    public static IServiceCollection AddBackupEndpoints(this IServiceCollection services, IConfiguration configuration) =>
        services.Configure<BackupOptions>(configuration.GetSection("NetPilot:Backup"));

    public static void MapBackupEndpoints(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<BackupOptions>>().Value;
        var dataDir = options.DataDirectory
            ?? app.Configuration["NetPilot:DataDirectory"]
            ?? "../../data";
        app.MapPost("/api/backup/download", (Delegate)DownloadAsync);
        return;

        async Task<IResult> DownloadAsync(HttpContext context)
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!context.Request.HasFormContentType)
                return Results.Json(new { status = "Invalid", message = "Send the download request as a form post." },
                    statusCode: 400);

            try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException) { return Results.BadRequest(); }

            if (!await DownloadGate.WaitAsync(0, context.RequestAborted))
            {
                context.Response.Headers.RetryAfter = "30";
                return Results.Json(new { status = "Busy", message = "A backup download is already in progress. Try again in a moment." },
                    statusCode: 429);
            }

            FileStream? archiveFile = null;
            try
            {
                var clock = context.RequestServices.GetRequiredService<UsagePeriodClock>();
                var cairoTime = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), clock.TimeZone);
                var fileName = $"netpilot-backup-{cairoTime.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)}.zip";

                // Build into a seekable temp file first: ZipArchive writes synchronously (Kestrel forbids
                // that on the response body) and a failure must not leave a half-written download.
                archiveFile = new FileStream(Path.Combine(Path.GetTempPath(), $"netpilot-backup-{Guid.NewGuid():N}.zip"),
                    FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920,
                    FileOptions.DeleteOnClose | FileOptions.Asynchronous);
                var result = await BackupArchiveBuilder.BuildAsync(dataDir, archiveFile, context.RequestAborted);
                if (!result.Success)
                {
                    await archiveFile.DisposeAsync();
                    return Results.Problem(title: "Backup failed", detail: result.Error, statusCode: 500);
                }

                archiveFile.Seek(0, SeekOrigin.Begin);
                return Results.File(archiveFile, "application/zip", fileName); // stream is disposed (and deleted) by the result
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (archiveFile is not null) await archiveFile.DisposeAsync();
                app.Logger.LogError(ex, "Backup download failed");
                return Results.Problem(title: "Backup failed", detail: "NetPilot could not create the backup. Try again.", statusCode: 500);
            }
            finally
            {
                DownloadGate.Release();
            }
        }
    }
}
