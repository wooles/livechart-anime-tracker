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
        Task<(string? avatarUrl, List<CalendarMonthEpisode> episodes, int totalWatching)> GetSeasonalMonthEpisodesAsync(int year, int month);
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
  MediaListCollection(userName: $userName, type: ANIME) {
    lists {
      name
      isCustomList
      status
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
          nextAiringEpisode {
            episode
            airingAt
            timeUntilAiring
          }
        }
      }
    }
  }
}";

            var userRes = await ExecuteGraphQLAsync(userQuery, new { userName = username });
            if (userRes == null)
            {
                throw new Exception($"AniList service is currently busy or rate-limited. Please wait a few seconds and try again.");
            }
            if (userRes["data"]?["User"] == null)
            {
                var errMsg = userRes["errors"]?[0]?["message"]?.ToString();
                if (!string.IsNullOrWhiteSpace(errMsg))
                {
                    throw new Exception($"AniList: {errMsg}");
                }
                throw new Exception($"User '{username}' was not found on AniList.");
            }

            var avatarUrl = userRes["data"]?["User"]?["avatar"]?["large"]?.ToString();
            var lists = userRes["data"]?["MediaListCollection"]?["lists"]?.AsArray();

            var watchingEntries = new Dictionary<int, (JsonNode media, int progress, double? score, string listStatus)>();

            if (lists != null)
            {
                foreach (var l in lists)
                {
                    string listStatusAttr = l?["status"]?.ToString() ?? "";
                    var entries = l?["entries"]?.AsArray();
                    if (entries == null) continue;

                    foreach (var e in entries)
                    {
                        var media = e?["media"];
                        int? mediaId = media?["id"]?.GetValue<int?>();
                        if (mediaId.HasValue && media != null && e != null)
                        {
                            string rawStatus = e["status"]?.ToString() ?? listStatusAttr;
                            if (rawStatus != "CURRENT" && rawStatus != "PLANNING" && rawStatus != "REPEATING")
                            {
                                continue;
                            }

                            int progress = e["progress"]?.GetValue<int?>() ?? 0;
                            double? score = e["score"]?.GetValue<double?>();
                            string listStatus = (rawStatus == "PLANNING") ? "PlanToWatch" : "Watching";
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

            var episodesList = new List<CalendarMonthEpisode>();

            // 1. Anchor projection for all airing series from nextAiringEpisode
            foreach (var kvp in watchingEntries)
            {
                int mediaId = kvp.Key;
                var (media, progress, score, listStatus) = kvp.Value;
                var nextEpNode = media["nextAiringEpisode"];
                if (nextEpNode != null)
                {
                    int anchorEp = nextEpNode["episode"]?.GetValue<int>() ?? 1;
                    long anchorAirSec = nextEpNode["airingAt"]?.GetValue<long>() ?? 0;
                    int? totalEp = media["episodes"]?.GetValue<int?>();

                    var anchorAirUtc = DateTimeOffset.FromUnixTimeSeconds(anchorAirSec).ToUniversalTime();

                    for (int k = -12; k <= 12; k++)
                    {
                        int targetEp = anchorEp + k;
                        if (targetEp < 1) continue;
                        if (totalEp.HasValue && targetEp > totalEp.Value) continue;

                        var targetAirUtc = anchorAirUtc.AddDays(k * 7);
                        if (targetAirUtc < startOfMonth || targetAirUtc > endOfMonth) continue;

                        episodesList.Add(new CalendarMonthEpisode
                        {
                            Id = $"anilist_{mediaId}_ep{targetEp}",
                            AniListId = mediaId,
                            MalId = media["idMal"]?.GetValue<int?>(),
                            TitleRomaji = media["title"]?["romaji"]?.ToString() ?? "",
                            TitleEnglish = media["title"]?["english"]?.ToString() ?? "",
                            TitleNative = media["title"]?["native"]?.ToString() ?? "",
                            CoverImage = media["coverImage"]?["extraLarge"]?.ToString() ?? media["coverImage"]?["large"]?.ToString() ?? "",
                            BannerImage = media["bannerImage"]?.ToString(),
                            Format = media["format"]?.ToString() ?? "TV",
                            Status = media["status"]?.ToString() ?? "RELEASING",
                            TotalEpisodes = totalEp,
                            EpisodeDuration = media["duration"]?.GetValue<int?>(),
                            EpisodeNumber = targetEp,
                            AiringAt = targetAirUtc,
                            AiringTimeFormatted = targetAirUtc.ToString("HH:mm"),
                            AiringDateFormatted = targetAirUtc.ToString("yyyy-MM-dd"),
                            TimeUntilAiringSeconds = (long)(targetAirUtc - DateTimeOffset.UtcNow).TotalSeconds,
                            AverageScore = media["averageScore"]?.GetValue<double?>(),
                            Synopsis = media["description"]?.ToString() ?? "",
                            SiteUrl = media["siteUrl"]?.ToString(),
                            AniListUrl = media["siteUrl"]?.ToString(),
                            MalUrl = media["idMal"] != null ? $"https://myanimelist.net/anime/{media["idMal"]}" : null,
                            UserProgress = progress,
                            UserScore = score,
                            ListStatus = listStatus
                        });
                    }
                }
            }

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

                            var airUtc = DateTimeOffset.FromUnixTimeSeconds(airSec).ToUniversalTime();

                            var existingIdx = episodesList.FindIndex(e => e.AniListId == mediaId && e.EpisodeNumber == epNum);
                            var exactEp = new CalendarMonthEpisode
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

                            if (existingIdx >= 0)
                            {
                                episodesList[existingIdx] = exactEp;
                            }
                            else
                            {
                                episodesList.Add(exactEp);
                            }

                            if (exactEp.MalId.HasValue)
                            {
                                exactEp.MalUrl = $"https://myanimelist.net/anime/{exactEp.MalId.Value}";
                            }

                            var genres = media["genres"]?.AsArray();
                            if (genres != null)
                            {
                                exactEp.Genres = genres.Select(g => g?.ToString() ?? "").Where(g => !string.IsNullOrEmpty(g)).ToList();
                            }

                            var studios = media["studios"]?["nodes"]?.AsArray();
                            if (studios != null)
                            {
                                exactEp.Studios = studios.Select(s => s?["name"]?.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList();
                            }
                        }
                    }

                    page++;
                }
            }

            return (avatarUrl, episodesList, totalWatching);
        }

        public async Task<(string? avatarUrl, List<CalendarMonthEpisode> episodes, int totalWatching)> GetSeasonalMonthEpisodesAsync(int year, int month)
        {
            string season = month switch
            {
                1 or 2 or 3 => "WINTER",
                4 or 5 or 6 => "SPRING",
                7 or 8 or 9 => "SUMMER",
                _ => "FALL"
            };

            const string seasonQuery = @"
query ($page: Int, $season: MediaSeason, $seasonYear: Int) {
  Page(page: $page, perPage: 50) {
    pageInfo {
      hasNextPage
    }
    media(season: $season, seasonYear: $seasonYear, type: ANIME, sort: POPULARITY_DESC) {
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
      nextAiringEpisode {
        episode
        airingAt
        timeUntilAiring
      }
    }
  }
}";

            var episodesList = new List<CalendarMonthEpisode>();
            var mediaList = new List<JsonNode>();

            var startOfMonth = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero).AddDays(-15);
            var endOfMonth = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1).AddDays(15);
            long startSec = startOfMonth.ToUnixTimeSeconds();
            long endSec = endOfMonth.ToUnixTimeSeconds();

            for (int p = 1; p <= 2; p++)
            {
                var res = await ExecuteGraphQLAsync(seasonQuery, new { page = p, season = season, seasonYear = year });
                var pageNode = res?["data"]?["Page"];
                if (pageNode == null) break;

                var items = pageNode["media"]?.AsArray();
                if (items != null)
                {
                    foreach (var it in items)
                    {
                        if (it != null) mediaList.Add(it);
                    }
                }

                bool hasNext = pageNode["pageInfo"]?["hasNextPage"]?.GetValue<bool>() ?? false;
                if (!hasNext) break;
            }

            foreach (var media in mediaList)
            {
                int mediaId = media["id"]?.GetValue<int>() ?? 0;
                if (mediaId == 0) continue;

                var nextEpNode = media["nextAiringEpisode"];
                if (nextEpNode != null)
                {
                    int anchorEp = nextEpNode["episode"]?.GetValue<int>() ?? 1;
                    long anchorAirSec = nextEpNode["airingAt"]?.GetValue<long>() ?? 0;
                    int? totalEp = media["episodes"]?.GetValue<int?>();

                    var anchorAirUtc = DateTimeOffset.FromUnixTimeSeconds(anchorAirSec).ToUniversalTime();

                    for (int k = -12; k <= 12; k++)
                    {
                        int targetEp = anchorEp + k;
                        if (targetEp < 1) continue;
                        if (totalEp.HasValue && targetEp > totalEp.Value) continue;

                        var targetAirUtc = anchorAirUtc.AddDays(k * 7);
                        if (targetAirUtc < startOfMonth || targetAirUtc > endOfMonth) continue;

                        episodesList.Add(new CalendarMonthEpisode
                        {
                            Id = $"season_{mediaId}_ep{targetEp}",
                            AniListId = mediaId,
                            MalId = media["idMal"]?.GetValue<int?>(),
                            TitleRomaji = media["title"]?["romaji"]?.ToString() ?? "",
                            TitleEnglish = media["title"]?["english"]?.ToString() ?? "",
                            TitleNative = media["title"]?["native"]?.ToString() ?? "",
                            CoverImage = media["coverImage"]?["extraLarge"]?.ToString() ?? media["coverImage"]?["large"]?.ToString() ?? "",
                            BannerImage = media["bannerImage"]?.ToString(),
                            Format = media["format"]?.ToString() ?? "TV",
                            Status = media["status"]?.ToString() ?? "RELEASING",
                            TotalEpisodes = totalEp,
                            EpisodeDuration = media["duration"]?.GetValue<int?>(),
                            EpisodeNumber = targetEp,
                            AiringAt = targetAirUtc,
                            AiringTimeFormatted = targetAirUtc.ToString("HH:mm"),
                            AiringDateFormatted = targetAirUtc.ToString("yyyy-MM-dd"),
                            TimeUntilAiringSeconds = (long)(targetAirUtc - DateTimeOffset.UtcNow).TotalSeconds,
                            AverageScore = media["averageScore"]?.GetValue<double?>(),
                            Synopsis = media["description"]?.ToString() ?? "",
                            SiteUrl = media["siteUrl"]?.ToString(),
                            AniListUrl = media["siteUrl"]?.ToString(),
                            MalUrl = media["idMal"] != null ? $"https://myanimelist.net/anime/{media["idMal"]}" : null,
                            ListStatus = "Airing"
                        });
                    }
                }
            }

            return ("https://sort.moe/favicon.ico", episodesList.OrderBy(e => e.AiringAt).ToList(), mediaList.Count);
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTimeOffset cachedAt, JsonNode? data)> _gqlCache = new();

        private async Task<JsonNode?> ExecuteGraphQLAsync(string query, object variables)
        {
            var cacheKey = $"{query.GetHashCode()}_{JsonSerializer.Serialize(variables)}";
            if (_gqlCache.TryGetValue(cacheKey, out var entry) && DateTimeOffset.UtcNow - entry.cachedAt < TimeSpan.FromMinutes(15))
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
                        await Task.Delay(1500 * (attempt + 1));
                        continue;
                    }

                    if (!response.IsSuccessStatusCode) return null;

                    var jsonString = await response.Content.ReadAsStringAsync();
                    var node = JsonNode.Parse(jsonString);
                    if (node != null)
                    {
                        _gqlCache[cacheKey] = (DateTimeOffset.UtcNow, node);
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
