# v0.8.1: Exclusive official-window hosting, no client binary patch

## Reproduced field failure (2026-10-23)
v0.8.0 log: launcher PID 10212, game PID 7692. The first official game
window was attached at 21:06:35.496 and alternated with launcher
approximately every 1.2 seconds. No error-level event was recorded,
but the UI clearly wasn't stable.

## Cause
`EnumWindows` lists top-level windows. After `SetParent`,
`osclient.exe` becomes a child HWND and is NOT eligible for that
enumeration. The previous scan treated this as 'no game', detached it,
re-attached the launcher, then found the game again.

## Change
- Pure transition logic enforces KeepGame even when the game is absent
  from top-level enumeration but the hosted HWND remains alive.
- Launcher-to-Game transition remains enabled and one-way for a live HWND.
- Host verifies ownership by actual Windows process rather than title,
  and validates attachment using the parent HWND.
- Embedded game's independent taskbar hint (`WS_EX_APPWINDOW`) is
  temporarily cleared, not patched in game code.
- Parent, window rectangle, standard and extended Win32 styles restored
  when released. OSRS process is not closed or logged out.
- Resizing now happens only when the host panel actually changes size.
- Regression test repeats the exact state combination observed in
  the failure 25 times and asserts the official game is retained.

## Acceptance criteria
1. `GAME_WINDOW_READY` is logged once during normal login handoff.
2. While game window exists, no automatic `WINDOW_DETACHED kind=Game`
   and `WINDOW_ATTACHED kind=Launcher` cycle.
3. The original gameplay window is visually inside OSRS Play, not a
   second separately rendered desktop window.
4. Ordinary clicks, keyboard input, window resizing and focus work.
5. Releasing the window restores a usable independent official client.
6. No credentials or account tokens enter OSRS Play.
7. Windows self-tests and legacy mining/GE regression suite remain green.

## Limitations and policies
Cross-process window hosting is not guaranteed across GPU/DPI modes.
This is an OS-owned HWND presentation change, **not** a modified Jagex
client binary. This does not enable automated official gameplay.
Official game-code patching is unnecessary and not performed.
Jagex's general rules disallow modified versions of game code:
https://legal.jagex.com/docs/rules/rules-of-runescape
Windows API reference:
https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setparent
