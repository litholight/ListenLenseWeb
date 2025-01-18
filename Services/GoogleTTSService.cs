using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Google.Cloud.TextToSpeech.V1Beta1;

namespace ListenLense.Services
{
    public class GoogleTTSService
    {
        private readonly TextToSpeechClient _ttsClient;

        public GoogleTTSService(IConfiguration config)
        {
            // 1) Read the credentials file path from appsettings.json
            string credentialsFile = config["GoogleCloud:CredentialsFile"];
            if (string.IsNullOrEmpty(credentialsFile))
                throw new Exception("Google Cloud credentials file path is not configured.");

            // 2) Set the environment variable so that Google TTS can authenticate
            Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", credentialsFile);

            // 3) Initialize the TextToSpeech client
            _ttsClient = TextToSpeechClient.Create();
        }

        /// <summary>
        /// Processes a .txt file in a "Polly-like" way:
        ///  1) Splits text into paragraphs.
        ///  2) Splits paragraphs into chunks (each in SSML) up to 5000 bytes each.
        ///  3) Adds <mark> tags around sentences so we can get timestamps (timepoints).
        ///  4) Merges MP3 data for all chunks.
        ///  5) Returns final MP3 + JSON with paragraphs + sentence time offsets.
        /// </summary>
        public async Task<(string mp3Path, string jsonPath)> ProcessTextFileAsync(
            string textFilePath,
            string workspaceFolder
        )
        {
            // 1) Read the text
            var textContent = File.ReadAllText(textFilePath);
            var baseFileName = Path.GetFileNameWithoutExtension(textFilePath);

            // 2) Determine output paths
            string mp3Path = Path.Combine(workspaceFolder, baseFileName + ".mp3");
            string jsonPath = Path.Combine(workspaceFolder, baseFileName + ".json");

            // 3) Split into paragraphs
            var paragraphs = SplitTextIntoParagraphs(textContent).ToList();

            // We'll collect final paragraphs and sentences
            var paragraphData = new List<ParagraphData>();
            var sentenceData = new List<SentenceMark>();

            // We'll merge audio into a MemoryStream
            using var combinedAudio = new MemoryStream();

            long runningOffsetMs = 0; // track total audio offset across all chunks

            for (int i = 0; i < paragraphs.Count; i++)
            {
                var (paragraphText, paragraphIndex, _) = paragraphs[i];

                // Keep track of paragraph for final JSON
                paragraphData.Add(
                    new ParagraphData
                    {
                        ParagraphIndex = paragraphIndex,
                        ParagraphText = paragraphText
                    }
                );

                // 4) For each paragraph, break into chunks (5000 bytes max)
                //    We'll build SSML for each chunk, adding <mark> tags around sentences.
                var paragraphChunks = BuildSsmlChunks(paragraphText, 5000, paragraphIndex);
                foreach (var ssmlChunk in paragraphChunks)
                {
                    // (A) Synthesize chunk
                    var request = new SynthesizeSpeechRequest
                    {
                        Input = new SynthesisInput { Ssml = ssmlChunk.Ssml },
                        Voice = new VoiceSelectionParams
                        {
                            Name = "en-US-Neural2-D", // Replace with a Neural2 or WaveNet voice
                            LanguageCode = "en-US"
                        },
                        AudioConfig = new AudioConfig
                        {
                            AudioEncoding = AudioEncoding.Mp3,
                            SpeakingRate = 1.0, // Adjust for speed
                            Pitch = 0.0, // Adjust for higher/lower pitch
                            VolumeGainDb = 0.0 // Adjust for loudness
                        },
                        EnableTimePointing =
                        {
                            SynthesizeSpeechRequest.Types.TimepointType.SsmlMark
                        }
                    };

                    var response = await _ttsClient.SynthesizeSpeechAsync(request);
                    var audioData = response.AudioContent.ToByteArray();

                    // (B) Measure chunk duration with ffprobe, so we can accurately offset the next chunk
                    int chunkDurationMs = GetMp3DurationMs(audioData);

                    // (C) Append chunk audio to final combined stream
                    combinedAudio.Write(audioData);

                    // (D) Process timepoints returned from Google
                    //     Each timepoint corresponds to a <mark name="xxx"/> in the SSML.
                    //     We'll find the matching sentence from chunk.Sentences by mark name.
                    foreach (var tp in response.Timepoints)
                    {
                        // time is returned in seconds
                        double timeInMs = tp.TimeSeconds * 1000;
                        var markName = tp.MarkName;
                        if (ssmlChunk.SentenceMap.TryGetValue(markName, out var sData))
                        {
                            // Add offset
                            long absoluteTimeMs = (long)(runningOffsetMs + timeInMs);
                            // Build final sentence mark
                            sentenceData.Add(
                                new SentenceMark
                                {
                                    time = absoluteTimeMs,
                                    value = sData.SentenceText,
                                    start = sData.StartIndex,
                                    end = sData.EndIndex,
                                    paragraphIndex = paragraphIndex
                                }
                            );
                        }
                    }

                    // (E) Update running offset by chunk duration
                    runningOffsetMs += chunkDurationMs;
                }
            }

            // 5) Write out final merged MP3
            using (var fs = File.Create(mp3Path))
            {
                combinedAudio.Position = 0;
                combinedAudio.CopyTo(fs);
            }

            // 6) Create final JSON (paragraphs + sentence data)
            //    Sort sentences by time, just in case
            sentenceData = sentenceData.OrderBy(s => s.time).ToList();

            var outputJsonObj = new { Paragraphs = paragraphData, Sentences = sentenceData };

            string finalJson = JsonSerializer.Serialize(
                outputJsonObj,
                new JsonSerializerOptions { WriteIndented = true }
            );
            await File.WriteAllTextAsync(jsonPath, finalJson);

            return (mp3Path, jsonPath);
        }

        /// <summary>
        /// Builds a list of SSML chunks from paragraph text.
        /// We split the paragraph by sentences, insert <mark name=""> for each sentence,
        /// and accumulate them into SSML <speak> blocks of up to maxBytes in UTF-8 encoding.
        ///
        /// Returns a list of objects with the SSML itself and a map of MarkName -> sentence data.
        /// </summary>
        private static List<SsmlChunk> BuildSsmlChunks(
            string paragraphText,
            int maxBytes,
            int paragraphIndex
        )
        {
            // 1) Split the paragraph into sentences
            var sentences = SplitIntoSentences(paragraphText);

            var chunks = new List<SsmlChunk>();

            // We'll build SSML in a StringBuilder
            var sb = new StringBuilder();
            // Start the <speak> block
            sb.Append("<speak>");

            // We'll store the map of markName -> sentence text within the current chunk
            var currentChunkMap = new Dictionary<string, SentenceData>();

            // Keep track of how many chunks we've created (used for generating markName)
            int chunkCounter = 0;
            // Keep track of how many sentences we've placed so far (used for unique markName)
            int sentenceCounter = 0;

            for (int sIdx = 0; sIdx < sentences.Count; sIdx++)
            {
                var sentence = sentences[sIdx];
                // We'll create a unique markName
                // Example: "p0_chunk0_s0"
                string markName = $"p{paragraphIndex}_c{chunkCounter}_s{sIdx}";
                // Insert the sentence text, then the mark
                // e.g. "Hello world. <mark name="p0_c0_s0" />"
                sb.Append($@"<mark name=""{markName}""/> ");
                sb.Append(EscapeXml(sentence.Value));

                // Keep track of this sentence in the map
                currentChunkMap[markName] = new SentenceData
                {
                    SentenceText = sentence.Value,
                    StartIndex = sentence.Start,
                    EndIndex = sentence.End
                };

                // Check the current length in bytes
                string currentSsml = sb.ToString() + "</speak>";
                int currentByteCount = Encoding.UTF8.GetByteCount(currentSsml);

                // If we've exceeded maxBytes, we need to backtrack:
                if (currentByteCount > maxBytes)
                {
                    // 1) Remove the just-added sentence + mark from the chunk
                    // Because adding that last sentence made us go over
                    sb.Replace($@" {EscapeXml(sentence.Value)} <mark name=""{markName}""/> ", "");

                    // Also remove from currentChunkMap
                    currentChunkMap.Remove(markName);

                    // 2) Finalize the chunk we already have
                    sb.Append("</speak>");
                    string chunkSsml = sb.ToString();
                    chunks.Add(
                        new SsmlChunk
                        {
                            Ssml = chunkSsml,
                            SentenceMap = new Dictionary<string, SentenceData>(currentChunkMap)
                        }
                    );

                    // 3) Start a new chunk
                    chunkCounter++;
                    sb.Clear();
                    currentChunkMap.Clear();

                    sb.Append("<speak>");
                    // Now re-add the sentence + mark that caused overflow
                    string newMarkName = $"p{paragraphIndex}_c{chunkCounter}_s{sIdx}";
                    sb.Append(EscapeXml(sentence.Value));
                    sb.Append($@" <mark name=""{newMarkName}""/> ");
                    currentChunkMap[newMarkName] = new SentenceData
                    {
                        SentenceText = sentence.Value,
                        StartIndex = sentence.Start,
                        EndIndex = sentence.End
                    };
                }
            }

            // End the last chunk if there's any leftover
            if (sb.Length > "<speak>".Length)
            {
                sb.Append("</speak>");
                chunks.Add(
                    new SsmlChunk
                    {
                        Ssml = sb.ToString(),
                        SentenceMap = new Dictionary<string, SentenceData>(currentChunkMap)
                    }
                );
            }

            return chunks;
        }

        /// <summary>
        /// Splits paragraph text into (Value, Start, End) for each sentence.
        /// This is similar to how you handle it in Polly, except we track start/end indexes.
        /// </summary>
        private static List<(string Value, int Start, int End)> SplitIntoSentences(string text)
        {
            var results = new List<(string, int, int)>();

            // A simplistic approach: split on period, question mark, exclamation mark, etc.
            // There are more robust ways, but let's keep it simple.
            var pattern = @"(?<=[.!?])\s+";
            var matches = Regex.Split(text, pattern);

            int currentIndex = 0;
            foreach (var chunk in matches)
            {
                var trimmed = chunk.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                int startPos = text.IndexOf(trimmed, currentIndex, StringComparison.Ordinal);
                if (startPos >= 0)
                {
                    currentIndex = startPos + trimmed.Length;
                    results.Add((trimmed, startPos, startPos + trimmed.Length));
                }
            }
            return results;
        }

        /// <summary>
        /// Escapes XML entities to ensure valid SSML.
        /// </summary>
        private static string EscapeXml(string input)
        {
            // Minimal replace. If you want more robust escaping, use System.Security.SecurityElement.Escape or similar
            return input
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }

        /// <summary>
        /// Splits text into paragraphs by blank lines,
        /// returning (paragraphText, paragraphIndex, paragraphStartPos).
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
                int idx = text.IndexOf(p, currentPos, StringComparison.Ordinal);
                if (idx >= 0)
                    currentPos = idx + p.Length;

                yield return (p, paragraphIndex, idx);
                paragraphIndex++;
            }
        }

        /// <summary>
        /// Measures an MP3's duration in milliseconds using ffprobe, similar to the PollyService.
        /// </summary>
        private static int GetMp3DurationMs(byte[] mp3Data)
        {
            // 1) Write out to a temp .mp3
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

        //========== Utility Classes for SSML chunking ==========//

        /// <summary>
        /// Represents a chunk of SSML plus a map of markName -> sentence data.
        /// </summary>
        private class SsmlChunk
        {
            public string Ssml { get; set; } = "";
            public Dictionary<string, SentenceData> SentenceMap { get; set; } = new();
        }

        private class SentenceData
        {
            public string SentenceText { get; set; } = "";
            public int StartIndex { get; set; }
            public int EndIndex { get; set; }
        }

        //========== JSON Output Classes Matching Polly Structure ==========//

        public class ParagraphData
        {
            public int ParagraphIndex { get; set; }
            public string ParagraphText { get; set; } = string.Empty;
        }

        public class SentenceMark
        {
            public long time { get; set; } // in ms
            public string value { get; set; } = ""; // The actual sentence text
            public int start { get; set; } // start char index in the paragraph
            public int end { get; set; } // end char index in the paragraph
            public int paragraphIndex { get; set; } // which paragraph it belongs to
        }
    }
}
