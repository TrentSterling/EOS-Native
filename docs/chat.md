# Text Chat

Lobby-based text chat with cloud-persisted history.

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
