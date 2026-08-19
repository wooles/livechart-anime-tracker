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
            var userUrl = $"{KitsuBaseUrl}/users?filter[name]={Uri.EscapeDataString(username)}";
            var userRes = await _httpClient.GetAsync(userUrl);
            userRes.EnsureSuccessStatusCode();

            var userJson = JsonNode.Parse(await userRes.Content.ReadAsStringAsync());
            var users = userJson?["data"]?.AsArray();
            if (users == null || users.Count == 0)
            {
                throw new Exception($"User '{username}' was not found on Kitsu.");
            }

            var userNode = users[0];
            var userId = userNode?["id"]?.ToString();
            var avatarUrl = userNode?["attributes"]?["avatar"]?["large"]?.ToString() 
                ?? userNode?["attributes"]?["avatar"]?["original"]?.ToString();

            var libraryUrl = $"{KitsuBaseUrl}/library-entries?filter[userId]={userId}&filter[kind]=anime&filter[status]=current,planned&include=anime,anime.mappings&page[limit]=100";
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
            var mappingsDict = new Dictionary<string, (int? malId, int? aniId)>();

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
                    else if (incType == "mappings" && inc != null)
                    {
                        var site = inc["attributes"]?["externalSite"]?.ToString();
                        var extIdStr = inc["attributes"]?["externalId"]?.ToString();
                        if (int.TryParse(extIdStr, out var extId))
                        {
                            var itemAnimeId = inc["relationships"]?["item"]?["data"]?["id"]?.ToString();
                            if (itemAnimeId != null)
                            {
                                if (!mappingsDict.TryGetValue(itemAnimeId, out var pair))
                                {
                                    pair = (null, null);
                                }
                                if (site == "myanimelist/anime") pair.malId = extId;
                                if (site == "anilist/anime") pair.aniId = extId;
                                mappingsDict[itemAnimeId] = pair;
                            }
                        }
                    }
                }
            }

            var startOfMonth = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero);
            var endOfMonth = new DateTimeOffset(year, month, DateTime.DaysInMonth(year, month), 23, 59, 59, TimeSpan.Zero);
            var episodes = new List<CalendarMonthEpisode>();

            var malIds = new List<int>();
            var aniIds = new List<int>();

            foreach (var kvp in mappingsDict)
            {
                if (kvp.Value.malId.HasValue) malIds.Add(kvp.Value.malId.Value);
                if (kvp.Value.aniId.HasValue) aniIds.Add(kvp.Value.aniId.Value);
            }

            if (malIds.Count > 0 || aniIds.Count > 0)
            {
                const string scheduleGql = @"
query ($malIds: [Int], $aniIds: [Int]) {
  Page(page: 1, perPage: 50) {
    media(idMal_in: $malIds, id_in: $aniIds, type: ANIME) {
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
      nextAiringEpisode { episode airingAt timeUntilAiring }
    }
  }
}";
                var gqlRes = await ExecuteGraphQLAsync(scheduleGql, new { malIds = malIds.Distinct().Take(40).ToList(), aniIds = aniIds.Distinct().Take(40).ToList() });
                var mediaList = gqlRes?["data"]?["Page"]?["media"]?.AsArray();
                if (mediaList != null)
                {
                    foreach (var media in mediaList)
                    {
                        if (media == null) continue;
                        int mId = media["id"]?.GetValue<int>() ?? 0;
                        int? malId = media["idMal"]?.GetValue<int?>();
                        var nextEp = media["nextAiringEpisode"];

                        if (nextEp != null)
                        {
                            int anchorEp = nextEp["episode"]?.GetValue<int>() ?? 1;
                            long anchorAirSec = nextEp["airingAt"]?.GetValue<long>() ?? 0;
                            int? totalEp = media["episodes"]?.GetValue<int?>();
                            var anchorAirUtc = DateTimeOffset.FromUnixTimeSeconds(anchorAirSec).ToUniversalTime();

                            for (int k = -12; k <= 12; k++)
                            {
                                int targetEp = anchorEp + k;
                                if (targetEp < 1) continue;
                                if (totalEp.HasValue && targetEp > totalEp.Value) continue;

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
                                    SiteUrl = malId.HasValue ? $"https://myanimelist.net/anime/{malId}" : media["siteUrl"]?.ToString(),
                                    ListStatus = "Watching"
                                });
                            }
                        }
                    }
                }
            }

            return (avatarUrl, episodes.OrderBy(e => e.AiringAt).ToList(), entries.Count);
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
                    var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                    var response = await _httpClient.PostAsync(GraphQlEndpoint, content);

                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        await Task.Delay(1500 * (attempt + 1));
                        continue;
                    }

                    if (!response.IsSuccessStatusCode) return null;

                    var jsonString = await response.Content.ReadAsStringAsync();
                    var node = JsonNode.Parse(jsonString);
                    if (node != null)
                    {
                        _kitsuGqlCache[cacheKey] = (DateTimeOffset.UtcNow, node);
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
    }
}
