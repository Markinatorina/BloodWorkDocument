using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace BloodWorkDocument_API.Services
{
    public class BloodWorkDocumentService
    {
        public async Task<string> ExtractToJsonAsync(Stream pdfStream, string seqn)
        {
            var analytes = new Dictionary<string, string>();
            string extractedText;

            using (var reader = PdfDocument.Open(pdfStream))
            {
                var sb = new StringBuilder();
                foreach (UglyToad.PdfPig.Content.Page page in reader.GetPages())
                {
                    sb.AppendLine(page.Text);
                }
                extractedText = sb.ToString();
            }

            foreach (var (code, label) in Dictionaries.NHANESVariableMap)
            {
                if (!string.IsNullOrWhiteSpace(label))
                {
                    var match = Regex.Match(extractedText, $@"{Regex.Escape(label)}.*?([\d.]+)", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        analytes[code] = match.Groups[1].Value;
                    }
                }
            }

            var json = JsonSerializer.Serialize(new
            {
                SEQN = seqn,
                ExtractedAt = DateTime.UtcNow,
                Analytes = analytes
            }, new JsonSerializerOptions { WriteIndented = true });

            var directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Extracted");
            Directory.CreateDirectory(directory);

            var filePath = Path.Combine(directory, $"{seqn}_lab_results.json");
            await File.WriteAllTextAsync(filePath, json);

            return json;
        }
    }
}
