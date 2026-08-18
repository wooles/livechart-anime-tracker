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
        Task<UserAnimeListResponse> GetUserAnimeListAsync(string username);
        Task<List<UnifiedAnimeEntry>> GetAiringScheduleAsync(DateTimeOffset start, DateTimeOffset end);
        Task<List<UnifiedAnimeEntry>> GetSeasonalAnimeAsync(string season, int year);
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

        public async Task<UserAnimeListResponse> GetUserAnimeListAsync(string username)
        {
            const string query = @"
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
          season
          seasonYear
          startDate {
            year
            month
            day
          }
          averageScore
          popularity
          genres
          studios(isMain: true) {
            nodes {
              name
            }
          }
          description
          source
          siteUrl
          nextAiringEpisode {
            airingAt
            timeUntilAiring
            episode
          }
        }
      }
    }
  }
}";

            var response = await ExecuteGraphQLAsync(query, new { userName = username });
            if (response == null || response["data"] == null)
            {
                throw new Exception($"Nie znaleziono użytkownika AniList o nazwie: {username}");
            }

            var userData = response["data"]?["User"];
            var listData = response["data"]?["MediaListCollection"]?["lists"]?.AsArray();

            var result = new UserAnimeListResponse
            {
                Platform = "AniList",
                Username = userData?["name"]?.ToString() ?? username,
                AvatarUrl = userData?["avatar"]?["large"]?.ToString()
            };

            if (listData != null)
            {
                foreach (var list in listData)
                {
                    var entries = list?["entries"]?.AsArray();
                    if (entries == null) continue;

                    foreach (var entry in entries)
                    {
                        var media = entry?["media"];
                        if (media == null) continue;

                        var anime = MapAniListMedia(media);
                        anime.UserPlatform = "AniList";
                        anime.UserStatus = entry?["status"]?.ToString()?.ToUpperInvariant();
                        anime.UserProgress = entry?["progress"]?.GetValue<int?>();
                        anime.UserScore = entry?["score"]?.GetValue<double?>();

                        switch (anime.UserStatus)
                        {
                            case "CURRENT":
                                result.Watching.Add(anime);
                                break;
                            case "PLANNING":
                                result.Planning.Add(anime);
                                break;
                            case "COMPLETED":
                                result.Completed.Add(anime);
                                break;
                            case "PAUSED":
                                result.Paused.Add(anime);
                                break;
                            case "DROPPED":
                                result.Dropped.Add(anime);
                                break;
                            default:
                                result.Watching.Add(anime);
                                break;
                        }
                    }
                }
            }

            result.TotalEntries = result.Watching.Count + result.Planning.Count + result.Completed.Count + result.Paused.Count + result.Dropped.Count;
            return result;
        }

        public async Task<List<UnifiedAnimeEntry>> GetAiringScheduleAsync(DateTimeOffset start, DateTimeOffset end)
        {
            const string query = @"
query ($page: Int, $perPage: Int, $airingAt_greater: Int, $airingAt_lesser: Int) {
  Page(page: $page, perPage: $perPage) {
    pageInfo {
      hasNextPage
    }
    airingSchedules(airingAt_greater: $airingAt_greater, airingAt_lesser: $airingAt_lesser, sort: TIME) {
      id
      airingAt
      timeUntilAiring
      episode
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
        season
        seasonYear
        startDate {
          year
          month
          day
        }
        averageScore
        popularity
        genres
        studios(isMain: true) {
          nodes {
            name
          }
        }
        description
        source
        siteUrl
      }
    }
  }
}";

            var list = new List<UnifiedAnimeEntry>();
            int page = 1;
            bool hasNextPage = true;
            long startSec = start.ToUnixTimeSeconds();
            long endSec = end.ToUnixTimeSeconds();

            while (hasNextPage && page <= 5) // fetch up to 250 airing shows
            {
                var response = await ExecuteGraphQLAsync(query, new
                {
                    page = page,
                    perPage = 50,
                    airingAt_greater = (int)startSec,
                    airingAt_lesser = (int)endSec
                });

                var pageNode = response?["data"]?["Page"];
                if (pageNode == null) break;

                hasNextPage = pageNode["pageInfo"]?["hasNextPage"]?.GetValue<bool>() ?? false;
                var schedules = pageNode["airingSchedules"]?.AsArray();
                if (schedules != null)
                {
                    foreach (var sch in schedules)
                    {
                        var media = sch?["media"];
                        if (media == null) continue;

                        var anime = MapAniListMedia(media);
                        long airingAtSec = sch?["airingAt"]?.GetValue<long>() ?? 0;
                        long timeUntil = sch?["timeUntilAiring"]?.GetValue<long>() ?? 0;
                        int ep = sch?["episode"]?.GetValue<int>() ?? 1;

                        var airingAtTime = DateTimeOffset.FromUnixTimeSeconds(airingAtSec);
                        anime.NextAiringEpisode = new AiringEpisodeInfo
                        {
                            Episode = ep,
                            AiringAt = airingAtTime,
                            TimeUntilAiringSeconds = timeUntil,
                            DayOfWeek = airingAtTime.DayOfWeek.ToString(),
                            FormattedTime = airingAtTime.ToLocalTime().ToString("HH:mm")
                        };

                        list.Add(anime);
                    }
                }

                page++;
            }

            return list;
        }

        public async Task<List<UnifiedAnimeEntry>> GetSeasonalAnimeAsync(string season, int year)
        {
            const string query = @"
query ($page: Int, $perPage: Int, $season: MediaSeason, $seasonYear: Int) {
  Page(page: $page, perPage: $perPage) {
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
      season
      seasonYear
      startDate {
        year
        month
        day
      }
      averageScore
      popularity
      genres
      studios(isMain: true) {
        nodes {
          name
        }
      }
      description
      source
      siteUrl
      nextAiringEpisode {
        airingAt
        timeUntilAiring
        episode
      }
    }
  }
}";

            var list = new List<UnifiedAnimeEntry>();
            int page = 1;
            bool hasNextPage = true;

            while (hasNextPage && page <= 3) // Top 150 seasonal shows
            {
                var response = await ExecuteGraphQLAsync(query, new
                {
                    page = page,
                    perPage = 50,
                    season = season.ToUpperInvariant(),
                    seasonYear = year
                });

                var pageNode = response?["data"]?["Page"];
                if (pageNode == null) break;

                hasNextPage = pageNode["pageInfo"]?["hasNextPage"]?.GetValue<bool>() ?? false;
                var mediaArray = pageNode["media"]?.AsArray();
                if (mediaArray != null)
                {
                    foreach (var media in mediaArray)
                    {
                        if (media != null)
                        {
                            list.Add(MapAniListMedia(media));
                        }
                    }
                }

                page++;
            }

            return list;
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

        private static UnifiedAnimeEntry MapAniListMedia(JsonNode media)
        {
            var anime = new UnifiedAnimeEntry
            {
                Id = "anilist_" + media["id"]?.ToString(),
                AniListId = media["id"]?.GetValue<int?>(),
                MalId = media["idMal"]?.GetValue<int?>(),
                TitleRomaji = media["title"]?["romaji"]?.ToString() ?? "",
                TitleEnglish = media["title"]?["english"]?.ToString() ?? "",
                TitleNative = media["title"]?["native"]?.ToString() ?? "",
                CoverImage = media["coverImage"]?["extraLarge"]?.ToString() ?? media["coverImage"]?["large"]?.ToString() ?? "",
                BannerImage = media["bannerImage"]?.ToString(),
                Format = media["format"]?.ToString() ?? "TV",
                Status = media["status"]?.ToString() ?? "RELEASING",
                Episodes = media["episodes"]?.GetValue<int?>(),
                EpisodeDuration = media["duration"]?.GetValue<int?>(),
                Season = media["season"]?.ToString(),
                SeasonYear = media["seasonYear"]?.GetValue<int?>(),
                AverageScore = media["averageScore"]?.GetValue<double?>(),
                Popularity = media["popularity"]?.GetValue<int?>(),
                Synopsis = media["description"]?.ToString() ?? "",
                Source = media["source"]?.ToString(),
                SiteUrl = media["siteUrl"]?.ToString(),
                AniListUrl = media["siteUrl"]?.ToString()
            };

            if (anime.MalId.HasValue)
            {
                anime.MalUrl = $"https://myanimelist.net/anime/{anime.MalId.Value}";
            }

            var startYear = media["startDate"]?["year"]?.GetValue<int?>();
            var startMonth = media["startDate"]?["month"]?.GetValue<int?>();
            var startDay = media["startDate"]?["day"]?.GetValue<int?>();
            if (startYear.HasValue)
            {
                anime.StartDate = $"{startYear.Value:D4}-{(startMonth ?? 1):D2}-{(startDay ?? 1):D2}";
            }

            var genres = media["genres"]?.AsArray();
            if (genres != null)
            {
                anime.Genres = genres.Select(g => g?.ToString() ?? "").Where(g => !string.IsNullOrEmpty(g)).ToList();
            }

            var studios = media["studios"]?["nodes"]?.AsArray();
            if (studios != null)
            {
                anime.Studios = studios.Select(s => s?["name"]?.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList();
            }

            var nextEp = media["nextAiringEpisode"];
            if (nextEp != null)
            {
                long airingAtSec = nextEp["airingAt"]?.GetValue<long>() ?? 0;
                long timeUntil = nextEp["timeUntilAiring"]?.GetValue<long>() ?? 0;
                int ep = nextEp["episode"]?.GetValue<int>() ?? 1;

                if (airingAtSec > 0)
                {
                    var airingAtTime = DateTimeOffset.FromUnixTimeSeconds(airingAtSec);
                    anime.NextAiringEpisode = new AiringEpisodeInfo
                    {
                        Episode = ep,
                        AiringAt = airingAtTime,
                        TimeUntilAiringSeconds = timeUntil,
                        DayOfWeek = airingAtTime.DayOfWeek.ToString(),
                        FormattedTime = airingAtTime.ToLocalTime().ToString("HH:mm")
                    };
                }
            }

            return anime;
        }
    }
}
