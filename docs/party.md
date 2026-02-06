# Party System

Persistent groups that follow the leader across games.

## Overview

Parties are independent of lobbies - they're persistent social groups that can move together between game sessions. When the party leader joins a game, members can automatically follow.

## Creating a Party

```csharp
var party = EOSPartyManager.Instance;

await party.CreatePartyAsync();

Debug.Log($"Party code: {party.PartyCode}");
```

## Joining a Party

```csharp
await party.JoinPartyAsync("ABC123");
```

## Inviting Players

```csharp
await party.InviteToPartyAsync(friendPuid);
await party.InviteFriendAsync("PlayerName");
```

## Following the Leader

When the party leader joins a game, members follow:

```csharp
// Leader joins a game lobby
await party.LeaderJoinGameAsync("1234");

// Members are notified
party.OnLeaderJoinedGame += (gameCode) =>
{
    Debug.Log($"Leader joined: {gameCode}");
};

// Members can follow
await party.FollowLeaderAsync();
```

### Follow Modes

```csharp
// Automatic - members auto-follow (default)
party.FollowMode = PartyFollowMode.Automatic;

// Confirm - prompt members before following
party.FollowMode = PartyFollowMode.Confirm;

// Ready Check - wait for everyone
party.FollowMode = PartyFollowMode.ReadyCheck;

// Manual - members click to follow
party.FollowMode = PartyFollowMode.Manual;
```

## Ready Checks

```csharp
// Leader starts ready check
party.StartReadyCheck();

// Members respond
party.OnReadyCheckStarted += (data) => { /* Show UI */ };
await party.RespondToReadyCheckAsync(true);

// Completion
party.OnReadyCheckCompleted += (allReady) =>
{
    if (allReady) Debug.Log("Everyone ready!");
};
```

## Party Leadership

```csharp
await party.PromoteToLeaderAsync(memberPuid);
await party.KickMemberAsync(memberPuid);

if (party.IsLeader) { /* Show leader controls */ }
```

## Party Chat

```csharp
await party.SendPartyChatAsync("Let's go!");

party.OnPartyChatReceived += (sender, message) =>
{
    Debug.Log($"[Party] {sender}: {message}");
};
```

## Leaving / Dissolving

```csharp
await party.LeavePartyAsync();
await party.DissolvePartyAsync();  // Leader only
```

## Events

```csharp
party.OnPartyCreated += () => { };
party.OnPartyJoined += () => { };
party.OnPartyLeft += () => { };
party.OnPartyDissolved += () => { };
party.OnMemberJoined += (member) => { };
party.OnMemberLeft += (puid) => { };
party.OnLeaderChanged += (oldPuid, newPuid) => { };
party.OnLeaderJoinedGame += (gameCode) => { };
party.OnFollowRequested += (request) => { };
party.OnReadyCheckStarted += (data) => { };
party.OnReadyCheckCompleted += (allReady) => { };
party.OnPartyChatReceived += (sender, message) => { };
party.OnSettingsChanged += (settings) => { };
```

## Party vs Lobby

| Feature | Party | Lobby |
|---------|-------|-------|
| Purpose | Social group | Game session |
| Persistence | Configurable | Session only |
| Max size | Configurable | 64 |
| Voice | Optional | Included |
| Survives game end | Yes | No |
