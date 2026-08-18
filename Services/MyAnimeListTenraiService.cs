using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using LiveChartTracker.Models;
using Tenrai;

namespace LiveChartTracker.Services
{
    public interface IMyAnimeListTenraiService
    {
        Task<UserAnimeListResponse> GetUserAnimeListAsync(string username);
        Task<List<UnifiedAnimeEntry>> GetCurrentSeasonAsync();
        Task<List<UnifiedAnimeEntry>> GetSeasonalAnimeAsync(int year, Season season);
        Task<List<UnifiedAnimeEntry>> GetScheduleAsync();
        Task<UnifiedAnimeEntry?> GetAnimeDetailsAsync(long malId);
    }

    public class MyAnimeListTenraiService : IMyAnimeListTenraiService
    {
        private readonly ITenrai _tenraiClient;
        private readonly HttpClient _httpClient;

        public MyAnimeListTenraiService(HttpClient httpClient)
        {
            _httpClient = httpClient;
            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            }
            _tenraiClient = new TenraiClient();
        }

        public async Task<UserAnimeListResponse> GetUserAnimeListAsync(string username)
        {
            var result = new UserAnimeListResponse
            {
                Platform = "MyAnimeList",
                Username = username,
                AvatarUrl = $"https://myanimelist.net/images/userimages/default.jpg"
            };

            int offset = 0;
            const int limit = 300;
            bool hasMore = true;

            while (hasMore && offset < 900)
            {
                var malListUrl = $"https://myanimelist.net/animelist/{Uri.EscapeDataString(username)}/load.json?offset={offset}&status=7";
                var req = new HttpRequestMessage(HttpMethod.Get, malListUrl);
                req.Headers.Add("Referer", $"https://myanimelist.net/animelist/{Uri.EscapeDataString(username)}");

                var response = await _httpClient.SendAsync(req);
                if (!response.IsSuccessStatusCode)
                {
                    // Fallback to Jikan user animelist if MAL direct gives 403/rate limit
                    try
                    {
                        var jikanUrl = $"https://api.jikan.moe/v4/users/{Uri.EscapeDataString(username)}/userlist/anime";
                        var jikanRes = await _httpClient.GetAsync(jikanUrl);
                        if (jikanRes.IsSuccessStatusCode)
                        {
                            var jikanJson = JsonNode.Parse(await jikanRes.Content.ReadAsStringAsync());
                            var jikanData = jikanJson?["data"]?.AsArray();
                            if (jikanData != null)
                            {
                                foreach (var item in jikanData)
                                {
                                    if (item != null)
                                    {
                                        var entry = MapJikanUserAnime(item);
                                        CategorizeAnime(result, entry);
                                    }
                                }
                            }
                            result.TotalEntries = result.Watching.Count + result.Planning.Count + result.Completed.Count + result.Paused.Count + result.Dropped.Count;
                            return result;
                        }
                    }
                    catch { }

                    throw new Exception($"Nie udało się pobrać listy MyAnimeList dla użytkownika {username}. Upewnij się, że lista jest publiczna.");
                }

                var jsonStr = await response.Content.ReadAsStringAsync();
                var array = JsonNode.Parse(jsonStr)?.AsArray();
                if (array == null || array.Count == 0) break;

                foreach (var item in array)
                {
                    if (item == null) continue;
                    var entry = MapMalJsonEntry(item);
                    CategorizeAnime(result, entry);
                }

                if (array.Count < limit)
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

        private static void CategorizeAnime(UserAnimeListResponse result, UnifiedAnimeEntry entry)
        {
            switch (entry.UserStatus)
            {
                case "CURRENT":
                case "1":
                    entry.UserStatus = "CURRENT";
                    result.Watching.Add(entry);
                    break;
                case "PLANNING":
                case "6":
                    entry.UserStatus = "PLANNING";
                    result.Planning.Add(entry);
                    break;
                case "COMPLETED":
                case "2":
                    entry.UserStatus = "COMPLETED";
                    result.Completed.Add(entry);
                    break;
                case "PAUSED":
                case "3":
                    entry.UserStatus = "PAUSED";
                    result.Paused.Add(entry);
                    break;
                case "DROPPED":
                case "4":
                    entry.UserStatus = "DROPPED";
                    result.Dropped.Add(entry);
                    break;
                default:
                    entry.UserStatus = "CURRENT";
                    result.Watching.Add(entry);
                    break;
            }
        }

        private static UnifiedAnimeEntry MapMalJsonEntry(JsonNode item)
        {
            int malId = item["anime_id"]?.GetValue<int>() ?? 0;
            int statusNum = item["status"]?.GetValue<int>() ?? 1;
            int watched = item["num_watched_episodes"]?.GetValue<int>() ?? 0;
            int totalEp = item["anime_num_episodes"]?.GetValue<int>() ?? 0;
            double score = item["score"]?.GetValue<double>() ?? 0;

            var anime = new UnifiedAnimeEntry
            {
                Id = "mal_" + malId,
                MalId = malId,
                TitleEnglish = item["anime_title_eng"]?.ToString() ?? item["anime_title"]?.ToString() ?? "",
                TitleRomaji = item["anime_title"]?.ToString() ?? "",
                CoverImage = item["anime_image_path"]?.ToString() ?? "",
                Format = (item["anime_media_type_string"]?.ToString() ?? "TV").ToUpperInvariant(),
                Episodes = totalEp > 0 ? totalEp : null,
                UserPlatform = "MyAnimeList",
                UserStatus = statusNum switch
                {
                    1 => "CURRENT",
                    6 => "PLANNING",
                    2 => "COMPLETED",
                    3 => "PAUSED",
                    4 => "DROPPED",
                    _ => "CURRENT"
                },
                UserProgress = watched,
                UserScore = score > 0 ? score * 10.0 : null,
                MalUrl = $"https://myanimelist.net/anime/{malId}",
                SiteUrl = $"https://myanimelist.net/anime/{malId}"
            };

            return anime;
        }

        private static UnifiedAnimeEntry MapJikanUserAnime(JsonNode item)
        {
            var entry = item["entry"];
            int malId = entry?["mal_id"]?.GetValue<int>() ?? 0;
            var anime = new UnifiedAnimeEntry
            {
                Id = "mal_" + malId,
                MalId = malId,
                TitleRomaji = entry?["title"]?.ToString() ?? "",
                TitleEnglish = entry?["title"]?.ToString() ?? "",
                CoverImage = entry?["images"]?["jpg"]?["large_image_url"]?.ToString() 
                    ?? entry?["images"]?["jpg"]?["image_url"]?.ToString() ?? "",
                UserPlatform = "MyAnimeList",
                UserStatus = item["status"]?.ToString()?.ToUpperInvariant() ?? "CURRENT",
                UserProgress = item["episodes_watched"]?.GetValue<int?>(),
                UserScore = item["score"]?.GetValue<double?>() * 10.0,
                MalUrl = entry?["url"]?.ToString() ?? $"https://myanimelist.net/anime/{malId}",
                SiteUrl = entry?["url"]?.ToString() ?? $"https://myanimelist.net/anime/{malId}"
            };
            return anime;
        }

        public async Task<List<UnifiedAnimeEntry>> GetCurrentSeasonAsync()
        {
            var seasonData = await _tenraiClient.GetCurrentSeasonAsync();
            var list = new List<UnifiedAnimeEntry>();
            if (seasonData?.Data != null)
            {
                foreach (var a in seasonData.Data)
                {
                    list.Add(MapTenraiAnime(a));
                }
            }
            return list;
        }

        public async Task<List<UnifiedAnimeEntry>> GetSeasonalAnimeAsync(int year, Season season)
        {
            var seasonData = await _tenraiClient.GetSeasonAsync(year, season);
            var list = new List<UnifiedAnimeEntry>();
            if (seasonData?.Data != null)
            {
                foreach (var a in seasonData.Data)
                {
                    list.Add(MapTenraiAnime(a));
                }
            }
            return list;
        }

        public async Task<List<UnifiedAnimeEntry>> GetScheduleAsync()
        {
            var scheduleData = await _tenraiClient.GetScheduleAsync();
            var list = new List<UnifiedAnimeEntry>();
            if (scheduleData?.Data != null)
            {
                foreach (var a in scheduleData.Data)
                {
                    list.Add(MapTenraiAnime(a));
                }
            }
            return list;
        }

        public async Task<UnifiedAnimeEntry?> GetAnimeDetailsAsync(long malId)
        {
            var fullData = await _tenraiClient.GetAnimeFullDataAsync(malId);
            if (fullData?.Data == null) return null;
            return MapTenraiAnime(fullData.Data);
        }

        private static UnifiedAnimeEntry MapTenraiAnime(Tenrai.Anime a)
        {
            string titleEnglish = a.Titles?.FirstOrDefault(t => t.Type == "English")?.Title ?? a.TitleEnglish ?? a.Title ?? "";
            string titleRomaji = a.Titles?.FirstOrDefault(t => t.Type == "Default")?.Title ?? a.Title ?? "";
            string titleJapanese = a.Titles?.FirstOrDefault(t => t.Type == "Japanese")?.Title ?? a.TitleJapanese ?? "";

            return new UnifiedAnimeEntry
            {
                Id = "mal_" + a.MalId,
                MalId = a.MalId.HasValue ? (int)a.MalId.Value : null,
                TitleRomaji = titleRomaji,
                TitleEnglish = titleEnglish,
                TitleNative = titleJapanese,
                CoverImage = a.Images?.JPG?.LargeImageUrl ?? a.Images?.JPG?.ImageUrl ?? "",
                Format = a.Type ?? "TV",
                Status = a.Status ?? "RELEASING",
                Episodes = a.Episodes,
                AverageScore = a.Score.HasValue ? a.Score.Value * 10.0 : null,
                Popularity = a.Popularity,
                Synopsis = a.Synopsis ?? "",
                Source = a.Source ?? "",
                SiteUrl = a.Url,
                MalUrl = a.Url,
                Season = a.Season?.ToString()?.ToUpperInvariant(),
                SeasonYear = a.Year,
                Genres = a.Genres != null ? a.Genres.Select(g => g.Name).ToList() : new(),
                Studios = a.Studios != null ? a.Studios.Select(s => s.Name).ToList() : new()
            };
        }
    }
}
