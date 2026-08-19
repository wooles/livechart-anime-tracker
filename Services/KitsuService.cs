using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using LiveChartTracker.Models;

namespace LiveChartTracker.Services
{
    public interface IKitsuService
    {
        Task<(string? avatarUrl, List<CalendarMonthEpisode> episodes, int totalWatching)> GetWatchingMonthEpisodesAsync(string username, int year, int month);
    }

    public class KitsuService : IKitsuService
    {
        private readonly HttpClient _httpClient;
        private const string KitsuBaseUrl = "https://kitsu.app/api/edge";

        public KitsuService(HttpClient httpClient)
        {
            _httpClient = httpClient;
            if (!_httpClient.DefaultRequestHeaders.Contains("Accept"))
            {
                _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.api+json");
            }
            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "LiveChartAnimeTracker/1.0");
            }
        }

        public async Task<(string? avatarUrl, List<CalendarMonthEpisode> episodes, int totalWatching)> GetWatchingMonthEpisodesAsync(string username, int year, int month)
        {
            var userUrl = $"{KitsuBaseUrl}/users?filter[name]={Uri.EscapeDataString(username)}";
            var userRes = await _httpClient.GetAsync(userUrl);
            userRes.EnsureSuccessStatusCode();

            var userJson = JsonNode.Parse(await userRes.Content.ReadAsStringAsync());
            var users = userJson?["data"]?.AsArray();
            if (users == null || users.Count == 0)
            {
                throw new Exception($"User {username} not found on Kitsu.");
            }

            var userNode = users[0];
            var userId = userNode?["id"]?.ToString();
            var avatarUrl = userNode?["attributes"]?["avatar"]?["large"]?.ToString() 
                ?? userNode?["attributes"]?["avatar"]?["original"]?.ToString();

            var libraryUrl = $"{KitsuBaseUrl}/library-entries?filter[userId]={userId}&filter[kind]=anime&filter[status]=current&include=anime&page[limit]=100";
            var libRes = await _httpClient.GetAsync(libraryUrl);
            if (!libRes.IsSuccessStatusCode)
            {
                return (avatarUrl, new List<CalendarMonthEpisode>(), 0);
            }

            var libJson = JsonNode.Parse(await libRes.Content.ReadAsStringAsync());
            var entries = libJson?["data"]?.AsArray();
            var included = libJson?["included"]?.AsArray();

            if (entries == null || entries.Count == 0)
            {
                return (avatarUrl, new List<CalendarMonthEpisode>(), 0);
            }

            var animeDict = new Dictionary<string, JsonNode>();
            if (included != null)
            {
                foreach (var inc in included)
                {
                    var incType = inc?["type"]?.ToString();
                    var incId = inc?["id"]?.ToString();
                    if (incType == "anime" && incId != null && inc != null)
                    {
                        animeDict[incId] = inc;
                    }
                }
            }

            var episodes = new List<CalendarMonthEpisode>();
            int daysInMonth = DateTime.DaysInMonth(year, month);
            var monthStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
            var monthEnd = new DateTime(year, month, daysInMonth, 23, 59, 59, DateTimeKind.Utc);

            int totalWatching = entries.Count;

            foreach (var entry in entries)
            {
                var entryAttr = entry?["attributes"];
                if (entryAttr == null) continue;

                var animeRelId = entry?["relationships"]?["anime"]?["data"]?["id"]?.ToString();
                if (animeRelId == null || !animeDict.TryGetValue(animeRelId, out var animeNode))
                {
                    continue;
                }

                var animeAttr = animeNode["attributes"];
                if (animeAttr == null) continue;

                string animeStatus = animeAttr["status"]?.ToString()?.ToLowerInvariant() ?? "current";
                string startStr = animeAttr["startDate"]?.ToString() ?? "";
                string endStr = animeAttr["endDate"]?.ToString() ?? "";

                DateTime? startDate = DateTime.TryParse(startStr, out var pStart) ? pStart : null;
                DateTime? endDate = DateTime.TryParse(endStr, out var pEnd) ? pEnd : null;

                // Airing status checks
                if (animeStatus == "finished" && endDate.HasValue && endDate.Value < monthStart)
                {
                    continue; // Finished in the past
                }
                if (startDate.HasValue && startDate.Value > monthEnd)
                {
                    continue; // Not yet started
                }

                int progress = entryAttr["progress"]?.GetValue<int?>() ?? 0;
                double? ratingTwenty = entryAttr["ratingTwenty"]?.GetValue<double?>();

                string title = animeAttr["canonicalTitle"]?.ToString() ?? animeAttr["titles"]?["en"]?.ToString() ?? "";
                string poster = animeAttr["posterImage"]?["large"]?.ToString() ?? animeAttr["posterImage"]?["medium"]?.ToString() ?? "";
                int? totalEp = animeAttr["episodeCount"]?.GetValue<int?>();
                string synopsis = animeAttr["synopsis"]?.ToString() ?? "";

                DateTime start = startDate ?? monthStart;
                DayOfWeek airDay = start.DayOfWeek;

                for (int day = 1; day <= daysInMonth; day++)
                {
                    var currentDate = new DateTime(year, month, day, 18, 0, 0, DateTimeKind.Utc);
                    if (startDate.HasValue && currentDate < startDate.Value.Date) continue;
                    if (endDate.HasValue && currentDate > endDate.Value.Date.AddDays(1)) continue;

                    if (currentDate.DayOfWeek == airDay)
                    {
                        int weeksDiff = (int)Math.Max(1, Math.Ceiling((currentDate - start).TotalDays / 7.0));
                        int epNumber = Math.Min(totalEp ?? 999, Math.Max(1, progress > 0 ? progress + (weeksDiff > 0 ? weeksDiff % 12 : 1) : weeksDiff));

                        var airTime = new DateTimeOffset(currentDate).ToLocalTime();

                        episodes.Add(new CalendarMonthEpisode
                        {
                            Id = $"kitsu_{animeRelId}_d{day}",
                            KitsuId = animeRelId,
                            TitleEnglish = title,
                            TitleRomaji = title,
                            CoverImage = poster,
                            Format = (animeAttr["showType"]?.ToString() ?? "TV").ToUpperInvariant(),
                            TotalEpisodes = totalEp,
                            EpisodeNumber = epNumber,
                            AiringAt = airTime,
                            AiringTimeFormatted = airTime.ToString("HH:mm"),
                            AiringDateFormatted = airTime.ToString("yyyy-MM-dd"),
                            TimeUntilAiringSeconds = (long)(airTime - DateTimeOffset.UtcNow).TotalSeconds,
                            AverageScore = ratingTwenty.HasValue ? ratingTwenty.Value * 5.0 : null,
                            Synopsis = synopsis,
                            KitsuUrl = $"https://kitsu.app/anime/{animeAttr["slug"]?.ToString() ?? animeRelId}",
                            SiteUrl = $"https://kitsu.app/anime/{animeAttr["slug"]?.ToString() ?? animeRelId}",
                            UserProgress = progress,
                            UserScore = ratingTwenty.HasValue ? ratingTwenty.Value * 5.0 : null
                        });
                    }
                }
            }

            return (avatarUrl, episodes.OrderBy(e => e.AiringAt).ToList(), totalWatching);
        }
    }
}
