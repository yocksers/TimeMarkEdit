using MediaBrowser.Model.Services;
using System.Collections.Generic;
using TimeMarkEdit.Api;

namespace TimeMarkEdit
{
    [Route(Routes.GetEpisodeChapters, "GET", Summary = "Gets all chapter markers for a specific episode.")]
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
    public class SaveEpisodeChaptersRequest : IReturn<object>
    {
        public string EpisodeId { get; set; } = string.Empty;
        public List<ChapterItemDto>? Chapters { get; set; }
    }
}
