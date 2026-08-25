using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace ACCODocs.Logic.LinkLibrary
{
    /// <summary>
    /// Master library load / cache / refresh (spec section 10).
    ///   - The cache renders first; the pane must open instantly, always.
    ///   - The master on the share is checked by comparing <c>revision</c>. NEVER timestamps —
    ///     robocopy, share migrations, and backup restores all scramble LastWriteTime.
    ///   - Share unreachable = keep the cache, no dialog.
    /// Pure file IO — no Revit API, no UI. Callers marshal results to the UI thread.
    /// </summary>
    public class MasterLibraryService
    {
        public const string CacheFileName = "LinkLibrary.master.json";

        private readonly LinkLibraryConfig _config;

        public MasterLibraryService(LinkLibraryConfig config)
        {
            _config = config;
        }

        public string CachePath => Path.Combine(_config.ExpandedLocalCacheFolder, CacheFileName);
        public string MasterPath => _config.ExpandedMasterLibraryPath;

        public enum RefreshStatus
        {
            /// <summary>Master unreachable/unreadable — keep whatever is already rendered.</summary>
            Offline,
            /// <summary>Master reachable, cache revision is current.</summary>
            UpToDate,
            /// <summary>Master had a newer revision; Document holds it and the cache was updated.</summary>
            Updated
        }

        public class RefreshResult
        {
            public RefreshStatus Status;
            public LinkLibraryDocument Document;
        }

        /// <summary>Loads the local cache. Null when missing or corrupt — never throws, never dialogs.</summary>
        public LinkLibraryDocument LoadCached()
        {
            var doc = TryParseFile(CachePath);
            Debug.WriteLine(doc == null
                ? $"[LinkLibrary] No usable cache at {CachePath}"
                : $"[LinkLibrary] Cache loaded, revision {doc.Revision}");
            return doc;
        }

        /// <summary>
        /// Checks the master and updates the cache when its revision is newer than
        /// <paramref name="currentRevision"/> (pass null when nothing is loaded yet).
        /// Safe to call from a background thread.
        /// </summary>
        public RefreshResult CheckForUpdate(int? currentRevision)
        {
            string masterJson = TryReadFile(MasterPath);
            var master = masterJson == null ? null : TryParseJson(masterJson, MasterPath);
            if (master == null)
            {
                Debug.WriteLine($"[LinkLibrary] Master unreachable/unreadable at '{MasterPath}' — offline.");
                return new RefreshResult { Status = RefreshStatus.Offline };
            }

            if (currentRevision.HasValue && master.Revision <= currentRevision.Value)
                return new RefreshResult { Status = RefreshStatus.UpToDate };

            // Copy the master down verbatim (not re-serialized) so the cache is a faithful copy.
            SaveCacheAtomic(masterJson);
            Debug.WriteLine($"[LinkLibrary] Cache updated to revision {master.Revision}.");
            return new RefreshResult { Status = RefreshStatus.Updated, Document = master };
        }

        /// <summary>
        /// Recursive filtered copy for the current Revit version (spec section 5:
        /// revitVersions omitted = all versions). Groups left with no visible content are dropped.
        /// </summary>
        public static List<LibraryNode> FilterForRevitVersion(IEnumerable<LibraryNode> nodes, int revitVersion)
        {
            var result = new List<LibraryNode>();
            if (nodes == null)
                return result;

            foreach (LibraryNode node in nodes)
            {
                if (!node.IsVisibleFor(revitVersion))
                    continue;

                if (node.IsGroup)
                {
                    List<LibraryNode> children = FilterForRevitVersion(node.Children, revitVersion);
                    if (children.Count == 0)
                        continue;

                    result.Add(new LibraryNode
                    {
                        Id = node.Id,
                        Title = node.Title,
                        Description = node.Description,
                        Tags = node.Tags,
                        Added = node.Added,
                        Updated = node.Updated,
                        Owner = node.Owner,
                        Source = node.Source,
                        Children = children
                    });
                }
                else
                {
                    result.Add(node);
                }
            }
            return result;
        }

        /// <summary>
        /// Stamps the runtime IsNew flag (spec section 7): anything whose added/updated
        /// date falls within newBadgeDays gets a badge — without it, people learn the
        /// list is static and stop opening it.
        /// </summary>
        public static void MarkNewBadges(IEnumerable<LibraryNode> nodes, int newBadgeDays)
        {
            if (nodes == null || newBadgeDays <= 0)
                return;

            DateTime cutoff = DateTime.UtcNow.AddDays(-newBadgeDays);
            foreach (LibraryNode node in nodes)
            {
                node.IsNew = !node.IsGroup &&
                             (IsAfter(node.Updated, cutoff) || IsAfter(node.Added, cutoff));
                if (node.Children != null)
                    MarkNewBadges(node.Children, newBadgeDays);
            }
        }

        private static bool IsAfter(string dateText, DateTime cutoffUtc)
        {
            return DateTime.TryParse(dateText, out DateTime parsed) && parsed >= cutoffUtc;
        }

        // ----- file helpers ---------------------------------------------------

        private static string TryReadFile(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return null;
                return File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LinkLibrary] Read failed '{path}': {ex.Message}");
                return null;
            }
        }

        private static LinkLibraryDocument TryParseFile(string path)
        {
            string json = TryReadFile(path);
            return json == null ? null : TryParseJson(json, path);
        }

        private static LinkLibraryDocument TryParseJson(string json, string sourceForLog)
        {
            try
            {
                return JsonConvert.DeserializeObject<LinkLibraryDocument>(json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LinkLibrary] Parse failed '{sourceForLog}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Atomic write: temp file then move into place — never write in place, Revit crashes
        /// (spec section 6 file handling). File.Replace is used when the target exists because
        /// net48 has no File.Move overwrite overload.
        /// </summary>
        private void SaveCacheAtomic(string json)
        {
            try
            {
                string folder = _config.ExpandedLocalCacheFolder;
                Directory.CreateDirectory(folder);

                string temp = Path.Combine(folder, CacheFileName + ".tmp");
                File.WriteAllText(temp, json);

                if (File.Exists(CachePath))
                    File.Replace(temp, CachePath, null);
                else
                    File.Move(temp, CachePath);
            }
            catch (Exception ex)
            {
                // A failed cache write must never surface to the user — next refresh retries.
                Debug.WriteLine($"[LinkLibrary] Cache write failed: {ex.Message}");
            }
        }
    }
}
