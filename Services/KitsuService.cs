using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using LiveChartTracker.Models;

namespace LiveChartTracker.Services
{
    public interface IKitsuService
    {
        Task<UserAnimeListResponse> GetUserAnimeListAsync(string username);
    }

    public class KitsuService : IKitsuService
    {
        private readonly HttpClient _httpClient;
        private const string KitsuBaseUrl = "https://kitsu.app/api/edge";

        public KitsuService(HttpClient httpClient)
        {
            _httpClient = httpClient;
            if (!_httpClient.DefaultRequestHeaders.Contains("Accept"))
            {
                _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.api+json");
            }
            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "LiveChartAnimeTracker/1.0");
            }
        }

        public async Task<UserAnimeListResponse> GetUserAnimeListAsync(string username)
        {
            // 1. Fetch User ID
            var userUrl = $"{KitsuBaseUrl}/users?filter[name]={Uri.EscapeDataString(username)}";
            var userRes = await _httpClient.GetAsync(userUrl);
            userRes.EnsureSuccessStatusCode();

            var userJson = JsonNode.Parse(await userRes.Content.ReadAsStringAsync());
            var users = userJson?["data"]?.AsArray();
            if (users == null || users.Count == 0)
            {
                throw new Exception($"Nie znaleziono użytkownika Kitsu o nazwie: {username}");
            }

            var userNode = users[0];
            var userId = userNode?["id"]?.ToString();
            var userName = userNode?["attributes"]?["name"]?.ToString() ?? username;
            var avatarUrl = userNode?["attributes"]?["avatar"]?["large"]?.ToString() 
                ?? userNode?["attributes"]?["avatar"]?["original"]?.ToString();

            var result = new UserAnimeListResponse
            {
                Platform = "Kitsu",
                Username = userName,
                AvatarUrl = avatarUrl
            };

            // 2. Fetch Library Entries (paginated)
            int offset = 0;
            const int limit = 100;
            bool hasMore = true;

            while (hasMore && offset < 500)
            {
                var libraryUrl = $"{KitsuBaseUrl}/library-entries?filter[userId]={userId}&filter[kind]=anime&include=anime&page[limit]={limit}&page[offset]={offset}&sort=-updated_at";
                var libRes = await _httpClient.GetAsync(libraryUrl);
                if (!libRes.IsSuccessStatusCode) break;

                var libJson = JsonNode.Parse(await libRes.Content.ReadAsStringAsync());
                var entries = libJson?["data"]?.AsArray();
                var included = libJson?["included"]?.AsArray();

                if (entries == null || entries.Count == 0) break;

                // Map included anime dictionary by ID
                var animeDict = new Dictionary<string, JsonNode>();
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
                    }
                }

                foreach (var entry in entries)
                {
                    var entryAttr = entry?["attributes"];
                    if (entryAttr == null) continue;

                    var animeRelId = entry?["relationships"]?["anime"]?["data"]?["id"]?.ToString();
                    if (animeRelId == null || !animeDict.TryGetValue(animeRelId, out var animeNode))
                    {
                        continue;
                    }

                    var animeAttr = animeNode["attributes"];
                    if (animeAttr == null) continue;

                    var status = entryAttr["status"]?.ToString()?.ToLowerInvariant();
                    var progress = entryAttr["progress"]?.GetValue<int?>();
                    var ratingTwenty = entryAttr["ratingTwenty"]?.GetValue<double?>();

                    var anime = new UnifiedAnimeEntry
                    {
                        Id = "kitsu_" + animeRelId,
                        KitsuId = animeRelId,
                        TitleRomaji = animeAttr["canonicalTitle"]?.ToString() ?? "",
                        TitleEnglish = animeAttr["titles"]?["en"]?.ToString() ?? animeAttr["titles"]?["en_jp"]?.ToString() ?? "",
                        TitleNative = animeAttr["titles"]?["ja_jp"]?.ToString() ?? "",
                        CoverImage = animeAttr["posterImage"]?["large"]?.ToString() ?? animeAttr["posterImage"]?["medium"]?.ToString() ?? "",
                        BannerImage = animeAttr["coverImage"]?["large"]?.ToString(),
                        Format = (animeAttr["showType"]?.ToString() ?? "TV").ToUpperInvariant(),
                        Status = (animeAttr["status"]?.ToString() ?? "current").ToUpperInvariant(),
                        Episodes = animeAttr["episodeCount"]?.GetValue<int?>(),
                        EpisodeDuration = animeAttr["episodeLength"]?.GetValue<int?>(),
                        AverageScore = animeAttr["averageRating"] != null ? double.TryParse(animeAttr["averageRating"]?.ToString(), out var r) ? r : null : null,
                        Popularity = animeAttr["userCount"]?.GetValue<int?>(),
                        Synopsis = animeAttr["synopsis"]?.ToString() ?? "",
                        StartDate = animeAttr["startDate"]?.ToString(),
                        SiteUrl = $"https://kitsu.app/anime/{animeAttr["slug"]?.ToString() ?? animeRelId}",
                        KitsuUrl = $"https://kitsu.app/anime/{animeAttr["slug"]?.ToString() ?? animeRelId}",
                        UserPlatform = "Kitsu",
                        UserProgress = progress,
                        UserScore = ratingTwenty.HasValue ? ratingTwenty.Value * 5.0 : null // convert 20-scale to 100
                    };

                    switch (status)
                    {
                        case "current":
                            anime.UserStatus = "CURRENT";
                            result.Watching.Add(anime);
                            break;
                        case "planned":
                            anime.UserStatus = "PLANNING";
                            result.Planning.Add(anime);
                            break;
                        case "completed":
                            anime.UserStatus = "COMPLETED";
                            result.Completed.Add(anime);
                            break;
                        case "on_hold":
                            anime.UserStatus = "PAUSED";
                            result.Paused.Add(anime);
                            break;
                        case "dropped":
                            anime.UserStatus = "DROPPED";
                            result.Dropped.Add(anime);
                            break;
                        default:
                            anime.UserStatus = "CURRENT";
                            result.Watching.Add(anime);
                            break;
                    }
                }

                if (entries.Count < limit)
                {
                    hasMore = false;
                }
                else
                {
                    offset += limit;
                }
            }

            result.TotalEntries = result.Watching.Count + result.Planning.Count + result.Completed.Count + result.Paused.Count + result.Dropped.Count;
            return result;
        }
    }
}
