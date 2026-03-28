using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using TimeMarkEdit.Services;

namespace TimeMarkEdit
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages, IHasThumbImage, IServerEntryPoint, IDisposable
    {
        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IItemRepository _itemRepository;

        public static Plugin? Instance { get; private set; }
        public static ChapterMarkerService? ChapterMarkerService { get; private set; }

        public override string Name => "TimeMarkEdit";
        public override string Description => "A chapter and timemark editor for Emby media items.";
        public override Guid Id => Guid.Parse("c4f7b3a8-2e5d-4f1a-8b6c-9d0e3a7f2c51");

        public Plugin(
            IApplicationPaths appPaths,
            IXmlSerializer xmlSerializer,
            ILogManager logManager,
            ILibraryManager libraryManager,
            IItemRepository itemRepository)
            : base(appPaths, xmlSerializer)
        {
            Instance = this;
            _logger = logManager.GetLogger(GetType().Name);
            _libraryManager = libraryManager;
            _itemRepository = itemRepository;
        }

        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = "TimeMarkEditPage",
                    EmbeddedResourcePath = "TimeMarkEdit.Configuration.TimeMarkEdit.html",
                    EnableInMainMenu = true,
                    MenuIcon = "edit",
                    DisplayName = "TimeMarkEdit"
                },
                new PluginPageInfo
                {
                    Name = "TimeMarkEditManager",
                    EmbeddedResourcePath = "TimeMarkEdit.Configuration.TimeMarkEditManager.js"
                }
            };
        }

        public Stream GetThumbImage()
        {
            var type = GetType();
            return type.Assembly.GetManifestResourceStream(type.Namespace + ".Images.logo.jpg")!;
        }

        public ImageFormat ThumbImageFormat => ImageFormat.Jpg;

        public void Run()
        {
            ChapterMarkerService = new ChapterMarkerService(_logger, _itemRepository);
            _logger.Info("TimeMarkEdit plugin started");
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing) { }
    }
}
