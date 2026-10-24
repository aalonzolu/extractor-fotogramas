using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ExtractorFotogramas
{
    public sealed class PaddingEstimate
    {
        public PaddingEstimate(long durationSeconds, long framesPerSecond, long estimatedFrames, int digits)
        {
            DurationSeconds = durationSeconds;
            FramesPerSecond = framesPerSecond;
            EstimatedFrames = estimatedFrames;
            Digits = digits;
        }

        public long DurationSeconds { get; private set; }
        public long FramesPerSecond { get; private set; }
        public long EstimatedFrames { get; private set; }
        public int Digits { get; private set; }
    }

    public sealed class TimeRange
    {
        public TimeRange(double start, double end)
        {
            Start = start;
            End = end;
        }

        public double Start { get; private set; }
        public double End { get; private set; }

        public double Seconds
        {
            get { return End - Start + 1; }
        }

        public bool IsSingleSecond
        {
            get { return End == Start; }
        }

        public bool Overlaps(TimeRange other)
        {
            return Start <= other.End && other.Start <= End;
        }
    }

    public sealed class FileHashes
    {
        public FileHashes(string sha256, string sha512)
        {
            Sha256 = sha256;
            Sha512 = sha512;
        }

        public string Sha256 { get; private set; }
        public string Sha512 { get; private set; }
    }

    public sealed class ReportHeader
    {
        public ReportHeader()
        {
            Notes = new List<string>();
        }

        public string Generator { get; set; }
        public string VideoPath { get; set; }
        public FileHashes VideoHashes { get; set; }
        public string VideoFormat { get; set; }
        public DateTime Timestamp { get; set; }
        public string Selection { get; set; }
        public string FfmpegVersion { get; set; }
        public string FfmpegArguments { get; set; }
        public int ReportedFrames { get; set; }
        public IList<string> Notes { get; private set; }
    }

    public sealed class VideoInfo
    {
        public const int RawStreamDefaultFrameRate = 25;
        static readonly string[] RawStreamFormats = { "hevc", "h264" };

        public string FormatName { get; set; }
        public string RawDuration { get; set; }
        public string RawFrameRate { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public long PacketCount { get; set; }
        public int AssumedFrameRate { get; set; }

        public bool HasVideoStream
        {
            get { return Width > 0 && Height > 0; }
        }

        public bool IsRawStream
        {
            get { return RawStreamFormats.Contains(FormatName); }
        }

        public double DurationSeconds
        {
            get
            {
                if (IsRawStream)
                {
                    return PacketCount > 0 && AssumedFrameRate > 0 ? (double)PacketCount / AssumedFrameRate : 0;
                }
                double seconds;
                return double.TryParse(RawDuration, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) ? seconds : 0;
            }
        }

        public double FramesPerSecond
        {
            get
            {
                if (IsRawStream) return AssumedFrameRate;
                var parts = (RawFrameRate ?? string.Empty).Split('/');
                double numerator, denominator = 1;
                if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out numerator)) return 0;
                var validDenominator = parts.Length == 1
                    || (double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out denominator) && denominator > 0);
                return validDenominator ? numerator / denominator : 0;
            }
        }

        public int? InputFrameRate
        {
            get { return IsRawStream ? AssumedFrameRate : (int?)null; }
        }
    }

    public static class FrameExtraction
    {
        public const string HashReportFileName = "hashes_fotogramas.txt";

        const string DefaultDuration = "600";
        const string DefaultFrameRate = "15";
        const long FallbackFramesPerSecond = 15;
        const int MinimumPadDigits = 2;
        const string Separator = "------------------------------------------------------------";
        static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static string FormatPlaybackTime(double seconds)
        {
            var whole = (long)Math.Floor(seconds);
            var clock = string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", whole / 3600, whole % 3600 / 60, whole % 60);
            var number = FormatNumber(seconds);
            var decimalPoint = number.IndexOf('.');
            return decimalPoint < 0 ? clock : clock + number.Substring(decimalPoint);
        }

        public static string DescribeRanges(IEnumerable<TimeRange> ranges)
        {
            return string.Join(", ", ranges.Select(range => range.IsSingleSecond
                ? FormatPlaybackTime(range.Start) + " (" + FormatNumber(range.Start) + " s)"
                : FormatPlaybackTime(range.Start) + " – " + FormatPlaybackTime(range.End)
                    + " (" + FormatNumber(range.Start) + " s a " + FormatNumber(range.End) + " s)"));
        }

        public static string BuildSelectExpression(IEnumerable<TimeRange> ranges)
        {
            return string.Join("+", ranges.Select(range =>
                "gte(t," + FormatNumber(range.Start) + ")*lt(t," + FormatNumber(range.End + 1) + ")"));
        }

        public static long ToEpochSeconds(DateTime timestamp)
        {
            return (long)(timestamp.ToUniversalTime() - UnixEpoch).TotalSeconds;
        }

        public static string OutputDirectoryFor(string destinationWithSeparator, string fullVideoPath, DateTime timestamp)
        {
            var folderName = "Fotogramas_" + Path.GetFileNameWithoutExtension(fullVideoPath) + "_" + ToEpochSeconds(timestamp);
            return destinationWithSeparator + folderName;
        }

        public static PaddingEstimate PaddingFor(VideoInfo video)
        {
            return video.IsRawStream
                ? EstimatePadding(FormatNumber(video.DurationSeconds), video.AssumedFrameRate.ToString(CultureInfo.InvariantCulture))
                : EstimatePadding(video.RawDuration, video.RawFrameRate);
        }

        public static PaddingEstimate EstimatePadding(string rawDuration, string rawFrameRate)
        {
            var durationSeconds = ParseWholePart(IsBlank(rawDuration) ? DefaultDuration : rawDuration, 0);
            var framesPerSecond = ParseFrameRate(IsBlank(rawFrameRate) ? DefaultFrameRate : rawFrameRate);
            var estimatedFrames = durationSeconds * framesPerSecond;
            var digits = Math.Max(MinimumPadDigits, estimatedFrames.ToString(CultureInfo.InvariantCulture).Length);
            return new PaddingEstimate(durationSeconds, framesPerSecond, estimatedFrames, digits);
        }

        public static long EstimateUncompressedPngBytes(int width, int height, double frameCount)
        {
            const int rgbBytesPerPixel = 3;
            return (long)((double)width * height * rgbBytesPerPixel * frameCount);
        }

        public static VideoInfo ProbeVideo(string ffprobePath, string videoPath)
        {
            var arguments = "-v error -select_streams v:0 -show_entries format=duration,format_name:stream=width,height,r_frame_rate "
                + "-of default=noprint_wrappers=1 " + Quote(videoPath);
            var values = CaptureOutputLines(ffprobePath, arguments)
                .Select(line => line.Split(new[] { '=' }, 2))
                .Where(pair => pair.Length == 2)
                .GroupBy(pair => pair[0])
                .ToDictionary(group => group.Key, group => group.Last()[1]);
            var video = new VideoInfo
            {
                FormatName = ValueOf(values, "format_name"),
                RawDuration = ValueOf(values, "duration"),
                RawFrameRate = ValueOf(values, "r_frame_rate"),
                Width = IntegerOf(values, "width"),
                Height = IntegerOf(values, "height"),
                AssumedFrameRate = VideoInfo.RawStreamDefaultFrameRate,
            };
            if (video.IsRawStream && video.HasVideoStream)
            {
                video.PacketCount = CountVideoPackets(ffprobePath, videoPath);
            }
            return video;
        }

        public static long CountVideoPackets(string ffprobePath, string videoPath)
        {
            var arguments = "-v error -select_streams v:0 -count_packets -show_entries stream=nb_read_packets "
                + "-of default=noprint_wrappers=1:nokey=1 " + Quote(videoPath);
            long packets;
            var output = CaptureOutputLines(ffprobePath, arguments).LastOrDefault() ?? string.Empty;
            return TryParseInteger(output, out packets) ? packets : 0;
        }

        public static string DescribeVideoFormat(VideoInfo video)
        {
            var frameRate = video.IsRawStream
                ? video.AssumedFrameRate + " fotogramas por segundo (indicado por el usuario; el archivo no trae este dato)"
                : FormatNumber(Math.Round(video.FramesPerSecond, 3)) + " fotogramas por segundo";
            var container = video.IsRawStream ? " (flujo de video sin contenedor)" : string.Empty;
            return video.FormatName + container + " | " + video.Width + "x" + video.Height + " | " + frameRate;
        }

        public static string FfmpegArguments(string videoPath, IList<TimeRange> ranges, int? inputFrameRate, string outputDirectory, int padDigits)
        {
            var outputPattern = Path.Combine(outputDirectory, Path.GetFileName(videoPath) + "_%0" + padDigits + "d.png");
            var wholeVideo = ranges == null;
            var readLimit = wholeVideo ? string.Empty : " -t " + FormatNumber(ranges.Max(range => range.End) + 1);
            var frameRate = inputFrameRate.HasValue ? " -framerate " + inputFrameRate.Value : string.Empty;
            var frameFilter = wholeVideo ? string.Empty : " -vf " + Quote("select='" + BuildSelectExpression(ranges) + "'");
            return "-hide_banner -nostdin -nostats -v warning -progress pipe:1 -y"
                + readLimit
                + frameRate
                + " -flags2 +showall"
                + " -i " + Quote(videoPath)
                + frameFilter
                + " -compression_level 0 -pred 0 -fps_mode passthrough -frame_pts 1 "
                + Quote(outputPattern);
        }

        public static bool IsDecodingProblem(string ffmpegMessage)
        {
            var markers = new[] { "invalid", "skipping", "error", "corrupt", "missing", "concealing", "does not exist" };
            return markers.Any(marker => ffmpegMessage.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static string LocateTool(string toolName, string applicationDirectory)
        {
            var bundled = new[]
            {
                Path.Combine(applicationDirectory, "ffmpeg", toolName + ".exe"),
                Path.Combine(applicationDirectory, "ffmpeg", "bin", toolName + ".exe"),
                Path.Combine(applicationDirectory, toolName + ".exe"),
            };
            var onPath = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator)
                .Where(directory => directory.Trim().Length > 0)
                .SelectMany(directory => new[]
                {
                    Path.Combine(directory.Trim().Trim('"'), toolName + ".exe"),
                    Path.Combine(directory.Trim().Trim('"'), toolName),
                });
            return bundled.Concat(onPath).FirstOrDefault(File.Exists);
        }

        public static string FfmpegVersion(string ffmpegPath)
        {
            return CaptureOutputLines(ffmpegPath, "-version").FirstOrDefault() ?? "desconocida";
        }

        public static IList<string> ExtractedFrames(string outputDirectory)
        {
            return LongPathFileSystem.FindFiles(outputDirectory, ".png")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static string WriteHashReport(string outputDirectory, ReportHeader header, Action<int, int> onFileHashed)
        {
            var frames = ExtractedFrames(outputDirectory);
            var lines = new List<string>
            {
                "REPORTE DE HASHES SHA-256 Y SHA-512",
                header.Generator,
                Separator,
                "Archivo de Video Original: " + header.VideoPath,
                "SHA-256 del Video Original: " + header.VideoHashes.Sha256,
                "SHA-512 del Video Original: " + header.VideoHashes.Sha512,
                "Formato del Video: " + header.VideoFormat,
                "Fecha y Hora: " + header.Timestamp.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture)
                    + " (epoch " + ToEpochSeconds(header.Timestamp) + ")",
                "Fotogramas Solicitados: " + header.Selection,
                "Fotogramas Extraídos: " + frames.Count,
                "Versión de FFmpeg: " + header.FfmpegVersion,
                "Parámetros de FFmpeg: " + header.FfmpegArguments,
            };
            if (frames.Count < header.ReportedFrames)
            {
                lines.Add(string.Format(
                    "ADVERTENCIA: FFmpeg generó {0} fotogramas pero solo quedaron {1} archivos; "
                    + "{2} se sobreescribieron por compartir la misma marca de tiempo (video de frecuencia variable).",
                    header.ReportedFrames, frames.Count, header.ReportedFrames - frames.Count));
            }
            lines.AddRange(header.Notes);

            var frameHashes = new List<FileHashes>(frames.Count);
            for (var index = 0; index < frames.Count; index++)
            {
                frameHashes.Add(ComputeHashes(frames[index]));
                onFileHashed(index + 1, frames.Count);
            }

            lines.Add(Separator);
            lines.Add("SHA-256");
            lines.AddRange(frames.Select((frame, index) => Path.GetFileName(frame) + "  :  " + frameHashes[index].Sha256));
            lines.Add(Separator);
            lines.Add("SHA-512");
            lines.AddRange(frames.Select((frame, index) => Path.GetFileName(frame) + "  :  " + frameHashes[index].Sha512));

            var reportPath = Path.Combine(outputDirectory, HashReportFileName);
            using (var writer = new StreamWriter(LongPathFileSystem.CreateNewForWriting(reportPath), new UTF8Encoding(true)))
            {
                foreach (var line in lines)
                {
                    writer.WriteLine(line);
                }
            }
            return reportPath;
        }

        public static FileHashes ComputeHashes(string path, Action<long, long> onBytesHashed = null)
        {
            using (var sha256 = SHA256.Create())
            using (var sha512 = SHA512.Create())
            using (var stream = LongPathFileSystem.OpenRead(path))
            {
                var total = stream.Length;
                var buffer = new byte[1024 * 1024];
                long hashed = 0;
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    sha256.TransformBlock(buffer, 0, read, null, 0);
                    sha512.TransformBlock(buffer, 0, read, null, 0);
                    hashed += read;
                    if (onBytesHashed != null) onBytesHashed(hashed, total);
                }
                sha256.TransformFinalBlock(buffer, 0, 0);
                sha512.TransformFinalBlock(buffer, 0, 0);
                return new FileHashes(ToHex(sha256.Hash), ToHex(sha512.Hash));
            }
        }

        static string ToHex(byte[] hash)
        {
            var hex = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
            {
                hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            }
            return hex.ToString();
        }

        static string ValueOf(IDictionary<string, string> values, string key)
        {
            string value;
            return values.TryGetValue(key, out value) ? value : string.Empty;
        }

        static int IntegerOf(IDictionary<string, string> values, string key)
        {
            int value;
            return int.TryParse(ValueOf(values, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : 0;
        }

        static IList<string> CaptureOutputLines(string executablePath, string arguments)
        {
            var startInfo = new ProcessStartInfo(executablePath, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using (var process = Process.Start(startInfo))
            {
                process.StandardError.ReadToEndAsync();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return output
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Trim())
                    .Where(line => line.Length > 0)
                    .ToList();
            }
        }

        static long ParseFrameRate(string raw)
        {
            var parts = raw.Split('/');
            if (parts.Length == 2)
            {
                long numerator, denominator;
                if (TryParseInteger(parts[0], out numerator) && TryParseInteger(parts[1], out denominator) && denominator != 0)
                {
                    return numerator / denominator;
                }
                return FallbackFramesPerSecond;
            }
            return ParseWholePart(raw, FallbackFramesPerSecond);
        }

        static long ParseWholePart(string raw, long fallback)
        {
            long value;
            return TryParseInteger(raw.Split('.')[0], out value) ? value : fallback;
        }

        static bool TryParseInteger(string raw, out long value)
        {
            return long.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        static bool IsBlank(string value)
        {
            return string.IsNullOrWhiteSpace(value);
        }

        static string FormatNumber(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        static string Quote(string value)
        {
            return "\"" + value + "\"";
        }
    }
}
