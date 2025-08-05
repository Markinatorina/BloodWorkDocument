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
using System.IO;
using System.Collections.Generic;
using BloodWorkDocument_API.Services;

namespace BloodWorkDocument_API.Services
{
    public class BloodWorkDocumentService
    {
        public async Task<string> ExtractToJsonAsync(Stream pdfStream, string seqn)
        {
            var rawJson = await GetRawDocument(pdfStream);
            var rows = JsonSerializer.Deserialize<List<List<string>>>(rawJson);
            if (rows == null)
                rows = new List<List<string>>();

            var valueToKey = new Dictionary<string, string>();
            foreach (var kv in Dictionaries.NHANESVariableMap)
            {
                if (!string.IsNullOrEmpty(kv.Value) && !valueToKey.ContainsKey(kv.Value))
                {
                    valueToKey[kv.Value] = kv.Key;
                }
            }

            var serialisedRows = new List<List<string>>();
            foreach (var arr in rows)
            {
                if (arr != null && arr.Count > 0 && !string.IsNullOrWhiteSpace(arr[0]) && valueToKey.TryGetValue(arr[0], out var key))
                {
                    arr[0] = key;
                    serialisedRows.Add(arr);
                }
            }

            var json = JsonSerializer.Serialize(serialisedRows);

            var extractedDir = Path.Combine(Directory.GetCurrentDirectory(), "Extracted");
            if (!Directory.Exists(extractedDir))
                Directory.CreateDirectory(extractedDir);

            var filePath = Path.Combine(extractedDir, $"{seqn}_lab_results.json");
            await File.WriteAllTextAsync(filePath, json);
            return json;
        }

        public async Task<string> GetRawDocument(Stream pdfStream)
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
        /*
        public async Task<List<(string Left, string Right)>> GetRawDocumentAsDictionary(Stream pdfStream)
        {
            var json = await GetRawDocument(pdfStream);
            var rows = JsonSerializer.Deserialize<List<List<string>>>(json);
            var result = new List<(string Left, string Right)>();
            if (rows != null)
            {
                foreach (var arr in rows)
                {
                    if (arr == null || arr.Count < 2) continue;
                    var left = arr[0];
                    var right = arr[1];
                    if (!string.IsNullOrWhiteSpace(left) || !string.IsNullOrWhiteSpace(right))
                    {
                        result.Add((left, right));
                    }
                }
            }
            return result;
        }*/
    }
}
