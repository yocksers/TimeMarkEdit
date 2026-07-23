using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;
using System.Collections.Generic;
using TimeMarkEdit.Api;

namespace TimeMarkEdit
{
    [Route(Routes.GetEpisodeChapters, "GET", Summary = "Gets all chapter markers for a specific episode.")]
    [Authenticated]
    public class GetEpisodeChaptersRequest : IReturn<object>
    {
        public string EpisodeId { get; set; } = string.Empty;
    }

    public class ChapterItemDto
    {
        public string Name { get; set; } = string.Empty;
        public long StartPositionTicks { get; set; }
        public string MarkerType { get; set; } = "Chapter";
    }

    [Route(Routes.SaveEpisodeChapters, "POST", Summary = "Saves the full chapter list for a specific episode.")]
    [Authenticated]
    public class SaveEpisodeChaptersRequest : IReturn<object>
    {
        public string EpisodeId { get; set; } = string.Empty;
        public List<ChapterItemDto>? Chapters { get; set; }
    }

    [Route(Routes.ApplySeasonMarks, "POST", Summary = "Applies special chapter markers from one episode to all other episodes in the same season.")]
    [Authenticated]
    public class ApplySeasonMarksRequest : IReturn<object>
    {
        public string EpisodeId { get; set; } = string.Empty;
    }

    [Route(Routes.FilterEpisodes, "GET", Summary = "Returns episodes/movies matching the given chapter filter criteria.")]
    [Authenticated]
    public class FilterEpisodesRequest : IReturn<object>
    {
        public string? ParentId { get; set; }
        public bool AllLibraries { get; set; }
        public bool NoChaptersOnly { get; set; }
        public int MaxChapterCount { get; set; } = -1;
        public int MinGapSeconds { get; set; } = -1;
        public int MinRuntimeSeconds { get; set; } = -1;
        public string? IntroFilter { get; set; }
        public string? CreditsFilter { get; set; }
    }

    [Route(Routes.GetSummary, "GET", Summary = "Returns intro and credits coverage statistics for all TV series.")]
    [Authenticated]
    public class GetSummaryRequest : IReturn<object>
    {
    }

    [Route(Routes.GetMkvChapters, "GET", Summary = "Returns embedded chapter markers read directly from an MKV file.")]
    [Authenticated]
    public class GetMkvChaptersRequest : IReturn<object>
    {
        public string ItemId { get; set; } = string.Empty;
    }

    [Route(Routes.ImportMkvChapters, "POST", Summary = "Imports embedded MKV chapter markers into Emby for a single item.")]
    [Authenticated]
    public class ImportMkvChaptersRequest : IReturn<object>
    {
        public string ItemId { get; set; } = string.Empty;
    }

    [Route(Routes.ImportMkvChaptersBulk, "POST", Summary = "Imports embedded MKV chapter markers into Emby for all episodes in a season or series.")]
    [Authenticated]
    public class ImportMkvChaptersBulkRequest : IReturn<object>
    {
        public string EpisodeId { get; set; } = string.Empty;
        public string Scope { get; set; } = "Season";
    }

    [Route(Routes.DownloadIntroDbTimestamps, "POST", Summary = "Downloads timestamps from TheIntroDB for a single item.")]
    [Authenticated]
    public class DownloadIntroDbRequest : IReturn<object>
    {
        public string ItemId { get; set; } = "";
    }

    [Route(Routes.DownloadIntroDbTimestampsBulk, "POST", Summary = "Downloads timestamps from TheIntroDB for all episodes in a season or series.")]
    [Authenticated]
    public class DownloadIntroDbBulkRequest : IReturn<object>
    {
        public string EpisodeId { get; set; } = "";
        public string Scope { get; set; } = "Season";
    }

    [Route(Routes.TestIntroDbConnection, "POST", Summary = "Tests the TheIntroDB API connection.")]
    [Authenticated]
    public class TestIntroDbConnectionRequest : IReturn<object> { }

    [Route(Routes.GetIntroDbConfig, "GET", Summary = "Gets the current TheIntroDB configuration.")]
    [Authenticated]
    public class GetIntroDbConfigRequest : IReturn<object> { }

    [Route(Routes.SetIntroDbConfig, "POST", Summary = "Saves TheIntroDB configuration.")]
    [Authenticated]
    public class SetIntroDbConfigRequest : IReturn<object>
    {
        public string ApiKey { get; set; } = "";
        public bool OverwriteExisting { get; set; } = true;
        public List<string> EnabledSegments { get; set; } = new() { "intro", "credits" };
    }

    public class DownloadIntroDbResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string ItemName { get; set; } = "";
        public int ChapterCount { get; set; }
        public List<ChapterItemDto> Chapters { get; set; } = new();
        public bool ApiKeyConfigured { get; set; }
        public bool ConnectionSuccessful { get; set; }
    }

    public class TestIntroDbConnectionResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool ApiKeyConfigured { get; set; }
    }

    public class SetIntroDbConfigResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
    }

    public class GetIntroDbConfigResponse
    {
        public string ApiKey { get; set; } = "";
        public bool ApiKeyConfigured { get; set; }
        public bool OverwriteExisting { get; set; }
        public List<string> EnabledSegments { get; set; } = new();
    }

    public class TheIntroDbMediaResponse
    {
        public int TmdbId { get; set; }
        public string Type { get; set; } = "";
        public List<TimeSegment> Intro { get; set; } = new();
        public List<TimeSegment> Recap { get; set; } = new();
        public List<TimeSegment> Credits { get; set; } = new();
        public List<TimeSegment> Preview { get; set; } = new();
    }

    public class TimeSegment
    {
        public int? StartMs { get; set; }
        public int? EndMs { get; set; }
    }

    [Route(Routes.UploadIntroDbTimestamps, "POST", Summary = "Uploads existing time marks from an item to TheIntroDB.")]
    [Authenticated]
    public class UploadIntroDbRequest : IReturn<object>
    {
        public string ItemId { get; set; } = "";
    }

    public class UploadIntroDbResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string ItemName { get; set; } = "";
        public int IntroCount { get; set; }
        public int RecapCount { get; set; }
        public int CreditsCount { get; set; }
    }
}
