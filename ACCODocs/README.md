# ACCODocs — Revit add-in project

The Revit add-in half of the Link Library dev sandbox. See the repo root
[`README.md`](../README.md) and [`LinkLibrary_DEV_Plan.md`](../LinkLibrary_DEV_Plan.md) for the
big picture; [`LinkLibrary_AddIn_Spec.md`](../LinkLibrary_AddIn_Spec.md) is the design authority
(§15 = implementation deltas).

## Layout

```
App.cs                          IExternalApplication — registers the pane, ONE ribbon button
Cmd_ACCODocs.cs                 THE ribbon button: toggles the pane (prompticon icons,
                                short tooltip + LongDescription extended tooltip)
Common\
  ModelessExternalEventHandler.cs   Copied from production + Execute hardening (clear-before-
                                    invoke, try/catch); DELETE at port, carry hardening back
  ButtonDataClass.cs / Utils.cs     Template helpers
Forms\
  LinkLibrary_Pane.xaml(.cs)        The dockable pane: Library + My Links tabs, search, Pick
                                    Element, export/import, deep link (ShowLink), Help link
  AddUserLinkWindow.xaml(.cs)       Add Link dialog: any location type (URL/file/folder/Box/
                                    mailto/Revit command id), kind auto-detect, Category/
                                    Sub-category (type-to-create), description, vocab + custom tags
  AddLinkHelpWindow.xaml(.cs)       Dedicated field-by-field Add Link help
  HelpWindow.xaml(.cs)              Pane user guide (modeless singleton)
  HelpZoom.cs                       Shared A−/A+ + Ctrl+wheel zoom for help windows (also linked
                                    into LinkLibraryEditor)
  ImportLinksModeWindow.xaml(.cs)   Import chooser: merge-new-only vs replace-all
  SuggestLinkWindow.xaml(.cs)       Suggest a link (gmail compose / mailto / clipboard)
Logic\LinkLibrary\
  LinkLibraryConfig.cs              Config probe order + all keys incl. fallbackLibraryPath,
                                    suggestion mail keys, recents caps (linked into the editor)
  LinkLibraryModels.cs              LinkLibraryDocument + LibraryNode (runtime IsNew/IsExpanded/
                                    IsSelected; linked into the editor)
  MasterLibraryService.cs           Cache / revision-only compare / network→fallback probe /
                                    version filter / NEW badges / atomic cache writes
  UserLibraryModels.cs / UserLibraryService.cs   Favorites/recents/user links: atomic camelCase
                                    writes, merge/export, EnsureUserGroup (categories)
  LibrarySearch.cs                  Flatten + ranked search (title/tags/target/description/path)
                                    + RankByTags for Pick Element
  ElementTagExtractor.cs            Pick Element tag extraction (API context only)
  LinkLibraryPaneRegistrar.cs       Pane GUID + guarded registration + ViewActivated startup hide
  TelemetryLogger.cs                JSONL usage log in ProgramData (src: tree/search/favorite/
                                    recent/pickElement/deepLink), 5 MB rotation
  DeadLinkChecker.cs                Once-per-session background URL probe → deadlinks_<user>.jsonl
```

## Build

Template-based multi-config project (template v3.5, Revit 2020–2026). **Dev target is
`Debug R25` (Revit 2025 / net8.0-windows)** — F5 launches Revit 2025. `Debug R23` (net48) is the
cross-framework compile check; run it after touching Logic classes.

```
dotnet build ACCODocs.csproj -c "Debug R25" -v q
```

Post-build copies the `.addin` + DLLs to `%AppData%\Autodesk\Revit\Addins\<year>\ACCODocs` and
deploys `..\TestData\LinkLibrary.config.dev.json` there as `LinkLibrary.config.json` (probe #2 —
note this OVERWRITES manual edits to the deployed config on every build of that year).

## Rules that bit us (full war stories in the DEV plan §4 + changelog)

- Anything a WPF template binds to must be a **property** — fields bind silently to nothing.
- Set explicit `Background`/`Foreground` on pane controls; Revit's dark theme renders unstyled
  template text black-on-black.
- WPF+WinForms are both enabled: alias `UserControl`, `ListBox`, `MenuItem`, `ContextMenu`,
  `Clipboard`, `MessageBox`; write `System.Windows.Visibility.Collapsed` (the instance property
  shadows the enum).
- Never touch the Revit API from a WPF handler — raise the `ExternalEvent`.
- Await inside `OnStartup`-created UI resumes off the UI thread — marshal via `Dispatcher`.
- Hide the pane on first `ViewActivated`, not `ApplicationInitialized` (layout restore overrides).
- Open folders via `explorer.exe "<path>"` — ShellExecute fails on Box/OneDrive ReparsePoint dirs;
  quote-trim targets (Explorer "Copy as path").
- Revision number is the ONLY update signal — content edits without a bump are invisible.
