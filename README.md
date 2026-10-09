# ACCODocs — ACCO Revit Link Library (DEV sandbox)

Development solution for the **ACCO Revit Link Library**: a dockable Revit pane giving users a
searchable, categorized library of documentation, reference links, and one-click Revit commands,
driven entirely by a JSON master file on a network share. Developed here first; ported to the
production `BTT_ACCORevit-Ribbons` solution when proven.

**Status (2026-10-08):** all spec build-order phases (0–7) implemented and verified in Revit
2025/2023, plus a long list of extensions (see the DEV plan changelog): built-in Help with zoom,
My Links export/import, user categories, custom tags, Revit-command links, Box-folder support,
and a **fallback master** (network share → installed RibbonsShared seed → local cache — tested
working). Remaining: manager review, spec §12 decisions, then the port (separate session).

| Document | Purpose |
|---|---|
| [`LinkLibrary_AddIn_Spec.md`](LinkLibrary_AddIn_Spec.md) | **Design authority.** Requirements, schemas, rules; §15 lists implementation deltas. |
| [`LinkLibrary_DEV_Plan.md`](LinkLibrary_DEV_Plan.md) | Status, lessons learned, changelog, port checklist. |
| [`ACCODocs/README.md`](ACCODocs/README.md) | The Revit add-in project (layout, build, gotchas). |
| [`LinkLibraryEditor/README.md`](LinkLibraryEditor/README.md) | Admin GUI for the master library AND the config file. |

## Solution contents

- **`ACCODocs/`** — the Revit add-in. One ribbon button ("ACCO Docs", prompticon icon set) toggles
  the dockable pane; everything else lives inside the pane: Library + My Links tabs, search,
  Pick Element, export/import, Suggest a link, Help.
- **`LinkLibraryEditor/`** — standalone WPF app for admins: edits the master library JSON
  (tree editor, reorder, vocabulary tags, auto revision bump) and `LinkLibrary.config.json`
  (typed form + path reachability check). No Revit required.
- **`TestData/`** — dev fixtures: `LinkLibrary.master.json` (sample library) and
  `LinkLibrary.config.dev.json`, which the post-build deploys next to the DLL (config probe #2).

## Key deployment concepts

- **Master library**: authoritative copy on the network share (`masterLibraryPath`); an installed
  **fallback/seed copy** in `C:\ACCORevit\ACCO\ACCORevit ADDINS\02-ACCORevit Ribbons\RibbonsShared\`
  answers when the share is unreachable; a per-machine **cache** (`%PROGRAMDATA%\ACCO\RevitLinkLibrary\cache`)
  renders instantly and survives full offline. Updates propagate by **revision number only** —
  content edits without a revision bump are invisible by design.
- **Config**: probe #1 = `RibbonsShared\LinkLibrary.config.json` (installed, company-wide),
  probe #2 = next to the DLL (per-machine override), probe #3 = compiled defaults. Read once at
  Revit startup.
- **Per-user data**: favorites/recents/personal links in `%LOCALAPPDATA%\ACCO\RevitLinkLibrary`;
  click telemetry as JSONL in `%PROGRAMDATA%\ACCO\RevitLinkLibrary\usage` (per-user filenames so
  the SYSTEM inventory script can collect them).

## Quick start

```
dotnet build "ACCODocs/ACCODocs.csproj" -c "Debug R25" -v q
```

F5 (or start Revit 2025), open a project, click **ACCO Docs** on the *ORH Dev* tab.
`Debug R23` (net48) doubles as the cross-framework compile check.

Do **not** modify `C:\Visual Studio Files\BTT_ACCORevit-Ribbons` from this solution — the port is
a deliberate, separate step (DEV plan §7).
