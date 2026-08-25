using ACCODocs.Common;
using ACCODocs.Forms;

namespace ACCODocs.Logic.LinkLibrary
{
    /// <summary>
    /// Single registration point for the Link Library dockable pane (spec section 3).
    ///
    /// RegisterDockablePane may only be called ONCE per DockablePaneId per Revit session.
    /// In production three tab add-ins (ConTech / BIM Team / Mechanical) may all call
    /// Register from their own IExternalApplication.OnStartup, possibly from separate
    /// load contexts — hence the static guard AND the try/catch. If registration fails
    /// because the pane already exists, swallow it: the ribbon button still resolves the
    /// existing pane via DockablePane.GetDockablePane(PaneId).
    ///
    /// The dev sandbox has one tab, so the race cannot fire here; the guards must still
    /// port unchanged (LinkLibrary_DEV_Plan.md section 5).
    /// </summary>
    public static class LinkLibraryPaneRegistrar
    {
        // Fixed pane GUID — defined here and nowhere else (spec section 3).
        public static readonly DockablePaneId PaneId =
            new DockablePaneId(new Guid("ACC0D0C5-11B2-4A2B-9E77-3F1A6C5B2D41"));

        public const string PaneTitle = "ACCO Link Library";

        private static bool _registered;
        private static bool _startupHidePending;
        private static LinkLibrary_Pane _paneControl;

        /// <summary>The session-singleton pane UserControl. Null until Register runs.</summary>
        public static LinkLibrary_Pane PaneControl => _paneControl;

        /// <summary>
        /// Must be called during IExternalApplication.OnStartup — not later (spec section 3).
        /// Safe to call more than once and from multiple tab assemblies.
        /// </summary>
        public static void Register(UIControlledApplication app)
        {
            if (_registered)
            {
                Debug.WriteLine("[LinkLibrary] Register skipped — already registered in this context.");
                return;
            }

            try
            {
                // ExternalEvent.Create requires a Revit API context — OnStartup qualifies.
                // The pane is modeless, so every Revit API touch goes through this event.
                var handler = new ModelessExternalEventHandler();
                var externalEvent = ExternalEvent.Create(handler);

                // Running Revit version, for the spec section 5 revitVersions node filter.
                int.TryParse(app.ControlledApplication.VersionNumber, out int revitVersion);

                // Session singleton, created before registration, lives for the whole session.
                _paneControl = new LinkLibrary_Pane(externalEvent, handler, revitVersion);
                app.RegisterDockablePane(PaneId, PaneTitle, _paneControl);
                _registered = true;
                Debug.WriteLine("[LinkLibrary] Dockable pane registered.");

                // Registered panes come up visible on first run (and Revit remembers state).
                // The pane must start CLOSED — the ribbon button is the entry point.
                // ApplicationInitialized is too early: pane layout is applied when the first
                // document view opens and overrides a Hide() done before that. So hide on the
                // FIRST ViewActivated of the session, then unsubscribe.
                _startupHidePending = true;
                app.ViewActivated += OnFirstViewActivated;
            }
            catch (Exception ex)
            {
                // Another tab assembly beat us to it (or registration failed). Do not take
                // the tab down — the button will resolve the existing pane by id.
                Debug.WriteLine($"[LinkLibrary] RegisterDockablePane swallowed: {ex.Message}");
            }
        }

        private static void OnFirstViewActivated(object sender, Autodesk.Revit.UI.Events.ViewActivatedEventArgs e)
        {
            if (!_startupHidePending)
                return;
            _startupHidePending = false;

            var uiapp = sender as UIApplication;
            try
            {
                TryGetPane(uiapp)?.Hide();
                Debug.WriteLine("[LinkLibrary] Pane hidden on first view activation (closed by default).");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LinkLibrary] Startup hide failed: {ex.Message}");
            }
            finally
            {
                try { if (uiapp != null) uiapp.ViewActivated -= OnFirstViewActivated; } catch { }
            }
        }

        /// <summary>
        /// Resolves the registered pane, whichever assembly registered it. Null if the
        /// pane id is unknown to this session.
        /// </summary>
        public static DockablePane TryGetPane(UIApplication uiapp)
        {
            try
            {
                return uiapp.GetDockablePane(PaneId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LinkLibrary] GetDockablePane failed: {ex.Message}");
                return null;
            }
        }
    }
}
