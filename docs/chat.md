# Text Chat

Lobby-based text chat and global chat channels.

## Sending Messages

```csharp
var chat = EOSLobbyChatManager.Instance;

// Send a message to the lobby
await chat.SendChatMessageAsync("Hello everyone!");
```

## Receiving Messages

```csharp
chat.OnChatMessageReceived += (sender, message) =>
{
    Debug.Log($"{sender}: {message}");
};
```

## Chat History

Messages persist and reload when rejoining the same lobby.

### Manual Control

```csharp
// Get chat history
var history = await chat.GetChatHistoryAsync();

// Clear history
chat.ClearHistory();
```

## Display Name

```csharp
// Set your display name for chat
chat.DisplayName = "PlayerOne";
```

## Rate Limiting

EOS enforces rate limits on lobby attribute updates (which includes chat):

| Limit | Value |
|-------|-------|
| Attribute updates | 100/min |

The chat system works within these limits.

## UI Integration

The F1 debug panel includes a chat interface in the Lobbies tab. For custom UI:

```csharp
// Display messages
foreach (var msg in chat.Messages)
{
    DisplayMessage(msg.SenderName, msg.Content, msg.Timestamp);
}

// Send from input field
public void OnSendClicked()
{
    if (!string.IsNullOrEmpty(inputField.text))
    {
        _ = chat.SendChatMessageAsync(inputField.text);
        inputField.text = "";
    }
}
```

## Global Chat

`EOSGlobalChatManager` provides lobby-independent chat channels for global/world chat, trade chat, etc.

### Joining Channels

```csharp
var globalChat = EOSGlobalChatManager.Instance;

// Join a channel
await globalChat.JoinChannelAsync("general");
await globalChat.JoinChannelAsync("trade");

// Leave a channel
await globalChat.LeaveChannelAsync("trade");

// Check membership
bool inGeneral = globalChat.IsInChannel("general");
```

### Sending Messages

```csharp
await globalChat.SendMessageAsync("general", "Hello world!");
```

### Receiving Messages

```csharp
globalChat.OnMessageReceived += (message) =>
{
    Debug.Log($"[{message.Channel}] {message.SenderName}: {message.Message}");
};
```

### Channel Management

```csharp
// Get all joined channels
var channels = globalChat.GetSubscribedChannels();

// Get users in a channel
var users = globalChat.GetChannelUsers("general");

// Get message history
var history = globalChat.GetMessageHistory("general", count: 50);

// Mute a user
globalChat.MuteUser(puid);
globalChat.UnmuteUser(puid);
```

### Events

```csharp
globalChat.OnChannelJoined += (channelName) => { };
globalChat.OnChannelLeft += (channelName) => { };
globalChat.OnMessageReceived += (message) => { };
globalChat.OnUserMuted += (puid) => { };
globalChat.OnUserUnmuted += (puid) => { };
```

### Limits

| Limit | Value |
|-------|-------|
| Max channels | 10 (configurable) |
| Max message length | 500 chars |
| Max history per channel | 100 messages |
