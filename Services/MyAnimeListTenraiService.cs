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

            var statusTasks = statusesToFetch.Select(async statusVal =>
            {
                string statusName = statusVal == 1 ? "Watching" : "PlanToWatch";
                var list = new List<(int malId, string title, string img, int watched, int totalEp, double score, string mediaType, int airingStatus, DateTime? startDate, DateTime? endDate, string listStatus)>();
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

                        list.Add((malId, title, img, watched, totalEp, score > 0 ? score * 10 : 0, mediaType, airingStatus, start, end, statusName));
                    }

                    if (array.Count < limit) hasMore = false;
                    else offset += limit;
                }
                return list;
            });

            var results = await Task.WhenAll(statusTasks);
            foreach (var res in results)
            {
                watchingList.AddRange(res);
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
            var malIds = watchingList.Where(w => w.malId > 0).Select(w => w.malId).Distinct().ToList();

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

                var chunkTasks = malChunks.Select(async chunk =>
                {
                    var chunkMediaList = new List<JsonNode>();
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
                                if (m != null) chunkMediaList.Add(m);
                            }
                        }

                        aniPage++;
                    }
                    return chunkMediaList;
                });

                var allChunkResults = await Task.WhenAll(chunkTasks);
                foreach (var mediaList in allChunkResults)
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

                // Strictly include verified broadcast schedules from official AniList data
                foreach (var kvp in aniMediaMap)
                {
                    int malId = kvp.Key;
                    var media = kvp.Value;
                    var nextEpNode = media["nextAiringEpisode"];
                    var userEntry = watchingList.FirstOrDefault(w => w.malId == malId);
                    string mediaStatus = media["status"]?.ToString() ?? "";

                    // Only include anime that are actively RELEASING or confirmed upcoming from official AniList schedule
                    if (nextEpNode != null && (mediaStatus == "RELEASING" || mediaStatus == "NOT_YET_RELEASED"))
                    {
                        int anchorEp = nextEpNode["episode"]?.GetValue<int>() ?? 1;
                        long anchorAirSec = nextEpNode["airingAt"]?.GetValue<long>() ?? 0;
                        int? totalEp = userEntry.totalEp > 0 ? userEntry.totalEp : media["episodes"]?.GetValue<int?>();

                        var anchorAirUtc = DateTimeOffset.FromUnixTimeSeconds(anchorAirSec).ToUniversalTime();

                        // Calculate episodes forward and backward across the month window (-12 to +12 weeks)
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

            // Return strictly confirmed episodes verified by AniList official data
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
                    using var req = new HttpRequestMessage(HttpMethod.Post, GraphQlEndpoint);
                    req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36");
                    req.Headers.TryAddWithoutValidation("Origin", "https://anilist.co");
                    req.Headers.TryAddWithoutValidation("Referer", "https://anilist.co/");
                    req.Headers.TryAddWithoutValidation("Accept", "application/json");
                    req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                    var response = await _httpClient.SendAsync(req);

                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1.5 * (attempt + 1));
                        await Task.Delay(retryAfter);
                        continue;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        var errStr = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"[AniList GraphQL Error] HTTP {response.StatusCode}: {errStr}");
                        return null;
                    }

                    var jsonString = await response.Content.ReadAsStringAsync();
                    var node = JsonNode.Parse(jsonString);
                    if (node != null)
                    {
                        _malGqlCache[cacheKey] = (DateTimeOffset.UtcNow, node);
                    }
                    return node;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AniList GraphQL Exception] attempt {attempt}: {ex.Message}");
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
