using MediaBrowser.Model.Plugins;
using System.Collections.Generic;

namespace TimeMarkEdit
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public string ApiKey { get; set; } = "";
        public bool OverwriteExisting { get; set; } = true;
        public List<string> EnabledSegmentTypes { get; set; } = new() { "intro", "credits" };
    }
}
