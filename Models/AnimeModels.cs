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

        public List<StreamingLink> StreamingLinks { get; set; } = new();

        // User watch tracking
        public string ListStatus { get; set; } = "Watching"; // "Watching" or "PlanToWatch"
        public int? UserProgress { get; set; } // e.g. watched 5
        public double? UserScore { get; set; }
    }

    public class StreamingLink
    {
        public string Site { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string? Icon { get; set; }
        public string? Color { get; set; }
    }

    public static class StreamingHelper
    {
        public static List<StreamingLink> ParseStreamingLinks(System.Text.Json.Nodes.JsonNode? externalLinksNode)
        {
            var links = new List<StreamingLink>();
            var addedSites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (externalLinksNode is System.Text.Json.Nodes.JsonArray arr)
            {
                foreach (var linkNode in arr)
                {
                    var site = linkNode?["site"]?.GetValue<string>()?.Trim();
                    var url = linkNode?["url"]?.GetValue<string>()?.Trim();
                    var type = linkNode?["type"]?.GetValue<string>()?.Trim();

                    if (string.IsNullOrWhiteSpace(site) || string.IsNullOrWhiteSpace(url))
                        continue;

                    if (string.Equals(type, "STREAMING", StringComparison.OrdinalIgnoreCase) || IsKnownStreaming(site))
                    {
                        var norm = NormalizeSite(site);
                        if (!addedSites.Contains(norm))
                        {
                            addedSites.Add(norm);
                            links.Add(new StreamingLink
                            {
                                Site = norm,
                                Url = url,
                                Icon = linkNode?["icon"]?.GetValue<string>(),
                                Color = linkNode?["color"]?.GetValue<string>()
                            });
                        }
                    }
                }
            }
            return links;
        }

        private static bool IsKnownStreaming(string site)
        {
            var s = site.ToLowerInvariant();
            if (s.Contains("bilibili") || s.Contains("iqiyi") || s.Contains("iQ"))
                return false;

            return s.Contains("crunchyroll") || s.Contains("netflix") || s.Contains("disney") || 
                   s.Contains("prime") || s.Contains("amazon") || s.Contains("max") || s.Contains("hbo") || 
                   s.Contains("adn") || s.Contains("animation digital network") || s.Contains("hidive") || 
                   s.Contains("hulu") || s.Contains("youtube");
        }

        private static string NormalizeSite(string site)
        {
            var s = site.Trim();
            if (s.Equals("Amazon Prime Video", StringComparison.OrdinalIgnoreCase) || s.Equals("Amazon", StringComparison.OrdinalIgnoreCase))
                return "Prime Video";
            if (s.Equals("Disney Plus", StringComparison.OrdinalIgnoreCase))
                return "Disney+";
            if (s.Equals("HBO Max", StringComparison.OrdinalIgnoreCase))
                return "Max";
            if (s.Equals("Animation Digital Network", StringComparison.OrdinalIgnoreCase))
                return "ADN";
            return s;
        }
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
