using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FetchLog.Models;

namespace FetchLog.Services
{
    public class ExportService
    {
        public async Task ExportToCsvAsync(IEnumerable<SearchResult> results, string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("File Name,Source Path,Size,Type,Last Modified,Matches,First Match Line,Hash (MD5),Format,Duplicate Of");

            foreach (var r in results)
            {
                sb.AppendLine(string.Join(",",
                    Q(r.FileName),
                    Q(r.SourcePath),
                    Q(r.Size),
                    Q(r.FileType),
                    Q(r.LastModifiedDisplay),
                    Q(r.MatchCountDisplay),
                    Q(r.FirstMatchLineDisplay),
                    Q(r.Md5Hash ?? ""),
                    Q(r.LogFormat),
                    Q(r.IsDuplicate ? (r.DuplicateOf ?? "") : "")));
            }

            await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);
        }

        public async Task ExportToHtmlAsync(IEnumerable<SearchResult> results, string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><meta charset='UTF-8'><title>FetchLog Search Results</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body { font-family: Consolas, monospace; font-size: 13px; margin: 20px; }");
            sb.AppendLine("h2 { color: #2e7d32; }");
            sb.AppendLine("p  { color: #555; }");
            sb.AppendLine("table { border-collapse: collapse; width: 100%; }");
            sb.AppendLine("th { background: #4CAF50; color: white; padding: 8px; text-align: left; }");
            sb.AppendLine("td { padding: 6px 8px; border-bottom: 1px solid #ddd; word-break: break-all; }");
            sb.AppendLine("tr:nth-child(even) { background: #f9f9f9; }");
            sb.AppendLine("tr:hover { background: #e8f5e9; }");
            sb.AppendLine(".dup { background: #FFF3E0 !important; }");
            sb.AppendLine("</style></head><body>");
            sb.AppendLine("<h2>FetchLog Search Results</h2>");
            sb.AppendLine($"<p>Exported: {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>");
            sb.AppendLine("<table><thead><tr>");
            sb.AppendLine("<th>File Name</th><th>Source Path</th><th>Size</th><th>Type</th>" +
                          "<th>Last Modified</th><th>Matches</th><th>First Line</th>" +
                          "<th>Hash (MD5)</th><th>Format</th><th>Duplicate Of</th>");
            sb.AppendLine("</tr></thead><tbody>");

            foreach (var r in results)
            {
                var rowClass = r.IsDuplicate ? " class=\"dup\"" : "";
                sb.AppendLine($"<tr{rowClass}>");
                sb.AppendLine($"<td>{H(r.FileName)}</td>");
                sb.AppendLine($"<td>{H(r.SourcePath)}</td>");
                sb.AppendLine($"<td>{r.Size}</td>");
                sb.AppendLine($"<td>{r.FileType}</td>");
                sb.AppendLine($"<td>{r.LastModifiedDisplay}</td>");
                sb.AppendLine($"<td>{r.MatchCountDisplay}</td>");
                sb.AppendLine($"<td>{r.FirstMatchLineDisplay}</td>");
                sb.AppendLine($"<td>{H(r.Md5Hash ?? "")}</td>");
                sb.AppendLine($"<td>{H(r.LogFormat)}</td>");
                sb.AppendLine($"<td>{H(r.IsDuplicate ? (r.DuplicateOf ?? "") : "")}</td>");
                sb.AppendLine("</tr>");
            }

            sb.AppendLine("</tbody></table></body></html>");
            await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);
        }

        // CSV: wrap in quotes and escape internal quotes
        private static string Q(string s) => $"\"{s.Replace("\"", "\"\"")}\"";

        // HTML: entity-encode
        private static string H(string s) =>
            s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }
}
