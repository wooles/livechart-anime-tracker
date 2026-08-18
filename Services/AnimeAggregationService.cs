using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LiveChartTracker.Models;

namespace LiveChartTracker.Services
{
    public interface IAnimeAggregationService
    {
        Task<WeeklyScheduleResponse> GetWeeklyScheduleAsync(string? userPlatform = null, string? username = null);
        Task<SeasonalAnimeResponse> GetSeasonalAnimeAsync(string season, int year, string? userPlatform = null, string? username = null);
        Task<UserAnimeListResponse> GetUserAnimeListAsync(string platform, string username);
        Task<string> ExportUserCalendarIcsAsync(string platform, string username, bool onlyWatching = true, int reminderMinutes = 15);
    }

    public class AnimeAggregationService : IAnimeAggregationService
    {
        private readonly IAniListService _aniListService;
        private readonly IKitsuService _kitsuService;
        private readonly IMyAnimeListTenraiService _malTenraiService;
        private readonly ICalendarExportService _calendarExportService;

        // In-memory cache for schedule to be snappy
        private static WeeklyScheduleResponse? _cachedSchedule;
        private static DateTimeOffset _cacheExpiry = DateTimeOffset.MinValue;
        private static readonly ConcurrentDictionary<string, UserAnimeListResponse> _userCache = new();

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

        public async Task<WeeklyScheduleResponse> GetWeeklyScheduleAsync(string? userPlatform = null, string? username = null)
        {
            // 1. Fetch base schedule
            WeeklyScheduleResponse baseSchedule;
            if (_cachedSchedule != null && DateTimeOffset.UtcNow < _cacheExpiry)
            {
                baseSchedule = _cachedSchedule;
            }
            else
            {
                baseSchedule = await FetchFreshWeeklyScheduleAsync();
                _cachedSchedule = baseSchedule;
                _cacheExpiry = DateTimeOffset.UtcNow.AddMinutes(15);
            }

            // If user sync is requested, filter and annotate
            if (!string.IsNullOrWhiteSpace(userPlatform) && !string.IsNullOrWhiteSpace(username))
            {
                var userList = await GetUserAnimeListAsync(userPlatform, username);
                return ApplyUserFilterToSchedule(baseSchedule, userList);
            }

            return baseSchedule;
        }

        public async Task<SeasonalAnimeResponse> GetSeasonalAnimeAsync(string season, int year, string? userPlatform = null, string? username = null)
        {
            var animeList = await _aniListService.GetSeasonalAnimeAsync(season, year);

            if (!string.IsNullOrWhiteSpace(userPlatform) && !string.IsNullOrWhiteSpace(username))
            {
                var userList = await GetUserAnimeListAsync(userPlatform, username);
                var allUserAnime = userList.Watching
                    .Concat(userList.Planning)
                    .Concat(userList.Completed)
                    .Concat(userList.Paused)
                    .Concat(userList.Dropped)
                    .ToList();

                foreach (var anime in animeList)
                {
                    var match = FindUserAnimeMatch(anime, allUserAnime);
                    if (match != null)
                    {
                        anime.UserStatus = match.UserStatus;
                        anime.UserProgress = match.UserProgress;
                        anime.UserScore = match.UserScore;
                        anime.UserPlatform = match.UserPlatform;
                    }
                }
            }

            return new SeasonalAnimeResponse
            {
                Season = season,
                Year = year,
                TotalAnime = animeList.Count,
                AnimeList = animeList
            };
        }

        public async Task<UserAnimeListResponse> GetUserAnimeListAsync(string platform, string username)
        {
            var cacheKey = $"{platform.ToLowerInvariant()}_{username.ToLowerInvariant()}";
            if (_userCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            UserAnimeListResponse response = platform.ToLowerInvariant() switch
            {
                "anilist" => await _aniListService.GetUserAnimeListAsync(username),
                "kitsu" => await _kitsuService.GetUserAnimeListAsync(username),
                "myanimelist" or "mal" => await _malTenraiService.GetUserAnimeListAsync(username),
                _ => throw new ArgumentException($"Nieznana platforma: {platform}. Dostępne: anilist, kitsu, myanimelist")
            };

            // Enrich user list with live next airing episodes from AniList schedule if available
            var schedule = await GetWeeklyScheduleAsync();
            var allScheduleAnime = schedule.Schedule.SelectMany(s => s.AnimeList).ToList();

            foreach (var uAnime in response.Watching.Concat(response.Planning))
            {
                var match = FindUserAnimeMatch(uAnime, allScheduleAnime);
                if (match?.NextAiringEpisode != null)
                {
                    uAnime.NextAiringEpisode = match.NextAiringEpisode;
                }
            }

            _userCache[cacheKey] = response;
            return response;
        }

        public async Task<string> ExportUserCalendarIcsAsync(string platform, string username, bool onlyWatching = true, int reminderMinutes = 15)
        {
            var userList = await GetUserAnimeListAsync(platform, username);
            var exportAnime = onlyWatching ? userList.Watching : userList.Watching.Concat(userList.Planning).ToList();

            return _calendarExportService.GenerateIcsCalendar(
                exportAnime,
                $"Harmonogram Anime - {username} ({platform})",
                reminderMinutes);
        }

        private async Task<WeeklyScheduleResponse> FetchFreshWeeklyScheduleAsync()
        {
            var now = DateTimeOffset.UtcNow;
            var startOfWeek = now.AddDays(-1); // include yesterday to cover all timezones
            var endOfWeek = now.AddDays(7);

            var animeList = await _aniListService.GetAiringScheduleAsync(startOfWeek, endOfWeek);

            // Group by Day of Week
            var daysOrder = new[]
            {
                ("Monday", "Poniedziałek"),
                ("Tuesday", "Wtorek"),
                ("Wednesday", "Środa"),
                ("Thursday", "Czwartek"),
                ("Friday", "Piątek"),
                ("Saturday", "Sobota"),
                ("Sunday", "Niedziela")
            };

            var daySchedules = new List<DaySchedule>();

            foreach (var (dayEng, dayPl) in daysOrder)
            {
                var dayAnime = animeList
                    .Where(a => a.NextAiringEpisode != null && a.NextAiringEpisode.AiringAt.ToLocalTime().DayOfWeek.ToString().Equals(dayEng, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(a => a.NextAiringEpisode!.AiringAt)
                    .GroupBy(a => a.DisplayTitle) // remove duplicates if multiple episodes in same week
                    .Select(g => g.First())
                    .ToList();

                daySchedules.Add(new DaySchedule
                {
                    Day = dayEng,
                    DayPl = dayPl,
                    AnimeList = dayAnime
                });
            }

            return new WeeklyScheduleResponse
            {
                FetchedAt = now,
                TotalAnime = daySchedules.Sum(d => d.AnimeList.Count),
                Schedule = daySchedules
            };
        }

        private static WeeklyScheduleResponse ApplyUserFilterToSchedule(WeeklyScheduleResponse baseSchedule, UserAnimeListResponse userList)
        {
            var userAnimeMap = userList.Watching.Concat(userList.Planning).ToList();

            var filteredDays = new List<DaySchedule>();
            foreach (var day in baseSchedule.Schedule)
            {
                var matchedInDay = new List<UnifiedAnimeEntry>();
                foreach (var anime in day.AnimeList)
                {
                    var match = FindUserAnimeMatch(anime, userAnimeMap);
                    if (match != null)
                    {
                        var clone = CloneAnime(anime);
                        clone.UserStatus = match.UserStatus;
                        clone.UserProgress = match.UserProgress;
                        clone.UserScore = match.UserScore;
                        clone.UserPlatform = match.UserPlatform;
                        matchedInDay.Add(clone);
                    }
                }

                filteredDays.Add(new DaySchedule
                {
                    Day = day.Day,
                    DayPl = day.DayPl,
                    AnimeList = matchedInDay
                });
            }

            return new WeeklyScheduleResponse
            {
                FetchedAt = baseSchedule.FetchedAt,
                TotalAnime = filteredDays.Sum(d => d.AnimeList.Count),
                Schedule = filteredDays
            };
        }

        private static UnifiedAnimeEntry? FindUserAnimeMatch(UnifiedAnimeEntry target, List<UnifiedAnimeEntry> candidates)
        {
            // 1. Match by AniListId
            if (target.AniListId.HasValue)
            {
                var match = candidates.FirstOrDefault(c => c.AniListId.HasValue && c.AniListId.Value == target.AniListId.Value);
                if (match != null) return match;
            }

            // 2. Match by MalId
            if (target.MalId.HasValue)
            {
                var match = candidates.FirstOrDefault(c => c.MalId.HasValue && c.MalId.Value == target.MalId.Value);
                if (match != null) return match;
            }

            // 3. Match by KitsuId
            if (!string.IsNullOrEmpty(target.KitsuId))
            {
                var match = candidates.FirstOrDefault(c => c.KitsuId == target.KitsuId);
                if (match != null) return match;
            }

            // 4. Fuzzy title match
            var targetTitleNorm = NormalizeTitle(target.DisplayTitle);
            return candidates.FirstOrDefault(c =>
                NormalizeTitle(c.DisplayTitle) == targetTitleNorm ||
                NormalizeTitle(c.TitleRomaji) == targetTitleNorm ||
                NormalizeTitle(c.TitleEnglish) == targetTitleNorm);
        }

        private static string NormalizeTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return string.Empty;
            return new string(title.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        }

        private static UnifiedAnimeEntry CloneAnime(UnifiedAnimeEntry a)
        {
            return new UnifiedAnimeEntry
            {
                Id = a.Id,
                MalId = a.MalId,
                AniListId = a.AniListId,
                KitsuId = a.KitsuId,
                TitleRomaji = a.TitleRomaji,
                TitleEnglish = a.TitleEnglish,
                TitleNative = a.TitleNative,
                CoverImage = a.CoverImage,
                BannerImage = a.BannerImage,
                Format = a.Format,
                Status = a.Status,
                Episodes = a.Episodes,
                EpisodeDuration = a.EpisodeDuration,
                Season = a.Season,
                SeasonYear = a.SeasonYear,
                StartDate = a.StartDate,
                AverageScore = a.AverageScore,
                Popularity = a.Popularity,
                Genres = new List<string>(a.Genres),
                Studios = new List<string>(a.Studios),
                Synopsis = a.Synopsis,
                Source = a.Source,
                SiteUrl = a.SiteUrl,
                LiveChartUrl = a.LiveChartUrl,
                MalUrl = a.MalUrl,
                AniListUrl = a.AniListUrl,
                KitsuUrl = a.KitsuUrl,
                NextAiringEpisode = a.NextAiringEpisode
            };
        }
    }
}
