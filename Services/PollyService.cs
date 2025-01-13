using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Amazon.Polly;
using Amazon.Polly.Model;
using ListenLense.Models; // If you have any shared models, adjust as needed

namespace ListenLense.Services
{
    public class PollyService
    {
        private readonly AmazonPollyClient _pollyClient;

        public PollyService(IConfiguration config)
        {
            // Retrieve AWS credentials from appsettings.json or environment variables
            string accessKey = config["AWS:AccessKey"] ?? "";
            string secretKey = config["AWS:SecretKey"] ?? "";
            string region = config["AWS:Region"] ?? "";

            _pollyClient = new AmazonPollyClient(
                accessKey,
                secretKey,
                Amazon.RegionEndpoint.GetBySystemName(region)
            );
        }

        /// <summary>
        /// Processes a .txt file: chunking, synthesizing MP3 + sentence marks, merging into one MP3/JSON.
        /// Returns (pathToFinalMp3, pathToFinalJson).
        /// </summary>
        public async Task<(string mp3Path, string jsonPath)> ProcessTextFileAsync(
            string textFilePath,
            string workspaceFolder
        )
        {
            // 1) Read text
            var textContent = File.ReadAllText(textFilePath);
            var baseFileName = Path.GetFileNameWithoutExtension(textFilePath);

            // 2) Output paths
            string mp3Path = Path.Combine(workspaceFolder, baseFileName + ".mp3");
            string jsonPath = Path.Combine(workspaceFolder, baseFileName + ".json");

            // 3) Break text into paragraphs
            var paragraphs = SplitTextIntoParagraphs(textContent).ToList();

            // We'll accumulate all sentence marks
            var combinedMarks = new List<SentenceMark>();
            long runningOffset = 0; // time offset in ms

            // Keep memory stream of final merged audio
            using var combinedAudio = new MemoryStream();

            // We might also store paragraph data for your final JSON
            var paragraphData = new List<ParagraphData>();

            // 4) Process each paragraph (then chunk if >3000 chars)
            for (int i = 0; i < paragraphs.Count; i++)
            {
                var (paragraphText, paragraphIndex, paragraphStartPos) = paragraphs[i];

                // Store minimal info about the paragraph in final JSON
                paragraphData.Add(
                    new ParagraphData
                    {
                        ParagraphIndex = paragraphIndex,
                        ParagraphText = paragraphText
                    }
                );

                // Chunks of up to 3000 characters
                var textChunks = SplitTextIntoChunks(paragraphText, 3000);
                foreach (var chunk in textChunks)
                {
                    // --------------------------
                    // (A) Synthesize MP3 chunk
                    // --------------------------
                    var audioRequest = new SynthesizeSpeechRequest
                    {
                        Text = chunk,
                        VoiceId = VoiceId.Matthew,
                        Engine = Engine.Standard,
                        OutputFormat = OutputFormat.Mp3
                    };
                    var audioResponse = await _pollyClient.SynthesizeSpeechAsync(audioRequest);

                    // Convert chunk to memory
                    using var chunkAudioStream = new MemoryStream();
                    audioResponse.AudioStream.CopyTo(chunkAudioStream);

                    // Optionally measure chunk duration via ffprobe
                    int chunkDurationMs = GetMp3DurationMs(chunkAudioStream.ToArray());

                    // --------------------------
                    // (B) Synthesize marks chunk
                    // --------------------------
                    var marksRequest = new SynthesizeSpeechRequest
                    {
                        Text = chunk,
                        VoiceId = VoiceId.Matthew,
                        Engine = Engine.Standard,
                        OutputFormat = OutputFormat.Json,
                        SpeechMarkTypes = new List<string> { "sentence" }
                    };
                    var marksResponse = await _pollyClient.SynthesizeSpeechAsync(marksRequest);

                    using (var sr = new StreamReader(marksResponse.AudioStream))
                    {
                        string line;
                        while ((line = sr.ReadLine()) != null)
                        {
                            var mark = JsonSerializer.Deserialize<SentenceMark>(line);
                            if (mark != null && mark.type == "sentence")
                            {
                                // Offset 'time' by runningOffset
                                mark.time += runningOffset;
                                // Also associate paragraph index
                                mark.paragraphIndex = paragraphIndex;
                                combinedMarks.Add(mark);
                            }
                        }
                    }

                    // --------------------------
                    // (C) Append chunk's MP3 to combined audio
                    // --------------------------
                    chunkAudioStream.Position = 0;
                    chunkAudioStream.CopyTo(combinedAudio);

                    // Bump our running offset
                    runningOffset += chunkDurationMs;
                }
            }

            // 5) Write out final merged MP3
            using (var fs = File.Create(mp3Path))
            {
                combinedAudio.Position = 0;
                combinedAudio.CopyTo(fs);
            }

            // 6) Build final JSON
            //    We'll store paragraphs + all aggregated sentence marks
            var outputJsonObj = new { Paragraphs = paragraphData, Sentences = combinedMarks };
            string finalJson = JsonSerializer.Serialize(
                outputJsonObj,
                new JsonSerializerOptions { WriteIndented = true }
            );
            await File.WriteAllTextAsync(jsonPath, finalJson);

            // Return final paths
            return (mp3Path, jsonPath);
        }

        /// <summary>
        /// Splits text into paragraphs by blank lines
        /// Returns (paragraphText, paragraphIndex, paragraphStartPos).
        /// </summary>
        private static IEnumerable<(
            string paragraphText,
            int paragraphIndex,
            int paragraphStartPos
        )> SplitTextIntoParagraphs(string text)
        {
            // Split on blank lines
            var rawParagraphs = Regex
                .Split(text, @"\r?\n\s*\r?\n")
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToArray();

            int paragraphIndex = 0;
            int currentPos = 0;

            for (int i = 0; i < rawParagraphs.Length; i++)
            {
                var p = rawParagraphs[i].Trim();
                // find index of p in text from currentPos
                int idx = text.IndexOf(p, currentPos, StringComparison.Ordinal);
                if (idx >= 0)
                {
                    currentPos = idx + p.Length;
                }

                yield return (p, paragraphIndex, idx);
                paragraphIndex++;
            }
        }

        /// <summary>
        /// Splits text into chunks of ~chunkSize characters without breaking words mid-word.
        /// </summary>
        private static IEnumerable<string> SplitTextIntoChunks(string text, int chunkSize)
        {
            var chunks = new List<string>();
            // We'll split by whitespace, then accumulate until we exceed chunkSize
            var words = Regex.Split(text, @"(?<=\s)");
            var sb = new StringBuilder();

            foreach (var word in words)
            {
                if (sb.Length + word.Length > chunkSize)
                {
                    chunks.Add(sb.ToString());
                    sb.Clear();
                }
                sb.Append(word);
            }
            if (sb.Length > 0)
                chunks.Add(sb.ToString());

            return chunks;
        }

        /// <summary>
        /// Measures the MP3 duration in ms by writing to a temp file and calling ffprobe.
        /// If you prefer not to rely on ffprobe, you can skip measuring chunk durations,
        /// but then your sentence timestamps won't line up perfectly for multi-chunk merges.
        /// </summary>
        private static int GetMp3DurationMs(byte[] mp3Data)
        {
            // Write out to a temp .mp3
            var tempFile = Path.GetTempFileName() + ".mp3";
            File.WriteAllBytes(tempFile, mp3Data);

            int durationMs = 0;
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "ffprobe",
                    Arguments =
                        $"-v error -show_entries format=duration -of csv=p=0 \"{tempFile}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo };
                process.Start();

                string result = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();

                if (double.TryParse(result, out double seconds))
                {
                    durationMs = (int)(seconds * 1000);
                }
            }
            finally
            {
                File.Delete(tempFile);
            }
            return durationMs;
        }

        // Basic classes for final JSON
        private class ParagraphData
        {
            public int ParagraphIndex { get; set; }
            public string ParagraphText { get; set; } = string.Empty;
        }

        private class SentenceMark
        {
            public long time { get; set; } // ms
            public string type { get; set; } = ""; // "sentence"
            public int start { get; set; }
            public int end { get; set; }
            public string value { get; set; } = ""; // The actual sentence
            public int paragraphIndex { get; set; } // which paragraph it belongs to
        }
    }
}
