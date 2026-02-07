# EOS SDK Conventions and Limitations

> Source: [dev.epicgames.com](https://dev.epicgames.com/docs/epic-online-services/working-with-the-eos-sdk/conventions-and-limitations)

## Naming Conventions

| Convention | Use |
|-----------|-----|
| **Create** | Allocates memory; pairs with `Release` |
| **Copy** | Retrieves cached data from prior `Query`; pairs with `Release` |
| **Release** | Frees memory from `Create` or `Copy` |
| **Query** | Async backend call; may be throttled or retried |
| **Get** | Cache values that don't require structure |

## Error Handling

| Error Code | Meaning |
|-----------|---------|
| `EOS_InvalidParameters` | Input parameters unset or invalid |
| `EOS_InvalidUser` | Operation requires a user but none provided |
| `EOS_MissingPermissions` | Backend rejected due to access restrictions |
| `EOS_UnrecognizedResponse` | SDK unable to parse backend response |
| `EOS_OperationWillRetry` | Connectivity impaired; SDK will retry |
| `EOS_IncompatibleVersion` | API version mismatch |
| `EOS_Auth_TokenInvalid` | Auth session expired; re-login needed |
| `EOS_TooManyRequests` | Rate limited (see below) |

## Strings

All strings (input and output) must be **UTF-8** encoded.

## Memory Management

- SDK functions allocate memory for callback data; freed when callback completes
- Make copies of callback data if you need to cache it
- `Copy` functions return data you own — must call `Release` before SDK shutdown
- C# SDK handles release automatically via wrappers

## Thread Safety

**The EOS SDK is not thread safe.** All SDK calls should come from the game's main thread. Avoid `async`, `await`, `Thread`, `Task`, or similar patterns for SDK calls.

## Thread Affinity

The SDK binds threads to specific CPU cores. Override defaults via `EOS_Initialize_ThreadAffinity`:

- `NetworkWork`
- `StorageIo`
- `WebSocketIo`
- `P2PIo`
- `HttpRequestIo`
- `EmbeddedOverlayMainThread`
- `EmbeddedOverlayWorkerThreads`

## Service Usage Limitations

EOS implements client request rate limiting and service usage quotas for ecosystem stability. **When APIs are integrated correctly, limits should never be reached.**

### Request Throttling

- **Client-side:** SDK self-throttles by rejecting calls with `EOS_TooManyRequests`
- **Server-side:** Backend rejects with HTTP 429; SDK handles retry internally

### Service Usage Quotas

- Per-deployment quotas ensure backend capacity
- Quotas are fixed or adjusted based on concurrent users (CCU)
- All quotas apply equally across all EOS products
- When limits are reached, new resource allocations fail until usage drops

> See [eos-limits.md](eos-limits.md) for the complete limits reference table.
