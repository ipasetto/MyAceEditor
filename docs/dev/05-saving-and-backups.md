# 5. Saving and backups

The user's game files are never left half-written and every change can be undone from a backup.

## `AtomicFileSave.Write(path, bytes, backup: true)`

1. Copies the current file into the backup store (unless `backup: false`, used when the caller already made the copy).
2. Writes the new bytes to a temporary file beside the original and flushes it to disk.
3. Swaps it in (`File.Replace`), so the original is replaced in one step.
4. Returns the path of the backup.

Use it for every file the app writes. Never write a game file with `File.WriteAllBytes` directly.

## Saves that touch several files

A save that writes several files (the 3D World writes `.ecwld`, `.ecbsd`, `litmodels.pck`, `map.bht`, `.rmap`…)
must be **all or nothing**: if one file fails, the ones already written go back to their backups. `DraftSaver.Write`
and `TerrainSaver.Write` do this; follow them when you write a new saver.

Split packages (`.pck` + `.pkx`, up to 2 GB) are not copied into memory: the changed part is written and its header
swapped; the backup keeps the part and the 4 header bytes.

## `BackupStore` — the copies

`%LocalAppData%\MyAceEditor\backups\<game folder name>-<hash>\<path inside the folder>.<yyyyMMdd-HHmmss>.gz`

- Files are gzipped (packages are kept raw).
- The 5 newest copies of each file are kept, **plus** the oldest copy (how the file was before MyAceEditor first
  touched it) and every copy still used by a backup set. Those are never pruned.

## `BackupSets` — one item per save

A backup set is a JSON file in `backups\conjuntos\` listing every file of one save with its backup copy. The
*Restore backup* menu shows sets first, so the user restores a whole save at once.

- **Sessions of the 3D World:** while a `WorldView` is open, `BackupSets.Session` is set. Every save of that window
  (objects, grass, terrain, water, scene) is merged into one set per map, `"<start>-mundo3d-<map>.json"`, keeping the
  first copy of each file. Restoring it puts the map back as it was when the window opened.
- **Restore is undoable:** after a successful restore, the state just before it is saved as a set named
  "Before restoring".
- **Restore is all or nothing:** if one file cannot be restored (open in another program), the others go back.
- After a restore, `BackupSets.Restored` fires and open windows reload what changed from disk.

## The saved window

Every save ends with `SavedDialog.Show(owner, summary, sendToServer)`: a clear success window with a summary in
plain words. When the saved files still have to reach the server, it offers *Send to server now*, and
`ServerNotice.Notify(file)` lights the send button in the main window.

## Checklist for a new saver

- Build the new bytes in memory first; reread them with the reader before writing (the format must still parse).
- Run the game rules (`ItemRules`, `QuestRules`, `DraftChecks`) and stop with a plain message if something blocks.
- Write with `AtomicFileSave`; undo the files already written if a later one fails.
- Save a `BackupSet` with every file of the save.
- Show `SavedDialog`; call `ServerNotice.Notify` for server files.
- Catch `IOException`, `UnauthorizedAccessException`, `InvalidDataException`; log the detail (`AppLog.Error`), show a
  short message (`Loc.T`).
