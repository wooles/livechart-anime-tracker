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

// Services
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IAniListService, AniListService>();
builder.Services.AddSingleton<IKitsuService, KitsuService>();
builder.Services.AddSingleton<IMyAnimeListTenraiService, MyAnimeListTenraiService>();
builder.Services.AddSingleton<ICalendarExportService, CalendarExportService>();
builder.Services.AddSingleton<IAnimeAggregationService, AnimeAggregationService>();

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

// ==================== REST API ====================

// 1. Monthly Calendar of Watching Anime
app.MapGet("/api/calendar/month", async (string? platform, string? username, int? year, int? month, IAnimeAggregationService aggService) =>
{
    if (string.IsNullOrWhiteSpace(platform) || string.IsNullOrWhiteSpace(username))
    {
        return Results.BadRequest(new { error = "Wymagane parametry 'platform' oraz 'username'." });
    }

    int targetYear = year ?? DateTime.UtcNow.Year;
    int targetMonth = month ?? DateTime.UtcNow.Month;

    if (targetMonth < 1 || targetMonth > 12)
    {
        return Results.BadRequest(new { error = "Miesiąc musi być w przedziale 1-12." });
    }

    try
    {
        var calendar = await aggService.GetMonthlyCalendarAsync(platform, username, targetYear, targetMonth);
        return Results.Ok(calendar);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// 2. Export Calendar (.ics)
app.MapGet("/api/export/ics", async (string? platform, string? username, int? year, int? month, int? remindMinutes, IAnimeAggregationService aggService) =>
{
    if (string.IsNullOrWhiteSpace(platform) || string.IsNullOrWhiteSpace(username))
    {
        return Results.BadRequest(new { error = "Wymagane parametry 'platform' oraz 'username'." });
    }

    int targetYear = year ?? DateTime.UtcNow.Year;
    int targetMonth = month ?? DateTime.UtcNow.Month;

    try
    {
        var icsContent = await aggService.ExportCalendarIcsAsync(
            platform,
            username,
            targetYear,
            targetMonth,
            remindMinutes ?? 15);

        var bytes = System.Text.Encoding.UTF8.GetBytes(icsContent);
        return Results.File(bytes, "text/calendar", $"anime-kalendarz-{platform}-{username}-{targetYear}-{targetMonth:D2}.ics");
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// 3. Status
app.MapGet("/api/status", () =>
{
    return Results.Ok(new
    {
        status = "Healthy",
        version = "2.0.0",
        tenraiVersion = "3.1.0",
        type = "Monthly Anime Watching Calendar",
        supportedPlatforms = new[] { "AniList", "Kitsu", "MyAnimeList" },
        serverTime = DateTimeOffset.UtcNow
    });
});

var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
Console.WriteLine("====================================================================");
Console.WriteLine($"  📅 Anime Monthly Calendar (.NET 8 + Tenrai.Net) is running on port {port}!");
Console.WriteLine($"  🌐 URL: http://localhost:{port}");
Console.WriteLine("====================================================================");

app.Run($"http://0.0.0.0:{port}");

// Helper method for CLI Mode
static async Task RunCliModeAsync(IServiceProvider services, string[] args)
{
    var aggService = services.GetRequiredService<IAnimeAggregationService>();

    if (args.Contains("--help") || args.Contains("-h"))
    {
        Console.WriteLine("Anime Monthly Calendar - Opcje wiersza poleceń:");
        Console.WriteLine("  --calendar <platform> <user> [rok] [miesiac] : Wyświetla kalendarz miesiąca oglądanych anime");
        Console.WriteLine("  --export-ics <platform> <user> <plik>        : Eksportuje kalendarz do pliku .ics");
        Console.WriteLine("  --server                                     : Uruchamia serwer webowy");
        return;
    }

    int calIdx = Array.IndexOf(args, "--calendar");
    if (calIdx >= 0 && calIdx + 2 < args.Length)
    {
        string plat = args[calIdx + 1];
        string user = args[calIdx + 2];
        int yr = (calIdx + 3 < args.Length && int.TryParse(args[calIdx + 3], out var yVal)) ? yVal : DateTime.UtcNow.Year;
        int mo = (calIdx + 4 < args.Length && int.TryParse(args[calIdx + 4], out var mVal)) ? mVal : DateTime.UtcNow.Month;

        Console.WriteLine($"Pobieranie kalendarza oglądanych anime dla {user} ({plat}) na {mo:D2}/{yr}...");
        var cal = await aggService.GetMonthlyCalendarAsync(plat, user, yr, mo);

        Console.WriteLine($"\n📅 KALENDARZ: {cal.MonthNamePl.ToUpper()} {cal.Year} - {cal.Username} ({cal.Platform})");
        Console.WriteLine($"Oglądanych serii: {cal.TotalWatchingAnime} | Premiry odcinków w tym miesiącu: {cal.TotalEpisodesInMonth}\n");

        foreach (var day in cal.Days.Where(d => d.IsCurrentMonth && d.Episodes.Any()))
        {
            Console.WriteLine($"  [{day.DateString}] ({day.DayOfWeekPl}):");
            foreach (var ep in day.Episodes)
            {
                Console.WriteLine($"     ⏰ {ep.AiringTimeFormatted} | Odc. {ep.EpisodeNumber,-3} | {ep.DisplayTitle} (Twój postęp: {ep.UserProgress}/{ep.TotalEpisodes?.ToString() ?? "?"})");
            }
        }
        return;
    }

    int exportIdx = Array.IndexOf(args, "--export-ics");
    if (exportIdx >= 0 && exportIdx + 3 < args.Length)
    {
        string plat = args[exportIdx + 1];
        string user = args[exportIdx + 2];
        string outFile = args[exportIdx + 3];
        int yr = DateTime.UtcNow.Year;
        int mo = DateTime.UtcNow.Month;

        Console.WriteLine($"Generowanie pliku .ics dla {user} ({plat})...");
        var ics = await aggService.ExportCalendarIcsAsync(plat, user, yr, mo);
        await File.WriteAllTextAsync(outFile, ics);
        Console.WriteLine($"Zapisano do: {outFile}");
        return;
    }
}
