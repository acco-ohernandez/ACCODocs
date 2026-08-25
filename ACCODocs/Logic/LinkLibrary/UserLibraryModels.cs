using System.Collections.Generic;

namespace ACCODocs.Logic.LinkLibrary
{
    /// <summary>
    /// Per-user library file (spec section 6): one file per user at
    /// &lt;userLibraryFolder&gt;\LinkLibrary.user.json. Favorites and recents store IDS ONLY,
    /// never copies — ids resolve at load time so a master URL update reaches everyone.
    /// User links reuse <see cref="LibraryNode"/> verbatim: one tree renderer, one click
    /// handler, and promoting a good user link into the master is a copy/paste.
    /// </summary>
    public class UserLibraryDocument
    {
        public int SchemaVersion { get; set; } = 1;

        /// <summary>domain\username, informational.</summary>
        public string User { get; set; }

        /// <summary>ISO 8601 UTC, stamped on every save.</summary>
        public string LastModified { get; set; }

        /// <summary>Ids only — master dotted ids or user GUIDs.</summary>
        public List<string> Favorites { get; set; } = new List<string>();

        public List<RecentEntry> Recents { get; set; } = new List<RecentEntry>();

        /// <summary>
        /// User-created content. Rendered under a single fixed "My Links" root node —
        /// users never create top-level groups that mirror master category names.
        /// User-created node ids are GUIDs so they can never collide with master ids.
        /// </summary>
        public List<LibraryNode> Groups { get; set; } = new List<LibraryNode>();
    }

    public class RecentEntry
    {
        public string Id { get; set; }
        /// <summary>ISO 8601 UTC.</summary>
        public string LastUsed { get; set; }
        public int Count { get; set; }
    }
}
