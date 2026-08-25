using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ACCODocs.Common;
using ACCODocs.Logic.LinkLibrary;
// The template enables WinForms alongside WPF — disambiguate the shared control names.
using UserControl = System.Windows.Controls.UserControl;
using ListBox = System.Windows.Controls.ListBox;
using MenuItem = System.Windows.Controls.MenuItem;
using ContextMenu = System.Windows.Controls.ContextMenu;
using Clipboard = System.Windows.Clipboard;

namespace ACCODocs.Forms
{
    /// <summary>
    /// The Link Library dockable pane content. Session singleton: created once by
    /// LinkLibraryPaneRegistrar before registration and alive for the whole Revit
    /// session (spec section 3). Never recreated on button press.
    ///
    /// The pane is modeless — every Revit API touch goes through the ExternalEvent
    /// handed in by the registrar, never directly from a WPF event handler.
    /// (Opening URLs/files is shell work, not Revit API, so it stays on the UI thread.)
    /// </summary>
    public partial class LinkLibrary_Pane : UserControl, IDockablePaneProvider
    {
        private readonly ExternalEvent _externalEvent;
        private readonly ModelessExternalEventHandler _handler;
        private readonly int _revitVersion;

        private LinkLibraryConfig _config;
        private MasterLibraryService _service;
        private UserLibraryService _userService;
        private LinkLibraryDocument _document;      // master content currently rendered
        private UserLibraryDocument _userLibrary;   // per-user favorites/recents/links
        private List<LibraryNode> _filteredRoots;   // version-filtered master tree
        private List<LibrarySearch.SearchEntry> _searchIndex;
        private TelemetryLogger _telemetry;
        private List<LibrarySearch.SearchEntry> _myLinksIndex;      // favorites + recents + user links
        private List<LibrarySearch.SearchEntry> _userLinkEntries;   // user links only — merged into Library search
        private ResultsMode _resultsMode = ResultsMode.None;
        private List<string> _lastPickTags;         // for "Suggest a link for this"
        private bool _refreshRunning;

        /// <summary>What the flat results list currently shows — decides the telemetry src.</summary>
        private enum ResultsMode { None, Search, Pick, DeepLink }
        private DateTime _lastCheckUtc = DateTime.MinValue;
        private DispatcherTimer _refreshTimer;

        public LinkLibrary_Pane(ExternalEvent externalEvent, ModelessExternalEventHandler handler, int revitVersion)
        {
            InitializeComponent();
            _externalEvent = externalEvent;
            _handler = handler;
            _revitVersion = revitVersion;

            ReloadConfig();

            // Never block pane load on the user file (spec section 6) — Load() never throws.
            _userLibrary = _userService.Load();

            // Spec section 10: cache renders immediately — the pane opens instantly, always —
            // then the master is checked asynchronously by revision.
            _document = _service.LoadCached();
            RenderTree();
            RenderMyLinks();
            RefreshFromMasterAsync();

            // Re-check every refreshCheckMinutes and whenever the pane becomes visible.
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(Math.Max(1, _config.RefreshCheckMinutes))
            };
            _refreshTimer.Tick += (s, e) => RefreshFromMasterAsync();
            _refreshTimer.Start();
            IsVisibleChanged += OnIsVisibleChanged;

            // Keyboard focus inside a Revit dockable pane is finicky: a click can select
            // the WPF control without moving Win32 keyboard focus, so keystrokes stay
            // with Revit (and can fire single-key shortcuts). Force keyboard focus on
            // click, and log focus/typing so a broken input path is visible in Output.
            HookSearchBox(TxtSearch);
            HookSearchBox(TxtSearchMy);
        }

        private static void HookSearchBox(System.Windows.Controls.TextBox box)
        {
            box.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (!box.IsKeyboardFocusWithin)
                {
                    e.Handled = true;   // swallow this click; it only claims focus
                    box.Focus();
                    System.Windows.Input.Keyboard.Focus(box);
                }
            };
            box.GotKeyboardFocus += (s, e) =>
                Debug.WriteLine($"[LinkLibrary] {box.Name} got keyboard focus");
        }

        public LinkLibraryConfig Config => _config;

        /// <summary>
        /// Loads config per the spec section 4 probe order. Never throws, never dialogs —
        /// worst case the pane runs on built-in defaults.
        /// </summary>
        public void ReloadConfig()
        {
            _config = LinkLibraryConfig.Load();
            _service = new MasterLibraryService(_config);
            _userService = new UserLibraryService(_config);
            _telemetry = new TelemetryLogger(_config, _revitVersion);
            TxtConfigStatus.Text = $"Config: {_config.Source}" +
                (_config.SourcePath.Length > 0 ? $"\n{_config.SourcePath}" : "") +
                (_config.ExpandedMasterLibraryPath.Length > 0 ? $"\nMaster: {_config.ExpandedMasterLibraryPath}" : "\nMaster: (not set)");
        }

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = this;
            // Initial state: tabbed with the Project Browser (spec section 3).
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Tabbed,
                TabBehind = DockablePanes.BuiltInDockablePanes.ProjectBrowser
            };
        }

        // ---------------------------------------------------------------------
        // Library load / refresh (Phase 2)
        // ---------------------------------------------------------------------

        private void RenderTree()
        {
            if (_document == null)
            {
                _filteredRoots = null;
                _searchIndex = null;
                TreeLibrary.ItemsSource = null;
                TxtLibraryStatus.Text = "No library available yet (no cache, master unreachable).";
                return;
            }

            // Filter to the running Revit version (spec section 5), then rebuild the
            // flattened search index (Phase 3) from the same filtered tree.
            _filteredRoots = MasterLibraryService.FilterForRevitVersion(_document.Groups, _revitVersion);
            MasterLibraryService.MarkNewBadges(_filteredRoots, _config.NewBadgeDays);
            _searchIndex = LibrarySearch.Flatten(_filteredRoots);
            TreeLibrary.ItemsSource = _filteredRoots;

            // Phase 7: background dead-link validation — flags failures to us, never the user.
            DeadLinkChecker.RunOnce(_config, _searchIndex);

            // If a search is active, its results may have changed with the new revision.
            if (TxtSearch.Text.Length > 0)
                UpdateSearchResults();
        }

        private void SetStatus(MasterLibraryService.RefreshStatus status)
        {
            if (_document == null)
            {
                TxtLibraryStatus.Text = "No library available (no cache, master unreachable).";
                return;
            }

            switch (status)
            {
                case MasterLibraryService.RefreshStatus.Offline:
                    // Subtle indicator, no dialog (spec section 10).
                    TxtLibraryStatus.Text = $"Offline — showing cached copy (rev {_document.Revision}).";
                    break;
                case MasterLibraryService.RefreshStatus.Updated:
                    TxtLibraryStatus.Text = $"Updated to rev {_document.Revision}.";
                    break;
                default:
                    TxtLibraryStatus.Text = $"Rev {_document.Revision} — up to date.";
                    break;
            }
        }

        /// <summary>
        /// Async master check (spec section 10): compare revision, copy down when newer,
        /// refresh the tree in place. File IO runs off the UI thread.
        /// </summary>
        private async void RefreshFromMasterAsync()
        {
            if (_refreshRunning)
                return;
            _refreshRunning = true;
            _lastCheckUtc = DateTime.UtcNow;

            try
            {
                int? currentRevision = _document?.Revision;
                MasterLibraryService.RefreshResult result =
                    await Task.Run(() => _service.CheckForUpdate(currentRevision));

                // The first refresh is kicked from the ctor during OnStartup, where there is
                // no WPF SynchronizationContext yet — this await can resume on a thread-pool
                // thread. All UI work must therefore be marshaled explicitly.
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (result.Status == MasterLibraryService.RefreshStatus.Updated)
                    {
                        _document = result.Document;
                        RenderTree();
                        RenderMyLinks();   // favorites/recents resolve against the new master
                    }
                    else if (_document != null && TreeLibrary.ItemsSource == null)
                    {
                        // A previous render was lost (e.g. startup-time refresh) — recover.
                        RenderTree();
                        RenderMyLinks();
                    }
                    SetStatus(result.Status);
                }));
            }
            catch (Exception ex)
            {
                // Never let a refresh failure surface as a dialog.
                Debug.WriteLine($"[LinkLibrary] Refresh failed: {ex}");
            }
            finally
            {
                _refreshRunning = false;
            }
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Re-check on pane open (spec section 10), throttled so toggling the pane
            // repeatedly doesn't hammer the share.
            if (IsVisible && (DateTime.UtcNow - _lastCheckUtc) > TimeSpan.FromSeconds(30))
                RefreshFromMasterAsync();
        }

        // ---------------------------------------------------------------------
        // Search (Phase 3, spec section 7): typing flattens the tree into ranked
        // results; clearing the box restores the tree.
        // ---------------------------------------------------------------------

        private void TxtSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            UpdateSearchResults();
        }

        private void UpdateSearchResults()
        {
            string query = TxtSearch.Text.Trim();
            PanelPickInfo.Visibility = System.Windows.Visibility.Collapsed;   // typing exits pick/deep-link mode

            if (query.Length == 0)
            {
                _resultsMode = ResultsMode.None;
                ListResults.Visibility = System.Windows.Visibility.Collapsed;
                TreeLibrary.Visibility = System.Windows.Visibility.Visible;
                ListResults.ItemsSource = null;
                return;
            }

            // Library search spans everything openable: the master tree AND the user's own
            // links ("My Links > ..." breadcrumbs) — a searcher shouldn't have to remember
            // which tab a link lives in. My Links search stays scoped to personal items.
            var universe = (_searchIndex ?? new List<LibrarySearch.SearchEntry>())
                .Concat(_userLinkEntries ?? new List<LibrarySearch.SearchEntry>())
                .ToList();
            var results = LibrarySearch.Rank(universe, query);
            Debug.WriteLine($"[LinkLibrary] Library search '{query}' -> {results.Count} of {universe.Count} indexed links");

            _resultsMode = ResultsMode.Search;
            ListResults.ItemsSource = results;
            ListResults.Visibility = System.Windows.Visibility.Visible;
            TreeLibrary.Visibility = System.Windows.Visibility.Collapsed;
        }

        // ---------------------------------------------------------------------
        // Pick Element (Phase 5, spec section 8). Deliberately on-demand only —
        // no Idling polling, no SelectionChanged subscription. The WPF click handler
        // must not touch the Revit API: it raises the ExternalEvent; the handler uses
        // the existing selection, falls back to PickObject only when it is empty,
        // extracts tags, and marshals the ranked results back to the UI thread.
        // ---------------------------------------------------------------------

        private void BtnPickElement_Click(object sender, RoutedEventArgs e)
        {
            _handler.HandlerAction = (app) =>
            {
                List<string> tags = null;
                bool cancelled = false;
                try
                {
                    UIDocument uidoc = app.ActiveUIDocument;
                    if (uidoc == null)
                        return;

                    Document doc = uidoc.Document;
                    var ids = uidoc.Selection.GetElementIds();
                    List<Element> elements;

                    if (ids.Count > 0)
                    {
                        // Never force a pick when they already selected the thing (spec section 8).
                        elements = ids.Select(id => doc.GetElement(id)).ToList();
                    }
                    else
                    {
                        try
                        {
                            var reference = uidoc.Selection.PickObject(
                                Autodesk.Revit.UI.Selection.ObjectType.Element,
                                "Select an element to find related links");
                            elements = new List<Element> { doc.GetElement(reference) };
                        }
                        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                        {
                            cancelled = true;
                            elements = new List<Element>();
                        }
                    }

                    if (!cancelled)
                        tags = ElementTagExtractor.ExtractTags(doc, elements);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LinkLibrary] Pick Element handler failed: {ex}");
                }

                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!cancelled && tags != null)
                        ShowPickResults(tags);
                }));
            };

            _externalEvent.Raise();
        }

        private void ShowPickResults(List<string> tags)
        {
            _lastPickTags = tags;

            // Clearing the search box flips the view back to the tree (TextChanged fires
            // synchronously), then pick mode takes over the results list.
            TxtSearch.Text = "";

            var results = LibrarySearch.RankByTags(
                _searchIndex ?? new List<LibrarySearch.SearchEntry>(), tags);

            _resultsMode = ResultsMode.Pick;
            PanelPickInfo.Visibility = System.Windows.Visibility.Visible;

            if (results.Count > 0)
            {
                TxtPickTags.Text = $"Links matching the selected element ({results.Count}). " +
                                   $"Tags: {string.Join(", ", tags)}";
                BtnPickSuggest.Visibility = System.Windows.Visibility.Collapsed;
                ListResults.ItemsSource = results;
                ListResults.Visibility = System.Windows.Visibility.Visible;
                TreeLibrary.Visibility = System.Windows.Visibility.Collapsed;
            }
            else
            {
                // Zero matches: never an empty pane — show what WAS extracted plus a
                // suggest action. Every dead end becomes a data point (spec section 8).
                TxtPickTags.Text = tags.Count > 0
                    ? $"No links match this element yet. Tags found: {string.Join(", ", tags)}"
                    : "No tags could be extracted from the selection.";
                BtnPickSuggest.Visibility = System.Windows.Visibility.Visible;
                ListResults.ItemsSource = null;
                ListResults.Visibility = System.Windows.Visibility.Collapsed;
                TreeLibrary.Visibility = System.Windows.Visibility.Visible;
            }
        }

        private void BtnPickClear_Click(object sender, RoutedEventArgs e)
        {
            _resultsMode = ResultsMode.None;
            _lastPickTags = null;
            PanelPickInfo.Visibility = System.Windows.Visibility.Collapsed;
            ListResults.ItemsSource = null;
            ListResults.Visibility = System.Windows.Visibility.Collapsed;
            TreeLibrary.Visibility = System.Windows.Visibility.Visible;
        }

        private void BtnPickSuggest_Click(object sender, RoutedEventArgs e)
        {
            string tagContext = _lastPickTags != null && _lastPickTags.Count > 0
                ? string.Join(", ", _lastPickTags)
                : "(none)";
            SuggestLink($"Element tags with no matching links: {tagContext}");
        }

        // ---------------------------------------------------------------------
        // My Links tab (Phase 4, spec section 6): favorites and recents store ids
        // only and resolve at render time; unresolvable ids are skipped silently.
        // ---------------------------------------------------------------------

        private void RenderMyLinks()
        {
            // Resolution universe: version-filtered master tree + the user's own links.
            var masterRoots = _filteredRoots ?? new List<LibraryNode>();
            var userRoots = _userLibrary.Groups ?? new List<LibraryNode>();

            LibraryNode Resolve(string id) =>
                UserLibraryService.FindById(masterRoots, id) ?? UserLibraryService.FindById(userRoots, id);

            ListFavorites.ItemsSource = _userLibrary.Favorites
                .Select(Resolve)
                .Where(n => n != null)
                .ToList();

            ListRecents.ItemsSource = _userLibrary.Recents
                .OrderByDescending(r => r.LastUsed, StringComparer.Ordinal)
                .Select(r => Resolve(r.Id))
                .Where(n => n != null)
                .Take(10)
                .ToList();

            // Personal content lives under ONE fixed root node (spec section 6) so nobody
            // confuses a personal "Standards" group with the authoritative master one.
            var root = new LibraryNode
            {
                Id = "user.root",
                Title = "My Links",
                Source = "user",
                Children = userRoots
            };
            TreeMyLinks.ItemsSource = new List<LibraryNode> { root };

            // Search index for this tab: favorites + recents + user links, deduped by id.
            var index = new List<LibrarySearch.SearchEntry>();
            var seen = new System.Collections.Generic.HashSet<string>();
            void AddEntries(IEnumerable<LibrarySearch.SearchEntry> entries)
            {
                foreach (var entry in entries)
                    if (entry.Node.Id != null && seen.Add(entry.Node.Id))
                        index.Add(entry);
            }
            AddEntries(((IEnumerable<LibraryNode>)ListFavorites.ItemsSource)
                .Select(n => LibrarySearch.MakeEntry(n, "Favorites")));
            AddEntries(((IEnumerable<LibraryNode>)ListRecents.ItemsSource)
                .Select(n => LibrarySearch.MakeEntry(n, "Recents")));
            _userLinkEntries = LibrarySearch.Flatten(new List<LibraryNode> { root });
            AddEntries(_userLinkEntries);
            _myLinksIndex = index;

            // A search may be active on either tab while favorites/recents/user links
            // change underneath it.
            if (TxtSearchMy.Text.Length > 0)
                UpdateMyLinksSearch();
            if (TxtSearch.Text.Length > 0)
                UpdateSearchResults();
        }

        private void TxtSearchMy_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            UpdateMyLinksSearch();
        }

        private void UpdateMyLinksSearch()
        {
            string query = TxtSearchMy.Text.Trim();

            if (query.Length == 0)
            {
                ListMyResults.Visibility = System.Windows.Visibility.Collapsed;
                PanelMyBrowse.Visibility = System.Windows.Visibility.Visible;
                ListMyResults.ItemsSource = null;
                return;
            }

            var results = LibrarySearch.Rank(
                _myLinksIndex ?? new List<LibrarySearch.SearchEntry>(), query);
            Debug.WriteLine($"[LinkLibrary] My Links search '{query}' -> {results.Count} of {_myLinksIndex?.Count ?? 0} indexed links");

            ListMyResults.ItemsSource = results;
            ListMyResults.Visibility = System.Windows.Visibility.Visible;
            PanelMyBrowse.Visibility = System.Windows.Visibility.Collapsed;
        }

        private void ListMyResults_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var entry = ListMyResults.SelectedItem as LibrarySearch.SearchEntry;
            if (entry?.Node != null)
                OpenLink(entry.Node, "search");
        }

        private void SaveUserLibrary()
        {
            _userService.Save(_userLibrary);
        }

        private void AddToFavorites(LibraryNode node)
        {
            if (node == null || !node.IsLink || _userLibrary.Favorites.Contains(node.Id))
                return;
            _userLibrary.Favorites.Add(node.Id);
            SaveUserLibrary();
            RenderMyLinks();
        }

        private void BtnAddLink_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AddUserLinkWindow { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() != true)
                return;

            // User-created ids are GUIDs — they can never collide with dotted master ids
            // and must never shadow one (spec section 6).
            _userLibrary.Groups.Add(new LibraryNode
            {
                Id = Guid.NewGuid().ToString(),
                Title = dialog.LinkTitle,
                Kind = "url",
                Target = dialog.LinkUrl,
                Added = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                Source = "user"
            });
            SaveUserLibrary();
            RenderMyLinks();
        }

        // ---------------------------------------------------------------------
        // Context menu + double-click plumbing
        // ---------------------------------------------------------------------

        /// <summary>Resolves which node a context-menu click refers to, whichever control raised it.</summary>
        private LibraryNode NodeFromContextMenu(object sender)
        {
            var menuItem = sender as MenuItem;
            var menu = menuItem?.Parent as ContextMenu;
            var target = menu?.PlacementTarget;

            if (ReferenceEquals(target, TreeLibrary)) return TreeLibrary.SelectedItem as LibraryNode;
            if (ReferenceEquals(target, ListResults)) return (ListResults.SelectedItem as LibrarySearch.SearchEntry)?.Node;
            if (ReferenceEquals(target, ListMyResults)) return (ListMyResults.SelectedItem as LibrarySearch.SearchEntry)?.Node;
            if (ReferenceEquals(target, ListFavorites)) return ListFavorites.SelectedItem as LibraryNode;
            if (ReferenceEquals(target, ListRecents)) return ListRecents.SelectedItem as LibraryNode;
            if (ReferenceEquals(target, TreeMyLinks)) return TreeMyLinks.SelectedItem as LibraryNode;
            return null;
        }

        /// <summary>Telemetry src for whatever the flat results list currently shows.</summary>
        private string ResultsSrc()
        {
            switch (_resultsMode)
            {
                case ResultsMode.Pick: return "pickElement";
                case ResultsMode.DeepLink: return "deepLink";
                default: return "search";
            }
        }

        private void CtxOpen_Click(object sender, RoutedEventArgs e)
        {
            var node = NodeFromContextMenu(sender);
            if (node == null || !node.IsLink)
                return;

            var menu = (sender as MenuItem)?.Parent as ContextMenu;
            var target = menu?.PlacementTarget;
            string src = ReferenceEquals(target, ListResults) ? ResultsSrc()
                       : ReferenceEquals(target, ListFavorites) ? "favorite"
                       : ReferenceEquals(target, ListRecents) ? "recent"
                       : "tree";
            OpenLink(node, src);
        }

        private void CtxCopy_Click(object sender, RoutedEventArgs e)
        {
            var node = NodeFromContextMenu(sender);
            if (node == null || !node.IsLink)
                return;
            try
            {
                Clipboard.SetText(node.Target);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LinkLibrary] Clipboard failed: {ex.Message}");
            }
        }

        private void CtxAddFavorite_Click(object sender, RoutedEventArgs e)
        {
            AddToFavorites(NodeFromContextMenu(sender));
        }

        private void CtxRemoveFavorite_Click(object sender, RoutedEventArgs e)
        {
            var node = NodeFromContextMenu(sender);
            if (node == null)
                return;
            _userLibrary.Favorites.Remove(node.Id);
            SaveUserLibrary();
            RenderMyLinks();
        }

        private void CtxDeleteUserLink_Click(object sender, RoutedEventArgs e)
        {
            var node = NodeFromContextMenu(sender);
            if (node == null || node.Id == "user.root" || node.Source != "user")
                return;

            UserLibraryService.RemoveById(_userLibrary.Groups, node.Id);
            _userLibrary.Favorites.Remove(node.Id);   // a deleted link can't stay a favorite
            SaveUserLibrary();
            RenderMyLinks();
        }

        private void TreeLibrary_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var node = TreeLibrary.SelectedItem as LibraryNode;
            if (node != null && node.IsLink)
                OpenLink(node, "tree");
        }

        private void TreeMyLinks_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var node = TreeMyLinks.SelectedItem as LibraryNode;
            if (node != null && node.IsLink)
                OpenLink(node, "tree");
        }

        private void ListResults_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var entry = ListResults.SelectedItem as LibrarySearch.SearchEntry;
            if (entry?.Node != null)
                OpenLink(entry.Node, ResultsSrc());
        }

        /// <summary>Shared double-click for the Favorites and Recents lists.</summary>
        private void NodeList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var node = (sender as ListBox)?.SelectedItem as LibraryNode;
            if (node != null && node.IsLink)
                OpenLink(node, ReferenceEquals(sender, ListFavorites) ? "favorite" : "recent");
        }

        // ---------------------------------------------------------------------
        // Open a link. url/video/mailto/file/folder all shell-open; 'command' fires
        // an existing ribbon command via the ExternalEvent (Phase 7, spec section 5).
        // ---------------------------------------------------------------------

        private void OpenLink(LibraryNode node, string src)
        {
            try
            {
                if (string.Equals(node.Kind, "command", StringComparison.OrdinalIgnoreCase))
                {
                    PostRevitCommand(node);
                }
                else
                {
                    Debug.WriteLine($"[LinkLibrary] Opening {node.Kind} '{node.Id}': {node.Target}");
                    Process.Start(new ProcessStartInfo(node.Target) { UseShellExecute = true });
                }

                // Every open becomes a recent (spec section 6) and a telemetry line (section 9).
                UserLibraryService.RecordRecent(_userLibrary, node.Id);
                SaveUserLibrary();
                RenderMyLinks();
                _telemetry.Log(node.Id, src);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LinkLibrary] Open failed for '{node.Id}': {ex.Message}");
                TxtLibraryStatus.Text = $"Could not open \"{node.Title}\" — {ex.Message}";
            }
        }

        /// <summary>
        /// 'command' kind: target is a Revit command id. PostCommand needs a UIApplication,
        /// so it goes through the ExternalEvent like every other API touch.
        /// </summary>
        private void PostRevitCommand(LibraryNode node)
        {
            string commandId = node.Target;
            _handler.HandlerAction = (app) =>
            {
                string problem = null;
                try
                {
                    RevitCommandId id = RevitCommandId.LookupCommandId(commandId);
                    if (id == null)
                        problem = $"Unknown Revit command id \"{commandId}\".";
                    else if (!app.CanPostCommand(id))
                        problem = $"Command \"{commandId}\" cannot be posted right now.";
                    else
                        app.PostCommand(id);
                }
                catch (Exception ex)
                {
                    problem = ex.Message;
                }

                if (problem != null)
                {
                    Debug.WriteLine($"[LinkLibrary] PostCommand failed: {problem}");
                    _ = Dispatcher.BeginInvoke(new Action(() => TxtLibraryStatus.Text = problem));
                }
            };
            _externalEvent.Raise();
        }

        // ---------------------------------------------------------------------
        // Suggest a link (Phase 7, spec section 7): prefilled mail to the config
        // recipient so contributions flow back into the master.
        // ---------------------------------------------------------------------

        private void BtnSuggestLink_Click(object sender, RoutedEventArgs e)
        {
            SuggestLink("");
        }

        private void SuggestLink(string context)
        {
            // A dialog, not a bare mailto: machines without a default mail client made
            // the button look dead. The dialog always opens; mail and clipboard are
            // both offered inside it.
            var dialog = new SuggestLinkWindow(_config, context)
            {
                Owner = Window.GetWindow(this)
            };
            dialog.ShowDialog();
        }

        // ---------------------------------------------------------------------
        // Deep linking (spec section 11): any add-in command can open the pane
        // scrolled to a specific link instead of a dialog that says "see documentation".
        // ---------------------------------------------------------------------

        /// <summary>
        /// Shows the pane focused on one link. Call from any Revit API context:
        ///   LinkLibrary_Pane.ShowLink(uiapp, "acco.standards.piping.hangers");
        /// </summary>
        public static void ShowLink(UIApplication uiapp, string linkId)
        {
            Logic.LinkLibrary.LinkLibraryPaneRegistrar.TryGetPane(uiapp)?.Show();
            Logic.LinkLibrary.LinkLibraryPaneRegistrar.PaneControl?.FocusLink(linkId);
        }

        private void FocusLink(string linkId)
        {
            var entry = (_searchIndex ?? new List<LibrarySearch.SearchEntry>())
                .FirstOrDefault(x => x.Node.Id == linkId);

            TxtSearch.Text = "";
            _resultsMode = ResultsMode.DeepLink;
            PanelPickInfo.Visibility = System.Windows.Visibility.Visible;
            BtnPickSuggest.Visibility = System.Windows.Visibility.Collapsed;

            if (entry != null)
            {
                TxtPickTags.Text = "Opened from a command:";
                ListResults.ItemsSource = new List<LibrarySearch.SearchEntry> { entry };
                ListResults.Visibility = System.Windows.Visibility.Visible;
                TreeLibrary.Visibility = System.Windows.Visibility.Collapsed;
            }
            else
            {
                TxtPickTags.Text = $"Link \"{linkId}\" was not found in the current library.";
                ListResults.ItemsSource = null;
                ListResults.Visibility = System.Windows.Visibility.Collapsed;
                TreeLibrary.Visibility = System.Windows.Visibility.Visible;
            }
        }

        // ---------------------------------------------------------------------
        // Dev-only Phase 0 self-test (see LinkLibrary_DEV_Plan.md section 3).
        // Proves: WPF click -> HandlerAction -> Revit API -> Dispatcher.BeginInvoke
        // back onto this pane's UI thread. Remove before port.
        // ---------------------------------------------------------------------
        private void BtnRoundTrip_Click(object sender, RoutedEventArgs e)
        {
            int uiThreadId = Thread.CurrentThread.ManagedThreadId;
            TxtTestOutput.Text = $"[UI thread {uiThreadId}] Raising ExternalEvent...";
            Debug.WriteLine($"[LinkLibrary] Self-test click on UI thread {uiThreadId}, raising ExternalEvent");

            _handler.HandlerAction = (app) =>
            {
                int apiThreadId = Thread.CurrentThread.ManagedThreadId;
                string result;
                try
                {
                    UIDocument uidoc = app.ActiveUIDocument;
                    Document doc = uidoc.Document;

                    int elementCount = new FilteredElementCollector(doc, doc.ActiveView.Id)
                        .WhereElementIsNotElementType()
                        .GetElementCount();

                    result = $"[API thread {apiThreadId}] Revit API call OK\n"
                           + $"  Document: {doc.Title}\n"
                           + $"  Active view: {doc.ActiveView.Name}\n"
                           + $"  Elements in view: {elementCount}";
                    Debug.WriteLine($"[LinkLibrary] Self-test handler ran on thread {apiThreadId}: {doc.Title}, {elementCount} elements");
                }
                catch (Exception ex)
                {
                    result = $"[API thread {apiThreadId}] Handler FAILED: {ex.Message}";
                    Debug.WriteLine($"[LinkLibrary] Self-test handler exception: {ex}");
                }

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    int backThreadId = Thread.CurrentThread.ManagedThreadId;
                    TxtTestOutput.Text = $"Round-trip complete.\n\n"
                                       + $"  Click raised from UI thread: {uiThreadId}\n"
                                       + $"{result}\n"
                                       + $"  Result displayed on UI thread: {backThreadId}";
                }));
            };

            _externalEvent.Raise();
        }
    }
}
