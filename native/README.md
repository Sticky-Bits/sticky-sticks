# Bundled upstream dependencies

- `SDL3.dll`: official SDL 3.4.16 Windows x64 release.
- `gamecontrollerdb.txt`: unmodified SDL_GameControllerDB at revision `5a12daa568d19344f9b6e9286ef5929833b25c7c`.
- `libusb-1.0.dll`: official libusb 1.0.30 Windows x64 MinGW release, dynamically loaded for GameCube adapters. Matching source is bundled under `licenses/`.

These files come from public upstream projects.
See [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md) for source URLs, SHA-256 checksums, and licence links.
The build copies these files beside the executable. SDL/database use zlib; libusb uses LGPL-2.1-or-later, with its licence and source archive copied under `licenses/`.
