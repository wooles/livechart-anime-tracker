using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using LiveChartTracker.Models;

namespace LiveChartTracker.Services
{
    public interface IAnimeAggregationService
    {
        Task<MonthlyCalendarResponse> GetMonthlyCalendarAsync(string platform, string username, int year, int month, bool refresh = false);
        Task<string> ExportCalendarIcsAsync(string platform, string username, int year, int month, int reminderMinutes = 15);
    }

    public class AnimeAggregationService : IAnimeAggregationService
    {
        private readonly IAniListService _aniListService;
        private readonly IKitsuService _kitsuService;
        private readonly IMyAnimeListTenraiService _malTenraiService;
        private readonly ICalendarExportService _calendarExportService;

        private static readonly ConcurrentDictionary<string, (DateTimeOffset cachedAt, MonthlyCalendarResponse data)> _cache = new();

        private static readonly string[] MonthNamesPl = new[]
        {
            "", "Styczeń", "Luty", "Marzec", "Kwiecień", "Maj", "Czerwiec",
            "Lipiec", "Sierpień", "Wrzesień", "Październik", "Listopad", "Grudzień"
        };

        private static readonly string[] DayNamesPl = new[]
        {
            "Niedziela", "Poniedziałek", "Wtorek", "Środa", "Czwartek", "Piątek", "Sobota"
        };

        public AnimeAggregationService(
            IAniListService aniListService,
            IKitsuService kitsuService,
            IMyAnimeListTenraiService malTenraiService,
            ICalendarExportService calendarExportService)
        {
            _aniListService = aniListService;
            _kitsuService = kitsuService;
            _malTenraiService = malTenraiService;
            _calendarExportService = calendarExportService;
        }

        public async Task<MonthlyCalendarResponse> GetMonthlyCalendarAsync(string platform, string username, int year, int month, bool refresh = false)
        {
            var cacheKey = $"{platform.ToLowerInvariant()}_{username.ToLowerInvariant()}_{year}_{month}";
            if (!refresh && _cache.TryGetValue(cacheKey, out var cached) && DateTimeOffset.UtcNow - cached.cachedAt < TimeSpan.FromMinutes(5))
            {
                return cached.data;
            }

            // 1. Fetch Watching Episodes from selected platform
            string? avatarUrl;
            List<CalendarMonthEpisode> episodes;
            int totalWatching;

            switch (platform.ToLowerInvariant())
            {
                case "anilist":
                    (avatarUrl, episodes, totalWatching) = await _aniListService.GetWatchingMonthEpisodesAsync(username, year, month);
                    break;
                case "kitsu":
                    (avatarUrl, episodes, totalWatching) = await _kitsuService.GetWatchingMonthEpisodesAsync(username, year, month);
                    break;
                case "myanimelist":
                case "mal":
                    (avatarUrl, episodes, totalWatching) = await _malTenraiService.GetWatchingMonthEpisodesAsync(username, year, month);
                    break;
                default:
                    throw new ArgumentException($"Nieobsługiwana platforma: {platform}. Dostępne: AniList, Kitsu, MyAnimeList");
            }

            // Group episodes by Date (YYYY-MM-DD)
            var episodesByDate = episodes
                .GroupBy(e => e.AiringDateFormatted)
                .ToDictionary(g => g.Key, g => g.OrderBy(e => e.AiringAt).ToList());

            // 2. Build 7-column Calendar Matrix (starting on Monday)
            var daysList = new List<CalendarDay>();
            var todayStr = DateTime.UtcNow.ToString("yyyy-MM-dd");

            var firstDayOfMonth = new DateTime(year, month, 1);
            int daysInMonth = DateTime.DaysInMonth(year, month);
            var lastDayOfMonth = new DateTime(year, month, daysInMonth);

            // In .NET: Sunday = 0, Monday = 1, Tuesday = 2 ... Saturday = 6
            // We want Monday = 1, Sunday = 7
            int firstDayOfWeekNum = ((int)firstDayOfMonth.DayOfWeek == 0) ? 7 : (int)firstDayOfMonth.DayOfWeek;
            int prevMonthPadding = firstDayOfWeekNum - 1; // days to prepend from previous month

            // Prepend previous month days
            if (prevMonthPadding > 0)
            {
                var prevMonthDate = firstDayOfMonth.AddDays(-prevMonthPadding);
                for (int i = 0; i < prevMonthPadding; i++)
                {
                    var d = prevMonthDate.AddDays(i);
                    var dStr = d.ToString("yyyy-MM-dd");
                    daysList.Add(new CalendarDay
                    {
                        DateString = dStr,
                        DayNumber = d.Day,
                        DayOfWeek = d.DayOfWeek.ToString(),
                        DayOfWeekPl = DayNamesPl[(int)d.DayOfWeek],
                        IsCurrentMonth = false,
                        IsToday = (dStr == todayStr),
                        Episodes = episodesByDate.TryGetValue(dStr, out var eps) ? eps : new List<CalendarMonthEpisode>()
                    });
                }
            }

            // Current month days
            for (int day = 1; day <= daysInMonth; day++)
            {
                var d = new DateTime(year, month, day);
                var dStr = d.ToString("yyyy-MM-dd");
                daysList.Add(new CalendarDay
                {
                    DateString = dStr,
                    DayNumber = day,
                    DayOfWeek = d.DayOfWeek.ToString(),
                    DayOfWeekPl = DayNamesPl[(int)d.DayOfWeek],
                    IsCurrentMonth = true,
                    IsToday = (dStr == todayStr),
                    Episodes = episodesByDate.TryGetValue(dStr, out var eps) ? eps : new List<CalendarMonthEpisode>()
                });
            }

            // Append next month days to complete rows (multiples of 7: 35 or 42 cells)
            int totalGridCells = daysList.Count <= 35 ? 35 : 42;
            int nextMonthPadding = totalGridCells - daysList.Count;

            for (int i = 1; i <= nextMonthPadding; i++)
            {
                var d = lastDayOfMonth.AddDays(i);
                var dStr = d.ToString("yyyy-MM-dd");
                daysList.Add(new CalendarDay
                {
                    DateString = dStr,
                    DayNumber = d.Day,
                    DayOfWeek = d.DayOfWeek.ToString(),
                    DayOfWeekPl = DayNamesPl[(int)d.DayOfWeek],
                    IsCurrentMonth = false,
                    IsToday = (dStr == todayStr),
                    Episodes = episodesByDate.TryGetValue(dStr, out var eps) ? eps : new List<CalendarMonthEpisode>()
                });
            }

            var response = new MonthlyCalendarResponse
            {
                Year = year,
                Month = month,
                MonthName = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month),
                MonthNamePl = MonthNamesPl[month],
                Platform = platform,
                Username = username,
                AvatarUrl = avatarUrl,
                TotalWatchingAnime = totalWatching,
                TotalEpisodesInMonth = episodes.Count,
                Days = daysList
            };

            _cache[cacheKey] = (DateTimeOffset.UtcNow, response);
            return response;
        }

        public async Task<string> ExportCalendarIcsAsync(string platform, string username, int year, int month, int reminderMinutes = 15)
        {
            var calendar = await GetMonthlyCalendarAsync(platform, username, year, month);
            var allEpisodes = calendar.Days.SelectMany(d => d.Episodes).ToList();

            return _calendarExportService.GenerateIcsCalendar(
                allEpisodes,
                $"Harmonogram Oglądanych Anime - {username} ({calendar.MonthNamePl} {year})",
                reminderMinutes);
        }
    }
}
