using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Linq;
using libomtnet;
using libomtnet.codecs;

namespace VMXBench
{
    class Program
    {
        private const int WIDTH = 3840;
        private const int HEIGHT = 2160;
        private const int TARGET_FPS = 60;

        private static List<BenchmarkResult> results = new List<BenchmarkResult>();
        private static StringBuilder logBuilder = new StringBuilder();

        static void Main(string[] args)
        {
            Console.WriteLine("=== VMX Codec Benchmark ===");
            Console.WriteLine(string.Format("Target Resolution: {0}x{1}", WIDTH, HEIGHT));
            Console.WriteLine(string.Format("Target FPS: {0}", TARGET_FPS));
            Console.WriteLine();

            Log("=== VMX Codec Benchmark ===");
            Log(string.Format("Target Resolution: {0}x{1}", WIDTH, HEIGHT));
            Log(string.Format("Target FPS: {0}", TARGET_FPS));
            Log(string.Format("Benchmark started: {0:O}", DateTime.Now));
            Log("");

            try
            {
// Targeted benchmark cases for OMT stock and PCModTR Enhanced Quality
            Dictionary<string, Func<byte[]>> framePatterns = new Dictionary<string, Func<byte[]>>
            {
                { "game_like", GenerateGameLikeFrame },
                { "high_freq_noise", GenerateHighFrequencyNoise }
            };

            var scenarios = new[]
            {
                new BenchmarkScenario("OMT stock", false, "Safe"),
                new BenchmarkScenario("Enhanced Safe", true, "Safe"),
                new BenchmarkScenario("Enhanced Max", true, "Max")
            };

            var formats = new[]
            {
                Tuple.Create("UYVY", VMXImageType.UYVY),
                Tuple.Create("P216", VMXImageType.P216)
            };

            int testCount = 0;
            int successCount = 0;

            foreach (KeyValuePair<string, Func<byte[]>> pattern in framePatterns)
            {
                string patternName = pattern.Key;
                Func<byte[]> frameGenerator = pattern.Value;

                Log(string.Format("\n--- Testing frame pattern: {0} ---", patternName));
                Console.WriteLine(string.Format("\nTesting frame pattern: {0}", patternName));

                byte[] testFrame = frameGenerator();

                foreach (var scenario in scenarios)
                {
                    foreach (var format in formats)
                    {
                        testCount++;
                        try
                        {
                            BenchmarkResult result = RunEncodingTest(testFrame, patternName, format.Item1, format.Item2, scenario);
                            if (result.Success)
                            {
                                successCount++;
                            }
                            results.Add(result);

                            Console.WriteLine(string.Format("  Completed {0} tests ({1} successful)...", testCount, successCount));
                        }
                        catch (Exception ex)
                        {
                            Log(string.Format("ERROR in test {0}: {1}", testCount, ex.Message));
                            results.Add(new BenchmarkResult
                            {
                                ProfileName = scenario.ProfileName,
                                Pattern = patternName,
                                Format = format.Item1,
                                Quality = "error",
                                FrameMax = "0",
                                MinQuality = "0",
                                EncodedBytes = 0,
                                EstimatedMbpsAt60fps = 0,
                                EncodeMilliseconds = 0,
                                Success = false,
                                Error = ex.Message
                            });
                            }
                        }
                    }
                }

                Log(string.Format("\nBenchmark completed: {0:O}", DateTime.Now));
                Log(string.Format("Total tests: {0}, Successful: {1}", testCount, successCount));

                // Generate outputs
                SaveResults();
                PrintSummary();
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("FATAL ERROR: {0}", ex.Message));
                Console.WriteLine(ex.StackTrace);
                Log(string.Format("FATAL ERROR: {0}\n{1}", ex.Message, ex.StackTrace));
            }

            Console.WriteLine("\nBenchmark completed. Results saved to results/ directory.");
        }

        static byte[] GenerateFlatGradient()
        {
            // UYVY format: U, Y1, V, Y2, U, Y1, V, Y2, ...
            byte[] frame = new byte[WIDTH * HEIGHT * 2];
            
            for (int y = 0; y < HEIGHT; y++)
            {
                for (int x = 0; x < WIDTH; x += 2)
                {
                    int byteIdx = (y * WIDTH + x) * 2;
                    byte y_val1 = (byte)((x * 255) / WIDTH);
                    byte y_val2 = (byte)(((x + 1) * 255) / WIDTH);
                    byte u_val = 128;
                    byte v_val = 128;
                    
                    frame[byteIdx] = u_val;       // U
                    frame[byteIdx + 1] = y_val1;  // Y1
                    frame[byteIdx + 2] = v_val;   // V
                    frame[byteIdx + 3] = y_val2;  // Y2
                }
            }
            return frame;
        }

        static byte[] GenerateCheckerboard()
        {
            byte[] frame = new byte[WIDTH * HEIGHT * 2];
            
            int blockSize = 32;
            for (int y = 0; y < HEIGHT; y++)
            {
                for (int x = 0; x < WIDTH; x += 2)
                {
                    int byteIdx = (y * WIDTH + x) * 2;
                    bool isBlack1 = ((x / blockSize) + (y / blockSize)) % 2 == 0;
                    bool isBlack2 = (((x + 1) / blockSize) + (y / blockSize)) % 2 == 0;
                    byte y_val1 = isBlack1 ? (byte)0 : (byte)255;
                    byte y_val2 = isBlack2 ? (byte)0 : (byte)255;
                    byte u_val = 128;
                    byte v_val = 128;
                    
                    frame[byteIdx] = u_val;       // U
                    frame[byteIdx + 1] = y_val1;  // Y1
                    frame[byteIdx + 2] = v_val;   // V
                    frame[byteIdx + 3] = y_val2;  // Y2
                }
            }
            return frame;
        }

        static byte[] GenerateHighFrequencyNoise()
        {
            byte[] frame = new byte[WIDTH * HEIGHT * 2];
            Random rand = new Random(42);
            
            for (int i = 0; i < frame.Length; i += 4)
            {
                frame[i] = (byte)rand.Next(256);     // U
                frame[i + 1] = (byte)rand.Next(256); // Y1
                frame[i + 2] = (byte)rand.Next(256); // V
                frame[i + 3] = (byte)rand.Next(256); // Y2
            }
            return frame;
        }

        static byte[] GenerateGameLikeFrame()
        {
            byte[] frame = new byte[WIDTH * HEIGHT * 2];
            Random rand = new Random(123);
            
            // Fill with base color (dark blue)
            for (int i = 0; i < frame.Length; i += 4)
            {
                frame[i] = 150;      // U
                frame[i + 1] = 64;   // Y1
                frame[i + 2] = 100;  // V
                frame[i + 3] = 64;   // Y2
            }
            
            // Add HUD-like horizontal lines (white)
            for (int y = 100; y < HEIGHT; y += 200)
            {
                for (int x = 0; x < WIDTH; x += 2)
                {
                    int byteIdx = (y * WIDTH + x) * 2;
                    if (byteIdx + 3 < frame.Length)
                    {
                        frame[byteIdx] = 128;      // U
                        frame[byteIdx + 1] = 255;  // Y1 (white)
                        frame[byteIdx + 2] = 128;  // V
                        frame[byteIdx + 3] = 255;  // Y2 (white)
                    }
                }
            }
            
            // Add color gradients in middle section
            for (int y = HEIGHT / 4; y < HEIGHT / 2; y++)
            {
                for (int x = 0; x < WIDTH; x += 2)
                {
                    int byteIdx = (y * WIDTH + x) * 2;
                    if (byteIdx + 3 < frame.Length)
                    {
                        byte u = (byte)((x * 255) / WIDTH);
                        byte v = (byte)((y * 255) / HEIGHT);
                        frame[byteIdx] = u;
                        frame[byteIdx + 1] = 128;
                        frame[byteIdx + 2] = v;
                        frame[byteIdx + 3] = 128;
                    }
                }
            }
            
            // Add pseudo-random foliage-like noise to bottom half
            for (int y = HEIGHT / 2; y < HEIGHT; y++)
            {
                for (int x = 0; x < WIDTH; x += 2)
                {
                    int byteIdx = (y * WIDTH + x) * 2;
                    if (byteIdx + 3 < frame.Length)
                    {
                        byte noise = (byte)rand.Next(50, 150);
                        frame[byteIdx] = noise;
                        frame[byteIdx + 1] = (byte)(100 + noise / 2);
                        frame[byteIdx + 2] = noise;
                        frame[byteIdx + 3] = (byte)(100 + noise / 2);
                    }
                }
            }
            
            return frame;
        }

        static BenchmarkResult RunEncodingTest(byte[] testFrame, string patternName, string format, VMXImageType imageType, BenchmarkScenario scenario)
        {
            BenchmarkResult result = new BenchmarkResult
            {
                ProfileName = scenario.ProfileName,
                Pattern = patternName,
                Format = format,
                Success = false
            };

            byte[] encodedBuffer = new byte[20 * 1024 * 1024]; // 20MB buffer

            try
            {
                OMTSettings settings = OMTSettings.GetInstance();
                settings.SetBoolean("EnhancedQualityEnabled", scenario.Enabled);
                settings.SetEnhancedQualityMode(scenario.Mode);

                OMTVMX1Codec encoder = null;
                try
                {
                    encoder = new OMTVMX1Codec(WIDTH, HEIGHT, TARGET_FPS, VMXProfile.OMT_HQ, VMXColorSpace.BT709);

                    int frameMin, frameMax, minQuality, dcShift;
                    encoder.GetEncodingParameters(out frameMin, out frameMax, out minQuality, out dcShift);
                    result.Quality = encoder.GetQuality().ToString();
                    result.FrameMax = frameMax.ToString();
                    result.MinQuality = minQuality.ToString();

                    GCHandle frameHandle = GCHandle.Alloc(testFrame, GCHandleType.Pinned);
                    try
                    {
                        Stopwatch sw = Stopwatch.StartNew();
                        int encodedLen = encoder.Encode(imageType, frameHandle.AddrOfPinnedObject(), WIDTH * 2, encodedBuffer, false);
                        sw.Stop();

                        if (encodedLen <= 0 || encodedLen > encodedBuffer.Length)
                        {
                            result.Error = string.Format("Invalid encoded length: {0}", encodedLen);
                            return result;
                        }

                        result.EncodedBytes = encodedLen;
                        result.EncodeMilliseconds = (int)sw.ElapsedMilliseconds;
                        result.EstimatedMbpsAt60fps = (double)encodedLen * 8 * 60 / (1024.0 * 1024.0 * 1024.0);
                        result.Success = true;
                    }
                    finally
                    {
                        frameHandle.Free();
                    }
                }
                finally
                {
                    if (encoder != null)
                    {
                        encoder.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.ToString();
                Console.WriteLine("RunEncodingTest failed: " + ex.ToString());
            }
            return result;
        }

        static void Log(string message)
        {
            logBuilder.AppendLine(message);
        }

        static void PrintSummary()
        {
            Console.WriteLine("\n=== Test Results Summary ===");
            Console.WriteLine(string.Format("Total tests: {0}", results.Count));
            List<BenchmarkResult> successfulResults = results.FindAll(r => r.Success);
            Console.WriteLine(string.Format("Successful: {0}", successfulResults.Count));

            if (results.Count > 0 && successfulResults.Count > 0)
            {
                Console.WriteLine("\n--- Results by Format ---");
                Dictionary<string, List<BenchmarkResult>> byFormat = new Dictionary<string, List<BenchmarkResult>>();
                foreach (BenchmarkResult r in successfulResults)
                {
                    if (!byFormat.ContainsKey(r.Format))
                        byFormat[r.Format] = new List<BenchmarkResult>();
                    byFormat[r.Format].Add(r);
                }

                foreach (string format in byFormat.Keys)
                {
                    List<BenchmarkResult> items = byFormat[format];
                    double avg = items.Count > 0 ? items.Average(r => r.EncodedBytes) : 0;
                    int max = items.Count > 0 ? items.Max(r => r.EncodedBytes) : 0;
                    Console.WriteLine(string.Format("{0}: avg={1:F2}MB, max={2:F2}MB, count={3}", 
                        format, avg / (1024 * 1024), max / (1024 * 1024), items.Count));
                }

                Console.WriteLine("\n--- Quality Impact Analysis ---");
                Dictionary<string, List<BenchmarkResult>> byQuality = new Dictionary<string, List<BenchmarkResult>>();
                foreach (BenchmarkResult r in successfulResults)
                {
                    if (!byQuality.ContainsKey(r.Quality))
                        byQuality[r.Quality] = new List<BenchmarkResult>();
                    byQuality[r.Quality].Add(r);
                }

                List<string> qualityKeys = new List<string>(byQuality.Keys);
                qualityKeys.Sort((a, b) =>
                {
                    int aVal = a == "default" ? 0 : int.Parse(a);
                    int bVal = b == "default" ? 0 : int.Parse(b);
                    return aVal.CompareTo(bVal);
                });

                foreach (string quality in qualityKeys)
                {
                    List<BenchmarkResult> items = byQuality[quality];
                    double avg = items.Count > 0 ? items.Average(r => r.EncodedBytes) : 0;
                    Console.WriteLine(string.Format("Quality {0}: avg={1:F2}MB, count={2}", quality, avg / (1024 * 1024), items.Count));
                }
            }
        }

        static void SaveResults()
        {
            string resultDir = Path.Combine(Directory.GetCurrentDirectory(), "results");
            Directory.CreateDirectory(resultDir);

            // Save CSV
            string csvPath = Path.Combine(resultDir, "vmxbench_results.csv");
            using (StreamWriter writer = new StreamWriter(csvPath, false, Encoding.UTF8))
            {
                writer.WriteLine("profileName,format,pattern,quality,frameMax,minQuality,encodedBytes,estimatedMbpsAt60fps,encodeMilliseconds");
                foreach (BenchmarkResult result in results)
                {
                    writer.WriteLine(string.Format("{0},{1},{2},{3},{4},{5},{6},{7:F4},{8}",
                        result.ProfileName, result.Format, result.Pattern, result.Quality,
                        result.FrameMax, result.MinQuality, result.EncodedBytes,
                        result.EstimatedMbpsAt60fps, result.EncodeMilliseconds));
                }
            }
            Console.WriteLine(string.Format("CSV saved to {0}", csvPath));
            Log(string.Format("CSV saved to {0}", csvPath));

            // Save log
            string logPath = Path.Combine(resultDir, "vmxbench_log.txt");
            File.WriteAllText(logPath, logBuilder.ToString());
            Console.WriteLine(string.Format("Log saved to {0}", logPath));
            Log(string.Format("Log saved to {0}", logPath));
        }
    }

    class BenchmarkResult
    {
        public string ProfileName { get; set; }
        public string Pattern { get; set; }
        public string Format { get; set; }
        public string Quality { get; set; }
        public string FrameMax { get; set; }
        public string MinQuality { get; set; }
        public int EncodedBytes { get; set; }
        public double EstimatedMbpsAt60fps { get; set; }
        public int EncodeMilliseconds { get; set; }
        public bool Success { get; set; }
        public string Error { get; set; }

        public BenchmarkResult()
        {
            ProfileName = "";
            Pattern = "";
            Format = "";
            Quality = "";
            FrameMax = "";
            MinQuality = "";
            Error = "";
        }
    }

    class BenchmarkScenario
    {
        public string ProfileName { get; }
        public bool Enabled { get; }
        public string Mode { get; }

        public BenchmarkScenario(string profileName, bool enabled, string mode)
        {
            ProfileName = profileName;
            Enabled = enabled;
            Mode = mode;
        }
    }
}
