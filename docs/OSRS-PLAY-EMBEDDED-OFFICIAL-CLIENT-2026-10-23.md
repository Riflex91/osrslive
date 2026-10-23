# OSRS Play v0.8.0 – Official Client Window Hosting
Date 2026-10-23

## Goal
Show the **real official OSRS C++ client window** inside OSRS Play,
with an actual Jagex Launcher login and live server/network session,
without claiming to have reverse-engineered the Jagex authentication or
game protocol.

## How
- The `Jagex Login` button launches the **existing official Jagex
  Launcher binary** from documented installation folders.
- Only the Jagex Launcher handles passwords, MFA, Jagex Account SSO
  and character selection. OSRS Play has no credential fields.
- Once Jagex Launcher starts `osclient.exe` and the player is logged
  in, the `Client einbetten` action enumerates visible Windows
  top-level windows with exactly process name `osclient`.
- When the user selects an existing official client window,
  the host saves its parent, frame style and location; Win32
  `SetParent` temporarily docks it into the OSRS Play WinForms panel.
- Releasing or closing OSRS Play restores the original frame/parent,
  does **not** terminate Jagex's game, and does **not** log out.
- The local sandbox is a separate tab and remains available for
  deterministic bot tests, autonomously buying/selling *fictitious* ores.
- All log events distinguish genuine `OFFICIAL_CLIENT WINDOW_ATTACHED`
  from *local* `MINI_WORLD` actions. No official game state is emitted
  or claimed simply because a window is embedded.

## Limitations
- Windows API cross-process SetParent is not a supported Jagex
  plug-in contract. GPU hardware accelerated windows, different
  DPI contexts and focus changes may cause failures or render issues.
- A live screenshot or hosted HWND does NOT supply the state machine,
  object IDs, inventory, XP, position or authorized input adapter
  required to operate the autonomous Bot against the live game.
- The `Live-Agent` control is disabled on the official tab.
  This prevents the false impression that pressing Start would
  train an official OSRS account.
- No Jagex protocol reverse engineering, MFA interception, client
  patching or injected DLL is performed.

## Windows acceptance checklist
1. Jagex Launcher and official client installed and functional
2. Play Now / authenticated game session visible
3. Attach the running `osclient.exe` in OSRS Play
4. Check game render, focus, clicks, world selection and resize
5. Release and check original OSRS window remains usable
6. Review `OFFICIAL_CLIENT` diagnostics. Avoid sharing secrets.
7. For live bot automation, establish a documented and authorized
   game-state/action interface before enabling automated actions

## References
https://oldschool.runescape.wiki/w/Jagex_Launcher
https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setparent
https://legal.jagex.com/docs/rules/rules-of-runescape
