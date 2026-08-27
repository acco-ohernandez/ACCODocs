using System.Collections.Generic;
using Newtonsoft.Json;

namespace ACCODocs.Logic.LinkLibrary
{
    /// <summary>
    /// The master library document (spec section 5). The same node shape is reused by
    /// the user library (spec section 6) — one tree renderer, one click handler.
    /// </summary>
    public class LinkLibraryDocument
    {
        public int SchemaVersion { get; set; } = 1;

        /// <summary>Version authority. Revision compare only — never file timestamps (spec section 5).</summary>
        public int Revision { get; set; }

        public string RevisionDate { get; set; }

        /// <summary>Controlled tag lists keyed by facet (discipline / category / topic).</summary>
        public Dictionary<string, List<string>> TagVocabulary { get; set; }

        public List<LibraryNode> Groups { get; set; } = new List<LibraryNode>();
    }

    /// <summary>
    /// A node is a group if it has children, a link if it has kind + target (spec section 5).
    /// </summary>
    public class LibraryNode
    {
        /// <summary>Stable, permanent. Master ids are dotted (acco.*), user ids are GUIDs.</summary>
        public string Id { get; set; }

        public string Title { get; set; }
        public string Description { get; set; }

        /// <summary>url | file | folder | mailto | video | command. Null/empty on groups.</summary>
        public string Kind { get; set; }

        public string Target { get; set; }
        public List<string> Tags { get; set; }

        /// <summary>Null/omitted = visible in all Revit versions (spec section 5).</summary>
        public List<int> RevitVersions { get; set; }

        public string Added { get; set; }
        public string Updated { get; set; }
        public string Owner { get; set; }

        public List<LibraryNode> Children { get; set; }

        [JsonIgnore]
        public bool IsGroup => Children != null;

        [JsonIgnore]
        public bool IsLink => !string.IsNullOrEmpty(Kind) && !string.IsNullOrEmpty(Target);

        /// <summary>Runtime only: "master" or "user" (spec section 6 — never stored in the file).</summary>
        [JsonIgnore]
        public string Source { get; set; } = "master";

        /// <summary>Runtime only: added/updated within newBadgeDays (spec section 7). Set at render time.</summary>
        [JsonIgnore]
        public bool IsNew { get; set; }

        /// <summary>
        /// Runtime only: tree-expansion state, bound TwoWay by the TreeViews' item container
        /// style. Lives on the node so re-rendering (every link open updates Recents) doesn't
        /// collapse the tree — containers are recreated but read this back.
        /// </summary>
        [JsonIgnore]
        public bool IsExpanded { get; set; }

        public bool IsVisibleFor(int revitVersion)
        {
            return RevitVersions == null || RevitVersions.Count == 0 || RevitVersions.Contains(revitVersion);
        }
    }
}
