using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Globalization;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tabula;
using Tabula.Detectors;
using Tabula.Extractors;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace BloodWorkDocument_API.Services
{
    public class BloodWorkDocumentService
    {
        public async Task<string> ExtractToJsonAsync(Stream pdfStream, string seqn)
        {
            var analytes = new Dictionary<string, string>();
            var labelToCode = Dictionaries.NHANESVariableMap
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                .ToDictionary(kv => kv.Value.Trim(), kv => kv.Key);

            var lines = new List<string>();
            using (var reader = PdfDocument.Open(pdfStream))
            {
                foreach (var page in reader.GetPages())
                {
                    var words = page.GetWords().ToList();
                    var groupedLines = words
                        .GroupBy(w => w.BoundingBox.Top, new YCoordinateComparer())
                        .OrderByDescending(g => g.Key);
                    foreach (var group in groupedLines)
                    {
                        var rowWords = group
                            .OrderBy(w => w.BoundingBox.Left)
                            .Select(w => w.Text.Trim())
                            .Where(t => !string.IsNullOrWhiteSpace(t))
                            .ToList();
                        if (rowWords.Count > 0)
                        {
                            lines.Add(string.Join("&& ", rowWords));
                        }
                    }
                }
            }

            foreach (var line in lines)
            {
                var parts = line.Split(new[] { "&&" }, 2, StringSplitOptions.None);
                if (parts.Length == 2)
                {
                    var left = parts[0].Trim();
                    var right = parts[1].Trim();
                    if (labelToCode.TryGetValue(left, out var code))
                    {
                        analytes[code] = right;
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

        public async Task<List<string>> GetRawDocument2(Stream pdfStream, string fileName)
        {
            var lines = new List<string>();

            using var reader = PdfDocument.Open(pdfStream);

            foreach (var page in reader.GetPages())
            {
                var words = page.GetWords().ToList();

                var groupedLines = words
                    .GroupBy(w => w.BoundingBox.Top, new YCoordinateComparer())
                    .OrderByDescending(g => g.Key);

                foreach (var group in groupedLines)
                {
                    var rowWords = group
                        .OrderBy(w => w.BoundingBox.Left)
                        .Select(w => w.Text.Trim())
                        .Where(t => !string.IsNullOrWhiteSpace(t))
                        .ToList();

                    if (rowWords.Count == 0)
                        continue;

                    var fullLine = string.Join(" ", rowWords);
                    var normalizedLine = Normalize(fullLine);

                    int splitIndex = rowWords.FindIndex(w =>
                        Regex.IsMatch(w, @"^\d") ||
                        Regex.IsMatch(w, @"\d") ||
                        Regex.IsMatch(w, @"[<≥≤–\-]\s*\d") ||
                        w.Contains("mg") || w.Contains("µ") || w.Contains("μ") ||
                        w.Contains("mL") || w.Contains("g/dL") ||
                        w.Contains("/hr") || w.Contains("mmol") ||
                        w.Contains("units")
                    );

                    if (splitIndex > 0)
                    {
                        var labelPart = string.Join(" ", rowWords.Take(splitIndex));
                        var valuePart = string.Join(" ", rowWords.Skip(splitIndex));
                        lines.Add($"{labelPart}: {valuePart}");
                    }
                    else
                    {
                        lines.Add(fullLine);
                    }
                }
            }

            return lines;
        }

        public async Task<List<string>> GetRawDocument(Stream pdfStream, string fileName)
        {
            var lines = new List<string>();

            using (var reader = PdfDocument.Open(pdfStream))
            {
                foreach (var page in reader.GetPages())
                {
                    var words = page.GetWords().ToList();

                    var groupedLines = words
                        .GroupBy(w => w.BoundingBox.Top, new YCoordinateComparer())
                        .OrderByDescending(g => g.Key);

                    foreach (var group in groupedLines)
                    {
                        var rowWords = group
                            .OrderBy(w => w.BoundingBox.Left)
                            .Select(w => w.Text.Trim())
                            .Where(t => !string.IsNullOrWhiteSpace(t))
                            .ToList();

                        if (rowWords.Count > 0)
                        {
                            lines.Add(string.Join(" ", rowWords));
                        }
                    }
                }
            }

            return lines;
        }
        private class YCoordinateComparer : IEqualityComparer<double>
        {
            private const double Tolerance = 6.0;
            public bool Equals(double y1, double y2)
            {
                return Math.Abs(y1 - y2) < Tolerance;
            }
            public int GetHashCode(double y)
            {
                return (int)(y / Tolerance);
            }
        }
        private static string Normalize(string input)
        {
            return string.Concat(input
                .ToLowerInvariant()
                .Normalize(NormalizationForm.FormD)
                .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark))
                .Replace("–", "-")
                .Trim();
        }

        public async Task<List<List<string>>> GetRawDocument3(Stream pdfStream, string fileName, double xThreshold = 240)
        {
            var rows = new List<List<string>>();

            using (var reader = PdfDocument.Open(pdfStream))
            {
                foreach (var page in reader.GetPages())
                {
                    var words = page.GetWords().ToList();

                    var groupedLines = words
                        .GroupBy(w => w.BoundingBox.Top, new YCoordinateComparer())
                        .OrderByDescending(g => g.Key);

                    foreach (var group in groupedLines)
                    {
                        var leftColumn = new List<string>();
                        var rightColumn = new List<string>();

                        foreach (var word in group.OrderBy(w => w.BoundingBox.Left))
                        {
                            var text = word.Text.Trim();
                            if (string.IsNullOrWhiteSpace(text))
                                continue;

                            if (word.BoundingBox.Left < xThreshold)
                                leftColumn.Add(text);
                            else
                                rightColumn.Add(text);
                        }

                        if (leftColumn.Count > 0 || rightColumn.Count > 0)
                        {
                            rows.Add(new List<string>
                    {
                        string.Join(" ", leftColumn),
                        string.Join(" ", rightColumn)
                    });
                        }
                    }
                }
            }

            return rows;
        }


        public async Task<string> GetRawDocument4(Stream pdfStream, string seqn)
        {
            using var ms = new MemoryStream();
            await pdfStream.CopyToAsync(ms);
            ms.Position = 0;

            using PdfDocument document = PdfDocument.Open(ms, new ParsingOptions { ClipPaths = true });

            using var outMs = new MemoryStream();
            using var writer = new Utf8JsonWriter(outMs, new JsonWriterOptions { Indented = true });
            writer.WriteStartObject();
            writer.WriteString("seqn", seqn);
            writer.WriteStartArray("pages");

            for (int pageNum = 1; pageNum <= document.NumberOfPages; pageNum++)
            {
                writer.WriteStartObject();
                writer.WriteNumber("page", pageNum);
                writer.WriteStartArray("tables");

                PageArea page = ObjectExtractor.Extract(document, pageNum);

                IExtractionAlgorithm algo = new BasicExtractionAlgorithm();
                var tables = algo.Extract(page);

                if (tables.Count == 0)
                {
                    algo = new SpreadsheetExtractionAlgorithm();
                    tables = algo.Extract(page);
                }

                foreach (var table in tables)
                {
                    writer.WriteStartArray();

                    foreach (var row in table.Rows)
                    {
                        writer.WriteStartArray();
                        foreach (var cell in row)
                        {
                            writer.WriteStringValue(cell?.ToString() ?? "");
                        }
                        writer.WriteEndArray();
                    }

                    writer.WriteEndArray();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            await writer.FlushAsync();

            return Encoding.UTF8.GetString(outMs.ToArray());
        }
        public class TextItem
        {
            public string Text;
            public double X;
            public double Y;
            public double Width;
            public double Height;
        }

        public List<List<string>> ExtractTableFromPage(List<TextItem> items, double xTol = 2, double yTol = 2)
        {
            var xEdges = new SortedSet<double>();
            var yEdges = new SortedSet<double>();

            foreach (var it in items)
            {
                xEdges.Add(it.X);
                xEdges.Add(it.X + it.Width);
                yEdges.Add(it.Y);
                yEdges.Add(it.Y + it.Height);
            }

            List<double> xs = ClusterSorted(xEdges.ToList(), xTol);
            List<double> ys = ClusterSorted(yEdges.ToList(), yTol);

            int rows = ys.Count - 1;
            int cols = xs.Count - 1;
            var grid = new string[rows, cols];

            foreach (var it in items)
            {
                int col = FindBin(it.X, xs);
                int row = FindBin(it.Y, ys);
                if (row >= 0 && row < rows && col >= 0 && col < cols)
                {
                    var prev = grid[row, col];
                    grid[row, col] = string.IsNullOrEmpty(prev)
                        ? it.Text
                        : prev + " " + it.Text;
                }
            }

            var result = new List<List<string>>();
            for (int r = 0; r < rows; r++)
            {
                var rowList = new List<string>();
                for (int c = 0; c < cols; c++)
                    rowList.Add(grid[r, c]?.Trim() ?? "");
                result.Add(rowList);
            }

            return result;
        }

        private List<double> ClusterSorted(List<double> sorted, double tol)
        {
            var clustered = new List<double>();
            double? current = null;
            foreach (var v in sorted)
            {
                if (current == null || Math.Abs(v - current.Value) > tol)
                    clustered.Add(v);
                current = current;
            }
            return clustered;
        }

        private int FindBin(double v, List<double> edges)
        {
            for (int i = 0; i < edges.Count - 1; i++)
                if (v >= edges[i] && v < edges[i + 1])
                    return i;
            return -1;
        }



        public async Task<string> GetRawDocument6(Stream pdfStream)
        {
            var rows = new List<string[]>();

            using (var document = PdfDocument.Open(pdfStream))
            {
                foreach (var page in document.GetPages())
                {
                    double midX = page.Width / 2.0;

                    var wordItems = page.GetWords()
                        .Select(w => new
                        {
                            Text = w.Text,
                            X = (w.BoundingBox.Left + w.BoundingBox.Right) / 2.0,
                            Y = (w.BoundingBox.Top + w.BoundingBox.Bottom) / 2.0
                        })
                        .ToList();

                    const double yTolerance = 5.0;
                    var rowClusters = new List<List<dynamic>>();

                    foreach (var item in wordItems)
                    {
                        bool added = false;
                        foreach (var cluster in rowClusters)
                        {
                            if (Math.Abs(item.Y - cluster[0].Y) <= yTolerance)
                            {
                                cluster.Add(item);
                                added = true;
                                break;
                            }
                        }
                        if (!added)
                        {
                            rowClusters.Add(new List<dynamic> { item });
                        }
                    }

                    foreach (var cluster in rowClusters.OrderByDescending(c => c[0].Y))
                    {
                        var leftText = string.Join(" ", cluster.Where(i => i.X < midX).OrderBy(i => i.X).Select(i => i.Text)).Trim();
                        var rightText = string.Join(" ", cluster.Where(i => i.X >= midX).OrderBy(i => i.X).Select(i => i.Text)).Trim();

                        if (!string.IsNullOrEmpty(leftText) || !string.IsNullOrEmpty(rightText))
                        {
                            rows.Add(new[] { leftText, rightText });
                        }
                    }
                }
            }

            string json = JsonSerializer.Serialize(rows);
            return await Task.FromResult(json);
        }
    }
}
