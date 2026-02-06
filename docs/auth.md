# Authentication

EOS Native supports multiple authentication methods.

## Auth Methods

| Method | Description | Use Case |
|--------|-------------|----------|
| Device Token | Anonymous, auto-creates device ID | Quick testing, anonymous play |
| Epic Account | Login via Epic overlay | Full social features |
| Persistent Auth | Silent re-login across sessions | Returning players |
| Smart Login | Persistent -> device token fallback | Production recommended |

## Device Token Login

Anonymous login that creates a unique device ID. No user interaction required.

```csharp
var result = await EOSManager.Instance.LoginWithDeviceTokenAsync("PlayerName");

if (result == Result.Success)
    Debug.Log($"Logged in as: {EOSManager.Instance.LocalProductUserId}");
```

## Epic Account Login

Full Epic Account login with overlay. Required for friends, presence, and other social features.

```csharp
var result = await EOSManager.Instance.LoginWithEpicAccountAsync();

if (result == Result.Success)
    Debug.Log($"Epic Account: {EOSManager.Instance.LocalEpicAccountId}");
```

## Persistent Auth

Silent re-login using cached credentials. No user interaction if credentials are still valid.

```csharp
var result = await EOSManager.Instance.LoginWithPersistentAuthAsync();

if (result == Result.Success)
    Debug.Log("Silently logged in!");
else
    Debug.Log("Need manual login");
```

## Smart Login (Recommended)

Tries persistent auth first, falls back to device token. Best for production use.

```csharp
var result = await EOSManager.Instance.LoginSmartAsync();
// Always succeeds (device token is the fallback)
```

## Auto Login

Enable in the EOSManager Inspector:
- **Auto Initialize** - Initialize SDK on Start
- **Auto Login** - Login automatically after init

## Logout

```csharp
// Logout Connect login
await EOSManager.Instance.LogoutAsync();

// Logout Epic Account
await EOSManager.Instance.LogoutEpicAccountAsync();
```

## Events

```csharp
var eos = EOSManager.Instance;

eos.OnInitialized += () => { };
eos.OnLoginSuccess += () => { };
eos.OnLoginFailed += (result) => { };
eos.OnLogout += () => { };
eos.OnAuthExpiring += () => { };
```

## State Checking

```csharp
var eos = EOSManager.Instance;

bool initialized = eos.IsInitialized;
bool loggedIn = eos.IsLoggedIn;
bool epicLinked = eos.IsEpicAccountLoggedIn;

string puid = eos.LocalProductUserId?.ToString();
string epicId = eos.LocalEpicAccountId?.ToString();
```
