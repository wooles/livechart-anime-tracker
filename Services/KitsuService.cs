using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using LiveChartTracker.Models;

namespace LiveChartTracker.Services
{
    public interface IKitsuService
    {
        Task<(string? avatarUrl, List<CalendarMonthEpisode> episodes, int totalWatching)> GetWatchingMonthEpisodesAsync(string username, int year, int month, bool bypassCache = false);
    }

    public class KitsuService : IKitsuService
    {
        private readonly HttpClient _httpClient;
        private const string KitsuBaseUrl = "https://kitsu.app/api/edge";

        private const string GraphQlEndpoint = "https://graphql.anilist.co";
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTimeOffset cachedAt, JsonNode? data)> _kitsuGqlCache = new();

        public KitsuService(HttpClient httpClient)
        {
            _httpClient = httpClient;
            if (!_httpClient.DefaultRequestHeaders.Contains("Accept"))
            {
                _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.api+json");
            }
            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            }
        }

        public async Task<(string? avatarUrl, List<CalendarMonthEpisode> episodes, int totalWatching)> GetWatchingMonthEpisodesAsync(string username, int year, int month, bool bypassCache = false)
        {
            // 1. Fetch user ID by slug/name
            var userRes = await _httpClient.GetAsync($"{KitsuBaseUrl}/users?filter[slug]={Uri.EscapeDataString(username)}");
            if (!userRes.IsSuccessStatusCode)
            {
                userRes = await _httpClient.GetAsync($"{KitsuBaseUrl}/users?filter[name]={Uri.EscapeDataString(username)}");
            }

            if (!userRes.IsSuccessStatusCode)
            {
                throw new Exception($"User '{username}' was not found on Kitsu.");
            }

            var userJson = JsonNode.Parse(await userRes.Content.ReadAsStringAsync());
            var userDataList = userJson?["data"]?.AsArray();
            if (userDataList == null || userDataList.Count == 0)
            {
                throw new Exception($"User '{username}' was not found on Kitsu.");
            }

            var userObj = userDataList[0];
            string userId = userObj?["id"]?.ToString() ?? "";
            string? avatarUrl = userObj?["attributes"]?["avatar"]?["medium"]?.ToString() 
                             ?? userObj?["attributes"]?["avatar"]?["original"]?.ToString();

            // 2. Fetch current/planned library entries
            var entriesRes = await _httpClient.GetAsync($"{KitsuBaseUrl}/library-entries?filter[userId]={userId}&filter[status]=current,planned&include=anime&page[limit]=100");
            if (!entriesRes.IsSuccessStatusCode)
            {
                throw new Exception("Could not fetch anime list from Kitsu.");
            }

            var entriesJson = JsonNode.Parse(await entriesRes.Content.ReadAsStringAsync());
            var entriesData = entriesJson?["data"]?.AsArray();
            var includedData = entriesJson?["included"]?.AsArray();

            int totalWatching = entriesData?.Count ?? 0;
            if (totalWatching == 0)
            {
                return (avatarUrl, new List<CalendarMonthEpisode>(), 0);
            }

            // Map included anime details
            var animeMap = new Dictionary<string, (string title, string img, int? totalEp)>();
            if (includedData != null)
            {
                foreach (var inc in includedData)
                {
                    if (inc?["type"]?.ToString() == "anime")
                    {
                        string id = inc["id"]?.ToString() ?? "";
                        var attr = inc["attributes"];
                        string title = attr?["canonicalTitle"]?.ToString() ?? attr?["titles"]?["en"]?.ToString() ?? attr?["titles"]?["en_jp"]?.ToString() ?? "";
                        string img = attr?["posterImage"]?["large"]?.ToString() ?? attr?["posterImage"]?["medium"]?.ToString() ?? "";
                        int? epCount = attr?["episodeCount"]?.GetValue<int?>();
                        animeMap[id] = (title, img, epCount);
                    }
                }
            }

            // 3. For each entry, get mappings to MAL/AniList
            var aniIds = new List<int>();
            var malIds = new List<int>();

            foreach (var entry in entriesData ?? new JsonArray())
            {
                var relAnime = entry?["relationships"]?["anime"]?["data"];
                string animeId = relAnime?["id"]?.ToString() ?? "";
                if (string.IsNullOrEmpty(animeId)) continue;

                var mapRes = await _httpClient.GetAsync($"{KitsuBaseUrl}/anime/{animeId}/mappings");
                if (mapRes.IsSuccessStatusCode)
                {
                    var mapJson = JsonNode.Parse(await mapRes.Content.ReadAsStringAsync());
                    var mapData = mapJson?["data"]?.AsArray();
                    if (mapData != null)
                    {
                        foreach (var m in mapData)
                        {
                            string site = m?["attributes"]?["externalSite"]?.ToString() ?? "";
                            string extIdStr = m?["attributes"]?["externalId"]?.ToString() ?? "";
                            if (int.TryParse(extIdStr, out int extId))
                            {
                                if (site.Contains("anilist", StringComparison.OrdinalIgnoreCase)) aniIds.Add(extId);
                                else if (site.Contains("myanimelist", StringComparison.OrdinalIgnoreCase)) malIds.Add(extId);
                            }
                        }
                    }
                }
            }

            var startOfMonth = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero).AddDays(-7);
            var endOfMonth = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1).AddDays(7);
            long startSec = startOfMonth.ToUnixTimeSeconds();
            long endSec = endOfMonth.ToUnixTimeSeconds();
            var episodes = new List<CalendarMonthEpisode>();

            // 4. Query AniList for live TV broadcast schedules
            if (malIds.Count > 0 || aniIds.Count > 0)
            {
                const string mapGql = @"
query ($aniIds: [Int], $malIds: [Int]) {
  byAni: Page(page: 1, perPage: 50) {
    media(id_in: $aniIds, type: ANIME) {
      id
      idMal
    }
  }
  byMal: Page(page: 1, perPage: 50) {
    media(idMal_in: $malIds, type: ANIME) {
      id
      idMal
    }
  }
}";
                var gqlRes = await ExecuteGraphQLAsync(mapGql, new { aniIds = aniIds.Distinct().Take(50).ToList(), malIds = malIds.Distinct().Take(50).ToList() }, bypassCache);
                var byAni = gqlRes?["data"]?["byAni"]?["media"]?.AsArray();
                var byMal = gqlRes?["data"]?["byMal"]?["media"]?.AsArray();
                
                var resolvedAniIds = new HashSet<int>();
                if (byAni != null)
                {
                    foreach (var m in byAni)
                    {
                        int id = m?["id"]?.GetValue<int>() ?? 0;
                        if (id > 0) resolvedAniIds.Add(id);
                    }
                }
                if (byMal != null)
                {
                    foreach (var m in byMal)
                    {
                        int id = m?["id"]?.GetValue<int>() ?? 0;
                        if (id > 0) resolvedAniIds.Add(id);
                    }
                }

                if (resolvedAniIds.Count > 0)
                {
                    const string scheduleQuery = @"
query ($page: Int, $perPage: Int, $mediaId_in: [Int], $airingAt_greater: Int, $airingAt_lesser: Int) {
  Page(page: $page, perPage: $perPage) {
    pageInfo {
      hasNextPage
    }
    airingSchedules(mediaId_in: $mediaId_in, airingAt_greater: $airingAt_greater, airingAt_lesser: $airingAt_lesser, sort: TIME) {
      id
      episode
      airingAt
      timeUntilAiring
      media {
        id
        idMal
        title {
          romaji
          english
          native
        }
        coverImage {
          extraLarge
          large
        }
        bannerImage
        format
        status
        episodes
        duration
        averageScore
        genres
        studios(isMain: true) {
          nodes {
            name
          }
        }
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
      }
    }
  }
}";

                    var idChunks = resolvedAniIds.Chunk(50).ToList();
                    foreach (var chunk in idChunks)
                    {
                        int page = 1;
                        bool hasNextPage = true;
                        while (hasNextPage && page <= 5)
                        {
                            var schedRes = await ExecuteGraphQLAsync(scheduleQuery, new
                            {
                                page = page,
                                perPage = 50,
                                mediaId_in = chunk,
                                airingAt_greater = (int)startSec,
                                airingAt_lesser = (int)endSec
                            }, bypassCache);

                            var pageNode = schedRes?["data"]?["Page"];
                            if (pageNode == null) break;

                            hasNextPage = pageNode["pageInfo"]?["hasNextPage"]?.GetValue<bool>() ?? false;
                            var schedules = pageNode["airingSchedules"]?.AsArray();
                            if (schedules != null)
                            {
                                foreach (var sch in schedules)
                                {
                                    var media = sch?["media"];
                                    if (media == null) continue;

                                    int mId = media["id"]?.GetValue<int>() ?? 0;
                                    int? malId = media["idMal"]?.GetValue<int?>();

                                    long airSec = sch?["airingAt"]?.GetValue<long>() ?? 0;
                                    long timeUntil = sch?["timeUntilAiring"]?.GetValue<long>() ?? 0;
                                    int epNum = sch?["episode"]?.GetValue<int>() ?? 1;

                                    var airUtc = DateTimeOffset.FromUnixTimeSeconds(airSec).ToUniversalTime();

                                    var epObj = new CalendarMonthEpisode
                                    {
                                        Id = $"kitsu_ani_{mId}_ep{epNum}",
                                        AniListId = mId,
                                        MalId = malId,
                                        TitleEnglish = media["title"]?["english"]?.ToString() ?? media["title"]?["romaji"]?.ToString() ?? "",
                                        TitleRomaji = media["title"]?["romaji"]?.ToString() ?? "",
                                        TitleNative = media["title"]?["native"]?.ToString() ?? "",
                                        CoverImage = media["coverImage"]?["extraLarge"]?.ToString() ?? media["coverImage"]?["large"]?.ToString() ?? "",
                                        BannerImage = media["bannerImage"]?.ToString(),
                                        Format = media["format"]?.ToString() ?? "TV",
                                        Status = media["status"]?.ToString() ?? "RELEASING",
                                        TotalEpisodes = media["episodes"]?.GetValue<int?>(),
                                        EpisodeDuration = media["duration"]?.GetValue<int?>(),
                                        EpisodeNumber = epNum,
                                        AiringAt = airUtc,
                                        AiringTimeFormatted = airUtc.ToString("HH:mm"),
                                        AiringDateFormatted = airUtc.ToString("yyyy-MM-dd"),
                                        TimeUntilAiringSeconds = timeUntil,
                                        AverageScore = media["averageScore"]?.GetValue<double?>(),
                                        Synopsis = media["description"]?.ToString() ?? "",
                                        AniListUrl = media["siteUrl"]?.ToString(),
                                        MalUrl = malId.HasValue ? $"https://myanimelist.net/anime/{malId}" : null,
                                        SiteUrl = malId.HasValue ? $"https://myanimelist.net/anime/{malId}" : media["siteUrl"]?.ToString(),
                                        StreamingLinks = StreamingHelper.ParseStreamingLinks(media["externalLinks"]),
                                        ListStatus = "Watching"
                                    };

                                    var genres = media["genres"]?.AsArray();
                                    if (genres != null)
                                    {
                                        epObj.Genres = genres.Select(g => g?.ToString() ?? "").Where(g => !string.IsNullOrEmpty(g)).ToList();
                                    }

                                    var studios = media["studios"]?["nodes"]?.AsArray();
                                    if (studios != null)
                                    {
                                        epObj.Studios = studios.Select(s => s?["name"]?.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList();
                                    }

                                    episodes.Add(epObj);
                                }
                            }
                            page++;
                        }
                    }
                }
            }

            return (avatarUrl, episodes.OrderBy(e => e.AiringAt).ToList(), totalWatching);
        }

        private async Task<JsonNode?> ExecuteGraphQLAsync(string query, object variables, bool bypassCache = false)
        {
            var cacheKey = $"{query.GetHashCode()}_{JsonSerializer.Serialize(variables)}";
            if (!bypassCache && _kitsuGqlCache.TryGetValue(cacheKey, out var entry) && DateTimeOffset.UtcNow - entry.cachedAt < TimeSpan.FromMinutes(15))
            {
                return entry.data;
            }

            var payload = new { query = query, variables = variables };

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
                        Console.WriteLine($"[AniList GraphQL Error in KitsuService] HTTP {response.StatusCode}: {errStr}");
                        return null;
                    }

                    var jsonString = await response.Content.ReadAsStringAsync();
                    var node = JsonNode.Parse(jsonString);
                    if (node != null)
                    {
                        _kitsuGqlCache[cacheKey] = (DateTimeOffset.UtcNow, node);
                    }
                    return node;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AniList GraphQL Exception in KitsuService] attempt {attempt}: {ex.Message}");
                    if (attempt == 2) return null;
                    await Task.Delay(1000);
                }
            }

            return null;
        }
    }
}
