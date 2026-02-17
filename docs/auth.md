# Authentication

EOS Native supports multiple authentication methods.

## Auth Methods

| Method | Description | Use Case |
|--------|-------------|----------|
| Device Token | Anonymous, auto-creates device ID | Quick testing, anonymous play |
| Epic Account | Login via Epic overlay | Full social features |
| Persistent Auth | Silent re-login across sessions | Returning players |
| Smart Login | Persistent -> device token fallback | Production recommended |
| External Auth | Platform-verified identity (Steam, Oculus, etc.) | VR, console, storefront |

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

## External Auth Login

Login using an external platform credential. This is the standard approach for platform-verified identity (VR headsets, Steam, consoles, mobile, etc.).

### Supported Providers

| Provider | ExternalCredentialType | Token Format |
|----------|----------------------|--------------|
| Meta/Oculus | `OculusUseridNonce` | `"{userId}\|{nonce}"` |
| Steam | `SteamSessionTicket` | Hex-encoded session ticket |
| Discord | `DiscordAccessToken` | OAuth2 access token |
| Apple | `AppleIdToken` | Sign in with Apple JWT |
| Google | `GoogleIdToken` | Google Sign-In ID token |
| PlayStation (PSN) | `PsnIdToken` | PSN auth code |
| Xbox Live | `XblXstsToken` | XSTS token |
| Nintendo | `NintendoIdToken` | NSA ID token |
| GOG Galaxy | `GogSessionTicket` | Galaxy session ticket |
| OpenID | `OpenidAccessToken` | OpenID Connect token |

### Generic Usage

```csharp
// You obtain the token from the platform SDK yourself
string steamTicket = GetSteamSessionTicket(); // your code

var result = await EOSManager.Instance.LoginWithExternalAuthAsync(
    ExternalCredentialType.SteamSessionTicket,
    steamTicket,
    "PlayerName"
);

if (result == Result.Success)
    Debug.Log($"Logged in: {EOSManager.Instance.LocalProductUserId}");
```

### Meta/Oculus (Quest VR)

Convenience method that formats the `"{userId}|{nonce}"` token automatically.

```csharp
// 1. Get nonce from Meta Platform SDK (your code)
string oculusUserId = "1234567890";
string nonce = GetOculusNonce(); // Platform.User.GetUserProof()

// 2. Login with EOS Native
var result = await EOSManager.Instance.LoginWithOculusNonceAsync(
    oculusUserId,
    nonce,
    "VRPlayer"
);
```

> **Note:** You must enable the "User ID" feature in the [Oculus Developer Dashboard](https://developer.oculus.com/) for nonce verification to work. EOS verifies the nonce directly with Meta's servers.

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
