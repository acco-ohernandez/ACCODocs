# Link Library Editor

Standalone WPF desktop app for **admins** (ConTech team) to edit the master Link Library JSON
without touching JSON text. No Revit installation required — just run the exe.

## Why it exists

The master file has rules that raw-JSON editing quietly breaks (see spec §5):
ids are permanent, tags must come from the controlled vocabulary, and `revision` — not file
timestamps — is the version authority. The editor makes those rules structural:

- **Ids are auto-generated** (parent id + slugified title, unique-suffixed) and **read-only
  forever** — changing an id would break every user's favorites pointing at it.
- **Tags are checkboxes** built from the file's own `tagVocabulary` — no free-form drift.
- **Save auto-bumps `revision` + `revisionDate`** and writes atomically (temp + replace);
  a failed write rolls the revision back.
- **Validate** flags duplicate ids, empty targets, missing kinds, and out-of-vocabulary tags.
- Deleting a link warns that favorites pointing at it die — prefer fixing the target.

## Usage

1. Build/run (`dotnet build -c Debug`, or F5 with the project set as startup).
2. **Open...** → pick the master file
   - dev: `C:\Visual Studio Files\ACCODocs\TestData\LinkLibrary.master.json`
   - production: the file on the network share (`masterLibraryPath` in `LinkLibrary.config.json`)
3. Edit in the tree + fields panel; **Apply Changes** per node; **Save** when done.
4. Open Revit panes pick the new revision up on their next check (pane reopen / periodic timer) —
   the pane status line shows "Updated to rev N".

## Implementation notes

- net8.0-windows WPF, Newtonsoft.Json 13.0.3 (same as the add-in and production).
- `Shared\LinkLibraryModels.cs` is **linked** from `ACCODocs\Logic\LinkLibrary\` — one schema
  definition for the add-in and the editor, so they can never drift.
- Plain `Debug`/`Release` configs; the `.slnx` maps every `Debug R2x`/`Release R2x` solution
  config onto them (mind the `"<config>|*"` syntax — see DEV plan section 7c-adjacent gotcha).
- Not part of the Revit add-in build order or the MSI; ship it to admins however convenient
  (it is a single small exe + Newtonsoft.Json).
