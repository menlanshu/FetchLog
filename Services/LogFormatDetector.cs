using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace FetchLog.Services
{
    /// <summary>
    /// Detects common log file formats by scanning a sample of lines. (#22)
    /// </summary>
    public static class LogFormatDetector
    {
        // Apache/Nginx: "127.0.0.1 - - [01/Jan/2024:12:00:00 +0000] "GET /path HTTP/1.1" 200 1234"
        private static readonly Regex _apache = new(
            @"^\d{1,3}(?:\.\d{1,3}){3} .+ \[.+\] "".+""",
            RegexOptions.Compiled);

        // Syslog: "Jan  1 00:00:00 hostname process[pid]: message"
        private static readonly Regex _syslog = new(
            @"^(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s+\d{1,2} \d{2}:\d{2}:\d{2}",
            RegexOptions.Compiled);

        // Log4j / NLog / Serilog: "2024-01-15 08:30:00 [INFO] message"  or "2024-01-15T08:30:00 INFO ..."
        private static readonly Regex _log4j = new(
            @"^\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}.*\b(?:DEBUG|INFO|WARN(?:ING)?|ERROR|FATAL|TRACE|VERBOSE)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // JSON log: entire line is a JSON object
        private static readonly Regex _json = new(
            @"^\s*\{.+\}\s*$",
            RegexOptions.Compiled);

        // Windows Event log text export
        private static readonly Regex _winEvent = new(
            @"^(?:Log Name|Source|Event ID|Task Category|Level|Keywords|User|Computer|Description):",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // CSV with at least 3 comma-separated fields
        private static readonly Regex _csv = new(
            @"^(?:[^,]*,){3,}[^,]*$",
            RegexOptions.Compiled);

        public static string Detect(string[] sampleLines)
        {
            if (sampleLines == null || sampleLines.Length == 0) return "";

            int toScan = Math.Min(sampleLines.Length, 15);
            int apache = 0, syslog = 0, log4j = 0, json = 0, winEvent = 0, csv = 0;

            for (int i = 0; i < toScan; i++)
            {
                var line = sampleLines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (_apache.IsMatch(line)) apache++;
                if (_syslog.IsMatch(line)) syslog++;
                if (_log4j.IsMatch(line)) log4j++;
                if (_json.IsMatch(line)) json++;
                if (_winEvent.IsMatch(line)) winEvent++;
                if (_csv.IsMatch(line)) csv++;
            }

            var scores = new (int score, string label)[]
            {
                (apache, "Apache"),
                (syslog, "Syslog"),
                (log4j, "Log4j"),
                (json, "JSON"),
                (winEvent, "WinEvent"),
                (csv, "CSV"),
            };

            var best = scores.OrderByDescending(x => x.score).First();
            return best.score >= 2 ? best.label : "Generic";
        }
    }
}
