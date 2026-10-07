# Cap Chaos Level Designer

A browser tool to design **LevelConfig** (`level_NNNN.json`, format v4) and **ConveyorConfig** (`<id>.json`, format v2)
— the files in `Assets/CapsChaos/Content/Configs/`. It edits those files **in place**: save in the tool, Unity's
config hot-reload picks them up. Modelled on the Card Factory level builder (React + Vite + Tailwind).

```bash
cd Tools/LevelDesigner
npm install
npm run dev        # http://localhost:5178
```

`npm run dev` serves the UI **and** a small API (`server/configApi.ts`, a Vite plugin) that lists / reads / writes the
config files and runs `Tools/LevelTool`. Set `CAPSCHAOS_CONFIGS=/path/to/Configs` to point it somewhere else.

## What it does

**LevelConfig**
- Settings: conveyor, slots / extraSlots, trayCapacity, camera preset, name / difficulty / notes, slot locks (R22).
- Conveyor preview drawn like the game: loop + feeders from the conveyor's splines, pick zone, merge points, the queue
  of each feeder (hidden rows grey, locked rows with their turn count) and the optional `initial` belt.
- Brush: paint feeder bottles in the queue editor or straight on the canvas; paint `initial` spots (dashed = empty).
- Feeder queues: row editor (fill, hide, lock turns, insert / delete / reorder), "From lanes" builds balanced queues.
- Lanes board: slots row + lanes (front tray on top); trays with size S/M/L/XL, hidden, lockTurns, links (R19).
  Keys: `1–8` recolour the selected tray · `Del` delete · `Esc` deselect.
- Live balance table (V4) and problems (V1, V4, V5, V7, V8, V9, V10). Any gameplay edit clears a recorded
  `meta.solution` (LevelTool would replay a stale one and fail).
- Play order: ↑/↓ on a level in the list edits `levels.index.json`; a new level is appended on save.

**ConveyorConfig**
- Drag knots, drag the round handle to set the heading (`yRotation`), Shift snaps (0.05 u / 15°); sharp corners
  (`tangentMode: 1`); insert / delete knots; "Make node 0" moves where the pick zone starts.
- rows / width / pickRows / scale; `mergeAt` per feeder, or **pick** it by clicking a loop row.
- Preview the bottles of any level that uses the conveyor.

**Checks**
- `Validate` runs `dotnet run --project Tools/LevelTool -- validate` (V1–V6, including the solver) on the files on disk.
- `Format check` runs `LevelTool migrate --check`.
- The writers match `LevelJson.Write` / `ConveyorJson.Write` byte for byte, so saved files keep `migrate --check` clean.

## Kept in sync by hand

| Tool | Source of truth |
| --- | --- |
| `src/lib/format.ts` (writers) | `Assets/CapsChaos/Domain/CapChaos/LevelJson.cs`, `ConveyorJson.cs` |
| `src/lib/validate.ts` | `LevelValidator.cs`, `ConveyorValidator.cs`, `docs/design/*.schema.json` |
| `src/lib/spline.ts` | `Views/Board/ConveyorBeltView.cs` (`SetRoute`, tangent lengths) |
| `src/components/ConveyorCanvas.tsx` (row / queue placement) | `Views/Board/LoopBeltView.cs`, `GameFeel.cs` defaults |
| `src/lib/colors.ts` | `Views/DesignTokens.cs` (`Flavors`, `ItemHidden`, `MysteryColors`) |

The live checks are a convenience; LevelTool (the C# code the game runs) is the authority.
