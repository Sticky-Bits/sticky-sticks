# Third-party components

Sticky Sticks source is MIT licensed. The following unmodified upstream files
retain their own licences; they are not relicensed under MIT.

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

These dependencies are included so a checkout can build without obtaining files
from a game installation. Keep these notices and the licence files with binaries.
No Rivals of Aether 2 executable, extracted assets, or private analysis is included.

## libusb 1.0.30

- Project: https://github.com/libusb/libusb
- Copyright: libusb contributors; see the included source archive for individual notices.
- Licence: LGPL-2.1-or-later; [full text](licenses/libusb.txt).
- Official binary archive: https://github.com/libusb/libusb/releases/download/v1.0.30/libusb-1.0.30.7z
- Archive SHA-256: `7fb1dfec805b97983763d7d0ae244320da12add1003d4249c96cc4d586398c79`
- Included unmodified binary: `native/libusb-1.0.dll`, from `MinGW64/dll/libusb-1.0.dll`.
- DLL SHA-256: `5bd409849825009b6fe25861a6147f76d256aab248f07049d78387e3bff12d94`
- Matching upstream source: https://github.com/libusb/libusb/releases/download/v1.0.30/libusb-1.0.30.tar.bz2
- Source SHA-256: `fea36f34f9156400209595e300840767ab1a385ede1dc7ee893015aea9c6dbaf`
- The complete source archive is included in `licenses/libusb-1.0.30-source.tar.bz2` and copied to published builds. It includes upstream build files and instructions.

libusb is dynamically loaded; users may replace the DLL with a compatible modified build. Keep its licence, notices and matching source archive with redistributed binaries. Sticky Sticks imposes no restriction on modifying/replacing this library or reverse engineering to debug such modifications.

## Protocol references

The Nintendo Switch Pro implementation was developed using the publicly documented
[controller protocol](https://github.com/dekuNukem/Nintendo_Switch_Reverse_Engineering),
including USB handshake and SPI stick-calibration documentation.

The GameCube adapter protocol reference is [Dolphin's GCAdapter backend](https://github.com/dolphin-emu/dolphin/blob/master/Source/Core/InputCommon/GCAdapter.cpp). Sticky Sticks uses its own C# implementation of the USB protocol; no Dolphin code is bundled.

Rivals of Aether 2, Nintendo, Xbox, and Steam names identify compatibility only.
Sticky Sticks is an independent project and is not affiliated with their owners.
