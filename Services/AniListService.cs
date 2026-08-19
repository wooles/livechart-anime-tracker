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
    public interface IAniListService
    {
        Task<(string? avatarUrl, List<CalendarMonthEpisode> episodes, int totalWatching)> GetWatchingMonthEpisodesAsync(string username, int year, int month);
    }

    public class AniListService : IAniListService
    {
        private readonly HttpClient _httpClient;
        private const string GraphQlEndpoint = "https://graphql.anilist.co";

        public AniListService(HttpClient httpClient)
        {
            _httpClient = httpClient;
            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "LiveChartAnimeTracker/1.0");
            }
        }

        public async Task<(string? avatarUrl, List<CalendarMonthEpisode> episodes, int totalWatching)> GetWatchingMonthEpisodesAsync(string username, int year, int month)
        {
            const string userQuery = @"
query ($userName: String) {
  User(name: $userName) {
    name
    avatar {
      large
    }
  }
  MediaListCollection(userName: $userName, type: ANIME, status_in: [CURRENT, PLANNING]) {
    lists {
      entries {
        status
        score
        progress
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
        }
      }
    }
  }
}";

            var userRes = await ExecuteGraphQLAsync(userQuery, new { userName = username });
            if (userRes == null || userRes["data"] == null || userRes["data"]?["User"] == null)
            {
                throw new Exception($"User {username} not found on AniList.");
            }

            var avatarUrl = userRes["data"]?["User"]?["avatar"]?["large"]?.ToString();
            var lists = userRes["data"]?["MediaListCollection"]?["lists"]?.AsArray();

            var watchingEntries = new Dictionary<int, (JsonNode media, int progress, double? score, string listStatus)>();

            if (lists != null)
            {
                foreach (var l in lists)
                {
                    var entries = l?["entries"]?.AsArray();
                    if (entries == null) continue;
                    foreach (var e in entries)
                    {
                        var media = e?["media"];
                        int? mediaId = media?["id"]?.GetValue<int?>();
                        if (mediaId.HasValue && media != null && e != null)
                        {
                            int progress = e["progress"]?.GetValue<int?>() ?? 0;
                            double? score = e["score"]?.GetValue<double?>();
                            string rawStatus = e["status"]?.ToString() ?? "CURRENT";
                            string listStatus = rawStatus == "PLANNING" ? "PlanToWatch" : "Watching";
                            watchingEntries[mediaId.Value] = (media, progress, score, listStatus);
                        }
                    }
                }
            }

            int totalWatching = watchingEntries.Count;
            if (totalWatching == 0)
            {
                return (avatarUrl, new List<CalendarMonthEpisode>(), 0);
            }

            var mediaIds = watchingEntries.Keys.ToList();

            var startOfMonth = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero).AddDays(-15);
            var endOfMonth = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1).AddDays(15);
            long startSec = startOfMonth.ToUnixTimeSeconds();
            long endSec = endOfMonth.ToUnixTimeSeconds();

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
      }
    }
  }
}";

            var episodesList = new List<CalendarMonthEpisode>();
            int page = 1;
            bool hasNextPage = true;

            var idChunks = mediaIds.Chunk(50).ToList();

            foreach (var chunk in idChunks)
            {
                page = 1;
                hasNextPage = true;

                while (hasNextPage && page <= 5)
                {
                    var schedRes = await ExecuteGraphQLAsync(scheduleQuery, new
                    {
                        page = page,
                        perPage = 50,
                        mediaId_in = chunk,
                        airingAt_greater = (int)startSec,
                        airingAt_lesser = (int)endSec
                    });

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

                            int mediaId = media["id"]?.GetValue<int>() ?? 0;
                            watchingEntries.TryGetValue(mediaId, out var userEntry);

                            long airSec = sch?["airingAt"]?.GetValue<long>() ?? 0;
                            long timeUntil = sch?["timeUntilAiring"]?.GetValue<long>() ?? 0;
                            int epNum = sch?["episode"]?.GetValue<int>() ?? 1;

                            // Store in UTC so client browser converts to exact local timezone
                            var airUtc = DateTimeOffset.FromUnixTimeSeconds(airSec).ToUniversalTime();

                            var ep = new CalendarMonthEpisode
                            {
                                Id = "anilist_" + mediaId + "_ep" + epNum,
                                AniListId = mediaId,
                                MalId = media["idMal"]?.GetValue<int?>(),
                                TitleRomaji = media["title"]?["romaji"]?.ToString() ?? "",
                                TitleEnglish = media["title"]?["english"]?.ToString() ?? "",
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
                                SiteUrl = media["siteUrl"]?.ToString(),
                                AniListUrl = media["siteUrl"]?.ToString(),
                                UserProgress = userEntry.progress,
                                UserScore = userEntry.score,
                                ListStatus = userEntry.listStatus ?? "Watching"
                            };

                            if (ep.MalId.HasValue)
                            {
                                ep.MalUrl = $"https://myanimelist.net/anime/{ep.MalId.Value}";
                            }

                            var genres = media["genres"]?.AsArray();
                            if (genres != null)
                            {
                                ep.Genres = genres.Select(g => g?.ToString() ?? "").Where(g => !string.IsNullOrEmpty(g)).ToList();
                            }

                            var studios = media["studios"]?["nodes"]?.AsArray();
                            if (studios != null)
                            {
                                ep.Studios = studios.Select(s => s?["name"]?.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList();
                            }

                            episodesList.Add(ep);
                        }
                    }

                    page++;
                }
            }

            return (avatarUrl, episodesList, totalWatching);
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
            response.EnsureSuccessStatusCode();

            var jsonString = await response.Content.ReadAsStringAsync();
            return JsonNode.Parse(jsonString);
        }
    }
}
