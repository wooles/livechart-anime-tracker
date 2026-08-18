using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using LiveChartTracker.Models;
using LiveChartTracker.Services;

var builder = WebApplication.CreateBuilder(args);

// Register Services
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IAniListService, AniListService>();
builder.Services.AddSingleton<IKitsuService, KitsuService>();
builder.Services.AddSingleton<IMyAnimeListTenraiService, MyAnimeListTenraiService>();
builder.Services.AddSingleton<ICalendarExportService, CalendarExportService>();
builder.Services.AddSingleton<IAnimeAggregationService, AnimeAggregationService>();

// CORS for development flexibility
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

var app = builder.Build();

// CLI Mode Check
if (args.Length > 0 && !args.Contains("--server"))
{
    await RunCliModeAsync(app.Services, args);
    return;
}

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

// ==================== REST API ENDPOINTS ====================

// 1. Weekly Schedule Endpoint
app.MapGet("/api/schedule", async (string? platform, string? username, IAnimeAggregationService aggService) =>
{
    try
    {
        var schedule = await aggService.GetWeeklyScheduleAsync(platform, username);
        return Results.Ok(schedule);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// 2. Seasonal Anime Endpoint
app.MapGet("/api/seasonal", async (string? season, int? year, string? platform, string? username, IAnimeAggregationService aggService) =>
{
    try
    {
        var targetSeason = string.IsNullOrWhiteSpace(season) ? GetCurrentSeasonName() : season;
        var targetYear = year ?? DateTime.UtcNow.Year;
        var seasonal = await aggService.GetSeasonalAnimeAsync(targetSeason, targetYear, platform, username);
        return Results.Ok(seasonal);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// 3. User Anime List (AniList / Kitsu / MyAnimeList)
app.MapGet("/api/user/{platform}/{username}", async (string platform, string username, IAnimeAggregationService aggService) =>
{
    try
    {
        var userList = await aggService.GetUserAnimeListAsync(platform, username);
        return Results.Ok(userList);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// 4. Export Calendar (.ics)
app.MapGet("/api/export/ics", async (string platform, string username, bool? onlyWatching, int? remindMinutes, IAnimeAggregationService aggService) =>
{
    try
    {
        var icsContent = await aggService.ExportUserCalendarIcsAsync(
            platform,
            username,
            onlyWatching ?? true,
            remindMinutes ?? 15);

        var bytes = System.Text.Encoding.UTF8.GetBytes(icsContent);
        return Results.File(bytes, "text/calendar", $"anime-schedule-{platform}-{username}.ics");
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// 5. System Status
app.MapGet("/api/status", () =>
{
    return Results.Ok(new
    {
        status = "Healthy",
        version = "1.0.0",
        tenraiVersion = "3.1.0",
        supportedPlatforms = new[] { "AniList", "Kitsu", "MyAnimeList" },
        features = new[] { "Weekly Schedule", "Seasonal Charts", "Live Countdowns", "User Sync", "iCal Export" },
        serverTime = DateTimeOffset.UtcNow
    });
});

Console.WriteLine("====================================================================");
Console.WriteLine("  🌸 LiveChart Anime Tracker (.NET 8 + Tenrai.Net) is running!");
Console.WriteLine("  🌐 Otwórz w przeglądarce: http://localhost:5000");
Console.WriteLine("====================================================================");

app.Run("http://0.0.0.0:5000");

// Helper method for CLI Mode
static async Task RunCliModeAsync(IServiceProvider services, string[] args)
{
    var aggService = services.GetRequiredService<IAnimeAggregationService>();

    if (args.Contains("--help") || args.Contains("-h"))
    {
        Console.WriteLine("LiveChart Anime Tracker - Opcje wiersza poleceń:");
        Console.WriteLine("  --schedule                              : Wyświetla harmonogram tygodniowy");
        Console.WriteLine("  --user <platform> <username>            : Wyświetla oglądane anime użytkownika");
        Console.WriteLine("  --export-ics <platform> <user> <file>   : Eksportuje harmonogram do pliku .ics");
        Console.WriteLine("  --server                                : Uruchamia serwer webowy (domyślnie)");
        return;
    }

    if (args.Contains("--schedule"))
    {
        Console.WriteLine("Pobieranie harmonogramu tygodniowego...");
        var sched = await aggService.GetWeeklyScheduleAsync();
        foreach (var day in sched.Schedule)
        {
            Console.WriteLine($"\n=== {day.DayPl.ToUpper()} ({day.Day}) - {day.AnimeList.Count} serii ===");
            foreach (var a in day.AnimeList)
            {
                var time = a.NextAiringEpisode?.FormattedTime ?? "--:--";
                var ep = a.NextAiringEpisode?.Episode.ToString() ?? "?";
                Console.WriteLine($"  [{time}] Odc. {ep,-3} | {a.DisplayTitle} (Ocena: {a.AverageScore:F0}/100)");
            }
        }
        return;
    }

    int userIdx = Array.IndexOf(args, "--user");
    if (userIdx >= 0 && userIdx + 2 < args.Length)
    {
        string plat = args[userIdx + 1];
        string user = args[userIdx + 2];
        Console.WriteLine($"Pobieranie listy użytkownika {user} z platformy {plat}...");
        var list = await aggService.GetUserAnimeListAsync(plat, user);
        Console.WriteLine($"Znaleziono {list.TotalEntries} wpisów (Oglądane: {list.Watching.Count}, Planowane: {list.Planning.Count}, Ukończone: {list.Completed.Count})");
        Console.WriteLine("\n--- AKTUALNIE OGLĄDANE (WATCHING) ---");
        foreach (var a in list.Watching)
        {
            Console.WriteLine($"  * {a.DisplayTitle} [Postęp: {a.UserProgress}/{a.Episodes?.ToString() ?? "?"}] (Format: {a.Format})");
        }
        return;
    }

    int exportIdx = Array.IndexOf(args, "--export-ics");
    if (exportIdx >= 0 && exportIdx + 3 < args.Length)
    {
        string plat = args[exportIdx + 1];
        string user = args[exportIdx + 2];
        string outFile = args[exportIdx + 3];
        Console.WriteLine($"Generowanie kalendarza .ics dla {user} ({plat})...");
        var ics = await aggService.ExportUserCalendarIcsAsync(plat, user);
        await File.WriteAllTextAsync(outFile, ics);
        Console.WriteLine($"Zapisano plik kalendarza do: {outFile}");
        return;
    }
}

static string GetCurrentSeasonName()
{
    int month = DateTime.UtcNow.Month;
    return month switch
    {
        12 or 1 or 2 => "WINTER",
        3 or 4 or 5 => "SPRING",
        6 or 7 or 8 => "SUMMER",
        9 or 10 or 11 => "FALL",
        _ => "WINTER"
    };
}
