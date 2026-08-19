using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using LiveChartTracker.Models;
using Tenrai;

namespace LiveChartTracker.Services
{
    public interface IMyAnimeListTenraiService
    {
        Task<(string? avatarUrl, List<CalendarMonthEpisode> episodes, int totalWatching)> GetWatchingMonthEpisodesAsync(string username, int year, int month);
    }

    public class MyAnimeListTenraiService : IMyAnimeListTenraiService
    {
        private readonly ITenrai _tenraiClient;
        private readonly HttpClient _httpClient;
        private const string GraphQlEndpoint = "https://graphql.anilist.co";

        public MyAnimeListTenraiService(HttpClient httpClient)
        {
            _httpClient = httpClient;
            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            }
            _tenraiClient = new TenraiClient();
        }

        public async Task<(string? avatarUrl, List<CalendarMonthEpisode> episodes, int totalWatching)> GetWatchingMonthEpisodesAsync(string username, int year, int month)
        {
            var avatarUrl = "https://myanimelist.net/images/userimages/default.jpg";
            var watchingList = new List<(int malId, string title, string img, int watched, int totalEp, double score, string mediaType, int airingStatus, DateTime? startDate, DateTime? endDate, string listStatus)>();

            int[] statusesToFetch = new[] { 1, 6 }; // 1 = Watching, 6 = Plan to Watch

            foreach (var statusVal in statusesToFetch)
            {
                string statusName = statusVal == 1 ? "Watching" : "PlanToWatch";
                int offset = 0;
                const int limit = 300;
                bool hasMore = true;

                while (hasMore && offset < 900)
                {
                    var malListUrl = $"https://myanimelist.net/animelist/{Uri.EscapeDataString(username)}/load.json?offset={offset}&status={statusVal}";
                    var req = new HttpRequestMessage(HttpMethod.Get, malListUrl);
                    req.Headers.Add("Referer", $"https://myanimelist.net/animelist/{Uri.EscapeDataString(username)}");

                    var response = await _httpClient.SendAsync(req);
                    if (!response.IsSuccessStatusCode)
                    {
                        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                        {
                            throw new Exception($"User '{username}' was not found on MyAnimeList.");
                        }
                        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                        {
                            throw new Exception($"Anime list for user '{username}' is private. Please make it public in MyAnimeList privacy settings.");
                        }
                        break;
                    }

                    var jsonStr = await response.Content.ReadAsStringAsync();
                    var array = JsonNode.Parse(jsonStr)?.AsArray();
                    if (array == null || array.Count == 0) break;

                    foreach (var item in array)
                    {
                        if (item == null) continue;
                        int malId = item["anime_id"]?.GetValue<int>() ?? 0;
                        string title = item["anime_title_eng"]?.ToString() ?? item["anime_title"]?.ToString() ?? "";
                        string img = item["anime_image_path"]?.ToString() ?? "";
                        int watched = item["num_watched_episodes"]?.GetValue<int>() ?? 0;
                        int totalEp = item["anime_num_episodes"]?.GetValue<int>() ?? 0;
                        double score = item["score"]?.GetValue<double>() ?? 0;
                        string mediaType = item["anime_media_type_string"]?.ToString() ?? "TV";
                        int airingStatus = item["anime_airing_status"]?.GetValue<int>() ?? 1;

                        DateTime? start = ParseMalDate(item["anime_start_date_string"]?.ToString());
                        DateTime? end = ParseMalDate(item["anime_end_date_string"]?.ToString());

                        watchingList.Add((malId, title, img, watched, totalEp, score > 0 ? score * 10 : 0, mediaType, airingStatus, start, end, statusName));
                    }

                    if (array.Count < limit) hasMore = false;
                    else offset += limit;
                }
            }

            int totalWatching = watchingList.Count;
            if (totalWatching == 0)
            {
                return (avatarUrl, new List<CalendarMonthEpisode>(), 0);
            }

            // Expanded range (+/- 15 days around month) to support rolling weekly schedules
            var startOfMonth = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero).AddDays(-15);
            var endOfMonth = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1).AddDays(15);
            long startSec = startOfMonth.ToUnixTimeSeconds();
            long endSec = endOfMonth.ToUnixTimeSeconds();

            var episodes = new List<CalendarMonthEpisode>();
            var processedMalIds = new HashSet<int>();

            // 1. Fetch exact live broadcasting schedules via AniList schedule network for currently airing / upcoming shows
            var relevantAiringShows = watchingList.Where(w => 
                w.malId > 0 && 
                (w.airingStatus == 1 || w.airingStatus == 3 || (w.startDate.HasValue && w.startDate.Value.Year >= year - 1 && (!w.endDate.HasValue || w.endDate.Value >= startOfMonth.DateTime)))
            ).ToList();

            var malIds = relevantAiringShows.Select(w => w.malId).Distinct().ToList();

            try
            {
                // Map MAL IDs to AniList IDs with nextAiringEpisode anchor
                const string malToAniQuery = @"
query ($page: Int, $malIds: [Int]) {
  Page(page: $page, perPage: 50) {
    pageInfo {
      hasNextPage
    }
    media(idMal_in: $malIds, type: ANIME) {
      id
      idMal
      title {
        romaji
        english
      }
      coverImage {
        large
      }
      format
      status
      episodes
      averageScore
      description
      siteUrl
      externalLinks {
        id
        site
        url
        type
        icon
        color
      }
      nextAiringEpisode {
        episode
        airingAt
        timeUntilAiring
      }
    }
  }
}";
                var malChunks = malIds.Chunk(50).ToList();
                var aniMediaMap = new Dictionary<int, JsonNode>(); // malId -> mediaNode

                foreach (var chunk in malChunks)
                {
                    int aniPage = 1;
                    bool hasMoreMedia = true;

                    while (hasMoreMedia && aniPage <= 3)
                    {
                        var mapRes = await ExecuteGraphQLAsync(malToAniQuery, new { page = aniPage, malIds = chunk });
                        var pageNode = mapRes?["data"]?["Page"];
                        if (pageNode == null) break;

                        hasMoreMedia = pageNode["pageInfo"]?["hasNextPage"]?.GetValue<bool>() ?? false;
                        var mediaList = pageNode["media"]?.AsArray();
                        if (mediaList != null)
                        {
                            foreach (var m in mediaList)
                            {
                                int mId = m?["id"]?.GetValue<int>() ?? 0;
                                int? malId = m?["idMal"]?.GetValue<int?>();
                                if (mId > 0 && malId.HasValue && m != null)
                                {
                                    if (!aniMediaMap.TryGetValue(malId.Value, out var existing) ||
                                        (m["nextAiringEpisode"] != null && existing["nextAiringEpisode"] == null) ||
                                        (m["status"]?.ToString() == "RELEASING" && existing["status"]?.ToString() != "RELEASING"))
                                    {
                                        aniMediaMap[malId.Value] = m;
                                    }
                                }
                            }
                        }

                        aniPage++;
                    }
                }

                // Project consistent weekly broadcast schedules from nextAiringEpisode anchors
                foreach (var kvp in aniMediaMap)
                {
                    int malId = kvp.Key;
                    var media = kvp.Value;
                    var nextEpNode = media["nextAiringEpisode"];
                    var userEntry = watchingList.FirstOrDefault(w => w.malId == malId);

                    if (nextEpNode != null)
                    {
                        int anchorEp = nextEpNode["episode"]?.GetValue<int>() ?? 1;
                        long anchorAirSec = nextEpNode["airingAt"]?.GetValue<long>() ?? 0;
                        int? totalEp = userEntry.totalEp > 0 ? userEntry.totalEp : media["episodes"]?.GetValue<int?>();

                        var anchorAirUtc = DateTimeOffset.FromUnixTimeSeconds(anchorAirSec).ToUniversalTime();

                        // Project weeks backward and forward across the month window (-12 to +12 weeks)
                        int maxProjectedEp = totalEp ?? (anchorEp > 26 ? anchorEp + 12 : Math.Max(anchorEp + 4, 13));
                        for (int k = -12; k <= 12; k++)
                        {
                            int targetEp = anchorEp + k;
                            if (targetEp < 1) continue;
                            if (targetEp > maxProjectedEp) continue;

                            var targetAirUtc = anchorAirUtc.AddDays(k * 7);
                            if (targetAirUtc < startOfMonth || targetAirUtc > endOfMonth) continue;

                            episodes.Add(new CalendarMonthEpisode
                            {
                                Id = $"mal_{malId}_ep{targetEp}",
                                MalId = malId,
                                AniListId = media["id"]?.GetValue<int?>(),
                                TitleEnglish = userEntry.title ?? media["title"]?["english"]?.ToString() ?? media["title"]?["romaji"]?.ToString() ?? "",
                                TitleRomaji = media["title"]?["romaji"]?.ToString() ?? userEntry.title ?? "",
                                CoverImage = userEntry.img ?? media["coverImage"]?["large"]?.ToString() ?? "",
                                Format = media["format"]?.ToString() ?? userEntry.mediaType ?? "TV",
                                TotalEpisodes = totalEp,
                                EpisodeNumber = targetEp,
                                AiringAt = targetAirUtc,
                                AiringTimeFormatted = targetAirUtc.ToString("HH:mm"),
                                AiringDateFormatted = targetAirUtc.ToString("yyyy-MM-dd"),
                                TimeUntilAiringSeconds = (long)(targetAirUtc - DateTimeOffset.UtcNow).TotalSeconds,
                                AverageScore = media["averageScore"]?.GetValue<double?>() ?? (userEntry.score > 0 ? userEntry.score : null),
                                Synopsis = media["description"]?.ToString() ?? "",
                                MalUrl = $"https://myanimelist.net/anime/{malId}",
                                AniListUrl = media["siteUrl"]?.ToString(),
                                SiteUrl = $"https://myanimelist.net/anime/{malId}",
                                StreamingLinks = StreamingHelper.ParseStreamingLinks(media["externalLinks"]),
                                UserProgress = userEntry.watched,
                                UserScore = userEntry.score > 0 ? userEntry.score : null,
                                ListStatus = userEntry.listStatus ?? "Watching"
                            });
                        }

                        processedMalIds.Add(malId);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Warning] Failed to fetch exact live schedules: {ex.Message}");
            }

            // 2. Fallback for any remaining shows that didn't have AniList schedule records
            int daysInMonth = DateTime.DaysInMonth(year, month);
            var monthStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
            var monthEnd = new DateTime(year, month, daysInMonth, 23, 59, 59, DateTimeKind.Utc);

            foreach (var item in watchingList)
            {
                if (processedMalIds.Contains(item.malId)) continue; // Already matched with exact airing time!
                if (item.airingStatus == 2) continue; // Skip finished/completed shows (MAL status 2 = Finished)

                if (item.airingStatus == 1) // Currently Airing (MAL status 1 = Airing)
                {
                    if (item.endDate.HasValue && item.endDate.Value < monthStart) continue;
                    if (item.startDate.HasValue && item.startDate.Value > monthEnd) continue;
                }
                else if (item.airingStatus == 3) // Not yet aired (MAL status 3 = Not Yet Aired)
                {
                    if (!item.startDate.HasValue || item.startDate.Value < monthStart || item.startDate.Value > monthEnd) continue;
                }
                else
                {
                    continue;
                }

                DateTime start = item.startDate ?? monthStart;
                if (start > monthEnd) continue;
                if (item.endDate.HasValue && item.endDate.Value < monthStart) continue;

                // Handle single release media (Movies / Specials / OVAs / ONAs without weekly schedules)
                if (item.mediaType == "Movie" || item.mediaType == "Special" || item.mediaType == "OVA")
                {
                    if (item.startDate.HasValue && item.startDate.Value >= monthStart && item.startDate.Value <= monthEnd)
                    {
                        var airUtc = new DateTimeOffset(item.startDate.Value, TimeSpan.Zero);
                        episodes.Add(new CalendarMonthEpisode
                        {
                            Id = $"mal_{item.malId}_m",
                            MalId = item.malId,
                            TitleEnglish = item.title,
                            TitleRomaji = item.title,
                            CoverImage = item.img,
                            Format = item.mediaType,
                            TotalEpisodes = item.totalEp > 0 ? item.totalEp : 1,
                            EpisodeNumber = 1,
                            AiringAt = airUtc,
                            AiringTimeFormatted = airUtc.ToString("HH:mm"),
                            AiringDateFormatted = airUtc.ToString("yyyy-MM-dd"),
                            TimeUntilAiringSeconds = (long)(airUtc - DateTimeOffset.UtcNow).TotalSeconds,
                            AverageScore = item.score > 0 ? item.score : null,
                            MalUrl = $"https://myanimelist.net/anime/{item.malId}",
                            SiteUrl = $"https://myanimelist.net/anime/{item.malId}",
                            UserProgress = item.watched,
                            UserScore = item.score > 0 ? item.score : null,
                            ListStatus = item.listStatus ?? "Watching"
                        });
                    }
                    continue;
                }

                DayOfWeek airDay = start.DayOfWeek;
                int hourUtc = 14; // Standard 16:00 Polish Time
                int minUtc = (item.malId % 2 == 0) ? 0 : 30;

                for (int day = 1; day <= daysInMonth; day++)
                {
                    var currentDate = new DateTime(year, month, day, hourUtc, minUtc, 0, DateTimeKind.Utc);
                    if (item.startDate.HasValue && currentDate < item.startDate.Value.Date) continue;
                    if (item.endDate.HasValue && currentDate > item.endDate.Value.Date.AddDays(1)) continue;

                    if (currentDate.DayOfWeek == airDay)
                    {
                        int weeksDiff = (int)Math.Max(1, Math.Ceiling((currentDate - start).TotalDays / 7.0));
                        int epNumber = Math.Min(item.totalEp > 0 ? item.totalEp : 999, weeksDiff);

                        var airUtc = new DateTimeOffset(currentDate, TimeSpan.Zero);

                        episodes.Add(new CalendarMonthEpisode
                        {
                            Id = $"mal_{item.malId}_d{day}",
                            MalId = item.malId,
                            TitleEnglish = item.title,
                            TitleRomaji = item.title,
                            CoverImage = item.img,
                            Format = item.mediaType,
                            TotalEpisodes = item.totalEp > 0 ? item.totalEp : null,
                            EpisodeNumber = epNumber,
                            AiringAt = airUtc,
                            AiringTimeFormatted = airUtc.ToString("HH:mm"),
                            AiringDateFormatted = airUtc.ToString("yyyy-MM-dd"),
                            TimeUntilAiringSeconds = (long)(airUtc - DateTimeOffset.UtcNow).TotalSeconds,
                            AverageScore = item.score > 0 ? item.score : null,
                            MalUrl = $"https://myanimelist.net/anime/{item.malId}",
                            SiteUrl = $"https://myanimelist.net/anime/{item.malId}",
                            UserProgress = item.watched,
                            UserScore = item.score > 0 ? item.score : null,
                            ListStatus = item.listStatus ?? "Watching"
                        });
                    }
                }
            }

            return (avatarUrl, episodes.OrderBy(e => e.AiringAt).ToList(), totalWatching);
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTimeOffset cachedAt, JsonNode? data)> _malGqlCache = new();

        private async Task<JsonNode?> ExecuteGraphQLAsync(string query, object variables)
        {
            var cacheKey = $"{query.GetHashCode()}_{JsonSerializer.Serialize(variables)}";
            if (_malGqlCache.TryGetValue(cacheKey, out var entry) && DateTimeOffset.UtcNow - entry.cachedAt < TimeSpan.FromMinutes(15))
            {
                return entry.data;
            }

            var payload = new
            {
                query = query,
                variables = variables
            };

            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                    var response = await _httpClient.PostAsync(GraphQlEndpoint, content);

                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1.5 * (attempt + 1));
                        await Task.Delay(retryAfter);
                        continue;
                    }

                    if (!response.IsSuccessStatusCode) return null;

                    var jsonString = await response.Content.ReadAsStringAsync();
                    var node = JsonNode.Parse(jsonString);
                    if (node != null)
                    {
                        _malGqlCache[cacheKey] = (DateTimeOffset.UtcNow, node);
                    }
                    return node;
                }
                catch
                {
                    if (attempt == 2) return null;
                    await Task.Delay(1000);
                }
            }

            return null;
        }

        private static DateTime? ParseMalDate(string? dateStr)
        {
            if (string.IsNullOrWhiteSpace(dateStr)) return null;
            var formats = new[] { "dd-MM-yy", "dd-MM-yyyy", "yyyy-MM-dd", "MM-dd-yy", "d-M-yy" };
            if (DateTime.TryParseExact(dateStr.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                return dt;
            }
            if (DateTime.TryParse(dateStr, out var parsed))
            {
                return parsed;
            }
            return null;
        }
    }
}
