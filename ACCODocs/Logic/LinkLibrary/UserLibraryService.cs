using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace ACCODocs.Logic.LinkLibrary
{
    /// <summary>
    /// Load/save for the per-user library file (spec section 6 file handling):
    ///   - Missing file is normal: treat as empty, move on, no dialog.
    ///   - Corrupt file: rename to .bak, start fresh, log it. Never throw a dialog
    ///     at someone who just opened a pane.
    ///   - Writes are atomic: temp file then move/replace. Never write in place.
    ///   - Never block pane load on the user file.
    /// Pure file IO — no Revit API, no UI.
    /// </summary>
    public class UserLibraryService
    {
        public const string UserFileName = "LinkLibrary.user.json";
        private const int MaxRecents = 20;

        private readonly LinkLibraryConfig _config;

        public UserLibraryService(LinkLibraryConfig config)
        {
            _config = config;
        }

        public string UserFilePath => Path.Combine(_config.ExpandedUserLibraryFolder, UserFileName);

        public UserLibraryDocument Load()
        {
            string path = UserFilePath;
            try
            {
                if (!File.Exists(path))
                    return NewDocument();   // missing is normal

                var doc = JsonConvert.DeserializeObject<UserLibraryDocument>(File.ReadAllText(path));
                if (doc == null)
                    return NewDocument();

                doc.Favorites = doc.Favorites ?? new List<string>();
                doc.Recents = doc.Recents ?? new List<RecentEntry>();
                doc.Groups = doc.Groups ?? new List<LibraryNode>();
                MarkAsUserSource(doc.Groups);
                return doc;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LinkLibrary] User file corrupt, starting fresh: {ex.Message}");
                try
                {
                    File.Copy(path, path + ".bak", true);
                    File.Delete(path);
                }
                catch (Exception bakEx)
                {
                    Debug.WriteLine($"[LinkLibrary] Could not .bak the corrupt user file: {bakEx.Message}");
                }
                return NewDocument();
            }
        }

        /// <summary>Atomic save (temp + move/replace). Failures are logged, never surfaced.</summary>
        public void Save(UserLibraryDocument doc)
        {
            try
            {
                doc.LastModified = DateTime.UtcNow.ToString("o");
                string folder = _config.ExpandedUserLibraryFolder;
                Directory.CreateDirectory(folder);

                string temp = Path.Combine(folder, UserFileName + ".tmp");
                File.WriteAllText(temp, JsonConvert.SerializeObject(doc, Formatting.Indented));

                if (File.Exists(UserFilePath))
                    File.Replace(temp, UserFilePath, null);
                else
                    File.Move(temp, UserFilePath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LinkLibrary] User file save failed: {ex.Message}");
            }
        }

        /// <summary>Bumps (or creates) the recent entry for a link id and trims the list.</summary>
        public static void RecordRecent(UserLibraryDocument doc, string linkId)
        {
            RecentEntry entry = doc.Recents.FirstOrDefault(r => r.Id == linkId);
            if (entry == null)
            {
                entry = new RecentEntry { Id = linkId, Count = 0 };
                doc.Recents.Add(entry);
            }
            entry.Count++;
            entry.LastUsed = DateTime.UtcNow.ToString("o");

            doc.Recents = doc.Recents
                .OrderByDescending(r => r.LastUsed, StringComparer.Ordinal)
                .Take(MaxRecents)
                .ToList();
        }

        /// <summary>Recursive id lookup. Null when unresolvable (dropped silently per spec section 6).</summary>
        public static LibraryNode FindById(IEnumerable<LibraryNode> roots, string id)
        {
            if (roots == null || string.IsNullOrEmpty(id))
                return null;

            foreach (LibraryNode node in roots)
            {
                if (node.Id == id)
                    return node;
                if (node.Children != null)
                {
                    LibraryNode hit = FindById(node.Children, id);
                    if (hit != null)
                        return hit;
                }
            }
            return null;
        }

        /// <summary>Recursively removes the node with the given id. True if something was removed.</summary>
        public static bool RemoveById(List<LibraryNode> nodes, string id)
        {
            if (nodes == null)
                return false;

            int removed = nodes.RemoveAll(n => n.Id == id);
            if (removed > 0)
                return true;

            foreach (LibraryNode node in nodes)
                if (node.Children != null && RemoveById(node.Children, id))
                    return true;

            return false;
        }

        private UserLibraryDocument NewDocument()
        {
            return new UserLibraryDocument
            {
                User = $"{Environment.UserDomainName}\\{Environment.UserName}"
            };
        }

        /// <summary>Source is runtime-only (spec section 6) — stamp user nodes at load.</summary>
        private static void MarkAsUserSource(IEnumerable<LibraryNode> nodes)
        {
            foreach (LibraryNode node in nodes)
            {
                node.Source = "user";
                if (node.Children != null)
                    MarkAsUserSource(node.Children);
            }
        }
    }
}
