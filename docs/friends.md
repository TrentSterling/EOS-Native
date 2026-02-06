# Friends System

EOS friends list management. Requires Epic Account login for full functionality.

## Basic Usage

```csharp
var friends = EOSFriends.Instance;

// Refresh friends list
await friends.RefreshFriendsAsync();

// Get all friends
var friendList = friends.Friends;
int count = friends.FriendCount;
```

## Friend Requests

```csharp
// Send friend request
await friends.SendFriendRequestAsync(targetAccountId);

// Accept incoming request
await friends.AcceptFriendRequestAsync(requestAccountId);

// Decline request
await friends.DeclineFriendRequestAsync(requestAccountId);

// Remove friend
await friends.RemoveFriendAsync(friendAccountId);
```

## Block/Unblock

```csharp
await friends.BlockUserAsync(targetAccountId);
await friends.UnblockUserAsync(targetAccountId);
```

## Events

```csharp
friends.OnFriendsListUpdated += () => { };
friends.OnFriendAdded += (accountId) => { };
friends.OnFriendRemoved += (accountId) => { };
friends.OnFriendStatusChanged += (accountId, status) => { };
```

## Player Registry

For PUID/name caching (works without Epic Account):

```csharp
var registry = EOSPlayerRegistry.Instance;

// Get player name from PUID
string name = registry.GetPlayerName(puid);
```

## UI Integration

The F1 debug panel Social tab shows friends with status indicators.
