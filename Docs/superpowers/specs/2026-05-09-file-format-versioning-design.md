# File Format Versioning — Design Spec

**Date:** 2026-05-09
**Status:** Approved
**Scope:** `Core/ShowFile.cs`, `Core/ShowFileSerializer.cs`, `ViewModels/MainViewModel.cs`

---

## Context

ShowCast saves its project state as `.scf` files (JSON). The serializer currently has no version field, no migration path, and no guard against opening files from a newer app version. As the model evolves, this will silently corrupt old files or produce confusing load failures. This spec adds versioning infrastructure before ShowCast ships.

ShowCast is pre-release — no existing `.scf` files are in the field. The first real migration (`v1 → v2`) will be written when the first breaking model change occurs.

---

## Decisions

| Question | Decision |
|---|---|
| Files in the wild? | No — pre-release |
| Open older file | Prompt user before migrating |
| Open newer file | Refuse with clear error |
| Migration style | Typed (operate on `ShowFile` object, not raw JSON) |

---

## Architecture

### 1. Data Model Changes — `ShowFile.cs`

```csharp
public class ShowFile
{
    public const int CurrentVersion = 1;
    public int Version { get; set; } = 1;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownFields { get; set; }

    // ... existing properties unchanged
}
```

- `CurrentVersion` is a constant on `ShowFile` — it's a model property, not a serializer concern.
- `UnknownFields` captures any JSON fields the current code doesn't recognize. This confirms a file is genuinely "from the future" and preserves unknown data for inspection during error reporting.

---

### 2. Load Path — Two-Phase (`ShowFileSerializer.cs`)

**Phase 1 — `LoadAsync`** (return type changes from `Task<ShowFile?>` to `Task<LoadResult?>`):
1. Deserialize as today.
2. `file.Version > CurrentVersion` → throw `ShowFileVersionTooNewException(file.Version)`.
3. `file.Version < CurrentVersion` → return `LoadResult { File = file, NeedsMigration = true }`.
4. `file.Version == CurrentVersion` → return `LoadResult { File = file, NeedsMigration = false }`.

**Phase 2 — `ApplyMigration`** (new static method):
1. Runs the migration chain (see below).
2. Sets `file.Version = CurrentVersion`.
3. Returns the migrated file.

**`MainViewModel` flow:**
1. Call `LoadAsync` → get `LoadResult`.
2. If `NeedsMigration`: show dialog — *"This file was saved with an older version of ShowCast. Upgrade it to the current format?"*
   - Yes → call `ApplyMigration`, continue loading.
   - No → treat as user-cancelled load.
3. If `ShowFileVersionTooNewException`: show modal — *"This file requires a newer version of ShowCast and cannot be opened."*
4. If migration throws: show error, leave original file untouched on disk.

`LoadResult` is a simple value type local to `ShowFileSerializer`:

```csharp
public record LoadResult(ShowFile File, bool NeedsMigration);
```

---

### 3. Migration Chain — `ShowFileSerializer.cs`

```csharp
static readonly List<Action<ShowFile>> Migrations = new()
{
    // index 0: v1 → v2  (populate as model evolves)
};

public static void ApplyMigration(ShowFile file)
{
    for (int v = file.Version; v < ShowFile.CurrentVersion; v++)
        Migrations[v - 1](file);
    file.Version = ShowFile.CurrentVersion;
}
```

- Each `Action<ShowFile>` handles exactly one version step.
- Running the loop from `file.Version` to `CurrentVersion` handles multi-step upgrades automatically.
- Migrations are plain C# operating on the fully typed `ShowFile` — no raw JSON manipulation.
- `Migrations` starts empty. The first breaking model change adds `Migrations[0]` and bumps `CurrentVersion` to `2`.

---

### 4. Save Path — `ShowFileSerializer.cs`

`SaveAsync` is structurally unchanged. One addition: strip `UnknownFields` before serializing so migrated files don't re-emit captured foreign fields.

```csharp
public static async Task SaveAsync(ShowFile file, string path)
{
    file.UnknownFields = null;  // don't round-trip fields from other format versions
    var tmp = path + ".tmp";
    // ... existing atomic write unchanged
}
```

New files created in-app initialize `Version = CurrentVersion` from the field default — no extra action needed.

---

## Error Handling

| Scenario | Behaviour |
|---|---|
| Version == current | Load normally |
| Version < current | Prompt → Yes: migrate + load / No: cancel |
| Version > current | Modal error, load aborted |
| Deserialization failure | Existing exception path (unchanged) |
| Migration throws | Error dialog, original file untouched |

---

## What This Is Not

- No automatic background save after migration — the user must explicitly save to commit the upgraded format.
- No backward compatibility — older app versions cannot open files saved by newer versions (by design, option B).
- No per-property migration tracking — version steps are coarse (one function per version bump), not fine-grained.

---

## Files Changed

| File | Change |
|---|---|
| `Core/ShowFile.cs` | Add `CurrentVersion`, `Version`, `UnknownFields` |
| `Core/ShowFileSerializer.cs` | Add `LoadResult`, `ShowFileVersionTooNewException`, `ApplyMigration`; extend `LoadAsync`; update `SaveAsync` |
| `ViewModels/MainViewModel.cs` | Handle `LoadResult.NeedsMigration` and `ShowFileVersionTooNewException` in file-open flow |
