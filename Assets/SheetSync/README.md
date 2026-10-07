# Sheet Sync

Keeps JSON configs in a Google Sheet, one row per config Id and one column per config type. The package covers two uses:

- **Editor:** writes the sheet's cells into the project's bundled JSON files. **Window → Sheet Sync** previews the changes first and writes only when you press Apply.
- **Runtime (optional):** fetches the sheet when the game starts and overrides the configs it already loaded. Designers can tune a live build without shipping a new one.

Extracted from Freight Frenzy (`loopfactory_prototype`), where it syncs the Level and Conveyor configs. Nothing in it is tied to that game's types.

## Install

**Package Manager → + → Add package from tarball…** and pick `com.cardfactory.sheetsync-1.0.0.tgz`. Or copy the `com.cardfactory.sheetsync` folder into the project's `Packages/`.

Newtonsoft JSON comes in as a dependency. **Addressables is optional.** If it's installed, new files can be added to a group and label automatically. Without it, everything else still works.

## Sheet layout

```
Id | Level                    | Conveyor
1  | { "Id": 1, ... }         | { "Id": 1, ... }
2  | { "Id": 2, ... }         |                     <- empty cell: that config is left alone
```

- The first row holds the column headers.
- Each JSON cell holds the **whole** object, not just the fields being tweaked. Pretty-printed JSON is fine, since cells may span multiple lines.
- Rows whose Id cell isn't an integer are skipped. Use this for notes or blank lines.

**Share it:** set General access to *Anyone with the link → Viewer*. The URL then has this shape:

```
https://docs.google.com/spreadsheets/d/<sheet id>/gviz/tq?tqx=out:csv&gid=<tab gid>
```

`<tab gid>` is the `gid=` value in the browser URL while that tab is open.

## Settings

**Assets → Create → Sheet Sync → Settings**, then fill in:

| Field | |
|---|---|
| Sheet Url | The CSV URL above |
| Id Column | Header of the Id column (`Id`) |
| Columns | One entry per JSON column |
| Enabled / Timeout Seconds | Runtime override only |

Per column:

| Field | Example (Freight Frenzy's Level column) |
|---|---|
| Column Name | `Level` |
| Asset Folder | `Assets/CardFactory/Configs/Levels/Boxes` |
| File Name Format | `lvl_box_{0:000}.json` (`{0}` = Id) |
| Json Id Field | `Id`: the field forced to equal the row's Id; empty = no check |
| Validate As Type | `Model.Level`: cells must deserialize into it; empty = JSON check only |
| Addressables Group / Label | `LevelBoxes` / `LevelBoxes`: applied to **new** files only |

## Editor sync

Open **Window → Sheet Sync** and pick the settings asset. The first one in the project is picked automatically.

1. **Fetch & Preview** reads the sheet and lists each Id/column as one of:
   - `Changed`, with the top-level keys that differ;
   - `New`, when no file exists yet;
   - `Unchanged`;
   - `Invalid`, when the cell isn't JSON or doesn't fit `Validate As Type`;
   - `Empty`.
   It also lists files whose Id isn't on the sheet.
2. **Apply** confirms, then writes the changed and new files.

**How cells are written:**
- Comparison is by JSON value, so formatting-only differences count as unchanged.
- A file gets the cell's own text with line endings normalised. Format the sheet like your files and the git diff shows only real changes.

**What it never does:**
- It never deletes files.
- It never touches files whose Id isn't on the sheet.
- It never writes an `Invalid` cell.

## Runtime override

```csharp
using SheetSync;

var sync = new SheetConfigOverride(settings);   // settings: your SheetSyncSettings asset

// Typed: the cell replaces every public field/property of the object you return for that Id.
// The object is copied into (not swapped), so references other systems hold stay valid.
sync.Register<Level>("Level", id => levelRepository.GetOrAdd(id));
sync.Register<Conveyor>("Conveyor", id => conveyorRepository.GetOrAdd(id));

// Or raw, if you'd rather handle the JSON yourself:
sync.Register("Tuning", (id, json) => tuning.Apply(id, json));

await sync.SyncAsync();   // false when disabled, no URL, or the fetch failed
```

- **Ids the game doesn't have yet:** your `getOrAdd` decides. Return a new registered object to add the Id, or `null` to ignore the row.
- **Failures never throw.** A failed fetch keeps every config as it was. A bad cell keeps only that one config as it was. Each failure is logged as a warning.
- **Id is forced:** after copying, the column's `Json Id Field` is set to the row's Id. Same rule as the editor sync.
- **No dependencies:** `SyncAsync` is a plain `Task`, with no UniTask, DI or Addressables. You can also feed rows in yourself: `sync.Apply(SheetRow.FromCsv(csv, "Id"))`.

## Differences from the source project

- **Configured, not hard-coded:**
  - The folders, file names, Addressables groups/labels and the Level/Conveyor types used to be constants. They now live in `SheetSyncSettings`.
  - Validation uses `Validate As Type` (a type name), not compile-time types.
- **Override is generic:** `ConfigOverrideApplier` (a hand-written field list per type) became `SheetConfigOverride.Register<T>`, which copies every public field and property by reflection. New fields on your config types are picked up automatically.
- **Plain Task:** `RemoteConfigSyncService` / `RemoteConfigSettingsProvider` (UniTask + VContainer + Addressables) became the plain-`Task` `SheetConfigOverride.SyncAsync`. Load the settings asset however your project loads assets.
- **One settings asset:** `RemoteConfigSettings` → `SheetSyncSettings`, which the editor window now reads too.
