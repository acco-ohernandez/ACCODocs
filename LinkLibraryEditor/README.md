# Link Library Editor

Standalone WPF desktop app for **admins** (ConTech team). Edits two files through typed,
validated GUIs so nobody hand-edits JSON:

1. **The master library** (`LinkLibrary.master.json`) — the shared content every user's pane shows.
2. **The config** (`LinkLibrary.config.json`, via the `Config...` button) — paths, fallback,
   suggestion-mail settings, behavior numbers.

No Revit installation required — just run the exe. Built-in **Help** (top-right link) explains
everything below in admin terms, with A−/A+ text zoom.

## Master library editing

- **Open... / Close File** — load any master copy (dev TestData, the network share, or the
  installed RibbonsShared seed); both warn about unsaved changes.
- **Tree editor** — Add Group / Add Link under the selection (new nodes come up selected with the
  parent expanded), fields panel (title, description, kind, target, owner, revitVersions),
  **▲/▼ Move Up/Down** to reorder (tree order = what users see in Revit). Selection and expansion
  survive every refresh.
- **Ids are auto-generated and permanent** — the Add-time placeholder id regenerates from the real
  title on first Apply (prefix from the actual tree parent); after that the field is read-only
  forever, because changing an id breaks every user's favorite pointing at it.
- **Tags** — checkboxes from the file's own `tagVocabulary`; the custom-tags box folds new tags
  into the vocabulary's `custom` group on Apply, so the controlled list stays authoritative.
- **Apply vs Save** — Apply stages the selected item's edits in memory; **Save (Ctrl+S)**
  validates (duplicate ids, empty targets, out-of-vocabulary tags), **auto-bumps `revision`**
  (the only signal Revit watches), and writes atomically in camelCase. A failed write rolls the
  revision back. Old PascalCase files load fine and convert on save.
- Deleting a link kills every user's favorite pointing at it — fix a target instead of
  delete-and-re-add.
- A global exception guard keeps the app open with a message if anything goes wrong — it never
  silently closes over unsaved work.

## Config editing (`Config...` button)

Typed form for all keys: master + fallback library paths (Browse; an unreachable master is
saveable — that's what the fallback and cache are for), local folders (keep the `%PROGRAMDATA%`/
`%LOCALAPPDATA%` variables — they expand per-machine), suggestion recipient/method/subject-prefix,
refresh/badge/recents numbers, telemetry toggle. **Check Paths** reports per-path reachability
on demand. Open / New (defaults) / Save / Save As, atomic camelCase writes, dirty guards.
Remember: Revit reads the config at startup — users need a Revit restart after changes.

## Usage

1. Build/run (`dotnet build -c Debug`, or F5 with the project set as startup).
2. **Open...** → pick the master file
   - dev: `C:\Visual Studio Files\ACCODocs\TestData\LinkLibrary.master.json`
   - production: the file on the network share (`masterLibraryPath` in the config)
   - seed: `C:\ACCORevit\ACCO\ACCORevit ADDINS\02-ACCORevit Ribbons\RibbonsShared\LinkLibrary.master.json`
3. Edit → Apply per item → **Save**. Open Revit panes pick the new revision up on their next
   check (pane reopen / periodic timer) — the pane status line shows "Updated to rev N".

## Implementation notes

- net8.0-windows WPF, Newtonsoft.Json 13.0.3 (same as the add-in and production).
- `Shared\` links three files from the add-in project — `LinkLibraryModels.cs`,
  `LinkLibraryConfig.cs`, `HelpZoom.cs` — one definition each, so schema and behavior can never
  drift. `GlobalUsings.cs` supplies `System.Diagnostics/IO/Reflection` for the linked sources
  (the WindowsDesktop SDK's implicit usings exclude `System.IO` — Shapes.Path clash guard).
- Plain `Debug`/`Release` configs; the `.slnx` maps every `Debug R2x`/`Release R2x` solution
  config onto them (the mapping needs the `"<config>|*"` pair syntax or VS can't parse the slnx).
- Not part of the Revit add-in build order or the MSIs; ship it to admins however convenient
  (single small exe + Newtonsoft.Json; a .NET 8 Desktop Runtime installer is kept locally in
  `DotNet_Runtime\`, gitignored).
