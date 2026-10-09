# ACCO Revit Link Library — DEV Plan & Port Handoff

**Status:** all spec build-order phases (0–7) **implemented and working in Revit 2025**, plus the
admin editor app and deep linking. Remaining: the short verification list in §5, the §6 decisions,
then the port.
**Companion to:** [`LinkLibrary_AddIn_Spec.md`](LinkLibrary_AddIn_Spec.md) — the **design
authority** (its §15 lists the implementation deltas). This file is the dev status, the lessons
learned, and the port checklist.
**DEV solution:** `C:\Visual Studio Files\ACCODocs` (`ACCODocs.slnx`)
**Production target (separate session):** `C:\Visual Studio Files\BTT_ACCORevit-Ribbons`
**Author:** Orlando · **Last updated:** 2026-08-24

---

## 1. Ground rules

1. **Do not touch `BTT_ACCORevit-Ribbons` from this solution.** Reading/copying out of it is fine;
   no edits, no commits. The port is a deliberate, separate session.
2. Only proven, working code gets ported.
3. **One ribbon button.** "ACCO Docs" toggles the pane; every other piece of UI lives inside the
   pane. This holds at port time too: one command class per hosting tab, nothing more.

## 2. DEV environment

- **Build/debug target: `Debug R25`** → Revit 2025 / net8.0-windows; F5 launches Revit 2025.
  `Debug R23` (net48) is the cross-framework compile check — run it after touching Logic classes.
  ```
  dotnet build "ACCODocs/ACCODocs.csproj" -c "Debug R25" -v q
  ```
- Post-build deploys DLLs + `.addin` to `%AppData%\Autodesk\Revit\Addins\2025\ACCODocs` and copies
  `TestData\LinkLibrary.config.dev.json` there as `LinkLibrary.config.json` (config probe #2; the
  shared probe-#1 path is deliberately absent on dev machines so fall-through gets exercised).
- Test fixtures: `TestData\LinkLibrary.master.json` — nested groups, all `kind` values incl. a
  `command` demo (`ID_SETTINGS_UNITS`), `revitVersions`-gated nodes (2025-only / 2026-only Help),
  currently **revision 3**. Bump `revision` to test the update flow.
- Runtime data lands at `%PROGRAMDATA%\ACCO\RevitLinkLibrary\` (cache + `usage\<user>.jsonl`
  telemetry + `deadlinks_<user>.jsonl`) and `%LOCALAPPDATA%\ACCO\RevitLinkLibrary\`
  (`LinkLibrary.user.json`).
- Second project: **`LinkLibraryEditor`** — admin GUI for the master JSON (own
  [README](LinkLibraryEditor/README.md)). Plain Debug/Release configs, mapped in the `.slnx`.

## 3. What is implemented (spec phase → state)

| Phase | Deliverable | State |
|---|---|---|
| 0 | ExternalEvent plumbing (`ModelessExternalEventHandler` copied from production, AlignEngine-specific methods omitted) | ✅ verified in Revit (round-trip self-test lives in the pane's dev expander) |
| 1 | Registrar + pane shell + config loader | ✅ verified — pane GUID `ACC0D0C5-11B2-4A2B-9E77-3F1A6C5B2D41` (defined ONLY in `LinkLibraryPaneRegistrar`), tabbed with Project Browser, starts CLOSED (hidden on first `ViewActivated`), probe order + built-in defaults |
| 2 | Master load, cache, revision compare, tree render | ✅ verified — cache renders instantly, async revision check, offline indicator, `revitVersions` filter confirmed (2026 Help hidden on 2025) |
| 3 | Search / flatten | ✅ verified — ranked (title 100/prefix 150 > tags 40 > **target/URL 25** > description 20 > path 10), spans master + user links, My Links tab has its own scoped search |
| 4 | My Links: favorites, recents, user links, atomic writes | ✅ verified — ids-only favorites/recents resolved at render, single fixed root, corrupt file → `.bak` silently |
| 5 | Pick Element + tag scoring | ✅ implemented — selection-first, `PickObject` fallback, `BuiltInCategory`/family/type/MEP tags, scored not filtered, dead-end shows tags + Suggest · **F5 verification pending** |
| 6 | Telemetry | ✅ implemented — JSONL append-only, all 6 `src` values wired, 5 MB rotation, `enableTelemetry` honored · **F5 verification pending** |
| 7 | NEW badge, Suggest a link, `command` kind, dead-link check | ✅ implemented — badge via `newBadgeDays`; suggest = dialog with gmail/mailto + clipboard (config-driven, subject prefix for filtering); `PostCommand` via ExternalEvent; once-per-session HEAD probe · **F5 verification pending** |
| — | Deep linking (spec §11) | ✅ `LinkLibrary_Pane.ShowLink(uiapp, linkId)` — pane opens focused on the link; opens log `src:"deepLink"` |
| — | Master editor (admin GUI) | ✅ `LinkLibraryEditor` app — permanent auto-ids, vocabulary checkboxes, auto revision bump, atomic save, validation |
| — | My Links export/import | ✅ Export/Import buttons on the My Links tab — export = valid user-file JSON (backup/sharing); import asks **Merge (add new only)** vs **Replace all** (double-click confirm + `.pre-import.bak` safety copy); merge is id-based (favorites union, recents union capped at 20, recursive group merge, links skipped if the id exists anywhere) · **F5 verification pending** |

File map: see [`ACCODocs/README.md`](ACCODocs/README.md).

## 4. Lessons learned (apply at port — each cost real debugging time)

1. **Startup refresh threading:** the pane is constructed during `OnStartup`, where no WPF
   `SynchronizationContext` exists — an `await` there resumes on a thread-pool thread and UI
   updates throw silently. Marshal ALL post-await UI work via `Dispatcher.BeginInvoke`, and keep
   the recovery branch (document loaded but tree empty → re-render).
2. **Pane closed at startup:** `ApplicationInitialized` is too early (layout restore overrides
   `Hide()`). Hide on the **first `ViewActivated`**, then unsubscribe.
3. **WPF bindings need properties.** `{Binding}` silently ignores public fields — search rows
   rendered blank for exactly this. Anything a template binds must be `{ get; set; }`.
4. **Dark theme:** Revit themes containers but not template TextBlocks → black-on-black. Every
   list/tree in the pane carries explicit `Background="White" Foreground="Black"`.
5. **WinForms+WPF template:** alias `UserControl`, `ListBox`, `MenuItem`, `ContextMenu`,
   `Clipboard`, `MessageBox` to WPF types (CS0104). Also `Visibility.Collapsed` must be written
   `System.Windows.Visibility.Collapsed` inside a `UserControl` (instance property shadows the enum).
6. **`LinkNode` name collides** with `Autodesk.Revit.DB.LinkNode` under solution-wide Revit global
   usings — the class is `LibraryNode`.
7. **`mailto:` is not reliable UX** on browser-mail machines: opens an empty draft or nothing.
   Gmail compose URL (`mail.google.com/mail/?view=cm&fs=1&to=..&su=..&body=..`) carries everything.
8. **net48/net8 dual-target details:** `File.Move` has no overwrite overload on net48 → temp +
   `File.Replace`; `HttpWebRequest` instead of `HttpClient` (no extra net48 reference; SYSLIB0014
   pragma'd on net8).
9. **`.slnx` config mappings** must use the pair form `Solution="Debug R25|*"` — omitting `|*`
   makes the whole solution unparseable in VS. Verify with `dotnet sln <file> list`.
10. **ShellExecute vs Box Drive folders:** `ProcessStartInfo { UseShellExecute = true }` on a
    virtual-provider ReparsePoint directory (Box Drive, OneDrive placeholders) fails, while real
    local/UNC folders work. Open folders via `explorer.exe "<path>"` instead — Explorer handles
    virtual filesystems natively. Also trim surrounding quotes from targets (Explorer's
    "Copy as path" wraps in quotes) and dispatch by what the target actually is, not the stored kind.
11. **Dead-link probe blind spot:** a lapsed domain redirecting to a parking page returns 200 and
    is not flagged (live example: The Building Coder's typepad URL → networksolutions parking).
    Candidate upgrade: flag when the final redirect domain differs from the stored one.

## 5. Remaining before port

**F5 verification (Phases 5–7):**
1. Pick a pipe with it preselected → no prompt, `OST_PipeCurves` + family/type/system tags,
   hanger standards ranked. Empty selection → pick cursor; Esc cancels silently.
2. Pick a wall → dead-end banner shows extracted tags + "Suggest a link for this".
3. Double-click "Project Units (command demo)" → Units dialog opens.
4. `usage\<user>.jsonl` grows one line per open with correct `src`; `enableTelemetry:false` stops it.
5. Suggest a link → Gmail compose opens prefilled (To / `[ACCO Revit Link Library] Suggestion: …` / body).

**Pre-port cleanup (do in the dev solution before copying files):**
- Remove the "Dev: ExternalEvent self-test" expander (XAML + `BtnRoundTrip_Click` + `HookSearchBox`
  debug lines can stay or go — the focus-forcing part of `HookSearchBox` should STAY).
- Delete `ACCODocs\Common\ModelessExternalEventHandler.cs` at port; rewire to the production one.
- Drop WPF aliases that production doesn't need (check — its Resources project may not enable WinForms).

## 6. Open questions (spec §12 — DECIDE BEFORE PORT)

1. **Roaming profiles?** → `%APPDATA%` vs `%LOCALAPPDATA%` for the user library. The manual
   export/import fallback the spec suggested for the non-roaming case is now BUILT (My Links tab),
   which lowers the stakes of this decision.
2. **Which production tab(s) host the button** — all three or ConTech only? (§3 handles all three.)
3. **Master edit rights** — recommended: ConTech team only, via `LinkLibraryEditor`; everyone else
   through Suggest a link.

## 7. Port-to-production checklist (separate session)

The `BTT-ACCORevit-Ribbons` Claude skill automates the import mechanics. "Done" means:

1. Move `Logic\LinkLibrary\*` and `Forms\LinkLibrary_Pane.xaml(.cs)`, `AddUserLinkWindow`,
   `SuggestLinkWindow` into `RevitRibbon_MainSourceCode_Resources` (same subfolders); remap
   namespaces `ACCODocs.*` → production.
2. Delete the copied `ModelessExternalEventHandler.cs`; use production's
   (`RevitRibbon_MainSourceCode_Resources\Common\`). **But first carry the dev copy's `Execute`
   hardening into the production handler** (2026-08-24 review): clear `HandlerAction` BEFORE
   invoking and wrap the invoke in try/catch — production's version invokes first and has no
   catch, so a throwing action can crash Revit and stays armed to re-fire on the next Raise.
3. Create `Cmd_ShowLinkLibraryPane` under `Unique Button Classes\<Tab>\` for each hosting tab
   (toggle logic from dev `Cmd_ACCODocs`); register in `.projitems`; add `.ribbon` XML entries.
4. Call `LinkLibraryPaneRegistrar.Register(...)` from each hosting tab's `OnStartup`.
5. Add `Newtonsoft.Json` is already in production Resources ✔; verify nothing else is missing.
6. Build every config `-p:Configuration=Debug-<year>` 2023–2027 (never pass
   `TargetFramework`/`RevitVersion` directly; restore per config).
7. Deployment payload: the `RibbonsShared\` folder (probe-#1 `LinkLibrary.config.json` + seed
   `LinkLibrary.master.json`, which doubles as the offline fallback master) into the MSIs at
   `C:\ACCORevit\ACCO\ACCORevit ADDINS\02-ACCORevit Ribbons\RibbonsShared\`; stand up the real
   master on the network share; point config at it (`fallbackLibraryPath` points at the seed).
8. Two tabs installed on one machine → no crash, second tab's button resolves the pane
   (registration race, spec §3 — untestable in dev).
9. Signed Release build verified on Revit 2026 (Trend Micro unsigned-DLL launch crash).
10. Decide where `LinkLibraryEditor` lives for admins (it is NOT part of the Revit MSIs).

## 8. Changelog (dev)

### 2026-10-08
- Config editor in LinkLibraryEditor (fallback test passed; Orlando asked for a GUI for
  `LinkLibrary.config.json`): new `ConfigEditorWindow` — typed form for all 13 keys (browse
  buttons for master/fallback, gmail/mailto combo, int range validation, telemetry checkbox),
  Open/New-from-defaults/Save/Save As with atomic camelCase writes, on-demand **Check Paths**
  reachability report (unreachable master is saveable by design), dirty guards, Help section
  added ("The config file": probe locations, env-var rule, Revit-restart reminder).
  `LinkLibraryConfig.cs` linked into the editor like the models; editor gained `GlobalUsings.cs`
  (Diagnostics/IO/Reflection — **WindowsDesktop SDK implicit usings exclude System.IO**, a
  Shapes.Path clash guard). "Config..." button on the main toolbar, works with no library open.
- Fallback master (Orlando's share-outage test): network `masterLibraryPath` unreachable →
  `CheckForUpdate` now probes `fallbackLibraryPath` (new config key; default = the installed
  `RibbonsShared\LinkLibrary.master.json`, the MSI seed per spec §10) through the SAME revision
  compare + cache copy-down; status line appends "using the local fallback copy"; Offline only
  when both fail. Config probe #1 moved into `RibbonsShared\` (Orlando's edit, kept — one MSI
  folder for config + seed). NOTE for testing: the fallback only SHOWS when its revision beats
  the cache — on a machine with a newer cache, delete
  `%PROGRAMDATA%\ACCO\RevitLinkLibrary\cache\LinkLibrary.master.json` (Revit closed) to see it.
- **Editor crash fixed** (Orlando hit it on first real Apply): `BtnApply_Click` read `_selected`
  AFTER `RefreshTree()`, whose ItemsSource reset fires `SelectedItemChanged(null)` and nulls
  `_selected` → NRE → unhandled → whole app closed, edits lost. Fix: captured node reference.
  Plus `App.DispatcherUnhandledException` guard (message, stay open) so no future bug silently
  closes the editor. **Lesson: any handler that calls RefreshTree must not touch `_selected` after.**
- Editor round 3 (after Orlando's rev-7/rev-10 file verification): writers now emit camelCase
  (CamelCasePropertyNamesContractResolver on editor Save + add-in user-file Save/Export —
  PascalCase was drifting from the spec schema; reads stay case-insensitive); placeholder ids
  ("…new-group"/"…new-link") regenerate from the real title on Apply, prefix taken from the
  ACTUAL tree parent (deriving from the old id kept a stale new-group segment); Close File
  button (unload without restarting); Open now dirty-guards; Help link (top-right) →
  `EditorHelpWindow` (Apply-vs-Save, revision semantics, permanent ids, vocabulary discipline)
  with the shared HelpZoom A−/A+ control (HelpZoom.cs linked into the editor project).
- Editor reorder + UX: ▲/▼ Move Up/Down toolbar buttons (reorder within siblings; display order =
  file order); `LibraryNode.IsSelected` runtime prop + TreeView container style (with IsExpanded)
  so rebuilds keep selection/expansion; new nodes come up selected with parent expanded; Ctrl+S
  saves; Apply/Add status texts now say "remember to Save".
- Help (option A — built-in, offline): `Forms\HelpWindow.xaml(.cs)` = pane help (tabs, search,
  Pick Element, export/import, suggest, status-message decoder) + separate
  `Forms\AddLinkHelpWindow.xaml(.cs)` = dedicated Add Link field-by-field reference (location
  forms with examples, type behavior, categories, tags, finding command ids via journal
  Jrn.RibbonEvent lines). Both modeless singletons; the Add Link one is deliberately NOT owned
  by the modal dialog so it stays interactive beside the form. "Help" hyperlink labels: pane
  header top-right, Add Link dialog bottom-left. Ribbon tooltip rewritten + `LongDescription`
  extended tooltip set on the PushButtonData (ButtonDataClass only handles ToolTip). Orlando
  also added prompticon icons to the button. 4K readability: help windows base font 14 + A−/A+
  zoom buttons and Ctrl+wheel (shared `Forms\HelpZoom.cs` LayoutTransform scaler, session-wide
  level shared by both windows, 80–220%); Add Link dialog base font 14 / width 500.
- Master library rev 5: "Revit Commands" group (7 PostCommand links; old demo entry removed) +
  "ACCO Resources" group (8 real links from `TestData\MasterLinks.txt`; Box WEB links as url,
  local `C:\ACCORevit\ACCO` as folder). Committed `ffd7763`.
- Config-redirect test lesson (Orlando): a content edit WITHOUT a revision bump is invisible by
  design (revision is the sole version authority); per-year deployed configs are overwritten by
  that year's next build — durable redirects belong in TestData's dev config or probe #1.
  Restarting numbering at 1 requires deleting every machine's cache — don't; keep counting up.
- Add Link dialog: "command" kind added — combo entry, auto-detect for `ID_*` / `CustomCtrl_%...`
  targets, location label/tooltip updated. User links can now fire Revit commands.
- Add Link dialog: Category / Sub-category — editable combos listing existing user groups (pick
  or type-to-create; sub-category cascades from the chosen category); link files under
  My Links > Category > Sub-category via new `UserLibraryService.EnsureUserGroup` (case-insensitive
  title match, GUID ids, created groups start expanded). Sub-category without a category is
  blocked inline. Empty = root, as before.

### 2026-08-24 (initial build-out)

- Phase 0+1: handler copy, round-trip proof, registrar/pane/config; `UserControl` alias gotcha.
- Single-button refactor (Orlando): `Cmd_ACCODocs` = toggle; self-test moved into pane.
- Phase 2: models/cache/revision-compare/tree; `LibraryNode` rename; startup-refresh
  Dispatcher bug found via screenshot and fixed.
- Phase 3: ranked search + placeholder box; `Visibility` shadowing gotcha; sample "Revit
  Reference" library added (rev 2).
- Pane-closed-at-startup: `ApplicationInitialized` failed → `ViewActivated` hide works.
- Phase 4: My Links tab (favorites/recents/user links, atomic writes, context menus,
  `AddUserLinkWindow`); group "()" cosmetic fix; more WinForms aliases.
- Phases 5–7 + deep link: Pick Element, telemetry, NEW badges, suggest, `command` kind
  (fixture, rev 3), dead-link checker; net48 cross-check build.
- Suggest fix round 1 (dialog + clipboard) and round 2 (gmail method + subject prefix, new
  config keys).
- `LinkLibraryEditor` admin app built; `.slnx` mapping syntax broke VS solution load → fixed.
- Search fixes: fields→properties (the real "search broken" cause), explicit colors for dark
  theme, target/URL indexing, Library search spans user links, My Links search added.
- My Links export/import: `UserLibraryService.Parse/Export/MergeInto`, `ImportLinksModeWindow`
  (merge-new vs replace-all with double-click confirm), pre-import `.bak`, buttons under the
  user-links tree.
- Recents caps made configurable: `maxRecentsStored` (default 20, 0 = off) / `maxRecentsShown`
  (default 10) — new config keys, threaded through `RecordRecent`/`MergeInto`/`RenderMyLinks`.
- Full-solution review (revit-review checklist): hardened `ModelessExternalEventHandler.Execute`
  (clear-before-invoke + try/catch — an escaping exception crashes Revit and a throwing action
  stayed armed); fixed telemetry `src` for context-menu Open on My Links search results
  ("tree" → "search"). Everything else clean; both TestData JSONs re-validated.
- Tree collapse fix + custom tags: `LibraryNode.IsExpanded` (runtime-only) bound TwoWay via
  `ItemContainerStyle` on both trees + a persistent My Links root node — re-rendering (every open
  updates Recents) no longer collapses the tree. Comma-separated custom-tag fields added to the
  Add Link dialog (merged with checked vocabulary tags, deduped case-insensitively) AND the
  editor — where customs are folded into a `custom` vocabulary group on Apply so master
  validation stays clean.
- Box-folder fix + Add Link v2: `OpenLink` now dispatches by actual target (quote-trim via
  `NormalizeTarget`, folders via `explorer.exe` — fixes Box Drive ReparsePoint folders, lesson 10 —
  friendly not-found status for dead paths, no recent/telemetry recorded then); `AddUserLinkWindow`
  rewritten — Location field (any URL/file/folder/share/Box path) with Browse File/Folder buttons
  (WinForms `FolderBrowserDialog`: works on net48 AND net8; `OpenFolderDialog` is net8-only),
  live kind auto-detect with manual override, Description, vocabulary tag checkboxes (hidden when
  offline), not-found paths need a confirming second Add click, browsed paths auto-fill the title.
