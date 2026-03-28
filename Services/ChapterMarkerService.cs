using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TimeMarkEdit.Services
{
    public class ChapterMarkerService
    {
        private readonly ILogger _logger;
        private readonly IItemRepository _itemRepository;

        public ChapterMarkerService(ILogger logger, IItemRepository itemRepository)
        {
            _logger = logger;
            _itemRepository = itemRepository;
        }

        public List<ChapterInfo> GetChapters(BaseItem item)
        {
            return _itemRepository.GetChapters(item)?.ToList() ?? new List<ChapterInfo>();
        }

        public string GetChapterMarkerType(ChapterInfo chapter)
        {
            return GetMarkerType(chapter) ?? "Chapter";
        }

        public void SaveChapterList(BaseItem item, IList<(string Name, long StartPositionTicks, string MarkerType)> entries)
        {
            try
            {
                var chapters = new List<ChapterInfo>();
                foreach (var (name, ticks, markerType) in entries)
                {
                    var chapter = new ChapterInfo
                    {
                        Name = name ?? string.Empty,
                        StartPositionTicks = ticks
                    };
                    if (!string.IsNullOrEmpty(markerType) && markerType != "Chapter")
                    {
                        if (Enum.TryParse<MarkerType>(markerType, out var mt))
                            SetMarkerType(chapter, mt);
                    }
                    chapters.Add(chapter);
                }
                chapters = chapters.OrderBy(c => c.StartPositionTicks).ToList();
                _itemRepository.SaveChapters(item.InternalId, chapters);
                _logger.Info($"TimeMarkEdit: saved {chapters.Count} chapter(s) for '{item.Name}'");
            }
            catch (Exception ex)
            {
                _logger.ErrorException($"Error saving chapter list for '{item.Name}'", ex);
                throw;
            }
        }

        private string? GetMarkerType(ChapterInfo chapter)
        {
            try
            {
                if (chapter == null)
                    return null;

                var markerTypeProp = chapter.GetType().GetProperty("MarkerType");
                if (markerTypeProp != null && markerTypeProp.CanRead)
                    return markerTypeProp.GetValue(chapter)?.ToString();
            }
            catch (Exception ex)
            {
                _logger.Debug($"Error reading MarkerType property: {ex.Message}");
            }
            return null;
        }

        private bool SetMarkerType(ChapterInfo chapter, MarkerType markerType)
        {
            try
            {
                if (chapter == null)
                    return false;

                var markerTypeProp = chapter.GetType().GetProperty("MarkerType");
                if (markerTypeProp != null && markerTypeProp.CanWrite)
                {
                    markerTypeProp.SetValue(chapter, markerType);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"Error setting MarkerType property: {ex.Message}");
            }
            return false;
        }
    }
}
