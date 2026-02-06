# Lobbies

Lobbies are the core of EOS multiplayer. They manage player presence, attributes, voice channels, and serve as the connection point for networking.

## Creating a Lobby

### Simple Create

```csharp
var lobby = EOSLobbyManager.Instance;

// Auto-generates 4-digit code
var result = await lobby.CreateLobbyAsync(new CreateLobbyOptions
{
    LobbyName = "My Room",
    MaxPlayers = 4
});

Debug.Log($"Code: {lobby.CurrentLobby.JoinCode}");
```

### With Voice Enabled

```csharp
var result = await lobby.CreateLobbyAsync(new CreateLobbyOptions
{
    LobbyName = "Voice Room",
    MaxPlayers = 8,
    IsPublic = true,
    EnableVoice = true
});
```

## Joining a Lobby

### By Code

```csharp
var result = await EOSLobbyManager.Instance.JoinLobbyByCodeAsync("1234");

if (result == Result.Success)
    Debug.Log("Joined!");
```

## Quick Match

Finds an available lobby or creates one:

```csharp
await EOSLobbyManager.Instance.QuickMatchAsync();
```

## Searching for Lobbies

```csharp
var lobbies = await EOSLobbyManager.Instance.SearchLobbiesAsync();

foreach (var lobby in lobbies)
{
    Debug.Log($"{lobby.LobbyName} ({lobby.JoinCode}) - {lobby.Members.Count}/{lobby.MaxPlayers}");
}
```

## Lobby Attributes

Custom data attached to lobbies:

### Setting Attributes (Host Only)

```csharp
var lobby = EOSLobbyManager.Instance;

await lobby.UpdateAttributeAsync("map", "dust2");
await lobby.UpdateAttributeAsync("difficulty", "hard");
```

### Reading Attributes

```csharp
string map = await lobby.GetAttributeAsync("map");
```

## Leaving a Lobby

```csharp
await EOSLobbyManager.Instance.LeaveLobbyAsync();
```

## State Checking

```csharp
var lobby = EOSLobbyManager.Instance;

if (lobby.IsInLobby)
    Debug.Log($"In lobby: {lobby.CurrentLobby.JoinCode}");

if (lobby.IsOwner)
    Debug.Log("I am the host");
```

## Events

```csharp
var lobby = EOSLobbyManager.Instance;

lobby.OnLobbyJoined += (lobbyData) => { };
lobby.OnLobbyLeft += () => { };
lobby.OnMemberJoined += (member) => { };
lobby.OnMemberLeft += (puid) => { };
lobby.OnOwnerChanged += (newOwnerPuid) => { };
lobby.OnLobbyUpdated += (lobbyData) => { };
```

## Service Limits

| Limit | Value |
|-------|-------|
| Max players per lobby | 64 |
| Max lobbies per user | 16 |
| Create/Join rate | 30/min |
| Attribute updates | 100/min |
| Attribute value length | 1000 chars |
| Voice participants | 64 (SDK 1.16+) |
