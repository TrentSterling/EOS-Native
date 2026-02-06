# TODO

## High Priority

### Parity with transport demo
- Ensure all features from the FishNet EOS Native Transport demo work correctly with this package
- Quick match, lobbies, voice, join by code

## Medium Priority

### ParrelSync documentation
- ParrelSync support is already built-in (reflection-based in EOSManager, preprocessor-based in EOSPlatformHelper)
- Each clone gets unique device ID via project path hash
- Document this more prominently so users know it works out of the box

### Inspector improvements
- Consider adding max players field to inspector lobby controls (currently hardcoded to 4)
- Consider adding lobby name field to inspector controls

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
