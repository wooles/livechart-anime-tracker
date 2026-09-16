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

            // 2. Fetch current/planned library entries with included anime and anime.mappings in batches
            int offset = 0;
            const int limit = 100;
            var allEntries = new List<JsonNode>();
            var animeMap = new Dictionary<string, (string title, string status, DateTime? start, DateTime? end, string img, int? totalEp, List<string> mappingIds)>();
            var mappingsMap = new Dictionary<string, (string site, int extId)>();
            var entryAnimeUserMap = new Dictionary<string, (string userStatus, int progress, double? score)>();

            while (offset < 500)
            {
                var entriesUrl = $"{KitsuBaseUrl}/library-entries?filter[userId]={userId}&filter[status]=current,planned&include=anime,anime.mappings&page[limit]={limit}&page[offset]={offset}";
                var entriesRes = await _httpClient.GetAsync(entriesUrl);
                if (!entriesRes.IsSuccessStatusCode)
                {
                    break;
                }

                var entriesJson = JsonNode.Parse(await entriesRes.Content.ReadAsStringAsync());
                var entriesData = entriesJson?["data"]?.AsArray();
                var includedData = entriesJson?["included"]?.AsArray();

                if (entriesData == null || entriesData.Count == 0) break;

                foreach (var ed in entriesData)
                {
                    if (ed != null) allEntries.Add(ed);
                }

                // Collect mappings from included
                if (includedData != null)
                {
                    foreach (var inc in includedData)
                    {
                        string type = inc?["type"]?.ToString() ?? "";
                        string id = inc?["id"]?.ToString() ?? "";
                        if (string.IsNullOrEmpty(id)) continue;

                        if (type == "mappings")
                        {
                            var attr = inc?["attributes"];
                            string site = attr?["externalSite"]?.ToString() ?? "";
                            string extIdStr = attr?["externalId"]?.ToString() ?? "";
                            if (int.TryParse(extIdStr, out int extId))
                            {
                                mappingsMap[id] = (site, extId);
                            }
                        }
                        else if (type == "anime")
                        {
                            var attr = inc?["attributes"];
                            string title = attr?["canonicalTitle"]?.ToString() ?? attr?["titles"]?["en"]?.ToString() ?? attr?["titles"]?["en_jp"]?.ToString() ?? "";
                            string status = attr?["status"]?.ToString() ?? "";
                            string img = attr?["posterImage"]?["large"]?.ToString() ?? attr?["posterImage"]?["medium"]?.ToString() ?? "";
                            int? epCount = attr?["episodeCount"]?.GetValue<int?>();
                            DateTime? start = DateTime.TryParse(attr?["startDate"]?.ToString(), out var sDt) ? sDt : null;
                            DateTime? end = DateTime.TryParse(attr?["endDate"]?.ToString(), out var eDt) ? eDt : null;

                            var mapIds = new List<string>();
                            var mapRelData = inc?["relationships"]?["mappings"]?["data"]?.AsArray();
                            if (mapRelData != null)
                            {
                                foreach (var mr in mapRelData)
                                {
                                    string mId = mr?["id"]?.ToString() ?? "";
                                    if (!string.IsNullOrEmpty(mId)) mapIds.Add(mId);
                                }
                            }

                            animeMap[id] = (title, status, start, end, img, epCount, mapIds);
                        }
                    }
                }

                // Associate entries with anime
                foreach (var entry in entriesData)
                {
                    var relAnime = entry?["relationships"]?["anime"]?["data"];
                    string animeId = relAnime?["id"]?.ToString() ?? "";
                    if (string.IsNullOrEmpty(animeId)) continue;

                    string uStatus = entry?["attributes"]?["status"]?.ToString() ?? "current";
                    int progress = entry?["attributes"]?["progress"]?.GetValue<int?>() ?? 0;
                    double? score = entry?["attributes"]?["ratingTwenty"]?.GetValue<double?>();

                    entryAnimeUserMap[animeId] = (uStatus, progress, score);
                }

                if (entriesData.Count < limit) break;
                offset += limit;
            }

            int totalWatching = allEntries.Count;
            if (totalWatching == 0)
            {
                return (avatarUrl, new List<CalendarMonthEpisode>(), 0);
            }

            var startOfMonth = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero).AddDays(-7);
            var endOfMonth = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1).AddDays(7);
            long startSec = startOfMonth.ToUnixTimeSeconds();
            long endSec = endOfMonth.ToUnixTimeSeconds();
            var episodes = new List<CalendarMonthEpisode>();

            // 3. Filter candidate anime that could air in target month
            var aniIdToUserEntry = new Dictionary<int, (string title, string img, int? totalEp, string userStatus, int progress, double? score)>();
            var malIdToUserEntry = new Dictionary<int, (string title, string img, int? totalEp, string userStatus, int progress, double? score)>();

            foreach (var (animeId, a) in animeMap)
            {
                bool isCandidate = (a.status != "finished" || (a.end.HasValue && a.end.Value >= startOfMonth.Date))
                                && (!a.start.HasValue || a.start.Value <= endOfMonth.Date);

                if (!isCandidate) continue;

                entryAnimeUserMap.TryGetValue(animeId, out var uEntry);

                foreach (var mId in a.mappingIds)
                {
                    if (mappingsMap.TryGetValue(mId, out var mapping))
                    {
                        if (mapping.site.Contains("anilist", StringComparison.OrdinalIgnoreCase))
                        {
                            aniIdToUserEntry[mapping.extId] = (a.title, a.img, a.totalEp, uEntry.userStatus, uEntry.progress, uEntry.score);
                        }
                        else if (mapping.site.Contains("myanimelist", StringComparison.OrdinalIgnoreCase))
                        {
                            malIdToUserEntry[mapping.extId] = (a.title, a.img, a.totalEp, uEntry.userStatus, uEntry.progress, uEntry.score);
                        }
                    }
                }
            }

            // Fallback if no candidate filtered (e.g. status/dates missing in Kitsu)
            if (aniIdToUserEntry.Count == 0 && malIdToUserEntry.Count == 0)
            {
                foreach (var (animeId, a) in animeMap)
                {
                    entryAnimeUserMap.TryGetValue(animeId, out var uEntry);
                    foreach (var mId in a.mappingIds)
                    {
                        if (mappingsMap.TryGetValue(mId, out var mapping))
                        {
                            if (mapping.site.Contains("anilist", StringComparison.OrdinalIgnoreCase))
                                aniIdToUserEntry[mapping.extId] = (a.title, a.img, a.totalEp, uEntry.userStatus, uEntry.progress, uEntry.score);
                            else if (mapping.site.Contains("myanimelist", StringComparison.OrdinalIgnoreCase))
                                malIdToUserEntry[mapping.extId] = (a.title, a.img, a.totalEp, uEntry.userStatus, uEntry.progress, uEntry.score);
                        }
                    }
                }
            }

            // 4. Query AniList for live TV broadcast schedules in sequential chunks
            const string singleQuery = @"
query ($aniIds: [Int], $malIds: [Int]) {
  byAni: Page(page: 1, perPage: 50) {
    media(id_in: $aniIds, type: ANIME) {
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
      startDate {
        year
        month
        day
      }
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
      airingSchedule(perPage: 25) {
        nodes {
          id
          episode
          airingAt
          timeUntilAiring
        }
      }
    }
  }
  byMal: Page(page: 1, perPage: 50) {
    media(idMal_in: $malIds, type: ANIME) {
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
      startDate {
        year
        month
        day
      }
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
      airingSchedule(perPage: 25) {
        nodes {
          id
          episode
          airingAt
          timeUntilAiring
        }
      }
    }
  }
}";

            var aniChunks = aniIdToUserEntry.Keys.Chunk(50).ToList();
            var malChunks = malIdToUserEntry.Keys.Chunk(50).ToList();
            int maxChunks = Math.Max(aniChunks.Count, malChunks.Count);
            if (maxChunks == 0 && (aniIdToUserEntry.Count > 0 || malIdToUserEntry.Count > 0)) maxChunks = 1;

            var processedMediaIds = new HashSet<int>();

            for (int i = 0; i < maxChunks; i++)
            {
                var curAni = i < aniChunks.Count ? aniChunks[i].ToList() : new List<int>();
                var curMal = i < malChunks.Count ? malChunks[i].ToList() : new List<int>();

                var gqlRes = await ExecuteGraphQLAsync(singleQuery, new { aniIds = curAni, malIds = curMal }, bypassCache);
                if (gqlRes == null) continue;

                var allMedia = new List<JsonNode>();
                var byAni = gqlRes["data"]?["byAni"]?["media"]?.AsArray();
                var byMal = gqlRes["data"]?["byMal"]?["media"]?.AsArray();

                if (byAni != null)
                {
                    foreach (var m in byAni) if (m != null) allMedia.Add(m);
                }
                if (byMal != null)
                {
                    foreach (var m in byMal) if (m != null) allMedia.Add(m);
                }

                foreach (var media in allMedia)
                {
                    int mId = media["id"]?.GetValue<int>() ?? 0;
                    if (mId == 0 || !processedMediaIds.Add(mId)) continue;

                    int? malId = media["idMal"]?.GetValue<int?>();

                    // Find matching user entry
                    (string title, string img, int? totalEp, string userStatus, int progress, double? score) uEntry = default;
                    if (!aniIdToUserEntry.TryGetValue(mId, out uEntry) && malId.HasValue)
                    {
                        malIdToUserEntry.TryGetValue(malId.Value, out uEntry);
                    }

                    string listStatus = (uEntry.userStatus == "planned") ? "PlanToWatch" : "Watching";

                    bool hasSchedulesInMonth = false;
                    var schedules = media["airingSchedule"]?["nodes"]?.AsArray();
                    if (schedules != null)
                    {
                        foreach (var sch in schedules)
                        {
                            long airSec = sch?["airingAt"]?.GetValue<long>() ?? 0;
                            if (airSec < startSec || airSec > endSec) continue;

                            hasSchedulesInMonth = true;
                            long timeUntil = sch?["timeUntilAiring"]?.GetValue<long>() ?? 0;
                            int epNum = sch?["episode"]?.GetValue<int>() ?? 1;
                            var airUtc = DateTimeOffset.FromUnixTimeSeconds(airSec).ToUniversalTime();

                            var epObj = new CalendarMonthEpisode
                            {
                                Id = $"kitsu_ani_{mId}_ep{epNum}",
                                AniListId = mId,
                                MalId = malId,
                                TitleEnglish = media["title"]?["english"]?.ToString() ?? media["title"]?["romaji"]?.ToString() ?? uEntry.title ?? "",
                                TitleRomaji = media["title"]?["romaji"]?.ToString() ?? uEntry.title ?? "",
                                TitleNative = media["title"]?["native"]?.ToString() ?? "",
                                CoverImage = media["coverImage"]?["extraLarge"]?.ToString() ?? media["coverImage"]?["large"]?.ToString() ?? uEntry.img ?? "",
                                BannerImage = media["bannerImage"]?.ToString(),
                                Format = media["format"]?.ToString() ?? "TV",
                                Status = media["status"]?.ToString() ?? "RELEASING",
                                TotalEpisodes = media["episodes"]?.GetValue<int?>() ?? uEntry.totalEp,
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
                                ListStatus = listStatus,
                                UserProgress = uEntry.progress,
                                UserScore = uEntry.score
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

                    // Fallback for Movies/OVAs/Specials released in this month
                    if (!hasSchedulesInMonth)
                    {
                        string fmt = media["format"]?.ToString() ?? "TV";
                        if (fmt == "MOVIE" || fmt == "OVA" || fmt == "SPECIAL" || fmt == "Movie" || fmt == "Special")
                        {
                            int? sYear = media["startDate"]?["year"]?.GetValue<int?>();
                            int? sMonth = media["startDate"]?["month"]?.GetValue<int?>();
                            int? sDay = media["startDate"]?["day"]?.GetValue<int?>();

                            if (sYear == year && sMonth == month && sDay.HasValue)
                            {
                                var releaseDate = new DateTimeOffset(year, month, sDay.Value, 14, 0, 0, TimeSpan.Zero);
                                episodes.Add(new CalendarMonthEpisode
                                {
                                    Id = $"kitsu_ani_{mId}_m",
                                    AniListId = mId,
                                    MalId = malId,
                                    TitleEnglish = media["title"]?["english"]?.ToString() ?? media["title"]?["romaji"]?.ToString() ?? uEntry.title ?? "",
                                    TitleRomaji = media["title"]?["romaji"]?.ToString() ?? uEntry.title ?? "",
                                    TitleNative = media["title"]?["native"]?.ToString() ?? "",
                                    CoverImage = media["coverImage"]?["extraLarge"]?.ToString() ?? media["coverImage"]?["large"]?.ToString() ?? uEntry.img ?? "",
                                    BannerImage = media["bannerImage"]?.ToString(),
                                    Format = fmt,
                                    Status = media["status"]?.ToString() ?? "RELEASING",
                                    TotalEpisodes = media["episodes"]?.GetValue<int?>() ?? uEntry.totalEp ?? 1,
                                    EpisodeDuration = media["duration"]?.GetValue<int?>(),
                                    EpisodeNumber = 1,
                                    AiringAt = releaseDate,
                                    AiringTimeFormatted = releaseDate.ToString("HH:mm"),
                                    AiringDateFormatted = releaseDate.ToString("yyyy-MM-dd"),
                                    TimeUntilAiringSeconds = (long)(releaseDate - DateTimeOffset.UtcNow).TotalSeconds,
                                    AverageScore = media["averageScore"]?.GetValue<double?>(),
                                    Synopsis = media["description"]?.ToString() ?? "",
                                    AniListUrl = media["siteUrl"]?.ToString(),
                                    MalUrl = malId.HasValue ? $"https://myanimelist.net/anime/{malId}" : null,
                                    SiteUrl = malId.HasValue ? $"https://myanimelist.net/anime/{malId}" : media["siteUrl"]?.ToString(),
                                    StreamingLinks = StreamingHelper.ParseStreamingLinks(media["externalLinks"]),
                                    ListStatus = listStatus,
                                    UserProgress = uEntry.progress,
                                    UserScore = uEntry.score
                                });
                            }
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
