using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Globalization;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;
using Tabula;
using Tabula.Detectors;
using Tabula.Extractors;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using System.IO;
using System.Collections.Generic;
using BloodWorkDocument_API.Services;
using Newtonsoft.Json;

namespace BloodWorkDocument_API.Services
{
    public class BloodWorkDocumentService
    {
        public async Task<string> ExtractToJsonAsync(Stream pdfStream, string seqn)
        {
            var rawJson = await GetPreprocessedDocumentAsJson(pdfStream);
            List<List<string>> rows = null;
            try
            {
                rows = JsonConvert.DeserializeObject<List<List<string>>>(rawJson);
            }
            catch (JsonReaderException)
            {
                var intermediate = JsonConvert.DeserializeObject<string>(rawJson);
                rows = JsonConvert.DeserializeObject<List<List<string>>>(intermediate);
            }
            if (rows == null)
                rows = new List<List<string>>();

            string lastKey = null;
            for (int i = 0; i < rows.Count; i++)
            {
                var arr = rows[i];
                if (arr.Count < 2) continue;

                var key = arr[0].Trim();
                var val = arr[1].Trim();

                if (string.IsNullOrEmpty(key))
                {
                    if (lastKey != null)
                    {
                        rows[i - 1][1] = (rows[i - 1][1] ?? "") + val;
                    }
                    rows.RemoveAt(i);
                    i--;
                    continue;
                }

                if (lastKey != null
                    && Regex.IsMatch(key, @"^[A-Za-z]{1,10}$")
                    && Regex.IsMatch(lastKey, @"[,:;\-]$"))
                {
                    rows[i - 1][0] = lastKey + " " + key;
                    rows[i - 1][1] = (rows[i - 1][1] ?? "") + " " + val;
                    rows.RemoveAt(i);
                    i--;
                    continue;
                }

                lastKey = key;
            }

            var valueToKeys = Dictionaries.NHANESVariableMap
                .GroupBy(x => x.Value)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Key).ToList());

            var extractedDict = new Dictionary<string, string>();
            foreach (var arr in rows)
            {
                if (arr != null && arr.Count > 0 && !string.IsNullOrWhiteSpace(arr[0]))
                {
                    var value = arr[0];
                    if (valueToKeys.TryGetValue(value, out var keys))
                    {
                        foreach (var key in keys)
                        {
                            extractedDict[key] = arr.Count > 1 ? arr[1] : "";
                        }
                    }
                }
            }

            var serialisedRows = new List<List<string>>
            {
                new List<string> { "SEQN", seqn }
            };

            foreach (var key in Dictionaries.NHANESVariableMap.Keys)
            {
                var value = extractedDict.ContainsKey(key) ? extractedDict[key] : "";
                serialisedRows.Add(new List<string> { key, value });
            }

            var json = JsonConvert.SerializeObject(serialisedRows);

            var extractedDir = Path.Combine(Directory.GetCurrentDirectory(), "Extracted");
            if (!Directory.Exists(extractedDir))
                Directory.CreateDirectory(extractedDir);

            var filePath = Path.Combine(extractedDir, $"{seqn}_lab_results.json");
            await File.WriteAllTextAsync(filePath, json);
            return json;
        }

        private List<List<string>> GetRawDocumentRows(Stream pdfStream)
        {
            var rows = new List<List<string>>();
            using (var document = PdfDocument.Open(pdfStream))
            {
                foreach (var page in document.GetPages())
                {
                    double midX = page.Width / 2.0;
                    const double yTolerance = 5.0;
                    var wordItems = page.GetWords()
                        .Select(w => new
                        {
                            Text = w.Text,
                            X = (w.BoundingBox.Left + w.BoundingBox.Right) / 2.0,
                            Y = (w.BoundingBox.Top + w.BoundingBox.Bottom) / 2.0
                        })
                        .ToList();
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
                            rows.Add(new List<string> { leftText, rightText });
                        }
                    }
                }
            }
            return rows;
        }

        public async Task<string> GetRawDocumentAsJson(Stream pdfStream)
        {
            var rows = GetRawDocumentRows(pdfStream);
            string json = JsonConvert.SerializeObject(rows);
            return await Task.FromResult(json);
        }

        public async Task<string> GetPreprocessedDocumentAsJson(Stream pdfStream)
        {
            var rows = GetRawDocumentRows(pdfStream);
            rows = PreprocessRows(rows);
            var finalJson = JsonConvert.SerializeObject(rows);
            return await Task.FromResult(finalJson);
        }
        private List<List<string>> PreprocessRows(List<List<string>> rows)
        {
            var output = new List<List<string>>();
            int i = 0;

            while (i < rows.Count)
            {
                var arr = rows[i];
                if (arr.Count < 2)
                {
                    i++;
                    continue;
                }

                var key = arr[0].Trim();
                var val = arr[1].Trim();

                // --- CASE 1: Multi-row key+subkeys with values all present
                // e.g. { "BaseKey,", "val1" },
                //      { "Sub1",      "val2" },
                //      { "[Method]",  "val3" }
                if (!string.IsNullOrWhiteSpace(key))
                {
                    int j = i + 1;
                    var keyParts = new List<string> { key.TrimEnd(',').Trim() };
                    var valParts = new List<string>();
                    if (!string.IsNullOrEmpty(val))
                        valParts.Add(val);

                    while (j < rows.Count)
                    {
                        var kj = rows[j][0].Trim();
                        var vj = rows[j][1].Trim();

                        bool isSubKey = Regex.IsMatch(kj, @"^[A-Za-z0-9\-\+]{1,15}$")   
                                     || Regex.IsMatch(kj, @"^\[.*\]$");               

                        if (!isSubKey)
                            break;

                        keyParts.Add(kj);
                        if (!string.IsNullOrEmpty(vj))
                            valParts.Add(vj);

                        j++;
                    }

                    if (j > i + 1)
                    {
                        var mergedKey = string.Join(" ", keyParts);
                        var mergedVal = string.Join("; ", valParts);
                        output.Add(new List<string> { mergedKey, mergedVal });
                        i = j;
                        continue;
                    }
                }

                // --- CASE 2: Method‐only merge when value is empty
                // e.g. ["Anti-RNP antibodies","val"], ["[by chemiluminescence]", ""]
                if (output.Count > 0
                    && Regex.IsMatch(key, @"^\[.*\]$")
                    && string.IsNullOrWhiteSpace(val))
                {
                    // append [method] to the last output key
                    output[output.Count - 1][0] += " " + key;
                    // do not change its value
                    i++;
                    continue;
                }

                // --- CASE 3: Sub‐key merge when base value empty (IgG/IgM pattern)
                // e.g. ["Anticardiolipin antibodies",""], ["IgG","…"], ["IgM","…"]
                if (!string.IsNullOrEmpty(output.LastOrDefault()?[1])
                    && string.IsNullOrWhiteSpace(val)
                    && Regex.IsMatch(key, @"^[A-Za-z0-9\-\+]{1,15}$"))
                {
                    //
                }

                // --- CASE 4: Empty‐key‐merge (append value to previous)
                if (string.IsNullOrEmpty(key)
                    && output.Count > 0)
                {
                    output[output.Count - 1][1] += val;
                    i++;
                    continue;
                }

                // --- CASE 5: Tiny continuation‐key stitch (when previous key ends in punctuation)
                if (output.Count > 0
                    && Regex.IsMatch(key, @"^[A-Za-z]{1,10}$")
                    && Regex.IsMatch(output[output.Count - 1][0], @"[,:;\-]$"))
                {
                    output[output.Count - 1][0] += " " + key;
                    output[output.Count - 1][1] += " " + val;
                    i++;
                    continue;
                }

                // --- DEFAULT: copy through
                output.Add(new List<string> { key, val });
                i++;
            }

            return output;
        }

        private static string EscapeForCSharp(string input)
        {
            if (input == null) return string.Empty;
            return input.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
        }

        public async Task<List<(string Left, string Right)>> GetDocumentAsDictionary(Stream pdfStream)
        {
            var json = await GetPreprocessedDocumentAsJson(pdfStream);
            var rows = JsonConvert.DeserializeObject<List<List<string>>>(json);
            var result = new List<(string Left, string Right)>();
            if (rows != null)
            {
                foreach (var arr in rows)
                {
                    if (arr == null || arr.Count < 2) continue;
                    var left = EscapeForCSharp(arr[0]);
                    var right = EscapeForCSharp(arr[1]);
                    if (!string.IsNullOrWhiteSpace(left) || !string.IsNullOrWhiteSpace(right))
                    {
                        result.Add((left, right));
                    }
                }
            }
            return result;
        }
    }
}
