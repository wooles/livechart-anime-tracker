using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LiveChartTracker.Models
{
    public class CalendarMonthEpisode
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

        public string Format { get; set; } = "TV";
        public string Status { get; set; } = "RELEASING";
        public int? TotalEpisodes { get; set; }
        public int? EpisodeDuration { get; set; } // minutes

        public int EpisodeNumber { get; set; }
        public DateTimeOffset AiringAt { get; set; }
        public string AiringTimeFormatted { get; set; } = string.Empty; // "18:30"
        public string AiringDateFormatted { get; set; } = string.Empty; // "2026-08-19"
        public long TimeUntilAiringSeconds { get; set; }

        public double? AverageScore { get; set; }
        public List<string> Genres { get; set; } = new();
        public List<string> Studios { get; set; } = new();
        public string Synopsis { get; set; } = string.Empty;

        public string? SiteUrl { get; set; }
        public string? MalUrl { get; set; }
        public string? AniListUrl { get; set; }
        public string? KitsuUrl { get; set; }

        // User watch tracking
        public int? UserProgress { get; set; } // e.g. watched 5
        public double? UserScore { get; set; }
    }

    public class CalendarDay
    {
        public string DateString { get; set; } = string.Empty; // YYYY-MM-DD
        public int DayNumber { get; set; } // 1..31
        public string DayOfWeek { get; set; } = string.Empty; // Monday, Tuesday...
        public string DayOfWeekPl { get; set; } = string.Empty; // Poniedziałek...
        public bool IsCurrentMonth { get; set; }
        public bool IsToday { get; set; }
        public List<CalendarMonthEpisode> Episodes { get; set; } = new();
    }

    public class MonthlyCalendarResponse
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public string MonthName { get; set; } = string.Empty;
        public string MonthNamePl { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public int TotalWatchingAnime { get; set; }
        public int TotalEpisodesInMonth { get; set; }
        public List<CalendarDay> Days { get; set; } = new();
    }

    public class UserAnimeListResponse
    {
        public string Platform { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public int TotalEntries { get; set; }
        public List<CalendarMonthEpisode> Watching { get; set; } = new();
    }
}
