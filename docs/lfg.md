# LFG (Looking for Group)

Post-based matchmaking system for finding players or groups.

## Creating a Post

```csharp
var lfg = EOSLFGManager.Instance;

await lfg.CreatePostAsync(LFGType.LookingForPlayers, "Need 2 for ranked", new LFGOptions
{
    GameMode = "ranked",
    Region = "us-east",
    MinRank = 10
});
```

## Searching Posts

```csharp
var posts = await lfg.SearchPostsAsync(new LFGSearchFilters
{
    GameMode = "ranked",
    Region = "us-east"
});

foreach (var post in posts)
{
    Debug.Log($"{post.Title} by {post.OwnerName} - {post.Status}");
}
```

## Post Types

| Type | Description |
|------|-------------|
| LookingForPlayers | Host seeking members |
| LookingForGroup | Player seeking a group |

## Post Status

| Status | Description |
|--------|-------------|
| Open | Accepting interest |
| Full | No more spots |
| Closed | Manually closed |
| InGame | Already playing |

## Expressing Interest

```csharp
await lfg.SendInterestAsync(postId);
await lfg.AcceptInterestAsync(playerId);
```

## Managing Posts

```csharp
await lfg.UpdatePostAsync(/* updated options */);
await lfg.ClosePostAsync();
await lfg.DeletePostAsync();
```
