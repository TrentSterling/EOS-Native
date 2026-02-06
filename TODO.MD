# TODO

## High Priority

### Parity with transport demo
- Ensure all features from the FishNet EOS Native Transport demo work correctly with this package
- Quick match, lobbies, voice, join by code

## Medium Priority

## Low Priority

### Testing
- Test all platforms (Windows x64/x86, macOS, Linux, iOS, Android)
- Test ParrelSync clone login flow end-to-end
- Test voice in/out of lobbies
- Test quick match + auto-host flow

## Completed

- [x] Fix Quick Match to auto-host when no match found
- [x] Add voice toggle to Inspector host controls
- [x] ParrelSync support (device ID uniqueness per clone)
- [x] XAudio2 DLL path resolution for UPM layout
- [x] Native DLL loading path fix (Path.GetFullPath for virtual paths)
- [x] Dark theme UI overhaul for F1 overlay (foldouts, level bars, dark backgrounds)
- [x] Mic/speaker device selection (EOSVoiceManager + F1 overlay Voice tab)
- [x] Host migration toggle in Inspector and F1 overlay
- [x] Port EOSGlobalChatManager from FishNet
- [x] Port EOSReplayHighlights from FishNet
- [x] Port EOSReplayVoicePlayer from FishNet
- [x] Port EOSReplayVoiceRecorder from FishNet
- [x] Port EOSMetrics from FishNet
- [x] Create CHANGELOG.MD
- [x] Expand F1 overlay from 4 to 6 tabs (17 new sections ported from FishNet UI)
- [x] Auto-create EOSLobbyChatManager singleton
- [x] All foldouts default to expanded
- [x] Fix ranked matchmaking OnDestroy crash during shutdown
- [x] Port EOSAfkManager from FishNet (framework-agnostic)
- [x] Port EOSVoteKickManager from FishNet (lobby attributes)
- [x] Port EOSMapVoteManager from FishNet (lobby attributes)
- [x] Port EOSRematchManager from FishNet (lobby system)
- [x] Port EOSBackfillManager from FishNet (lobby search)
- [x] Add KickMemberAsync to EOSLobbyManager
- [x] Write reputation.md docs
- [x] Write match-history.md docs
- [x] Write globalchat.md docs
- [x] Write highlights.md docs
- [x] Write votekick.md, mapvote.md, backfill.md, rematch.md, afk.md docs
- [x] Update _sidebar.md with Lobby Systems section
- [x] Player profile popup in F1 overlay (info button per lobby member)
- [x] Inspector lobby name field (was unnamed)
- [x] Inspector max players field (was hardcoded to 4, now 2-64)
- [x] ParrelSync docs page (parrelsync.md)
