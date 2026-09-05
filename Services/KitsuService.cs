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
        Task<(string? avatarUrl, List<CalendarMonthEpisode> episodes, int totalWatching)> GetWatchingMonthEpisodesAsync(string username, int year, int month);
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

        public async Task<(string? avatarUrl, List<CalendarMonthEpisode> episodes, int totalWatching)> GetWatchingMonthEpisodesAsync(string username, int year, int month)
        {
            // 1. User lookup with slug / name fallback
            var userUrl = $"{KitsuBaseUrl}/users?filter[slug]={Uri.EscapeDataString(username)}";
            var userRes = await _httpClient.GetAsync(userUrl);
            var userJson = userRes.IsSuccessStatusCode ? JsonNode.Parse(await userRes.Content.ReadAsStringAsync()) : null;
            var users = userJson?["data"]?.AsArray();

            if (users == null || users.Count == 0)
            {
                userUrl = $"{KitsuBaseUrl}/users?filter[name]={Uri.EscapeDataString(username)}";
                userRes = await _httpClient.GetAsync(userUrl);
                userJson = userRes.IsSuccessStatusCode ? JsonNode.Parse(await userRes.Content.ReadAsStringAsync()) : null;
                users = userJson?["data"]?.AsArray();
            }

            if (users == null || users.Count == 0)
            {
                throw new Exception($"User '{username}' was not found on Kitsu.");
            }

            var userNode = users[0];
            var userId = userNode?["id"]?.ToString();
            var avatarUrl = userNode?["attributes"]?["avatar"]?["large"]?.ToString() 
                ?? userNode?["attributes"]?["avatar"]?["original"]?.ToString();

            // 2. Fetch both 'current' (Watching) and 'planned' (Plan to Watch) entries
            var animeDict = new Dictionary<string, JsonNode>();
            var rawMappingsDict = new Dictionary<string, (string site, int extId)>();
            int totalWatching = 0;

            var statusList = new[] { "current", "planned" };
            foreach (var status in statusList)
            {
                var libraryUrl = $"{KitsuBaseUrl}/library-entries?filter[userId]={userId}&filter[kind]=anime&filter[status]={status}&include=anime,anime.mappings&page[limit]=50";
                var libRes = await _httpClient.GetAsync(libraryUrl);
                if (!libRes.IsSuccessStatusCode) continue;

                var libJson = JsonNode.Parse(await libRes.Content.ReadAsStringAsync());
                var entries = libJson?["data"]?.AsArray();
                var included = libJson?["included"]?.AsArray();

                if (entries != null) totalWatching += entries.Count;

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
                        else if (incType == "mappings" && incId != null && inc != null)
                        {
                            var site = inc["attributes"]?["externalSite"]?.ToString() ?? "";
                            var extIdStr = inc["attributes"]?["externalId"]?.ToString() ?? "";
                            if (int.TryParse(extIdStr, out var extId))
                            {
                                rawMappingsDict[incId] = (site, extId);
                            }
                        }
                    }
                }
            }

            if (animeDict.Count == 0)
            {
                return (avatarUrl, new List<CalendarMonthEpisode>(), 0);
            }

            // 3. Resolve MAL and AniList IDs from anime mapping relationships
            var malIds = new List<int>();
            var aniIds = new List<int>();

            foreach (var anime in animeDict.Values)
            {
                var mapRefs = anime["relationships"]?["mappings"]?["data"]?.AsArray();
                if (mapRefs != null)
                {
                    foreach (var mRef in mapRefs)
                    {
                        var mId = mRef?["id"]?.ToString();
                        if (mId != null && rawMappingsDict.TryGetValue(mId, out var mInfo))
                        {
                            if (mInfo.site == "myanimelist/anime") malIds.Add(mInfo.extId);
                            if (mInfo.site == "anilist/anime") aniIds.Add(mInfo.extId);
                        }
                    }
                }
            }

            var startOfMonth = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero);
            var endOfMonth = new DateTimeOffset(year, month, DateTime.DaysInMonth(year, month), 23, 59, 59, TimeSpan.Zero);
            var episodes = new List<CalendarMonthEpisode>();

            // 4. Query AniList for live TV broadcast schedules
            if (malIds.Count > 0 || aniIds.Count > 0)
            {
                const string scheduleGql = @"
query ($aniIds: [Int], $malIds: [Int]) {
  byAni: Page(page: 1, perPage: 50) {
    media(id_in: $aniIds, type: ANIME) {
      id
      idMal
      title { romaji english }
      coverImage { large }
      format
      status
      episodes
      averageScore
      description
      siteUrl
      externalLinks { id site url type icon color }
      nextAiringEpisode { episode airingAt timeUntilAiring }
    }
  }
  byMal: Page(page: 1, perPage: 50) {
    media(idMal_in: $malIds, type: ANIME) {
      id
      idMal
      title { romaji english }
      coverImage { large }
      format
      status
      episodes
      averageScore
      description
      siteUrl
      externalLinks { id site url type icon color }
      nextAiringEpisode { episode airingAt timeUntilAiring }
    }
  }
}";
                var gqlRes = await ExecuteGraphQLAsync(scheduleGql, new { aniIds = aniIds.Distinct().Take(50).ToList(), malIds = malIds.Distinct().Take(50).ToList() });
                var byAni = gqlRes?["data"]?["byAni"]?["media"]?.AsArray();
                var byMal = gqlRes?["data"]?["byMal"]?["media"]?.AsArray();
                
                var allMedia = new Dictionary<int, JsonNode>();
                if (byAni != null)
                {
                    foreach (var m in byAni)
                    {
                        if (m != null && m["id"] != null) allMedia[m["id"]!.GetValue<int>()] = m;
                    }
                }
                if (byMal != null)
                {
                    foreach (var m in byMal)
                    {
                        if (m != null && m["id"] != null) allMedia[m["id"]!.GetValue<int>()] = m;
                    }
                }

                foreach (var media in allMedia.Values)
                {
                    int mId = media["id"]?.GetValue<int>() ?? 0;
                    int? malId = media["idMal"]?.GetValue<int?>();
                    var nextEp = media["nextAiringEpisode"];

                    if (nextEp != null)
                    {
                        int anchorEp = nextEp["episode"]?.GetValue<int>() ?? 1;
                        long anchorAirSec = nextEp["airingAt"]?.GetValue<long>() ?? 0;
                        int? totalEp = media["episodes"]?.GetValue<int?>();
                        var anchorAirUtc = DateTimeOffset.FromUnixTimeSeconds(anchorAirSec).ToUniversalTime();

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
                                Id = $"kitsu_ani_{mId}_ep{targetEp}",
                                AniListId = mId,
                                MalId = malId,
                                TitleEnglish = media["title"]?["english"]?.ToString() ?? media["title"]?["romaji"]?.ToString() ?? "",
                                TitleRomaji = media["title"]?["romaji"]?.ToString() ?? "",
                                CoverImage = media["coverImage"]?["large"]?.ToString() ?? "",
                                Format = media["format"]?.ToString() ?? "TV",
                                TotalEpisodes = totalEp,
                                EpisodeNumber = targetEp,
                                AiringAt = targetAirUtc,
                                AiringTimeFormatted = targetAirUtc.ToString("HH:mm"),
                                AiringDateFormatted = targetAirUtc.ToString("yyyy-MM-dd"),
                                TimeUntilAiringSeconds = (long)(targetAirUtc - DateTimeOffset.UtcNow).TotalSeconds,
                                AverageScore = media["averageScore"]?.GetValue<double?>(),
                                Synopsis = media["description"]?.ToString() ?? "",
                                AniListUrl = media["siteUrl"]?.ToString(),
                                MalUrl = malId.HasValue ? $"https://myanimelist.net/anime/{malId}" : null,
                                SiteUrl = malId.HasValue ? $"https://myanimelist.net/anime/{malId}" : media["siteUrl"]?.ToString(),
                                StreamingLinks = StreamingHelper.ParseStreamingLinks(media["externalLinks"]),
                                ListStatus = "Watching"
                            });
                        }
                    }
                }
            }

            return (avatarUrl, episodes.OrderBy(e => e.AiringAt).ToList(), totalWatching);
        }

        private async Task<JsonNode?> ExecuteGraphQLAsync(string query, object variables)
        {
            var cacheKey = $"{query.GetHashCode()}_{JsonSerializer.Serialize(variables)}";
            if (_kitsuGqlCache.TryGetValue(cacheKey, out var entry) && DateTimeOffset.UtcNow - entry.cachedAt < TimeSpan.FromMinutes(15))
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
