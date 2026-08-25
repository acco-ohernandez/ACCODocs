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
10. **Dead-link probe blind spot:** a lapsed domain redirecting to a parking page returns 200 and
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

1. **Roaming profiles?** → `%APPDATA%` vs `%LOCALAPPDATA%` for the user library; if not roaming,
   consider an export/import button later.
2. **Which production tab(s) host the button** — all three or ConTech only? (§3 handles all three.)
3. **Master edit rights** — recommended: ConTech team only, via `LinkLibraryEditor`; everyone else
   through Suggest a link.

## 7. Port-to-production checklist (separate session)

The `BTT-ACCORevit-Ribbons` Claude skill automates the import mechanics. "Done" means:

1. Move `Logic\LinkLibrary\*` and `Forms\LinkLibrary_Pane.xaml(.cs)`, `AddUserLinkWindow`,
   `SuggestLinkWindow` into `RevitRibbon_MainSourceCode_Resources` (same subfolders); remap
   namespaces `ACCODocs.*` → production.
2. Delete the copied `ModelessExternalEventHandler.cs`; use production's
   (`RevitRibbon_MainSourceCode_Resources\Common\`).
3. Create `Cmd_ShowLinkLibraryPane` under `Unique Button Classes\<Tab>\` for each hosting tab
   (toggle logic from dev `Cmd_ACCODocs`); register in `.projitems`; add `.ribbon` XML entries.
4. Call `LinkLibraryPaneRegistrar.Register(...)` from each hosting tab's `OnStartup`.
5. Add `Newtonsoft.Json` is already in production Resources ✔; verify nothing else is missing.
6. Build every config `-p:Configuration=Debug-<year>` 2023–2027 (never pass
   `TargetFramework`/`RevitVersion` directly; restore per config).
7. Deployment payload: seed `LinkLibrary.master.json` + shared `LinkLibrary.config.json`
   (probe #1: `C:\ACCORevit\ACCO\ACCORevit ADDINS\02-ACCORevit Ribbons\`) into the MSIs; stand up
   the real master on the network share; point config at it.
8. Two tabs installed on one machine → no crash, second tab's button resolves the pane
   (registration race, spec §3 — untestable in dev).
9. Signed Release build verified on Revit 2026 (Trend Micro unsigned-DLL launch crash).
10. Decide where `LinkLibraryEditor` lives for admins (it is NOT part of the Revit MSIs).

## 8. Changelog (dev, all 2026-08-24)

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
