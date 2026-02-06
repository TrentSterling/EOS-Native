# Teams & Clans

Persistent clans with membership, roles, and chat.

## Creating a Clan

```csharp
var clan = EOSClanManager.Instance;

await clan.CreateClanAsync("Awesome Squad", "AWE");
```

## Joining / Leaving

```csharp
await clan.JoinClanAsync(clanId);
await clan.LeaveClanAsync();
```

## Roles

| Role | Permissions |
|------|------------|
| Owner | Full control, disband |
| Officer | Kick, manage members |
| Member | Chat, participate |

```csharp
await clan.ChangeRoleAsync(memberPuid, ClanRole.Officer);
```

## Member Management

```csharp
await clan.KickMemberAsync(puid);
await clan.BanMemberAsync(puid);
```

## Clan Chat

```csharp
await clan.SendClanChatAsync("Hello clan!");

clan.OnChatMessageReceived += (sender, message) =>
{
    Debug.Log($"[Clan] {sender}: {message}");
};
```

## Events

```csharp
clan.OnClanCreated += () => { };
clan.OnClanJoined += () => { };
clan.OnClanLeft += () => { };
clan.OnMemberJoined += (member) => { };
clan.OnMemberLeft += (puid) => { };
clan.OnMemberRoleChanged += (puid, role) => { };
clan.OnChatMessageReceived += (sender, message) => { };
```

> **Note:** Clan data uses client-writable cloud storage. Implement server-side validation for production.
