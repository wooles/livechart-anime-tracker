using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LiveChartTracker.Models
{
    public class AiringEpisodeInfo
    {
        public int Episode { get; set; }
        public DateTimeOffset AiringAt { get; set; }
        public long TimeUntilAiringSeconds { get; set; }
        public string DayOfWeek { get; set; } = string.Empty;
        public string FormattedTime { get; set; } = string.Empty;
    }

    public class UnifiedAnimeEntry
    {
        public string Id { get; set; } = string.Empty;
        public int? MalId { get; set; }
        public int? AniListId { get; set; }
        public string? KitsuId { get; set; }

        public string TitleRomaji { get; set; } = string.Empty;
        public string TitleEnglish { get; set; } = string.Empty;
        public string TitleNative { get; set; } = string.Empty;

        public string DisplayTitle => !string.IsNullOrWhiteSpace(TitleEnglish) 
            ? TitleEnglish 
            : (!string.IsNullOrWhiteSpace(TitleRomaji) ? TitleRomaji : TitleNative);

        public string CoverImage { get; set; } = string.Empty;
        public string? BannerImage { get; set; }

        public string Format { get; set; } = "TV"; // TV, MOVIE, OVA, ONA, SPECIAL
        public string Status { get; set; } = "RELEASING"; // RELEASING, NOT_YET_RELEASED, FINISHED, CANCELLED
        public int? Episodes { get; set; }
        public int? EpisodeDuration { get; set; } // minutes

        public string? Season { get; set; } // WINTER, SPRING, SUMMER, FALL
        public int? SeasonYear { get; set; }
        public string? StartDate { get; set; }

        public double? AverageScore { get; set; } // 0 - 100 or 0 - 10
        public int? Popularity { get; set; }

        public List<string> Genres { get; set; } = new();
        public List<string> Studios { get; set; } = new();
        public string Synopsis { get; set; } = string.Empty;
        public string? Source { get; set; } // MANGA, LIGHT_NOVEL, ORIGINAL, etc.
        public string? SiteUrl { get; set; }
        public string? LiveChartUrl { get; set; }
        public string? MalUrl { get; set; }
        public string? AniListUrl { get; set; }
        public string? KitsuUrl { get; set; }

        public AiringEpisodeInfo? NextAiringEpisode { get; set; }

        // User specific tracking information (if synced)
        public string? UserStatus { get; set; } // WATCHING, PLANNING, COMPLETED, PAUSED, DROPPED
        public int? UserProgress { get; set; } // Episodes watched
        public double? UserScore { get; set; }
        public string? UserPlatform { get; set; } // AniList, Kitsu, MyAnimeList
    }

    public class DaySchedule
    {
        public string Day { get; set; } = string.Empty; // Monday, Tuesday, etc.
        public string DayPl { get; set; } = string.Empty; // Poniedziałek, Wtorek, etc.
        public List<UnifiedAnimeEntry> AnimeList { get; set; } = new();
    }

    public class WeeklyScheduleResponse
    {
        public DateTimeOffset FetchedAt { get; set; } = DateTimeOffset.UtcNow;
        public int TotalAnime { get; set; }
        public List<DaySchedule> Schedule { get; set; } = new();
    }

    public class SeasonalAnimeResponse
    {
        public string Season { get; set; } = string.Empty;
        public int Year { get; set; }
        public int TotalAnime { get; set; }
        public List<UnifiedAnimeEntry> AnimeList { get; set; } = new();
    }

    public class UserAnimeListResponse
    {
        public string Platform { get; set; } = string.Empty; // AniList, Kitsu, MyAnimeList
        public string Username { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public int TotalEntries { get; set; }
        public List<UnifiedAnimeEntry> Watching { get; set; } = new();
        public List<UnifiedAnimeEntry> Planning { get; set; } = new();
        public List<UnifiedAnimeEntry> Completed { get; set; } = new();
        public List<UnifiedAnimeEntry> Paused { get; set; } = new();
        public List<UnifiedAnimeEntry> Dropped { get; set; } = new();
    }

    public class ExportCalendarRequest
    {
        public string? Platform { get; set; }
        public string? Username { get; set; }
        public bool OnlyWatching { get; set; } = true;
        public int ReminderMinutesBefore { get; set; } = 15;
    }
}
