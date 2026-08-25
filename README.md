# ACCODocs — ACCO Revit Link Library (DEV sandbox)

Development solution for the **ACCO Revit Link Library**: a dockable Revit pane giving users a
searchable, categorized library of documentation and reference links, driven entirely by a JSON
file on a network share. Developed here first; ported to the production `BTT_ACCORevit-Ribbons`
solution when proven.

**Status (2026-08-24):** all build-order phases (0–7) implemented and working in Revit 2025.
Remaining: final F5 verification of Phases 5–7, spec §12 decisions, then the port (planned for a
separate session).

| Document | Purpose |
|---|---|
| [`LinkLibrary_AddIn_Spec.md`](LinkLibrary_AddIn_Spec.md) | **Design authority.** Requirements, schemas, rules. Read first. |
| [`LinkLibrary_DEV_Plan.md`](LinkLibrary_DEV_Plan.md) | Dev status, build/test instructions, lessons learned, port checklist. |
| [`ACCODocs/README.md`](ACCODocs/README.md) | The Revit add-in project (template heritage, build configs). |
| [`LinkLibraryEditor/README.md`](LinkLibraryEditor/README.md) | Admin GUI for editing the master JSON. |

## Solution contents

- **`ACCODocs/`** — the Revit add-in (dockable pane, one ribbon button "ACCO Docs" on the dev
  "ORH Dev" tab). Build config `Debug R25` = Revit 2025, the active dev target; F5 launches Revit.
- **`LinkLibraryEditor/`** — standalone WPF app for admins to edit the master library JSON without
  touching JSON text. No Revit required.
- **`TestData/`** — local test fixtures: `LinkLibrary.master.json` (sample library, revision-bumped
  to test the update flow) and `LinkLibrary.config.dev.json` (deployed next to the DLL by the
  post-build as `LinkLibrary.config.json`).

## Quick start

```
dotnet build "ACCODocs/ACCODocs.csproj" -c "Debug R25" -v q
```

Then F5 (or start Revit 2025), open a project, click **ACCO Docs** on the *ORH Dev* tab.
The pane loads `TestData\LinkLibrary.master.json` via the deployed dev config.

Do **not** modify `C:\Visual Studio Files\BTT_ACCORevit-Ribbons` from this solution — the port is
a deliberate, separate step (see the DEV plan, section "Port to production").
