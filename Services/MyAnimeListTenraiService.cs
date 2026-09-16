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
        Task<(string? avatarUrl, List<CalendarMonthEpisode> episodes, int totalWatching)> GetWatchingMonthEpisodesAsync(string username, int year, int month, bool bypassCache = false);
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

        public async Task<(string? avatarUrl, List<CalendarMonthEpisode> episodes, int totalWatching)> GetWatchingMonthEpisodesAsync(string username, int year, int month, bool bypassCache = false)
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

            var startOfMonth = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero).AddDays(-7);
            var endOfMonth = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1).AddDays(7);
            long startSec = startOfMonth.ToUnixTimeSeconds();
            long endSec = endOfMonth.ToUnixTimeSeconds();

            var episodes = new List<CalendarMonthEpisode>();

            // 1. Filter candidate shows from watchingList (airingStatus != 2 or ended in/after this month)
            var candidateMalItems = watchingList
                .Where(w => w.malId > 0 && (w.airingStatus != 2 || (w.endDate.HasValue && w.endDate.Value >= startOfMonth.DateTime)))
                .ToList();

            var malIds = candidateMalItems.Select(w => w.malId).Distinct().ToList();
            if (malIds.Count == 0)
            {
                malIds = watchingList.Where(w => w.malId > 0).Select(w => w.malId).Distinct().ToList();
            }

            try
            {
                const string malToAniQuery = @"
query ($malIds: [Int]) {
  Page(page: 1, perPage: 50) {
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
                var malChunks = malIds.Chunk(50).ToList();

                foreach (var chunk in malChunks)
                {
                    var mapRes = await ExecuteGraphQLAsync(malToAniQuery, new { malIds = chunk }, bypassCache);
                    var pageNode = mapRes?["data"]?["Page"];
                    if (pageNode == null) continue;

                    var mediaList = pageNode["media"]?.AsArray();
                    if (mediaList != null)
                    {
                        foreach (var media in mediaList)
                        {
                            if (media == null) continue;
                            int mId = media["id"]?.GetValue<int>() ?? 0;
                            int? malId = media["idMal"]?.GetValue<int?>();
                            if (!malId.HasValue || malId.Value <= 0) continue;

                            var userEntry = watchingList.FirstOrDefault(w => w.malId == malId.Value);

                            var schedules = media["airingSchedule"]?["nodes"]?.AsArray();
                            bool foundAiringInWindow = false;

                            if (schedules != null && schedules.Count > 0)
                            {
                                foreach (var sch in schedules)
                                {
                                    if (sch == null) continue;
                                    long airSec = sch["airingAt"]?.GetValue<long>() ?? 0;
                                    if (airSec < startSec || airSec > endSec) continue;

                                    foundAiringInWindow = true;
                                    long timeUntil = sch["timeUntilAiring"]?.GetValue<long>() ?? 0;
                                    int epNum = sch["episode"]?.GetValue<int>() ?? 1;
                                    var airUtc = DateTimeOffset.FromUnixTimeSeconds(airSec).ToUniversalTime();

                                    var epObj = new CalendarMonthEpisode
                                    {
                                        Id = $"mal_{malId.Value}_ep{epNum}",
                                        MalId = malId,
                                        AniListId = mId,
                                        TitleEnglish = userEntry.title ?? media["title"]?["english"]?.ToString() ?? media["title"]?["romaji"]?.ToString() ?? "",
                                        TitleRomaji = media["title"]?["romaji"]?.ToString() ?? userEntry.title ?? "",
                                        TitleNative = media["title"]?["native"]?.ToString() ?? "",
                                        CoverImage = (!string.IsNullOrEmpty(userEntry.img) ? userEntry.img : media["coverImage"]?["large"]?.ToString()) ?? "",
                                        BannerImage = media["bannerImage"]?.ToString(),
                                        Format = media["format"]?.ToString() ?? userEntry.mediaType ?? "TV",
                                        Status = media["status"]?.ToString() ?? "RELEASING",
                                        TotalEpisodes = userEntry.totalEp > 0 ? userEntry.totalEp : media["episodes"]?.GetValue<int?>(),
                                        EpisodeDuration = media["duration"]?.GetValue<int?>(),
                                        EpisodeNumber = epNum,
                                        AiringAt = airUtc,
                                        AiringTimeFormatted = airUtc.ToString("HH:mm"),
                                        AiringDateFormatted = airUtc.ToString("yyyy-MM-dd"),
                                        TimeUntilAiringSeconds = timeUntil,
                                        AverageScore = media["averageScore"]?.GetValue<double?>() ?? (userEntry.score > 0 ? userEntry.score : null),
                                        Synopsis = media["description"]?.ToString() ?? "",
                                        MalUrl = $"https://myanimelist.net/anime/{malId.Value}",
                                        AniListUrl = media["siteUrl"]?.ToString(),
                                        SiteUrl = $"https://myanimelist.net/anime/{malId.Value}",
                                        StreamingLinks = StreamingHelper.ParseStreamingLinks(media["externalLinks"]),
                                        UserProgress = userEntry.watched,
                                        UserScore = userEntry.score > 0 ? userEntry.score : null,
                                        ListStatus = userEntry.listStatus ?? "Watching"
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

                            // If format is MOVIE / OVA / SPECIAL without separate weekly airing schedule, check release date
                            if (!foundAiringInWindow)
                            {
                                string fmt = media["format"]?.ToString() ?? userEntry.mediaType ?? "";
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
                                            Id = $"mal_{malId.Value}_m",
                                            MalId = malId,
                                            AniListId = mId,
                                            TitleEnglish = userEntry.title ?? media["title"]?["english"]?.ToString() ?? media["title"]?["romaji"]?.ToString() ?? "",
                                            TitleRomaji = media["title"]?["romaji"]?.ToString() ?? userEntry.title ?? "",
                                            TitleNative = media["title"]?["native"]?.ToString() ?? "",
                                            CoverImage = (!string.IsNullOrEmpty(userEntry.img) ? userEntry.img : media["coverImage"]?["large"]?.ToString()) ?? "",
                                            BannerImage = media["bannerImage"]?.ToString(),
                                            Format = fmt,
                                            Status = media["status"]?.ToString() ?? "RELEASING",
                                            TotalEpisodes = userEntry.totalEp > 0 ? userEntry.totalEp : 1,
                                            EpisodeDuration = media["duration"]?.GetValue<int?>(),
                                            EpisodeNumber = 1,
                                            AiringAt = releaseDate,
                                            AiringTimeFormatted = releaseDate.ToString("HH:mm"),
                                            AiringDateFormatted = releaseDate.ToString("yyyy-MM-dd"),
                                            TimeUntilAiringSeconds = (long)(releaseDate - DateTimeOffset.UtcNow).TotalSeconds,
                                            AverageScore = media["averageScore"]?.GetValue<double?>() ?? (userEntry.score > 0 ? userEntry.score : null),
                                            Synopsis = media["description"]?.ToString() ?? "",
                                            MalUrl = $"https://myanimelist.net/anime/{malId.Value}",
                                            AniListUrl = media["siteUrl"]?.ToString(),
                                            SiteUrl = $"https://myanimelist.net/anime/{malId.Value}",
                                            StreamingLinks = StreamingHelper.ParseStreamingLinks(media["externalLinks"]),
                                            UserProgress = userEntry.watched,
                                            UserScore = userEntry.score > 0 ? userEntry.score : null,
                                            ListStatus = userEntry.listStatus ?? "Watching"
                                        });
                                    }
                                }
                            }
                        }
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

        private async Task<JsonNode?> ExecuteGraphQLAsync(string query, object variables, bool bypassCache = false)
        {
            var cacheKey = $"{query.GetHashCode()}_{JsonSerializer.Serialize(variables)}";
            if (!bypassCache && _malGqlCache.TryGetValue(cacheKey, out var entry) && DateTimeOffset.UtcNow - entry.cachedAt < TimeSpan.FromMinutes(15))
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
