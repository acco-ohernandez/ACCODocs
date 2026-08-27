# ACCO Revit Link Library — Design Spec

**Status:** design locked — **implemented in the DEV sandbox 2026-08-24** (all build-order phases
0–7; see §15 for deltas and `LinkLibrary_DEV_Plan.md` for status/port checklist). Port to
production not started.
**DEV solution:** `C:\Visual Studio Files\ACCODocs` (`ACCODocs.slnx`)
**Production target solution:** `BTT_ACCORevit-Ribbons` (`C:\Visual Studio Files\BTT_ACCORevit-Ribbons`)
**Author:** Orlando
**Date:** 2026-08-24

---

## 1. Purpose

A dockable pane inside Revit that gives users a searchable, categorized library of documentation
and reference links, so they stop leaving Revit to hunt through browser bookmarks and network folders.

Content is driven entirely by an external JSON file on a network share. Adding, editing, or removing
a link must never require recompiling or redeploying the add-in.

Secondary goal: local click telemetry, so we can see which references people actually need. That data
tells us what to automate or fix instead of re-explaining it.

---

## 2. Solution placement and conventions

Follow the existing repo conventions. Do not invent new ones.

- **Command class:** `Cmd_ShowLinkLibraryPane` under `RevitRibbon_MainSourceCode/Unique Button Classes/<Tab>/`,
  registered in the `.projitems`, plus a button entry in that tab's `.ribbon` XML.
- **WPF UserControl:** `RevitRibbon_MainSourceCode_Resources\Forms\LinkLibrary_Pane.xaml(.cs)`
- **Domain / logic:** new folder `RevitRibbon_MainSourceCode_Resources\Logic\LinkLibrary\`
- **ExternalEvent:** reuse the existing `Common\ModelessExternalEventHandler.cs` pattern. Do not write a new one.
- **Build matrix:** must compile for Revit 2023–2027 → `net48` (2023/2024), `net8.0-windows` (2025/2026),
  `net10.0-windows` (2027). Revit 2020–2022 is out of scope, support was dropped 2026-07-22.
- **Build command:** always `-p:Configuration=Debug-<year>`. Never pass `TargetFramework` or `RevitVersion` directly.
- Smoke test: `dotnet build "RevitRibbon_MainSourceCode_Resources/RevitRibbon_MainSourceCode_Resources.csproj" -v q -p:Configuration=Debug-2025`

The `DockablePane` API has been stable since Revit 2014, so **no `#if REVIT20xx` guards should be
needed for this feature.** If you find yourself reaching for one, stop and flag it.

---

## 3. Critical gotcha: pane registration across three tabs

We ship three separate add-ins (ConTech, Engineering BIM Team, Engineering Mechanical). Each has its
own `IExternalApplication`. `RegisterDockablePane` can only be called **once per `DockablePaneId` per
Revit session**. If two tabs are installed on the same machine and both try to register, the second
one throws and takes the tab down with it.

Required approach:

1. Put a static registrar in the Resources project, for example `LinkLibraryPaneRegistrar.Register(UIControlledApplication)`.
2. Guard with a static `bool _registered` **and** a try/catch, because two tab assemblies may load into
   separate contexts. Belt and suspenders.
3. If registration fails because the pane already exists, swallow it and continue. The button in the
   second tab should still resolve the existing pane via `DockablePane.GetDockablePane(paneId)`.
4. Fixed GUID constant for the pane id, defined in one place only.

Additional pane rules:
- Registration must happen during `OnStartup`. Not later.
- The `UserControl` instance is a **session singleton**, created before registration and living for the
  whole session. Do not recreate it on each button press.
- Ribbon button toggles `DockablePane.Show()` / `Hide()`.
- `SetupDockablePane` initial state: tabbed with the Project Browser.

---

## 4. Configuration file

A config file sits with the deployed add-in payload so paths can change without a rebuild.

**Probe order (first hit wins):**

1. `C:\ACCORevit\ACCO\ACCORevit ADDINS\02-ACCORevit Ribbons\LinkLibrary.config.json`  ← preferred, one file for all tabs and years
2. `<assembly folder>\LinkLibrary.config.json`  ← per-tab per-year override, for testing
3. Hardcoded defaults compiled in, so the pane never fails to open

Note on placement: putting the config next to the DLL means 3 tabs × 5 years = 15 copies to edit for
one path change, which defeats the point. The shared parent folder is the default for that reason.
The per-assembly path stays as an override only.

```json
{
  "configVersion": 1,
  "masterLibraryPath": "\\\\<server>\\<share>\\ACCORevit\\LinkLibrary\\LinkLibrary.master.json",
  "localCacheFolder": "%PROGRAMDATA%\\ACCO\\RevitLinkLibrary\\cache",
  "userLibraryFolder": "%LOCALAPPDATA%\\ACCO\\RevitLinkLibrary",
  "telemetryFolder": "%PROGRAMDATA%\\ACCO\\RevitLinkLibrary\\usage",
  "suggestionRecipient": "mailto:<team>@accoes.com",
  "refreshCheckMinutes": 60,
  "newBadgeDays": 14,
  "enableTelemetry": true
}
```

All path values must run through `Environment.ExpandEnvironmentVariables`.

---

## 5. Master JSON schema

Lives on the network share. Read-only to users. Edited by the ConTech team.

```json
{
  "schemaVersion": 1,
  "revision": 42,
  "revisionDate": "2026-08-24",
  "tagVocabulary": {
    "discipline": ["Mechanical", "Plumbing", "Piping", "Sheetmetal", "Electrical", "Structural", "General"],
    "category":   ["OST_DuctCurves", "OST_PipeCurves", "OST_MechanicalEquipment", "OST_PipeFitting"],
    "topic":      ["Standards", "Training", "Troubleshooting", "Content", "Workflow"]
  },
  "groups": [
    {
      "id": "acco.standards",
      "title": "ACCO Standards",
      "children": [
        {
          "id": "acco.standards.piping.hangers",
          "title": "Pipe Hanger Standards",
          "description": "Approved hanger types, spacing, and detailing rules.",
          "kind": "url",
          "target": "https://<intranet>/standards/hangers",
          "tags": ["Piping", "OST_PipeCurves", "Standards"],
          "revitVersions": [2025, 2026, 2027],
          "added": "2026-08-01",
          "updated": "2026-08-15",
          "owner": "<name>@accoes.com"
        }
      ]
    }
  ]
}
```

**Rules:**

- `groups` nest arbitrarily. A node is a group if it has `children`, a link if it has `kind` + `target`.
- `tagVocabulary` is the **controlled list**. Tags on links must validate against it. Free-form tags are
  how this quietly stops working six months from now, when one person types `Ductwork` and another types `duct`.
- `revision` is the version authority. **Never compare file timestamps.** Robocopy, share migrations, and
  backup restores all scramble `LastWriteTime`.
- `revitVersions` omitted means "all versions". Present means filter the node to those versions only.
- `id` is stable and permanent. Renaming a title is fine, changing an id breaks every favorite pointing at it.

**`kind` values (implement `url` first, schema supports all from day one):**

| kind | `target` | Behavior |
|---|---|---|
| `url` | https URL | Default browser |
| `file` | UNC path | Shell open (PDF, DWG, XLSX) |
| `folder` | UNC path | Explorer |
| `mailto` | mailto URI | Mail client, for "who do I ask" |
| `video` | https URL | Default browser |
| `command` | Revit command id | Fires an existing ribbon command. Turns the pane into a launcher. |

---

## 6. User JSON schema

One file per user: `<userLibraryFolder>\LinkLibrary.user.json`

```json
{
  "schemaVersion": 1,
  "user": "<domain>\\<username>",
  "lastModified": "2026-08-24T10:12:03Z",
  "favorites": ["acco.standards.piping.hangers", "b1f2c3d4-..."],
  "recents": [
    { "id": "acco.standards.piping.hangers", "lastUsed": "2026-08-24T09:40:11Z", "count": 7 }
  ],
  "groups": [
    {
      "id": "b0000000-0000-0000-0000-000000000001",
      "title": "My Piping Refs",
      "children": [
        {
          "id": "b1f2c3d4-1111-2222-3333-444455556666",
          "title": "Vendor submittal portal",
          "kind": "url",
          "target": "https://...",
          "tags": ["Piping"],
          "added": "2026-08-20"
        }
      ]
    }
  ]
}
```

**Rules:**

- A user link is **structurally identical** to a master link. One tree renderer, one click handler, one
  telemetry path, and promoting a good user link into the master is a copy/paste.
- `source` (`master` vs `user`) is assigned at **runtime**, not stored in the file. UI badges from it.
- **Favorites and recents store ids only, never copies.** If you copy the whole object, the user keeps a
  stale URL forever after a master update. Resolve ids at load time.
- **Id namespacing:** master ids are dotted (`acco.*`), user-created ids are GUIDs. They must never collide,
  and a user file must never shadow a master id.
- Favorites may point at either a master id or a user GUID. An unresolvable id is dropped silently on load.
- Personal groups live under a **single fixed root node** in the My Links tab. Do not let users create
  top-level groups that mirror master category names, or nobody will know which "Standards" is authoritative.

**File handling:**

- Write atomically: temp file, then `File.Move` with overwrite. Never write in place. Revit crashes.
- Missing file is normal. Treat as empty, move on, no dialog.
- Corrupt file: rename to `.bak`, start fresh, log it. Never throw a dialog at someone who just opened a pane.
- **Never block pane load on the user file.**

---

## 7. Pane UI

Two tabs.

### Tab 1: Library

- Tree view of the master JSON, filtered by current Revit version.
- Search box at the top. Typing **flattens** the tree into ranked results matching title, description, and tags.
  Trees are good for browsing and bad for finding. Both are required.
- "New and updated" badge on anything where `added` or `updated` is within `newBadgeDays`. Without this,
  people learn the list is static and stop opening it.
- **Pick Element** button (see section 8).
- Right-click a node: Open, Copy link, Add to favorites.

### Tab 2: My Links

- Auto-populated **Favorites** and **Recents** sections at the top. This is what people actually use daily.
- User-created groups and links below, under one fixed root.
- Manual add is a secondary button, not the headline.
- **"Suggest a link"** action: opens a prefilled mail to `suggestionRecipient` with URL, title, and suggested
  category. User contributions flow back into the master where everyone benefits, instead of dying in one
  person's AppData.

---

## 8. Pick Element flow

Deliberately **on-demand only**. No `Idling` polling, no `SelectionChanged` subscription. Zero background
cost, identical code path across all supported Revit versions, and behavior the user can predict because
results only change when they ask.

Flow:

1. User clicks **Pick Element** in the pane.
2. Raise the `ExternalEvent`. The pane is modeless, so **the Revit API cannot be touched from the WPF
   click handler.** This is non-negotiable and is the most likely thing to eat a day of debugging.
3. In the handler: if `Selection.GetElementIds()` is non-empty, use that selection. Only fall back to
   `PickObject` when the selection is empty. Forcing a pick when they already selected the thing is exactly
   the friction that makes people stop pressing the button.
4. Extract tags from the element(s), in this order:
   - `BuiltInCategory` (the enum name, **not** the localized display name)
   - Family name and type name
   - MEP system type / system classification, when present
   - A dedicated tag-bearing shared parameter, if we add one later (see `Common\ACCOSharedParams`)
5. **Score** matches, do not hard filter. A link matching category + system type outranks one matching
   category only. Show a ranked list.
6. Marshal results back to the ViewModel on the UI thread.
7. **On zero matches, never show an empty pane.** Show the tags that were actually extracted, plus a
   "Suggest a link for this" action. Every dead end becomes a data point telling us where the tag
   vocabulary has holes.

---

## 9. Telemetry

Local only. A future version of the computer inventory script will collect it. Nothing phones home.

**Location:** `%PROGRAMDATA%\ACCO\RevitLinkLibrary\usage\<username>.jsonl`

ProgramData, not AppData, because the inventory script runs as SYSTEM and walking every user profile on a
shared machine to find per-user AppData files is fragile. One folder, per-user filenames.

**Format: JSON Lines, append-only.** One record per line. Never rewrite the whole file, so a crash mid-write
costs one line instead of the file.

```json
{"ts":"2026-08-24T10:12:03Z","linkId":"acco.standards.piping.hangers","src":"search","revit":2026,"user":"<username>"}
```

`src` values: `tree`, `search`, `favorite`, `recent`, `pickElement`, `deepLink`

That `src` field tells us whether the tree organization is working or whether everyone just searches,
which is the more useful signal.

Rotate or cap the file (suggest 5 MB or 90 days). Honor `enableTelemetry` in config.

---

## 10. Load, cache, and refresh

1. On startup, load the **local cache** immediately. The pane must open instantly, always.
2. Asynchronously check the master on the share. Compare `revision`, not timestamps.
3. If master `revision` > cache `revision`, copy down and refresh the tree in place.
4. Share unreachable: keep using the cache, show a subtle "offline, showing cached" indicator. No dialog.
5. Re-check on pane open and every `refreshCheckMinutes`.

**Cold start:** ship a seed copy of the master JSON in the MSI. First run on a new machine with no VPN
otherwise gives an empty pane, and that is the impression the user keeps. The Intune pipeline already
handles file placement, so this costs nothing.

**Dead link checking:** a background validation pass that flags 404s **to us, not to the user**. One broken
link and people quietly go back to their browser. Phase 7, not v1.

---

## 11. Deep linking from other add-ins

Expose a public static method so any existing ConTech / BIM Team / Mechanical command can open the pane
scrolled to a specific node:

```
LinkLibraryPane.ShowLink(string linkId)
```

When a command fails, point the user straight at the doc instead of a dialog that says "see documentation."
Log those with `src: deepLink`.

---

## 12. Open questions

1. **Does ACCO use roaming profiles?** Decides `%APPDATA%` vs `%LOCALAPPDATA%` for the user library, and
   whether favorites follow a user to another workstation. One-line change now, migration script later.
   If not roaming, add a manual export/import button so someone changing machines can carry their library over.
2. **Which tab hosts the ribbon button?** All three, or ConTech only? Section 3 handles all three, but
   confirm before wiring the `.ribbon` XML.
3. **Who can edit the master JSON?** Recommend: nobody but the ConTech team writes directly. Everything else
   flows through "Suggest a link". A network share the whole company can write to gets concurrent write
   corruption and an unreviewed vocabulary within a month.

---

## 13. Build order

Build in this order. Do not skip phase 0.

| Phase | Deliverable | Done when |
|---|---|---|
| 0 | ExternalEvent plumbing, reusing `ModelessExternalEventHandler` | A trivial handler round-trips from a modeless button click to the Revit API and back to the UI thread |
| 1 | Pane registration + empty shell + config loader | Pane docks next to Project Browser, opens/closes from the ribbon, survives all three tabs being installed |
| 2 | Master JSON load, local cache, revision compare, tree render | Tree populates from the share, edits to the share appear after a re-open, cache works with the share disconnected |
| 3 | Search / flatten | Typing filters across title, description, tags |
| 4 | My Links tab, favorites, recents, atomic writes | Favorites resolve by id and survive a master URL change |
| 5 | Pick Element + tag scoring | Ranked results from a real selection, useful empty state |
| 6 | Telemetry | `.jsonl` written on every open, all `src` values firing |
| 7 | New badge, Suggest a link, dead link check, non-`url` kinds | — |

---

## 14. Non-goals for v1

- No embedded WebView2 browser. Auth is the killer: SSO, SharePoint, and Autodesk sign-in run in a
  separate cookie jar from the user's Edge profile, so users get re-prompted for credentials they already
  have cached. MFA and conditional access make it flakier. Revisit only if users actually complain about alt-tabbing.
- No binding links to specific element instances via Extensible Storage. That scatters reference data across
  hundreds of project models, makes central URL updates impossible, and dies when the model gets archived.
  Company reference documentation and project-specific annotation are different kinds of data and want different homes.
- No live selection tracking. See section 8.

---

## 15. Implementation deltas (added 2026-08-24, after dev implementation)

The design above stands. These are the places where the dev implementation deliberately extended
or deviated from it — carry them into the production port.

1. **One ribbon button.** The add-in exposes exactly one button ("ACCO Docs") that toggles the
   pane; ALL other UI lives inside the pane (Orlando's rule, supersedes any reading of §7 that
   implies more buttons).
2. **Pane starts closed.** Registered panes come up visible; the registrar hides the pane on the
   first `ViewActivated` of the session (hiding earlier — e.g. `ApplicationInitialized` — is
   overridden by Revit's layout restore).
3. **Node class is `LibraryNode`, not `LinkNode`** — `Autodesk.Revit.DB.LinkNode` collides under
   the solution-wide Revit global usings.
4. **Config gained three keys** (all with compiled-in defaults, so old configs stay valid):
   - `suggestionMailMethod`: `"gmail"` (Gmail compose URL — ACCO is a Google Workspace shop;
     plain `mailto:` opened an EMPTY browser draft) or `"mailto"` (default).
   - `suggestionSubjectPrefix` (default `[ACCO Revit Link Library]`): subjects are
     `<prefix> Suggestion: <title>` so admins can filter/label suggestion mail.
5. **"Suggest a link" is a dialog**, not a bare mailto — machines without a default mail client
   made the button look dead. Offers Open-in-Mail AND Copy-to-Clipboard.
6. **Search scope extended (§7):** the Library-tab search covers the master tree AND the user's
   own links (breadcrumbed "My Links > ..."), and matches the link `target` too so a domain query
   ("accoes.com") works. The My Links tab has its own search scoped to favorites/recents/user links.
7. **Master editing is a GUI**, not raw JSON: the standalone `LinkLibraryEditor` WPF app enforces
   permanent auto-generated ids, vocabulary-checkbox tags, auto revision bump, atomic writes, and
   validation. Recommended §12-Q3 answer: ConTech team edits via the editor; everyone else uses
   Suggest a link.
8. **Deep link (§11) signature:** `LinkLibrary_Pane.ShowLink(UIApplication uiapp, string linkId)` —
   a `UIApplication` argument was unavoidable for resolving the pane.
9. **Dead-link checker limitation:** HEAD probes catch hard failures only; a lapsed domain that
   redirects to a parking page (HTTP 200) is NOT flagged — e.g. The Building Coder's typepad URL
   currently redirects to a networksolutions page. Possible future upgrade: flag final-redirect
   domains that differ from the stored one.
10. **WPF-in-Revit lessons** (full details in the DEV plan): template bindings need properties,
    not fields; set explicit Background+Foreground (dark theme renders unstyled template text
    black-on-black); alias WPF control names because the template enables WinForms; marshal
    post-`await` UI work through the `Dispatcher` when the pane is built during `OnStartup`.
11. **My Links export/import (added 2026-08-24):** Export/Import buttons on the My Links tab.
    Exports use the §6 user-file schema verbatim (a backup restores by file copy too). Import
    offers "Merge (add new only)" — id-based union of favorites/recents plus a recursive by-id
    group/link merge, never touching existing entries — or "Replace all", which double-click
    confirms and first snapshots the current file as `LinkLibrary.user.json.pre-import.bak`.
    This implements §12 Q1's manual export/import fallback.
12. **Recents caps are configurable (added 2026-08-24):** `maxRecentsStored` (default 20 — distinct
    links kept in the user file; 0 disables recents) and `maxRecentsShown` (default 10 — rows the
    My Links tab displays and its search indexes). Defaults compiled in like all config keys.
13. **Any-location user links + folder-open hardening (added 2026-08-24):** the Add Link dialog
    takes a generic Location (URL, file, folder, network share, or Box path) with browse buttons,
    live kind auto-detection (overridable), a description, and vocabulary tag checkboxes.
    `OpenLink` dispatches by what the target actually is: folders open via `explorer.exe`
    (ShellExecute fails on Box Drive/OneDrive ReparsePoint folders), quoted "Copy as path"
    targets are unquoted, and an unreachable path shows a friendly status instead of an error
    (and records no recent/telemetry).
