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
            return chapter?.MarkerType.ToString() ?? "Chapter";
        }

        public void SaveChapterList(BaseItem item, IList<(string Name, long StartPositionTicks, string MarkerType)> entries)
        {
            try
            {
                var runtimeTicks = item.RunTimeTicks;
                var chapters = new List<ChapterInfo>();
                foreach (var (name, ticks, markerType) in entries)
                {
                    if (runtimeTicks.HasValue && ticks >= runtimeTicks.Value)
                    {
                        _logger.Warn($"TimeMarkEdit: skipping chapter '{name}' at {ticks} ticks — at or beyond runtime ({runtimeTicks.Value} ticks) for '{item.Name}'");
                        continue;
                    }
                    var chapter = new ChapterInfo
                    {
                        Name = name ?? string.Empty,
                        StartPositionTicks = ticks
                    };
                    if (!string.IsNullOrEmpty(markerType) && markerType != "Chapter")
                    {
                        if (Enum.TryParse<MarkerType>(markerType, out var mt))
                            chapter.MarkerType = mt;
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

    }
}
