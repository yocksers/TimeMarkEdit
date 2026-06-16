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
}
