# Cloud Storage

EOS provides two storage systems: Player Data Storage (per-player) and Title Storage (shared).

## Player Data Storage

400MB per player for save data, settings, and personal data.

### Writing Data

```csharp
var storage = EOSPlayerDataStorage.Instance;

await storage.SaveFileAsync("save.json", jsonString);
```

### Reading Data

```csharp
var data = await storage.LoadFileAsync("save.json");
```

### Listing Files

```csharp
var files = await storage.GetFileListAsync();

foreach (var file in files)
{
    Debug.Log($"{file.Filename} - {file.Size} bytes");
}
```

### Deleting Files

```csharp
await storage.DeleteFileAsync("old_save.json");
```

## Title Storage

Read-only storage for game data that all players can access. Content is uploaded through the EOS Developer Portal.

```csharp
var titleStorage = EOSTitleStorage.Instance;

// Read shared config
var config = await titleStorage.ReadFileAsync("config.json");
```

### Use Cases

- Game configuration
- Level data
- Localization files
- Patch notes

## Events

```csharp
storage.OnFileOperationComplete += (filename, success) => { };
storage.OnFileListUpdated += (files) => { };
```

## Storage Limits

### Player Data Storage

| Limit | Value |
|-------|-------|
| Total storage | 400 MB per player |
| Max file size | 200 MB |
| Max files | No limit |

### Title Storage

| Limit | Value |
|-------|-------|
| Total storage | Varies by plan |
| Max file size | 200 MB |
| Managed via | Developer Portal |
