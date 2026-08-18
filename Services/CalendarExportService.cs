using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LiveChartTracker.Models;

namespace LiveChartTracker.Services
{
    public interface ICalendarExportService
    {
        string GenerateIcsCalendar(IEnumerable<UnifiedAnimeEntry> animeList, string calendarName, int reminderMinutes = 15);
    }

    public class CalendarExportService : ICalendarExportService
    {
        public string GenerateIcsCalendar(IEnumerable<UnifiedAnimeEntry> animeList, string calendarName, int reminderMinutes = 15)
        {
            var sb = new StringBuilder();
            sb.AppendLine("BEGIN:VCALENDAR");
            sb.AppendLine("VERSION:2.0");
            sb.AppendLine("PRODID:-//LiveChart Anime Tracker//PL");
            sb.AppendLine("CALSCALE:GREGORIAN");
            sb.AppendLine("METHOD:PUBLISH");
            sb.AppendLine($"X-WR-CALNAME:{EscapeIcs(calendarName)}");
            sb.AppendLine("X-WR-TIMEZONE:UTC");

            var now = DateTimeOffset.UtcNow;

            foreach (var anime in animeList)
            {
                if (anime.NextAiringEpisode == null) continue;

                var ep = anime.NextAiringEpisode;
                var startTime = ep.AiringAt.UtcDateTime;
                var durationMinutes = anime.EpisodeDuration ?? 24;
                var endTime = startTime.AddMinutes(durationMinutes);
                var uid = $"anime-{anime.Id}-ep{ep.Episode}-{startTime:yyyyMMddTHHmmssZ}@livechart-tracker";

                sb.AppendLine("BEGIN:VEVENT");
                sb.AppendLine($"UID:{uid}");
                sb.AppendLine($"DTSTAMP:{now:yyyyMMddTHHmmssZ}");
                sb.AppendLine($"DTSTART:{startTime:yyyyMMddTHHmmssZ}");
                sb.AppendLine($"DTEND:{endTime:yyyyMMddTHHmmssZ}");
                sb.AppendLine($"SUMMARY:{EscapeIcs($"{anime.DisplayTitle} - Odc. {ep.Episode}")}");
                
                var desc = new StringBuilder();
                desc.AppendLine($"Premiera odcinka {ep.Episode} serii {anime.DisplayTitle}");
                if (!string.IsNullOrEmpty(anime.Format)) desc.AppendLine($"Typ: {anime.Format}");
                if (anime.Studios.Any()) desc.AppendLine($"Studio: {string.Join(", ", anime.Studios)}");
                if (anime.AverageScore.HasValue) desc.AppendLine($"Ocena: {anime.AverageScore.Value:F1}/100");
                if (anime.UserProgress.HasValue) desc.AppendLine($"Twój postęp: {anime.UserProgress}/{anime.Episodes?.ToString() ?? "?"}");
                if (!string.IsNullOrEmpty(anime.SiteUrl)) desc.AppendLine($"Więcej info: {anime.SiteUrl}");
                
                sb.AppendLine($"DESCRIPTION:{EscapeIcs(desc.ToString())}");
                if (!string.IsNullOrEmpty(anime.SiteUrl))
                {
                    sb.AppendLine($"URL:{anime.SiteUrl}");
                }

                // Reminder / Alarm
                if (reminderMinutes > 0)
                {
                    sb.AppendLine("BEGIN:VALARM");
                    sb.AppendLine("ACTION:DISPLAY");
                    sb.AppendLine($"DESCRIPTION:{EscapeIcs($"Nowy odcinek: {anime.DisplayTitle} (Odc. {ep.Episode})")}");
                    sb.AppendLine($"TRIGGER:-PT{reminderMinutes}M");
                    sb.AppendLine("END:VALARM");
                }

                sb.AppendLine("END:VEVENT");
            }

            sb.AppendLine("END:VCALENDAR");
            return sb.ToString();
        }

        private static string EscapeIcs(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text
                .Replace("\\", "\\\\")
                .Replace(";", "\\;")
                .Replace(",", "\\,")
                .Replace("\r\n", "\\n")
                .Replace("\n", "\\n")
                .Replace("\r", "\\n");
        }
    }
}
