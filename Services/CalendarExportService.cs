using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LiveChartTracker.Models;

namespace LiveChartTracker.Services
{
    public interface ICalendarExportService
    {
        string GenerateIcsCalendar(IEnumerable<CalendarMonthEpisode> episodes, string calendarName, int reminderMinutes = 15);
    }

    public class CalendarExportService : ICalendarExportService
    {
        public string GenerateIcsCalendar(IEnumerable<CalendarMonthEpisode> episodes, string calendarName, int reminderMinutes = 15)
        {
            var sb = new StringBuilder();
            sb.AppendLine("BEGIN:VCALENDAR");
            sb.AppendLine("VERSION:2.0");
            sb.AppendLine("PRODID:-//LiveChart Anime Monthly Calendar//PL");
            sb.AppendLine("CALSCALE:GREGORIAN");
            sb.AppendLine("METHOD:PUBLISH");
            sb.AppendLine($"X-WR-CALNAME:{EscapeIcs(calendarName)}");
            sb.AppendLine("X-WR-TIMEZONE:UTC");

            var now = DateTimeOffset.UtcNow;

            foreach (var ep in episodes)
            {
                var startTime = ep.AiringAt.UtcDateTime;
                var durationMinutes = ep.EpisodeDuration ?? 24;
                var endTime = startTime.AddMinutes(durationMinutes);
                var uid = $"anime-{ep.Id}-{startTime:yyyyMMddTHHmmssZ}@livechart-calendar";

                sb.AppendLine("BEGIN:VEVENT");
                sb.AppendLine($"UID:{uid}");
                sb.AppendLine($"DTSTAMP:{now:yyyyMMddTHHmmssZ}");
                sb.AppendLine($"DTSTART:{startTime:yyyyMMddTHHmmssZ}");
                sb.AppendLine($"DTEND:{endTime:yyyyMMddTHHmmssZ}");
                sb.AppendLine($"SUMMARY:{EscapeIcs($"{ep.DisplayTitle} - Odc. {ep.EpisodeNumber}")}");
                
                var desc = new StringBuilder();
                desc.AppendLine($"Premiera odcinka {ep.EpisodeNumber} serii {ep.DisplayTitle}");
                if (!string.IsNullOrEmpty(ep.Format)) desc.AppendLine($"Format: {ep.Format}");
                if (ep.Studios.Any()) desc.AppendLine($"Studio: {string.Join(", ", ep.Studios)}");
                if (ep.UserProgress.HasValue) desc.AppendLine($"Twój postęp: {ep.UserProgress}/{ep.TotalEpisodes?.ToString() ?? "?"}");
                if (!string.IsNullOrEmpty(ep.SiteUrl)) desc.AppendLine($"Szczegóły: {ep.SiteUrl}");
                
                sb.AppendLine($"DESCRIPTION:{EscapeIcs(desc.ToString())}");
                if (!string.IsNullOrEmpty(ep.SiteUrl))
                {
                    sb.AppendLine($"URL:{ep.SiteUrl}");
                }

                if (reminderMinutes > 0)
                {
                    sb.AppendLine("BEGIN:VALARM");
                    sb.AppendLine("ACTION:DISPLAY");
                    sb.AppendLine($"DESCRIPTION:{EscapeIcs($"Nowy odcinek: {ep.DisplayTitle} (Odc. {ep.EpisodeNumber})")}");
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
