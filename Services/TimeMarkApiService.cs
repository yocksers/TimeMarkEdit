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

        private BaseItem? ResolveItem(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
                return null;
            if (Guid.TryParse(itemId, out var guid))
                return _libraryManager.GetItemById(guid);
            if (long.TryParse(itemId, out var internalId))
                return _libraryManager.GetItemById(internalId);
            return null;
        }

        public object Get(GetEpisodeChaptersRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.EpisodeId))
                    return new { Success = false, Message = "EpisodeId is required" };

                var item = ResolveItem(request.EpisodeId);
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

                var item = ResolveItem(request.EpisodeId);
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

        public object Post(ApplySeasonMarksRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.EpisodeId))
                    return new { Success = false, Message = "EpisodeId is required" };

                var sourceItem = ResolveItem(request.EpisodeId);
                if (sourceItem == null)
                    return new { Success = false, Message = "Item not found" };

                var sourceEp = sourceItem as Episode;
                if (sourceEp == null)
                    return new { Success = false, Message = "Item is not an episode" };

                var chapterService = Plugin.ChapterMarkerService;
                if (chapterService == null)
                    return new { Success = false, Message = "Chapter service not available" };

                var sourceChapters = chapterService.GetChapters(sourceItem);
                var markersToApply = sourceChapters
                    .Where(c => chapterService.GetChapterMarkerType(c) != "Chapter")
                    .Select(c => (c.Name ?? string.Empty, c.StartPositionTicks, chapterService.GetChapterMarkerType(c)))
                    .ToList();

                if (markersToApply.Count == 0)
                    return new { Success = false, Message = "No special markers (IntroStart, IntroEnd, CreditsStart) found on this episode" };

                var markerTypeNames = markersToApply.Select(m => m.Item3).Distinct().ToList();

                var parentSeason = _libraryManager.GetItemById(sourceEp.ParentId);
                if (parentSeason == null)
                    return new { Success = false, Message = "Could not resolve parent season" };

                var query = new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { "Episode" },
                    AncestorIds = new long[] { parentSeason.InternalId }
                };

                var seasonResult = _libraryManager.GetItemsResult(query);
                var updatedCount = 0;

                foreach (var ep in seasonResult.Items)
                {
                    if (ep.Id == sourceItem.Id) continue;

                    var epChapters = chapterService.GetChapters(ep);
                    var retained = epChapters
                        .Where(c => !markerTypeNames.Contains(chapterService.GetChapterMarkerType(c)))
                        .Select(c => (c.Name ?? string.Empty, c.StartPositionTicks, chapterService.GetChapterMarkerType(c)))
                        .ToList();

                    retained.AddRange(markersToApply);
                    chapterService.SaveChapterList(ep, retained);
                    updatedCount++;
                }

                return new
                {
                    Success = true,
                    Message = $"Applied {markersToApply.Count} marker(s) to {updatedCount} episode(s) in the season",
                    UpdatedCount = updatedCount,
                    MarkerCount = markersToApply.Count
                };
            }
            catch (Exception ex)
            {
                _logger?.ErrorException("Error applying season marks", ex);
                return new { Success = false, Message = ex.Message };
            }
        }

        public object Get(FilterEpisodesRequest request)
        {
            try
            {
                var chapterService = Plugin.ChapterMarkerService;
                if (chapterService == null)
                    return new { Success = false, Message = "Chapter service not available" };

                var includeTypes = request.AllLibraries
                    ? new[] { "Episode", "Movie" }
                    : new[] { "Episode" };

                var query = new InternalItemsQuery
                {
                    IncludeItemTypes = includeTypes,
                    Recursive = true
                };

                if (!string.IsNullOrEmpty(request.ParentId))
                {
                    var parentItem = ResolveItem(request.ParentId);
                    if (parentItem == null)
                        return new { Success = true, Items = Array.Empty<object>() };
                    query.AncestorIds = new long[] { parentItem.InternalId };
                }

                var queryResult = _libraryManager.GetItemsResult(query);

                var minGapTicks = request.MinGapSeconds > 0
                    ? (long)request.MinGapSeconds * TimeSpan.TicksPerSecond
                    : -1L;

                var introFilter = request.IntroFilter ?? string.Empty;
                var creditsFilter = request.CreditsFilter ?? string.Empty;

                var matchingItems = new List<(int? season, int? episode, string seriesName, object dto)>();

                foreach (var item in queryResult.Items)
                {
                    var chapters = chapterService.GetChapters(item);
                    var count = chapters.Count;

                    if (request.NoChaptersOnly && count != 0) continue;
                    if (request.MaxChapterCount >= 0 && count >= request.MaxChapterCount) continue;

                    if (introFilter.Length > 0 || creditsFilter.Length > 0)
                    {
                        var markerTypes = chapters.Select(c => chapterService.GetChapterMarkerType(c)).ToList();
                        if (introFilter == "has" && !markerTypes.Contains("IntroStart")) continue;
                        if (introFilter == "missing" && markerTypes.Contains("IntroStart")) continue;
                        if (creditsFilter == "has" && !markerTypes.Contains("CreditsStart")) continue;
                        if (creditsFilter == "missing" && markerTypes.Contains("CreditsStart")) continue;
                    }

                    if (minGapTicks > 0)
                    {
                        var hasLargeGap = false;
                        for (var i = 1; i < count; i++)
                        {
                            if (chapters[i].StartPositionTicks - chapters[i - 1].StartPositionTicks > minGapTicks)
                            {
                                hasLargeGap = true;
                                break;
                            }
                        }
                        if (!hasLargeGap) continue;
                    }

                    var ep = item as Episode;
                    matchingItems.Add((
                        ep?.ParentIndexNumber,
                        ep?.IndexNumber,
                        ep?.SeriesName ?? item.Name ?? string.Empty,
                        new
                        {
                            Id = item.Id.ToString("N"),
                            Name = item.Name,
                            Type = item.GetType().Name,
                            IndexNumber = ep?.IndexNumber,
                            ParentIndexNumber = ep?.ParentIndexNumber,
                            SeriesName = ep?.SeriesName
                        }
                    ));
                }

                var results = matchingItems
                    .OrderBy(x => x.seriesName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.season ?? 9999)
                    .ThenBy(x => x.episode ?? 9999)
                    .Select(x => x.dto)
                    .ToList();

                return new { Success = true, Items = results };
            }
            catch (Exception ex)
            {
                _logger?.ErrorException("Error filtering episodes", ex);
                return new { Success = false, Message = ex.Message };
            }
        }
    }
}
