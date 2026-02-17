# Security

## EOS Config Values Are NOT Secret

Your EOS configuration values — `ProductId`, `SandboxId`, `DeploymentId`, `ClientId`, and `ClientSecret` — are **not secret**. This is by design:

- They appear in **plaintext in network requests** to Epic's servers
- Anyone running Wireshark or Fiddler on a game client can read them
- They grant **application-level access**, not user-level access
- They identify your game to EOS, similar to a public API key

**Do not waste time** trying to hide these values. They cannot be used to impersonate users or access user data without proper authentication.

### What About ClientSecret?

Despite the name, `ClientSecret` is a client credential — it identifies your application, not a user. It is transmitted in Connect login requests and is visible to anyone inspecting network traffic. This is the same pattern used by OAuth2 public clients.

### What About EncryptionKey?

The `EncryptionKey` is used for **PlayerDataStorage** and **TitleStorage** encryption. It prevents Epic from reading your stored data at rest. It does **not** protect network traffic (EOS uses TLS for that). If you don't use PlayerDataStorage, the encryption key value doesn't matter — many projects use all-1s (`111...111`).

## Where Real Security Comes From

EOS security is based on **platform identity verification**:

1. A user authenticates with their platform (Meta, Steam, PlayStation, etc.)
2. The platform SDK gives you a **signed token or nonce**
3. You pass that token to EOS via `Connect.Login`
4. EOS **verifies the token directly with the platform** server-side
5. If valid, EOS issues a `ProductUserId` — the user's verified identity

No amount of client key exposure can bypass this flow. The security comes from the platform's authentication, not from hiding config values.

## Obfuscation Options (Optional)

If your organization requires credential obfuscation for compliance or policy reasons, here are two approaches:

### Option 1: Custom Init DLL

Build a native DLL (C/C++) that calls `EOS_Platform_Create` internally with hardcoded credentials. Your Unity code calls the DLL instead of passing credentials in C#. The values are still in the binary but not in plaintext C# strings.

### Option 2: Side-Loaded Config

Load credentials at runtime from a separate encrypted file or environment variable instead of a ScriptableObject. This moves the values out of the main assembly but they still must exist in memory at init time.

Both approaches add complexity without meaningful security improvement — they only raise the bar from "read a config file" to "attach a debugger."

## Further Reading

- [PlayEveryWare EOS Plugin Security Notes](https://github.com/PlayEveryWare/eos_plugin_for_unity/blob/development/Documentation/Sandboxes%2C%20Deployment%2C%20and%20Credentials.md) — confirms these values are not secret
- [EOS Connect Interface Documentation](https://dev.epicgames.com/docs/game-services/eos-connect-interface) — external auth flow details
- [Authentication](auth.md) — all login methods including external auth
