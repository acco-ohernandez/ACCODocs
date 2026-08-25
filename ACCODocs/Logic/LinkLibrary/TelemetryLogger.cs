using Newtonsoft.Json;

namespace ACCODocs.Logic.LinkLibrary
{
    /// <summary>
    /// Local click telemetry (spec section 9). JSON Lines, append-only — one record per
    /// line so a crash mid-write costs one line, never the file. Lives in ProgramData
    /// (one folder, per-user filenames) so the SYSTEM-context inventory script can
    /// collect it without walking user profiles. Nothing phones home.
    /// src values: tree, search, favorite, recent, pickElement, deepLink.
    /// </summary>
    public class TelemetryLogger
    {
        private const long MaxFileBytes = 5 * 1024 * 1024;   // rotate at 5 MB (spec section 9)

        private readonly LinkLibraryConfig _config;
        private readonly int _revitVersion;

        public TelemetryLogger(LinkLibraryConfig config, int revitVersion)
        {
            _config = config;
            _revitVersion = revitVersion;
        }

        public string TelemetryPath =>
            Path.Combine(_config.ExpandedTelemetryFolder, Environment.UserName + ".jsonl");

        /// <summary>Appends one usage record. Never throws, never dialogs.</summary>
        public void Log(string linkId, string src)
        {
            if (!_config.EnableTelemetry)
                return;

            try
            {
                Directory.CreateDirectory(_config.ExpandedTelemetryFolder);
                string path = TelemetryPath;
                RotateIfNeeded(path);

                string line = JsonConvert.SerializeObject(new
                {
                    ts = DateTime.UtcNow.ToString("o"),
                    linkId,
                    src,
                    revit = _revitVersion,
                    user = Environment.UserName
                });
                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LinkLibrary] Telemetry write failed: {ex.Message}");
            }
        }

        private static void RotateIfNeeded(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxFileBytes)
                {
                    string old = path + ".old";
                    if (File.Exists(old))
                        File.Delete(old);
                    File.Move(path, old);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LinkLibrary] Telemetry rotate failed: {ex.Message}");
            }
        }
    }
}
