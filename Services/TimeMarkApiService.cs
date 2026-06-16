using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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

        private bool TryGetService([NotNullWhen(true)] out ChapterMarkerService? svc) => (svc = Plugin.ChapterMarkerService) != null;

        public object Get(GetEpisodeChaptersRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.EpisodeId))
                    return new { Success = false, Message = "EpisodeId is required" };

                var item = ResolveItem(request.EpisodeId);
                if (item == null)
                    return new { Success = false, Message = "Item not found" };

                if (!TryGetService(out var chapterService))
                    return new { Success = false, Message = "Chapter service not available" };

                var chapters = chapterService.GetChapters(item);
                var result = chapters.Select((c, i) => new
                {
                    Index = i,
                    Name = c.Name ?? string.Empty,
                    StartPositionTicks = c.StartPositionTicks,
                    StartTime = TimeSpan.FromTicks(c.StartPositionTicks).ToString(@"hh\:mm\:ss\.fff"),
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

                if (!TryGetService(out var chapterService))
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

                if (!TryGetService(out var chapterService))
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
                    IsVirtualItem = false,
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

        public object Get(GetSummaryRequest request)
        {
            try
            {
                if (!TryGetService(out var chapterService))
                    return new { Success = false, Message = "Chapter service not available" };

                var seriesQuery = new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { "Series" },
                    IsVirtualItem = false,
                    Recursive = true
                };

                var seriesItems = _libraryManager.GetItemsResult(seriesQuery).Items
                    .OrderBy(s => s.SortName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var summaryData = new List<object>();

                foreach (var series in seriesItems)
                {
                    var seasonQuery = new InternalItemsQuery
                    {
                        IncludeItemTypes = new[] { "Season" },
                        IsVirtualItem = false,
                        ParentIds = new long[] { series.InternalId }
                    };

                    var seasonItems = _libraryManager.GetItemsResult(seasonQuery).Items
                        .OrderBy(s => s.IndexNumber ?? 9999)
                        .ToList();

                    var seasonsData = new List<object>();
                    int totalEpisodes = 0, totalIntro = 0, totalCredits = 0;

                    foreach (var season in seasonItems)
                    {
                        var episodeQuery = new InternalItemsQuery
                        {
                            IncludeItemTypes = new[] { "Episode" },
                            IsVirtualItem = false,
                            ParentIds = new long[] { season.InternalId }
                        };

                        var episodeItems = _libraryManager.GetItemsResult(episodeQuery).Items;
                        int epCount = 0, introCount = 0, creditsCount = 0;
                        long totalIntroDuration = 0;

                        foreach (var episode in episodeItems)
                        {
                            epCount++;
                            var chapters = chapterService.GetChapters(episode);
                            long introStart = -1, introEnd = -1, credits = -1;

                            foreach (var c in chapters)
                            {
                                var mt = chapterService.GetChapterMarkerType(c);
                                if (introStart == -1 && mt == "IntroStart") introStart = c.StartPositionTicks;
                                else if (introEnd == -1 && mt == "IntroEnd") introEnd = c.StartPositionTicks;
                                else if (credits == -1 && mt == "CreditsStart") credits = c.StartPositionTicks;
                            }

                            if (introStart != -1 && introEnd != -1 && introStart < introEnd)
                            {
                                introCount++;
                                totalIntroDuration += introEnd - introStart;
                            }
                            if (credits != -1) creditsCount++;
                        }

                        var avgIntro = introCount > 0
                            ? TimeSpan.FromTicks(totalIntroDuration / introCount).ToString(@"mm\:ss")
                            : "--:--";

                        seasonsData.Add(new
                        {
                            Name = season.Name,
                            EpisodeCount = epCount,
                            IntroCount = introCount,
                            CreditsCount = creditsCount,
                            AvgIntro = avgIntro
                        });

                        totalEpisodes += epCount;
                        totalIntro += introCount;
                        totalCredits += creditsCount;
                    }

                    summaryData.Add(new
                    {
                        Name = series.Name,
                        EpisodeCount = totalEpisodes,
                        IntroCount = totalIntro,
                        CreditsCount = totalCredits,
                        Seasons = seasonsData
                    });
                }

                return new { Success = true, Series = summaryData };
            }
            catch (Exception ex)
            {
                _logger?.ErrorException("Error getting summary", ex);
                return new { Success = false, Message = ex.Message };
            }
        }

        public object Get(FilterEpisodesRequest request)
        {
            try
            {
                if (!TryGetService(out var chapterService))
                    return new { Success = false, Message = "Chapter service not available" };

                var includeTypes = request.AllLibraries
                    ? new[] { "Episode", "Movie" }
                    : new[] { "Episode" };

                var query = new InternalItemsQuery
                {
                    IncludeItemTypes = includeTypes,
                    IsVirtualItem = false,
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

                var matchingItems = new List<(int? season, int? episode, string seriesName, string itemType, object dto)>();

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

                    if (request.MinRuntimeSeconds > 0)
                    {
                        var minRuntimeTicks = (long)request.MinRuntimeSeconds * TimeSpan.TicksPerSecond;
                        if (!item.RunTimeTicks.HasValue || item.RunTimeTicks.Value < minRuntimeTicks) continue;
                    }

                    var ep = item as Episode;
                    var typeName = item.GetType().Name;
                    matchingItems.Add((
                        ep?.ParentIndexNumber,
                        ep?.IndexNumber,
                        ep?.SeriesName ?? item.Name ?? string.Empty,
                        typeName,
                        new
                        {
                            Id = item.Id.ToString("N"),
                            Name = item.Name,
                            Type = typeName,
                            IndexNumber = ep?.IndexNumber,
                            ParentIndexNumber = ep?.ParentIndexNumber,
                            SeriesName = ep?.SeriesName,
                            RunTimeTicks = item.RunTimeTicks
                        }
                    ));
                }

                var results = matchingItems
                    .OrderBy(x => x.itemType == "Movie" ? 0 : 1)
                    .ThenBy(x => x.seriesName, StringComparer.OrdinalIgnoreCase)
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

        private bool TryGetMkvService([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out MkvChapterService? svc)
            => (svc = Plugin.MkvChapterService) != null;

        private static string DetectMarkerType(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Chapter";
            var n = name.Trim().ToLowerInvariant();

            if (n == "intro" || n == "intro start" || n == "introstart" || n == "intro begin" ||
                n == "intro begins" || n == "opening" || n == "opening start" || n == "op" ||
                n == "op start" || n == "op begin" || n == "title" || n == "title sequence" ||
                n == "title card" || n == "titles" || n == "main titles" || n == "main title" ||
                n == "opening titles" || n == "title theme")
                return "IntroStart";

            if (n == "intro end" || n == "introend" || n == "intro ends" || n == "end intro" ||
                n == "end of intro" || n == "opening end" || n == "op end")
                return "IntroEnd";

            if (n == "credits" || n == "end credits" || n == "ending credits" || n == "credits start" ||
                n == "creditsstart" || n == "outro" || n == "outro start" || n == "ed" ||
                n == "ending" || n == "end" || n == "end card")
                return "CreditsStart";

            return "Chapter";
        }

        private static List<(string Name, long Ticks, string MarkerType)> BuildImportEntries(
            List<(string Name, long StartPositionTicks)> mkvChapters)
        {
            var result = mkvChapters
                .Select(c => (c.Name ?? string.Empty, c.StartPositionTicks, DetectMarkerType(c.Name ?? string.Empty)))
                .ToList();

            for (int i = 0; i < result.Count - 1; i++)
            {
                if (result[i].Item3 == "IntroStart" && result[i + 1].Item3 == "Chapter")
                    result[i + 1] = (result[i + 1].Item1, result[i + 1].Item2, "IntroEnd");
            }

            return result;
        }

        public object Get(GetMkvChaptersRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.ItemId))
                    return new { Success = false, Message = "ItemId is required" };

                var item = ResolveItem(request.ItemId);
                if (item == null)
                    return new { Success = false, Message = "Item not found" };

                if (string.IsNullOrEmpty(item.Path))
                    return new { Success = false, Message = "Item has no file path" };

                if (!TryGetMkvService(out var mkvService))
                    return new { Success = false, Message = "MKV chapter service not available" };

                var mkvChapters = mkvService.ReadChapters(item.Path);

                var built = BuildImportEntries(mkvChapters);
                var result = built.Select((c, i) => new
                {
                    Index = i,
                    Name = c.Name,
                    StartPositionTicks = c.Ticks,
                    StartTime = TimeSpan.FromTicks(c.Ticks).ToString(@"hh\:mm\:ss\.fff"),
                    MarkerType = c.MarkerType
                }).ToList();

                var ext = System.IO.Path.GetExtension(item.Path).ToUpperInvariant();
                return new
                {
                    Success = true,
                    ItemId = request.ItemId,
                    ItemName = item.Name,
                    FileExtension = ext,
                    IsMkv = !string.IsNullOrEmpty(ext) && new[] { ".MKV", ".MKA", ".MKS", ".MK3D", ".WEBM" }.Contains(ext),
                    Chapters = result
                };
            }
            catch (Exception ex)
            {
                _logger?.ErrorException("Error reading MKV chapters", ex);
                return new { Success = false, Message = ex.Message };
            }
        }

        public object Post(ImportMkvChaptersRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.ItemId))
                    return new { Success = false, Message = "ItemId is required" };

                var item = ResolveItem(request.ItemId);
                if (item == null)
                    return new { Success = false, Message = "Item not found" };

                if (string.IsNullOrEmpty(item.Path))
                    return new { Success = false, Message = "Item has no file path" };

                if (!TryGetMkvService(out var mkvService))
                    return new { Success = false, Message = "MKV chapter service not available" };

                if (!TryGetService(out var chapterService))
                    return new { Success = false, Message = "Chapter service not available" };

                var mkvChapters = mkvService.ReadChapters(item.Path);
                if (mkvChapters.Count == 0)
                    return new { Success = false, Message = "No embedded chapters found in this file" };

                var entries = BuildImportEntries(mkvChapters);
                var saveList = entries.Select(e => (e.Name, e.Ticks, e.MarkerType)).ToList();

                chapterService.SaveChapterList(item, saveList);

                return new
                {
                    Success = true,
                    Message = $"Imported {entries.Count} chapter(s) from MKV into '{item.Name}'",
                    ChapterCount = entries.Count
                };
            }
            catch (Exception ex)
            {
                _logger?.ErrorException("Error importing MKV chapters", ex);
                return new { Success = false, Message = ex.Message };
            }
        }

        public object Post(ImportMkvChaptersBulkRequest request)
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

                if (!TryGetMkvService(out var mkvService))
                    return new { Success = false, Message = "MKV chapter service not available" };

                if (!TryGetService(out var chapterService))
                    return new { Success = false, Message = "Chapter service not available" };

                var scope = (request.Scope ?? "Season").Trim();

                BaseItem? scopeItem = null;
                if (string.Equals(scope, "Series", StringComparison.OrdinalIgnoreCase))
                {
                    var parentSeason = _libraryManager.GetItemById(sourceEp.ParentId);
                    if (parentSeason != null)
                        scopeItem = _libraryManager.GetItemById(parentSeason.ParentId);
                }
                else
                {
                    scopeItem = _libraryManager.GetItemById(sourceEp.ParentId);
                }

                if (scopeItem == null)
                    return new { Success = false, Message = $"Could not resolve parent {scope}" };

                var query = new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { "Episode" },
                    IsVirtualItem = false,
                    AncestorIds = new long[] { scopeItem.InternalId },
                    Recursive = true
                };

                var episodeItems = _libraryManager.GetItemsResult(query).Items;
                int processed = 0, succeeded = 0, skipped = 0, failed = 0;

                foreach (var ep in episodeItems)
                {
                    processed++;
                    if (string.IsNullOrEmpty(ep.Path))
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        var mkvChapters = mkvService.ReadChapters(ep.Path);
                        if (mkvChapters.Count == 0)
                        {
                            skipped++;
                            continue;
                        }

                        var built2 = BuildImportEntries(mkvChapters);
                        var entries = built2.Select(e => (e.Name, e.Ticks, e.MarkerType)).ToList();

                        chapterService.SaveChapterList(ep, entries);
                        succeeded++;
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn($"TimeMarkEdit: Failed to import MKV chapters for '{ep.Name}': {ex.Message}");
                        failed++;
                    }
                }

                return new
                {
                    Success = true,
                    Message = $"Processed {processed} episode(s): {succeeded} imported, {skipped} skipped (no MKV chapters), {failed} failed",
                    Processed = processed,
                    Succeeded = succeeded,
                    Skipped = skipped,
                    Failed = failed
                };
            }
            catch (Exception ex)
            {
                _logger?.ErrorException("Error bulk-importing MKV chapters", ex);
                return new { Success = false, Message = ex.Message };
            }
        }
    }
}
