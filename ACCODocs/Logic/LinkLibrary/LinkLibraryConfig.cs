using Newtonsoft.Json;

namespace ACCODocs.Logic.LinkLibrary
{
    /// <summary>
    /// Deployment config for the Link Library pane (spec section 4).
    /// Probe order, first hit wins:
    ///   1. C:\ACCORevit\ACCO\ACCORevit ADDINS\02-ACCORevit Ribbons\LinkLibrary.config.json  (shared, all tabs/years)
    ///   2. &lt;assembly folder&gt;\LinkLibrary.config.json                                    (per-deploy override / dev)
    ///   3. Hardcoded defaults — the pane must never fail to open because config is missing.
    /// </summary>
    public class LinkLibraryConfig
    {
        public const string ConfigFileName = "LinkLibrary.config.json";
        public const string SharedConfigFolder = @"C:\ACCORevit\ACCO\ACCORevit ADDINS\02-ACCORevit Ribbons";

        public int ConfigVersion { get; set; } = 1;
        public string MasterLibraryPath { get; set; } = "";
        public string LocalCacheFolder { get; set; } = @"%PROGRAMDATA%\ACCO\RevitLinkLibrary\cache";
        public string UserLibraryFolder { get; set; } = @"%LOCALAPPDATA%\ACCO\RevitLinkLibrary";
        public string TelemetryFolder { get; set; } = @"%PROGRAMDATA%\ACCO\RevitLinkLibrary\usage";
        public string SuggestionRecipient { get; set; } = "";

        /// <summary>
        /// How "Suggest a link" opens a mail draft: "mailto" (default OS mail handler)
        /// or "gmail" (Gmail compose URL in the browser — ACCO is a Google Workspace shop).
        /// </summary>
        public string SuggestionMailMethod { get; set; } = "mailto";

        /// <summary>Prefixed to every suggestion subject so admins can filter/label the requests.</summary>
        public string SuggestionSubjectPrefix { get; set; } = "[ACCO Revit Link Library]";
        public int RefreshCheckMinutes { get; set; } = 60;
        public int NewBadgeDays { get; set; } = 14;
        public bool EnableTelemetry { get; set; } = true;

        /// <summary>How many distinct recent links are kept in the user file (0 disables recents).</summary>
        public int MaxRecentsStored { get; set; } = 20;

        /// <summary>How many recents the My Links tab displays (and its search indexes).</summary>
        public int MaxRecentsShown { get; set; } = 10;

        /// <summary>Where this config came from ("shared", "assembly folder", or "built-in defaults"). Runtime only.</summary>
        [JsonIgnore]
        public string Source { get; private set; } = "built-in defaults";

        /// <summary>Path of the file that was loaded, empty when running on defaults. Runtime only.</summary>
        [JsonIgnore]
        public string SourcePath { get; private set; } = "";

        /// <summary>
        /// Loads config following the probe order. Never throws and never shows a dialog —
        /// a bad or missing file falls through to the next probe (spec section 4).
        /// </summary>
        public static LinkLibraryConfig Load()
        {
            string sharedPath = Path.Combine(SharedConfigFolder, ConfigFileName);
            string assemblyFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            string assemblyPath = Path.Combine(assemblyFolder, ConfigFileName);

            LinkLibraryConfig config =
                TryLoadFile(sharedPath, "shared")
                ?? TryLoadFile(assemblyPath, "assembly folder")
                ?? new LinkLibraryConfig();

            Debug.WriteLine($"[LinkLibrary] Config loaded from {config.Source}" +
                            (config.SourcePath.Length > 0 ? $" ({config.SourcePath})" : ""));
            return config;
        }

        private static LinkLibraryConfig TryLoadFile(string path, string sourceLabel)
        {
            try
            {
                if (!File.Exists(path))
                    return null;

                var config = JsonConvert.DeserializeObject<LinkLibraryConfig>(File.ReadAllText(path));
                if (config == null)
                    return null;

                config.Source = sourceLabel;
                config.SourcePath = path;
                return config;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LinkLibrary] Config probe '{path}' failed, falling through: {ex.Message}");
                return null;
            }
        }

        // All path values run through Environment.ExpandEnvironmentVariables (spec section 4).
        public string ExpandedMasterLibraryPath => Environment.ExpandEnvironmentVariables(MasterLibraryPath ?? "");
        public string ExpandedLocalCacheFolder => Environment.ExpandEnvironmentVariables(LocalCacheFolder ?? "");
        public string ExpandedUserLibraryFolder => Environment.ExpandEnvironmentVariables(UserLibraryFolder ?? "");
        public string ExpandedTelemetryFolder => Environment.ExpandEnvironmentVariables(TelemetryFolder ?? "");
    }
}
