# Third-party components

Sticky Sticks source is MIT licensed. The following unmodified upstream files
retain their own zlib licences; they are not relicensed under MIT.

## SDL 3.4.16

- Project: https://github.com/libsdl-org/SDL
- Official Windows x64 archive: https://github.com/libsdl-org/SDL/releases/download/release-3.4.16/SDL3-3.4.16-win32-x64.zip
- Archive SHA-256: `4217944b4e51457af4a59c82d883f8443b3e65964b2acd8943484c492756c4b6`
- Included file: `native/SDL3.dll`
- DLL SHA-256: `1f98969319302a100931f4385e5918a0bd53ab07773040682d22e7edb54858c0`
- Licence: [licenses/SDL.txt](licenses/SDL.txt)

## SDL GameControllerDB

- Project: https://github.com/mdqinc/SDL_GameControllerDB
- Pinned revision: `5a12daa568d19344f9b6e9286ef5929833b25c7c`
- Included file: `native/gamecontrollerdb.txt`
- Source: https://raw.githubusercontent.com/mdqinc/SDL_GameControllerDB/5a12daa568d19344f9b6e9286ef5929833b25c7c/gamecontrollerdb.txt
- Database SHA-256: `07ec5b753e685c4829987919b27905cc3c45c8e18c11b3046e275fcf38b0f3cb`
- Licence: [licenses/SDL_GameControllerDB.txt](licenses/SDL_GameControllerDB.txt)

Both dependencies are included so a checkout can build without obtaining files
from a game installation. Keep these notices and the licence files with binaries.
No Rivals of Aether 2 executable, extracted assets, or private analysis is included.

## Protocol references

The Nintendo Switch Pro implementation was developed using the publicly documented
[controller protocol](https://github.com/dekuNukem/Nintendo_Switch_Reverse_Engineering),
including USB handshake and SPI stick-calibration documentation.

Rivals of Aether 2, Nintendo, Xbox, and Steam names identify compatibility only.
Sticky Sticks is an independent project and is not affiliated with their owners.
