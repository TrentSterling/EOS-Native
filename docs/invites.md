# Invites & Presence

Cross-platform game invitations and online presence.

## Custom Invites

```csharp
var invites = EOSCustomInvites.Instance;

// Set invite payload (data sent with invite)
invites.SetPayload("lobby:1234");

// Send invite
await invites.SendInviteAsync(targetPuid);

// Receive invites
invites.OnInviteReceived += (senderId, payload) =>
{
    Debug.Log($"Invite from {senderId}: {payload}");
};

// Accept/Reject
await invites.AcceptInviteAsync(inviteId);
await invites.RejectInviteAsync(inviteId);
```

## Request to Join

```csharp
await invites.SendRequestToJoinAsync(targetPuid);

invites.OnRequestToJoinReceived += (requesterId) =>
{
    // Show join request UI
};
```

## Presence

Online status and rich presence (requires Epic Account):

```csharp
var presence = EOSPresence.Instance;

// Update your presence
await presence.UpdatePresenceAsync("In Lobby", "Playing Ranked");
```

## Player Info

```csharp
var userInfo = EOSUserInfo.Instance;

// Get display name
string name = await userInfo.GetDisplayNameAsync(epicAccountId);
```
