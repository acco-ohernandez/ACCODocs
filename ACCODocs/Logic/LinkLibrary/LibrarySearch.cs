using System.Collections.Generic;
using System.Linq;

namespace ACCODocs.Logic.LinkLibrary
{
    /// <summary>
    /// Search / flatten (spec section 7, Phase 3): typing flattens the tree into ranked
    /// results matching title, description, and tags. Pure logic — no UI, no Revit API.
    /// Matches are SCORED, not hard-filtered; the same ranking approach carries into the
    /// Pick Element flow (spec section 8, Phase 5).
    /// </summary>
    public static class LibrarySearch
    {
        public class SearchEntry
        {
            // Properties, NOT fields — WPF data templates bind to these, and bindings
            // silently ignore public fields (rows render empty with no error).
            public LibraryNode Node { get; set; }
            /// <summary>Group breadcrumb, e.g. "ACCO Standards > Piping".</summary>
            public string Path { get; set; }

            // Lowercase copies so every keystroke doesn't re-lowercase the whole library.
            internal string TitleLower;
            internal string DescriptionLower;
            internal string PathLower;
            internal string TargetLower;   // searchable so "accoes.com" finds links by domain
            internal List<string> TagsLower;
        }

        /// <summary>Builds one searchable entry from a node outside a tree walk (favorites/recents lists).</summary>
        public static SearchEntry MakeEntry(LibraryNode node, string path)
        {
            return new SearchEntry
            {
                Node = node,
                Path = path ?? "",
                TitleLower = (node.Title ?? "").ToLowerInvariant(),
                DescriptionLower = (node.Description ?? "").ToLowerInvariant(),
                PathLower = (path ?? "").ToLowerInvariant(),
                TargetLower = (node.Target ?? "").ToLowerInvariant(),
                TagsLower = (node.Tags ?? new List<string>()).Select(t => t.ToLowerInvariant()).ToList()
            };
        }

        /// <summary>Flattens a (version-filtered) tree into searchable link entries.</summary>
        public static List<SearchEntry> Flatten(IEnumerable<LibraryNode> roots)
        {
            var entries = new List<SearchEntry>();
            if (roots != null)
                FlattenInto(roots, "", entries);
            return entries;
        }

        private static void FlattenInto(IEnumerable<LibraryNode> nodes, string path, List<SearchEntry> entries)
        {
            foreach (LibraryNode node in nodes)
            {
                if (node.IsGroup)
                {
                    string childPath = path.Length == 0 ? node.Title : $"{path} > {node.Title}";
                    FlattenInto(node.Children, childPath, entries);
                }
                else if (node.IsLink)
                {
                    entries.Add(MakeEntry(node, path));
                }
            }
        }

        /// <summary>
        /// Ranks entries against a query. Terms are ANDed: every term must match somewhere
        /// on an entry; scores sum across terms. Title beats tags beats description beats path.
        /// </summary>
        public static List<SearchEntry> Rank(IEnumerable<SearchEntry> entries, string query)
        {
            string[] terms = (query ?? "")
                .ToLowerInvariant()
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (terms.Length == 0)
                return new List<SearchEntry>();

            return entries
                .Select(entry => new { entry, score = Score(entry, terms) })
                .Where(x => x.score > 0)
                .OrderByDescending(x => x.score)
                .ThenBy(x => x.entry.TitleLower)
                .Select(x => x.entry)
                .ToList();
        }

        /// <summary>
        /// Ranks entries against tags extracted from a picked element (spec section 8,
        /// step 5): SCORE matches, do not hard filter — a link matching category + system
        /// type outranks one matching category only.
        /// </summary>
        public static List<SearchEntry> RankByTags(IEnumerable<SearchEntry> entries, IList<string> elementTags)
        {
            var tagsLower = (elementTags ?? new List<string>())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.ToLowerInvariant())
                .ToList();

            if (tagsLower.Count == 0)
                return new List<SearchEntry>();

            return entries
                .Select(entry => new { entry, score = ScoreTags(entry, tagsLower) })
                .Where(x => x.score > 0)
                .OrderByDescending(x => x.score)
                .ThenBy(x => x.entry.TitleLower)
                .Select(x => x.entry)
                .ToList();
        }

        private static int ScoreTags(SearchEntry entry, List<string> elementTagsLower)
        {
            int total = 0;
            foreach (string tag in elementTagsLower)
            {
                if (entry.TagsLower.Contains(tag))
                    total += 100;                                   // exact controlled-tag match
                else if (entry.TagsLower.Any(t => t.Contains(tag) || tag.Contains(t)))
                    total += 50;                                    // partial tag overlap

                if (entry.TitleLower.Contains(tag))
                    total += 30;
                if (entry.DescriptionLower.Contains(tag))
                    total += 15;
            }
            return total;
        }

        private static int Score(SearchEntry entry, string[] terms)
        {
            int total = 0;
            foreach (string term in terms)
            {
                int termScore = 0;

                if (entry.TitleLower.Contains(term))
                    termScore += entry.TitleLower.StartsWith(term) ? 150 : 100;

                if (entry.TagsLower.Any(tag => tag.Contains(term)))
                    termScore += 40;

                if (entry.DescriptionLower.Contains(term))
                    termScore += 20;

                // URL/target match — lets "accoes.com" find links by domain.
                if (entry.TargetLower.Contains(term))
                    termScore += 25;

                if (entry.PathLower.Contains(term))
                    termScore += 10;

                if (termScore == 0)
                    return 0;   // AND semantics: a term that matches nothing kills the entry

                total += termScore;
            }
            return total;
        }
    }
}
