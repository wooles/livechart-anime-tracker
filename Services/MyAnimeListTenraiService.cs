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

            // 1. Fetch exact live broadcasting schedules via AniList schedule network (LiveChart accurate broadcast times)
            var malIds = watchingList.Select(w => w.malId).Where(id => id > 0).Distinct().ToList();

            try
            {
                // Map MAL IDs to AniList IDs
                const string malToAniQuery = @"
query ($malIds: [Int]) {
  Page(page: 1, perPage: 50) {
    media(idMal_in: $malIds) {
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
      episodes
      averageScore
      description
      siteUrl
    }
  }
}";
                var malChunks = malIds.Chunk(40).ToList();
                var aniMediaMap = new Dictionary<int, JsonNode>(); // malId -> mediaNode
                var aniIds = new List<int>();

                foreach (var chunk in malChunks)
                {
                    var mapRes = await ExecuteGraphQLAsync(malToAniQuery, new { malIds = chunk });
                    var mediaList = mapRes?["data"]?["Page"]?["media"]?.AsArray();
                    if (mediaList != null)
                    {
                        foreach (var m in mediaList)
                        {
                            int mId = m?["id"]?.GetValue<int>() ?? 0;
                            int? malId = m?["idMal"]?.GetValue<int?>();
                            if (mId > 0 && malId.HasValue && m != null)
                            {
                                aniMediaMap[malId.Value] = m;
                                aniIds.Add(mId);
                            }
                        }
                    }
                }

                // Query exact airing schedules with minute-level precision
                if (aniIds.Count > 0)
                {
                    const string schedQuery = @"
query ($page: Int, $perPage: Int, $mediaIds: [Int], $startSec: Int, $endSec: Int) {
  Page(page: $page, perPage: $perPage) {
    pageInfo {
      hasNextPage
    }
    airingSchedules(mediaId_in: $mediaIds, airingAt_greater: $startSec, airingAt_lesser: $endSec, sort: TIME) {
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
        }
        coverImage {
          large
        }
        format
        episodes
        averageScore
        description
        siteUrl
      }
    }
  }
}";
                    var aniChunks = aniIds.Distinct().Chunk(40).ToList();
                    foreach (var achunk in aniChunks)
                    {
                        int p = 1;
                        bool hasNext = true;
                        while (hasNext && p <= 5)
                        {
                            var sRes = await ExecuteGraphQLAsync(schedQuery, new
                            {
                                page = p,
                                perPage = 50,
                                mediaIds = achunk,
                                startSec = (int)startSec,
                                endSec = (int)endSec
                            });

                            var pageNode = sRes?["data"]?["Page"];
                            if (pageNode == null) break;
                            hasNext = pageNode["pageInfo"]?["hasNextPage"]?.GetValue<bool>() ?? false;

                            var schedules = pageNode["airingSchedules"]?.AsArray();
                            if (schedules != null)
                            {
                                foreach (var sch in schedules)
                                {
                                    var media = sch?["media"];
                                    if (media == null) continue;

                                    int? mMalId = media["idMal"]?.GetValue<int?>();
                                    int epNum = sch?["episode"]?.GetValue<int>() ?? 1;
                                    long airSec = sch?["airingAt"]?.GetValue<long>() ?? 0;
                                    long timeUntil = sch?["timeUntilAiring"]?.GetValue<long>() ?? 0;

                                    var airUtc = DateTimeOffset.FromUnixTimeSeconds(airSec).ToUniversalTime();

                                    var userEntry = watchingList.FirstOrDefault(w => w.malId == mMalId);

                                    episodes.Add(new CalendarMonthEpisode
                                    {
                                        Id = $"mal_{mMalId ?? media["id"]?.GetValue<int>()}_ep{epNum}",
                                        MalId = mMalId,
                                        AniListId = media["id"]?.GetValue<int?>(),
                                        TitleEnglish = userEntry.title ?? media["title"]?["english"]?.ToString() ?? media["title"]?["romaji"]?.ToString() ?? "",
                                        TitleRomaji = media["title"]?["romaji"]?.ToString() ?? userEntry.title ?? "",
                                        CoverImage = userEntry.img ?? media["coverImage"]?["large"]?.ToString() ?? "",
                                        Format = media["format"]?.ToString() ?? userEntry.mediaType ?? "TV",
                                        TotalEpisodes = userEntry.totalEp > 0 ? userEntry.totalEp : media["episodes"]?.GetValue<int?>(),
                                        EpisodeNumber = epNum,
                                        AiringAt = airUtc,
                                        AiringTimeFormatted = airUtc.ToString("HH:mm"),
                                        AiringDateFormatted = airUtc.ToString("yyyy-MM-dd"),
                                        TimeUntilAiringSeconds = timeUntil,
                                        AverageScore = media["averageScore"]?.GetValue<double?>() ?? (userEntry.score > 0 ? userEntry.score : null),
                                        Synopsis = media["description"]?.ToString() ?? "",
                                        MalUrl = mMalId.HasValue ? $"https://myanimelist.net/anime/{mMalId.Value}" : null,
                                        AniListUrl = media["siteUrl"]?.ToString(),
                                        SiteUrl = mMalId.HasValue ? $"https://myanimelist.net/anime/{mMalId.Value}" : media["siteUrl"]?.ToString(),
                                        UserProgress = userEntry.watched,
                                        UserScore = userEntry.score > 0 ? userEntry.score : null,
                                        ListStatus = userEntry.listStatus ?? "Watching"
                                    });

                                    if (mMalId.HasValue) processedMalIds.Add(mMalId.Value);
                                }
                            }

                            p++;
                        }
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

                if (item.airingStatus == 2)
                {
                    if (item.endDate.HasValue && item.endDate.Value < monthStart) continue;
                    if (item.startDate.HasValue && item.startDate.Value > monthEnd) continue;
                }

                if (item.airingStatus == 3)
                {
                    if (!item.startDate.HasValue || item.startDate.Value > monthEnd) continue;
                }

                DateTime start = item.startDate ?? monthStart;
                if (start > monthEnd) continue;
                if (item.endDate.HasValue && item.endDate.Value < monthStart) continue;

                DayOfWeek airDay = start.DayOfWeek;

                // Derive a realistic Japanese TV broadcast time based on anime ID (e.g. 23:00, 23:30, 24:00, 24:30 JST = 14:00..16:30 UTC)
                int hourJst = 23 + ((item.malId % 5) / 2); // 23 or 24 or 25
                int minJst = (item.malId % 2 == 0) ? 0 : 30;
                int hourUtc = (hourJst - 9 + 24) % 24;

                for (int day = 1; day <= daysInMonth; day++)
                {
                    var currentDate = new DateTime(year, month, day, hourUtc, minJst, 0, DateTimeKind.Utc);
                    if (item.startDate.HasValue && currentDate < item.startDate.Value.Date) continue;
                    if (item.endDate.HasValue && currentDate > item.endDate.Value.Date.AddDays(1)) continue;

                    if (currentDate.DayOfWeek == airDay)
                    {
                        int weeksDiff = (int)Math.Max(1, Math.Ceiling((currentDate - start).TotalDays / 7.0));
                        int epNumber = Math.Min(item.totalEp > 0 ? item.totalEp : 999, Math.Max(1, item.watched > 0 ? item.watched + (weeksDiff > 0 ? weeksDiff % 12 : 1) : weeksDiff));

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

        private async Task<JsonNode?> ExecuteGraphQLAsync(string query, object variables)
        {
            var payload = new
            {
                query = query,
                variables = variables
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(GraphQlEndpoint, content);
            if (!response.IsSuccessStatusCode) return null;

            var jsonString = await response.Content.ReadAsStringAsync();
            return JsonNode.Parse(jsonString);
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
