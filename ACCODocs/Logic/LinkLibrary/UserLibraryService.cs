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
        /// <summary>Fallback stored-recents cap; the real value comes from config (maxRecentsStored).</summary>
        public const int DefaultMaxRecents = 20;

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

                UserLibraryDocument doc = Parse(File.ReadAllText(path));
                if (doc == null)
                    throw new InvalidDataException("User file did not parse to a document.");
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

        /// <summary>
        /// Parses and normalizes a user-library document from JSON text (the user file,
        /// or an exported/shared copy on import). Null when unparseable — never throws.
        /// </summary>
        public static UserLibraryDocument Parse(string json)
        {
            try
            {
                var doc = JsonConvert.DeserializeObject<UserLibraryDocument>(json);
                if (doc == null)
                    return null;

                doc.Favorites = doc.Favorites ?? new List<string>();
                doc.Recents = doc.Recents ?? new List<RecentEntry>();
                doc.Groups = doc.Groups ?? new List<LibraryNode>();
                MarkAsUserSource(doc.Groups);
                return doc;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LinkLibrary] User-library parse failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Writes an export copy (backup / sharing). The export IS a valid user file —
        /// same schema — so restoring by hand is just copying it over the user file.
        /// Throws on failure; the caller reports it in the status line.
        /// </summary>
        public void Export(UserLibraryDocument doc, string path)
        {
            File.WriteAllText(path, JsonConvert.SerializeObject(doc, Formatting.Indented));
        }

        public class ImportCounts
        {
            public int FavoritesAdded { get; set; }
            public int RecentsAdded { get; set; }
            public int LinksAdded { get; set; }
            public int Total => FavoritesAdded + RecentsAdded + LinksAdded;
        }

        /// <summary>
        /// "Merge new only" import: adds favorites/recents/links from <paramref name="imported"/>
        /// that the current document doesn't already have (matched by id). Existing entries are
        /// never modified. Same-id groups merge recursively.
        /// </summary>
        public static ImportCounts MergeInto(UserLibraryDocument current, UserLibraryDocument imported, int maxRecents = DefaultMaxRecents)
        {
            var counts = new ImportCounts();

            foreach (string id in imported.Favorites)
            {
                if (!string.IsNullOrEmpty(id) && !current.Favorites.Contains(id))
                {
                    current.Favorites.Add(id);
                    counts.FavoritesAdded++;
                }
            }

            foreach (RecentEntry entry in imported.Recents)
            {
                if (!string.IsNullOrEmpty(entry?.Id) && current.Recents.All(r => r.Id != entry.Id))
                {
                    current.Recents.Add(entry);
                    counts.RecentsAdded++;
                }
            }
            current.Recents = current.Recents
                .OrderByDescending(r => r.LastUsed, StringComparer.Ordinal)
                .Take(Math.Max(0, maxRecents))
                .ToList();

            counts.LinksAdded = MergeNodes(current.Groups, imported.Groups, current.Groups);
            return counts;
        }

        /// <summary>
        /// Recursive by-id node merge. A link whose id exists ANYWHERE in the current tree is
        /// skipped (it may have been moved to a different group); a same-id group at the same
        /// level merges its children; anything else is added whole. Returns links added.
        /// </summary>
        private static int MergeNodes(List<LibraryNode> currentLevel, List<LibraryNode> importedLevel, List<LibraryNode> currentRoots)
        {
            int added = 0;
            foreach (LibraryNode importedNode in importedLevel)
            {
                if (string.IsNullOrEmpty(importedNode?.Id))
                    continue;

                LibraryNode existingHere = currentLevel.FirstOrDefault(n => n.Id == importedNode.Id);

                if (importedNode.IsGroup)
                {
                    if (existingHere != null && existingHere.IsGroup)
                    {
                        added += MergeNodes(existingHere.Children, importedNode.Children, currentRoots);
                    }
                    else if (FindById(currentRoots, importedNode.Id) == null)
                    {
                        currentLevel.Add(importedNode);
                        added += CountLinks(importedNode.Children);
                    }
                }
                else if (FindById(currentRoots, importedNode.Id) == null)
                {
                    currentLevel.Add(importedNode);
                    added++;
                }
            }
            return added;
        }

        private static int CountLinks(IEnumerable<LibraryNode> nodes)
        {
            int count = 0;
            foreach (LibraryNode node in nodes ?? Enumerable.Empty<LibraryNode>())
                count += node.IsGroup ? CountLinks(node.Children) : 1;
            return count;
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

        /// <summary>Bumps (or creates) the recent entry for a link id and trims to the configured cap.</summary>
        public static void RecordRecent(UserLibraryDocument doc, string linkId, int maxRecents = DefaultMaxRecents)
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
                .Take(Math.Max(0, maxRecents))
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
