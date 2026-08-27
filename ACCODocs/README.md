# ACCODocs — Revit add-in project

The Revit add-in half of the Link Library dev sandbox. See the repo root
[`README.md`](../README.md) and [`LinkLibrary_DEV_Plan.md`](../LinkLibrary_DEV_Plan.md) for the
big picture; [`LinkLibrary_AddIn_Spec.md`](../LinkLibrary_AddIn_Spec.md) is the design authority.

## Layout

```
App.cs                          IExternalApplication — registers the pane, one ribbon button
Cmd_ACCODocs.cs                 THE ribbon button: toggles the dockable pane (single-button rule)
Common\
  ModelessExternalEventHandler.cs   Copied from production; DELETE at port and rewire
  ButtonDataClass.cs / Utils.cs     Template helpers
Forms\
  LinkLibrary_Pane.xaml(.cs)        The dockable pane (Library + My Links tabs)
  AddUserLinkWindow.xaml(.cs)       "Add link..." dialog
  SuggestLinkWindow.xaml(.cs)       "Suggest a link" dialog (gmail/mailto + clipboard)
  ImportLinksModeWindow.xaml(.cs)   Import chooser: merge-new-only vs replace-all
Logic\LinkLibrary\
  LinkLibraryConfig.cs              Config probe order + new suggestion-mail keys
  LinkLibraryModels.cs              LinkLibraryDocument + LibraryNode (shared with the editor)
  MasterLibraryService.cs           Cache / revision compare / version filter / NEW badges
  UserLibraryModels.cs / UserLibraryService.cs   Per-user favorites/recents/links, atomic writes
  LibrarySearch.cs                  Flatten + ranked search (title/tags/target/description/path)
  ElementTagExtractor.cs            Pick Element tag extraction (API context only)
  LinkLibraryPaneRegistrar.cs       Pane GUID + guarded registration + startup hide
  TelemetryLogger.cs                JSONL usage log in ProgramData
  DeadLinkChecker.cs                Once-per-session background URL probe
```

## Build

Template-based multi-config project (template v3.5, Revit 2020–2026). **Dev target is
`Debug R25` (Revit 2025 / net8.0-windows)** — F5 launches Revit 2025. `Debug R23` (net48) is
used as a cross-framework compile check.

```
dotnet build ACCODocs.csproj -c "Debug R25" -v q
```

Post-build copies the `.addin` + DLLs to `%AppData%\Autodesk\Revit\Addins\<year>\ACCODocs` and
deploys `..\TestData\LinkLibrary.config.dev.json` next to the DLL as `LinkLibrary.config.json`
(config probe #2).

## Rules that bit us (details in the DEV plan)

- WPF+WinForms are both enabled: alias `UserControl`, `ListBox`, `MenuItem`, `ContextMenu`,
  `Clipboard`, `MessageBox` to the WPF types.
- Anything a WPF template binds to must be a **property** — fields bind silently to nothing.
- Set explicit `Background`/`Foreground` on pane controls; Revit's dark theme makes unstyled
  template text black-on-black.
- Never touch the Revit API from a WPF handler — raise the `ExternalEvent`.
- Await inside `OnStartup`-created UI resumes off the UI thread — marshal via `Dispatcher`.

---

### Template change log (heritage)

This project began from the ACCO Revit add-in template. Template supports Revit 2020–2026;
Revit 2025+ requires the .NET 8 SDK (https://dotnet.microsoft.com/download/dotnet/8.0).

- 3.0 — Added support for Revit 2025
- 3.3 — Added R20 build config, fixed error in Command2.cs, added ButtonDataClass
- 3.4 — Added CopyLocalLockFileAssemblies property to .csproj file
- 3.5 — Added support for Revit 2026
