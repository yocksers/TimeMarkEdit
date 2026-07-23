using MediaBrowser.Model.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace TimeMarkEdit.Services
{
    public class TheIntroDbService
    {
        private readonly ILogger _logger;
        private readonly HttpClient _httpClient;
        private const string BaseUrl = "https://api.theintrodb.org";

        public TheIntroDbService(ILogger logger)
        {
            _logger = logger;
            _httpClient = new HttpClient();
            _httpClient.BaseAddress = new Uri(BaseUrl);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "TimeMarkEdit-EmbyPlugin/1.0");
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        private string ApiKey => CredentialProtection.Unprotect(Plugin.Instance?.Configuration?.ApiKey);

        public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

        public async Task<TheIntroDbMediaResponse?> GetMediaTimestampsAsync(
            string tmdbId,
            int? season = null,
            int? episode = null,
            long? durationMs = null)
        {
            var queryParts = new List<string> { "tmdb_id=" + Uri.EscapeDataString(tmdbId) };
            if (season.HasValue) queryParts.Add("season=" + season.Value);
            if (episode.HasValue) queryParts.Add("episode=" + episode.Value);
            if (durationMs.HasValue) queryParts.Add("duration_ms=" + durationMs.Value);

            var url = "/v3/media?" + string.Join("&", queryParts);

            var request = new HttpRequestMessage(HttpMethod.Get, url);

            _logger.Debug("TimeMarkEdit: TheIntroDB request: " + BaseUrl + url);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new HttpRequestException("Failed to connect to TheIntroDB: " + ex.Message, ex);
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                throw new UnauthorizedAccessException("TheIntroDB API key is invalid or missing");

            if ((int)response.StatusCode == 429)
                throw new InvalidOperationException("TheIntroDB rate limit exceeded — try again later");

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException("TheIntroDB API returned HTTP " + (int)response.StatusCode);

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var raw = JsonSerializer.Deserialize<ApiResponse>(json, options);
            if (raw == null) return null;

            return new TheIntroDbMediaResponse
            {
                TmdbId = raw.TmdbId,
                Type = raw.Type ?? string.Empty,
                Intro = ConvertSegments(raw.Intro),
                Recap = ConvertSegments(raw.Recap),
                Credits = ConvertSegments(raw.Credits),
                Preview = ConvertSegments(raw.Preview)
            };
        }

        public List<(string Name, long StartPositionTicks, string MarkerType)> ParseTimestamps(
            TheIntroDbMediaResponse response,
            ICollection<string> enabledSegments,
            long? runtimeTicks = null)
        {
            var results = new List<(string Name, long StartPositionTicks, string MarkerType)>();

            if (enabledSegments.Contains("intro"))
                AddSegments(results, response.Intro, "Intro Start", "Intro End", "IntroStart", "IntroEnd", runtimeTicks);

            if (enabledSegments.Contains("recap"))
                AddSegments(results, response.Recap, "Recap Start", "Recap End", "IntroStart", "IntroEnd", runtimeTicks);

            if (enabledSegments.Contains("credits"))
                AddSegments(results, response.Credits, "Credits Start", "Credits End", "CreditsStart", "CreditsEnd", runtimeTicks);

            if (enabledSegments.Contains("preview"))
                AddSegments(results, response.Preview, "Preview Start", "Preview End", "IntroStart", "IntroEnd", runtimeTicks);

            results.Sort((a, b) => a.StartPositionTicks.CompareTo(b.StartPositionTicks));
            return results;
        }

        private static void AddSegments(
            List<(string Name, long StartPositionTicks, string MarkerType)> results,
            List<TimeSegment> segments,
            string startName,
            string endName,
            string startMarkerType,
            string endMarkerType,
            long? runtimeTicks)
        {
            foreach (var seg in segments)
            {
                if (!seg.StartMs.HasValue) continue;

                var startTicks = (long)seg.StartMs.Value * 10000L;
                if (runtimeTicks.HasValue && startTicks >= runtimeTicks.Value) continue;

                results.Add((startName, startTicks, startMarkerType));

                if (seg.EndMs.HasValue)
                {
                    var endTicks = (long)seg.EndMs.Value * 10000L;
                    if (!runtimeTicks.HasValue || endTicks < runtimeTicks.Value)
                        results.Add((endName, endTicks, endMarkerType));
                }
            }
        }

        private static List<TimeSegment> ConvertSegments(List<ApiSegment>? raw)
        {
            if (raw == null) return new List<TimeSegment>();
            var result = new List<TimeSegment>(raw.Count);
            foreach (var r in raw)
                result.Add(new TimeSegment { StartMs = r.StartMs, EndMs = r.EndMs });
            return result;
        }

        private sealed class ApiResponse
        {
            [JsonPropertyName("tmdb_id")]
            public int TmdbId { get; set; }

            [JsonPropertyName("type")]
            public string? Type { get; set; }

            [JsonPropertyName("intro")]
            public List<ApiSegment>? Intro { get; set; }

            [JsonPropertyName("recap")]
            public List<ApiSegment>? Recap { get; set; }

            [JsonPropertyName("credits")]
            public List<ApiSegment>? Credits { get; set; }

            [JsonPropertyName("preview")]
            public List<ApiSegment>? Preview { get; set; }
        }

        private sealed class ApiSegment
        {
            [JsonPropertyName("start_ms")]
            public int? StartMs { get; set; }

            [JsonPropertyName("end_ms")]
            public int? EndMs { get; set; }
        }

        public async Task SubmitTimestampsAsync(
            string tmdbId,
            string mediaType,
            int? season,
            int? episode,
            long? videoDurationMs,
            List<TimeSegment> intro,
            List<TimeSegment> recap,
            List<TimeSegment> credits)
        {
            if (!int.TryParse(tmdbId, out var tmdbIdInt))
                throw new ArgumentException("Invalid TMDB ID: " + tmdbId);

            var options = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

            var segmentGroups = new (string Type, List<TimeSegment> Items)[]
            {
                ("intro", intro),
                ("recap", recap),
                ("credits", credits)
            };

            foreach (var (segmentType, items) in segmentGroups)
            {
                foreach (var seg in items)
                {
                    var body = new SegmentSubmit
                    {
                        TmdbId = tmdbIdInt,
                        Type = mediaType,
                        Segment = segmentType,
                        Season = season.HasValue ? season.Value.ToString() : null,
                        Episode = episode.HasValue ? episode.Value.ToString() : null,
                        StartSec = seg.StartMs.HasValue ? seg.StartMs.Value / 1000.0 : (double?)null,
                        EndSec = seg.EndMs.HasValue ? seg.EndMs.Value / 1000.0 : (double?)null,
                        VideoDurationMs = videoDurationMs
                    };

                    var json = JsonSerializer.Serialize(body, options);
                    var request = new HttpRequestMessage(HttpMethod.Post, "/v3/submit");
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + ApiKey);

                    _logger.Debug($"TimeMarkEdit: TheIntroDB submit segment={segmentType} tmdb_id={tmdbId}");

                    HttpResponseMessage response;
                    try
                    {
                        response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                    }
                    catch (HttpRequestException ex)
                    {
                        throw new HttpRequestException("Failed to connect to TheIntroDB: " + ex.Message, ex);
                    }

                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                        throw new UnauthorizedAccessException("Upload API key is invalid or missing");

                    if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
                        continue;

                    if ((int)response.StatusCode == 429)
                        throw new InvalidOperationException("TheIntroDB rate limit exceeded — try again later");

                    if (!response.IsSuccessStatusCode)
                        throw new HttpRequestException("TheIntroDB API returned HTTP " + (int)response.StatusCode);
                }
            }
        }

        private sealed class SegmentSubmit
        {
            [JsonPropertyName("tmdb_id")]
            public int TmdbId { get; set; }

            [JsonPropertyName("type")]
            public string Type { get; set; } = "";

            [JsonPropertyName("segment")]
            public string Segment { get; set; } = "";

            [JsonPropertyName("season")]
            public string? Season { get; set; }

            [JsonPropertyName("episode")]
            public string? Episode { get; set; }

            [JsonPropertyName("start_sec")]
            public double? StartSec { get; set; }

            [JsonPropertyName("end_sec")]
            public double? EndSec { get; set; }

            [JsonPropertyName("video_duration_ms")]
            public long? VideoDurationMs { get; set; }
        }
    }
}
