using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;

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

        private bool TryGetIntroDbService([NotNullWhen(true)] out TheIntroDbService? svc)
            => (svc = Plugin.TheIntroDbService) != null;

        private static List<(string Name, long StartPositionTicks, string MarkerType)> MergeWithExisting(
            BaseItem item,
            List<(string Name, long StartPositionTicks, string MarkerType)> newChapters,
            ChapterMarkerService chapterService)
        {
            var incomingTypes = new HashSet<string>(newChapters.Select(c => c.MarkerType));
            var newPositions = newChapters.Select(c => c.StartPositionTicks).ToList();
            const long tenSecondsTicks = 100_000_000L;

            var retained = chapterService.GetChapters(item)
                .Where(c => !incomingTypes.Contains(chapterService.GetChapterMarkerType(c)))
                .Where(c => !newPositions.Any(p => Math.Abs(c.StartPositionTicks - p) <= tenSecondsTicks))
                .Select(c => (c.Name ?? string.Empty, c.StartPositionTicks, chapterService.GetChapterMarkerType(c)))
                .ToList();
            retained.AddRange(newChapters);
            return retained;
        }

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

        public object Post(DownloadIntroDbRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.ItemId))
                    return new { Success = false, Message = "ItemId is required" };

                if (!TryGetIntroDbService(out var introDbService))
                    return new { Success = false, Message = "TheIntroDB service not available" };

                var item = ResolveItem(request.ItemId);
                if (item == null)
                    return new { Success = false, Message = "Item not found" };

                if (!TryGetService(out var chapterService))
                    return new { Success = false, Message = "Chapter service not available" };

                var enabledSegments = Plugin.Instance?.Configuration?.EnabledSegmentTypes ?? new List<string> { "intro", "credits" };

                string? tmdbId = null;
                int? season = null;
                int? episode = null;
                long? durationMs = item.RunTimeTicks.HasValue ? item.RunTimeTicks.Value / 10000 : (long?)null;

                var ep = item as Episode;
                if (ep != null)
                {
                    var parentSeason = _libraryManager.GetItemById(ep.ParentId);
                    var series = parentSeason != null ? _libraryManager.GetItemById(parentSeason.ParentId) : null;
                    if (series?.ProviderIds != null && series.ProviderIds.TryGetValue("Tmdb", out var st))
                        tmdbId = st;
                    season = ep.ParentIndexNumber;
                    episode = ep.IndexNumber;
                }
                else
                {
                    if (item.ProviderIds != null && item.ProviderIds.TryGetValue("Tmdb", out var mt))
                        tmdbId = mt;
                }

                if (string.IsNullOrEmpty(tmdbId))
                    return new DownloadIntroDbResponse { Success = false, Message = "Item has no TMDB ID — cannot look up timestamps", ApiKeyConfigured = true, ItemName = item.Name ?? string.Empty };

                TheIntroDbMediaResponse? timestamps;
                try
                {
                    timestamps = introDbService.GetMediaTimestampsAsync(tmdbId, season, episode, durationMs).GetAwaiter().GetResult();
                }
                catch (UnauthorizedAccessException)
                {
                    return new DownloadIntroDbResponse { Success = false, Message = "API key invalid — check your TheIntroDB configuration", ApiKeyConfigured = true };
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("rate limit"))
                {
                    return new DownloadIntroDbResponse { Success = false, Message = "Rate limit exceeded — try again later", ApiKeyConfigured = true, ConnectionSuccessful = true };
                }
                catch (HttpRequestException ex)
                {
                    return new DownloadIntroDbResponse { Success = false, Message = "Connection failed: " + ex.Message, ApiKeyConfigured = true };
                }

                if (timestamps == null)
                    return new DownloadIntroDbResponse { Success = false, Message = "No timestamps found for this item in TheIntroDB", ApiKeyConfigured = true, ConnectionSuccessful = true, ItemName = item.Name ?? string.Empty };

                var chapters = introDbService.ParseTimestamps(timestamps, enabledSegments, item.RunTimeTicks);
                var overwrite = Plugin.Instance?.Configuration?.OverwriteExisting ?? true;
                if (!overwrite)
                    chapters = MergeWithExisting(item, chapters, chapterService);
                chapterService.SaveChapterList(item, chapters);

                return new DownloadIntroDbResponse
                {
                    Success = true,
                    Message = $"Downloaded {chapters.Count} timestamp(s) for '{item.Name}'",
                    ItemName = item.Name ?? string.Empty,
                    ChapterCount = chapters.Count,
                    Chapters = chapters.Select(c => new ChapterItemDto { Name = c.Name, StartPositionTicks = c.StartPositionTicks, MarkerType = c.MarkerType }).ToList(),
                    ApiKeyConfigured = true,
                    ConnectionSuccessful = true
                };
            }
            catch (Exception ex)
            {
                _logger?.ErrorException("Error downloading TheIntroDB timestamps", ex);
                return new { Success = false, Message = ex.Message };
            }
        }

        public object Post(DownloadIntroDbBulkRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.EpisodeId))
                    return new { Success = false, Message = "EpisodeId is required" };

                if (!TryGetIntroDbService(out var introDbService))
                    return new { Success = false, Message = "TheIntroDB service not available" };

                var sourceItem = ResolveItem(request.EpisodeId);
                if (sourceItem == null)
                    return new { Success = false, Message = "Item not found" };

                var sourceEp = sourceItem as Episode;
                if (sourceEp == null)
                    return new { Success = false, Message = "Item is not an episode" };

                if (!TryGetService(out var chapterService))
                    return new { Success = false, Message = "Chapter service not available" };

                var scope = (request.Scope ?? "Season").Trim();
                BaseItem? scopeItem;
                if (string.Equals(scope, "Series", StringComparison.OrdinalIgnoreCase))
                {
                    var parentSeason2 = _libraryManager.GetItemById(sourceEp.ParentId);
                    scopeItem = parentSeason2 != null ? _libraryManager.GetItemById(parentSeason2.ParentId) : null;
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
                var enabledSegments = Plugin.Instance?.Configuration?.EnabledSegmentTypes ?? new List<string> { "intro", "credits" };
                int processed = 0, succeeded = 0, skipped = 0, notFound = 0, failed = 0;

                foreach (var ep in episodeItems)
                {
                    processed++;
                    var epCast = ep as Episode;
                    if (epCast == null) { skipped++; continue; }

                    var parentSeason = _libraryManager.GetItemById(epCast.ParentId);
                    var series = parentSeason != null ? _libraryManager.GetItemById(parentSeason.ParentId) : null;

                    string? tmdbId = null;
                    if (series?.ProviderIds != null && series.ProviderIds.TryGetValue("Tmdb", out var st))
                        tmdbId = st;

                    if (string.IsNullOrEmpty(tmdbId)) { skipped++; continue; }

                    long? durationMs = ep.RunTimeTicks.HasValue ? ep.RunTimeTicks.Value / 10000 : (long?)null;

                    try
                    {
                        var timestamps = introDbService.GetMediaTimestampsAsync(tmdbId, epCast.ParentIndexNumber, epCast.IndexNumber, durationMs).GetAwaiter().GetResult();
                        if (timestamps == null) { notFound++; continue; }

                        var chapters = introDbService.ParseTimestamps(timestamps, enabledSegments, ep.RunTimeTicks);
                        var overwrite = Plugin.Instance?.Configuration?.OverwriteExisting ?? true;
                        if (!overwrite)
                            chapters = MergeWithExisting(ep, chapters, chapterService);
                        chapterService.SaveChapterList(ep, chapters);
                        succeeded++;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        return new { Success = false, Message = "API key invalid — bulk download aborted", Processed = processed, Succeeded = succeeded };
                    }
                    catch (InvalidOperationException ex) when (ex.Message.Contains("rate limit"))
                    {
                        return new { Success = false, Message = $"Rate limit exceeded after {processed} item(s) — try again later", Processed = processed, Succeeded = succeeded };
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn($"TimeMarkEdit: Failed to download timestamps for '{ep.Name}': {ex.Message}");
                        failed++;
                    }
                }

                return new
                {
                    Success = true,
                    Message = $"Processed {processed} episode(s): {succeeded} updated, {notFound} not found in TheIntroDB, {skipped} missing TMDB ID, {failed} failed",
                    Processed = processed,
                    Succeeded = succeeded,
                    NotFound = notFound,
                    Skipped = skipped,
                    Failed = failed
                };
            }
            catch (Exception ex)
            {
                _logger?.ErrorException("Error bulk-downloading TheIntroDB timestamps", ex);
                return new { Success = false, Message = ex.Message };
            }
        }

        public object Post(TestIntroDbConnectionRequest request)
        {
            try
            {
                if (!TryGetIntroDbService(out var introDbService))
                    return new TestIntroDbConnectionResponse { Success = false, Message = "TheIntroDB service not available" };

                try
                {
                    introDbService.GetMediaTimestampsAsync("1396", season: 1, episode: 1).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    return new TestIntroDbConnectionResponse { Success = false, Message = "Connection failed: " + ex.Message, ApiKeyConfigured = introDbService.IsConfigured };
                }

                return new TestIntroDbConnectionResponse { Success = true, Message = "Connection successful", ApiKeyConfigured = introDbService.IsConfigured };
            }
            catch (Exception ex)
            {
                _logger?.ErrorException("Error testing TheIntroDB connection", ex);
                return new TestIntroDbConnectionResponse { Success = false, Message = ex.Message };
            }
        }

        public object Get(GetIntroDbConfigRequest request)
        {
            try
            {
                var config = Plugin.Instance?.Configuration;
                var apiKey = config?.ApiKey ?? string.Empty;
                return new GetIntroDbConfigResponse
                {
                    ApiKey = "",
                    ApiKeyConfigured = !string.IsNullOrWhiteSpace(apiKey),
                    OverwriteExisting = config?.OverwriteExisting ?? true,
                    EnabledSegments = config?.EnabledSegmentTypes?.ToList() ?? new List<string> { "intro", "credits" }
                };
            }
            catch (Exception ex)
            {
                _logger?.ErrorException("Error getting TheIntroDB config", ex);
                return new { Success = false, Message = ex.Message };
            }
        }

        public object Post(SetIntroDbConfigRequest request)
        {
            try
            {
                if (Plugin.Instance == null)
                    return new SetIntroDbConfigResponse { Success = false, Message = "Plugin not initialized" };

                var config = Plugin.Instance.Configuration;
                if (!string.IsNullOrEmpty(request.ApiKey))
                    config.ApiKey = CredentialProtection.Protect(request.ApiKey);

                config.OverwriteExisting = request.OverwriteExisting;
                config.EnabledSegmentTypes = request.EnabledSegments ?? new List<string> { "intro", "credits" };

                Plugin.Instance.SaveConfiguration();
                return new SetIntroDbConfigResponse { Success = true, Message = "Configuration saved" };
            }
            catch (Exception ex)
            {
                _logger?.ErrorException("Error saving TheIntroDB config", ex);
                return new SetIntroDbConfigResponse { Success = false, Message = ex.Message };
            }
        }

        public object Post(UploadIntroDbRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.ItemId))
                    return new { Success = false, Message = "ItemId is required" };

                if (!TryGetIntroDbService(out var introDbService))
                    return new { Success = false, Message = "TheIntroDB service not available" };

                if (!introDbService.IsConfigured)
                    return new UploadIntroDbResponse { Success = false, Message = "TheIntroDB API key is not configured" };

                var item = ResolveItem(request.ItemId);
                if (item == null)
                    return new { Success = false, Message = "Item not found" };

                if (!TryGetService(out var chapterService))
                    return new { Success = false, Message = "Chapter service not available" };

                string? tmdbId = null;
                int? season = null;
                int? episode = null;

                var ep = item as Episode;
                if (ep != null)
                {
                    var parentSeason = _libraryManager.GetItemById(ep.ParentId);
                    var series = parentSeason != null ? _libraryManager.GetItemById(parentSeason.ParentId) : null;
                    if (series?.ProviderIds != null && series.ProviderIds.TryGetValue("Tmdb", out var st))
                        tmdbId = st;
                    season = ep.ParentIndexNumber;
                    episode = ep.IndexNumber;
                }
                else
                {
                    if (item.ProviderIds != null && item.ProviderIds.TryGetValue("Tmdb", out var mt))
                        tmdbId = mt;
                }

                if (string.IsNullOrEmpty(tmdbId))
                    return new UploadIntroDbResponse { Success = false, Message = "Item has no TMDB ID — cannot upload timestamps", ItemName = item.Name ?? string.Empty };

                var (intro, recap, credits) = ExtractSegmentsForUpload(item, chapterService);

                if (intro.Count == 0 && recap.Count == 0 && credits.Count == 0)
                    return new UploadIntroDbResponse { Success = false, Message = "No intro, recap or credits marks found to upload", ItemName = item.Name ?? string.Empty };

                var mediaType = ep != null ? "tv" : "movie";
                var durationMs = item.RunTimeTicks.HasValue ? item.RunTimeTicks.Value / 10000 : (long?)null;

                try
                {
                    introDbService.SubmitTimestampsAsync(tmdbId, mediaType, season, episode, durationMs, intro, recap, credits).GetAwaiter().GetResult();
                }
                catch (UnauthorizedAccessException)
                {
                    return new UploadIntroDbResponse { Success = false, Message = "API key invalid — check your TheIntroDB configuration", ItemName = item.Name ?? string.Empty };
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("rate limit"))
                {
                    return new UploadIntroDbResponse { Success = false, Message = "Rate limit exceeded — try again later", ItemName = item.Name ?? string.Empty };
                }
                catch (System.Net.Http.HttpRequestException ex)
                {
                    return new UploadIntroDbResponse { Success = false, Message = "Connection failed: " + ex.Message, ItemName = item.Name ?? string.Empty };
                }

                return new UploadIntroDbResponse
                {
                    Success = true,
                    Message = $"Uploaded {intro.Count} intro, {recap.Count} recap, {credits.Count} credits segment(s) for '{item.Name}'",
                    ItemName = item.Name ?? string.Empty,
                    IntroCount = intro.Count,
                    RecapCount = recap.Count,
                    CreditsCount = credits.Count
                };
            }
            catch (Exception ex)
            {
                _logger?.ErrorException("Error uploading TheIntroDB timestamps", ex);
                return new { Success = false, Message = ex.Message };
            }
        }

        private static (List<TimeSegment> Intro, List<TimeSegment> Recap, List<TimeSegment> Credits)
            ExtractSegmentsForUpload(BaseItem item, ChapterMarkerService chapterService)
        {
            var chapters = chapterService.GetChapters(item);
            var intro = new List<TimeSegment>();
            var recap = new List<TimeSegment>();
            var credits = new List<TimeSegment>();

            long? introStartTicks = null;
            bool introStartIsRecap = false;
            long? creditsStartTicks = null;

            foreach (var ch in chapters.OrderBy(c => c.StartPositionTicks))
            {
                var markerType = chapterService.GetChapterMarkerType(ch);
                var name = (ch.Name ?? string.Empty).Trim();

                if (markerType == "IntroStart")
                {
                    if (introStartTicks.HasValue)
                    {
                        var unclosed = new TimeSegment { StartMs = (int)(introStartTicks.Value / 10000) };
                        if (introStartIsRecap) recap.Add(unclosed); else intro.Add(unclosed);
                    }
                    introStartTicks = ch.StartPositionTicks;
                    introStartIsRecap = name.IndexOf("recap", StringComparison.OrdinalIgnoreCase) >= 0;
                }
                else if (markerType == "IntroEnd")
                {
                    if (introStartTicks.HasValue)
                    {
                        var seg = new TimeSegment { StartMs = (int)(introStartTicks.Value / 10000), EndMs = (int)(ch.StartPositionTicks / 10000) };
                        if (introStartIsRecap) recap.Add(seg); else intro.Add(seg);
                        introStartTicks = null;
                    }
                }
                else if (markerType == "CreditsStart")
                {
                    if (creditsStartTicks.HasValue)
                        credits.Add(new TimeSegment { StartMs = (int)(creditsStartTicks.Value / 10000) });
                    creditsStartTicks = ch.StartPositionTicks;
                }
                else if (markerType == "CreditsEnd")
                {
                    if (creditsStartTicks.HasValue)
                    {
                        credits.Add(new TimeSegment { StartMs = (int)(creditsStartTicks.Value / 10000), EndMs = (int)(ch.StartPositionTicks / 10000) });
                        creditsStartTicks = null;
                    }
                }
            }

            if (introStartTicks.HasValue)
            {
                var seg = new TimeSegment { StartMs = (int)(introStartTicks.Value / 10000) };
                if (introStartIsRecap) recap.Add(seg); else intro.Add(seg);
            }
            if (creditsStartTicks.HasValue)
                credits.Add(new TimeSegment { StartMs = (int)(creditsStartTicks.Value / 10000) });

            return (intro, recap, credits);
        }
    }
}
