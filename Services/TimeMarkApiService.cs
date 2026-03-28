using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TimeMarkEdit.Services
{
    public class TimeMarkApiService : IService
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;

        public TimeMarkApiService(ILibraryManager libraryManager, ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _logger = logManager.GetLogger(GetType().Name);
        }

        public object Get(GetEpisodeChaptersRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.EpisodeId))
                    return new { Success = false, Message = "EpisodeId is required" };

                if (!Guid.TryParse(request.EpisodeId, out var guid))
                    return new { Success = false, Message = "Invalid EpisodeId format" };

                var item = _libraryManager.GetItemById(guid);
                if (item == null)
                    return new { Success = false, Message = "Item not found" };

                var chapterService = Plugin.ChapterMarkerService;
                if (chapterService == null)
                    return new { Success = false, Message = "Chapter service not available" };

                var chapters = chapterService.GetChapters(item);
                var result = chapters.Select((c, i) => new
                {
                    Index = i,
                    Name = c.Name ?? string.Empty,
                    StartPositionTicks = c.StartPositionTicks,
                    MarkerType = chapterService.GetChapterMarkerType(c)
                }).ToList();

                var ep = item as Episode;
                return new
                {
                    Success = true,
                    EpisodeId = request.EpisodeId,
                    EpisodeName = item.Name,
                    SeriesName = ep?.SeriesName,
                    SeasonNumber = ep?.ParentIndexNumber,
                    EpisodeNumber = ep?.IndexNumber,
                    DurationSeconds = item.RunTimeTicks.HasValue
                        ? item.RunTimeTicks.Value / (double)TimeSpan.TicksPerSecond
                        : 0,
                    Chapters = result
                };
            }
            catch (Exception ex)
            {
                _logger?.ErrorException("Error getting episode chapters", ex);
                return new { Success = false, Message = ex.Message };
            }
        }

        public object Post(SaveEpisodeChaptersRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.EpisodeId))
                    return new { Success = false, Message = "EpisodeId is required" };

                if (!Guid.TryParse(request.EpisodeId, out var guid))
                    return new { Success = false, Message = "Invalid EpisodeId format" };

                var item = _libraryManager.GetItemById(guid);
                if (item == null)
                    return new { Success = false, Message = "Item not found" };

                var chapterService = Plugin.ChapterMarkerService;
                if (chapterService == null)
                    return new { Success = false, Message = "Chapter service not available" };

                var entries = (request.Chapters ?? new List<ChapterItemDto>())
                    .Select(c => (c.Name ?? string.Empty, c.StartPositionTicks, c.MarkerType ?? "Chapter"))
                    .ToList();

                chapterService.SaveChapterList(item, entries);

                return new
                {
                    Success = true,
                    Message = $"Saved {entries.Count} chapter(s) for '{item.Name}'",
                    ChapterCount = entries.Count
                };
            }
            catch (Exception ex)
            {
                _logger?.ErrorException("Error saving episode chapters", ex);
                return new { Success = false, Message = ex.Message };
            }
        }
    }
}
