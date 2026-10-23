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
- Login flow starts from OSRS Play and hosts the original `JagexLauncher.exe`
  window within its Official OSRS tab. User clicks `Play Now` here.
- Once Jagex Launcher starts `osclient.exe`, OSRS Play automatically
  transitions that same host surface to the official game process.
- Only exact process names `JagexLauncher`, `Jagex Launcher`, or
  `osclient` can be hosted. It never adopts random browser windows.
- When the user selects an existing official client window,
  the host saves its parent, frame style and location; Win32
  `SetParent` temporarily docks it into the OSRS Play WinForms panel.
- The launcher is minimized after successful game hosting so the user
  remains in the OSRS Play window for the normal gameplay path.
- Releasing or closing OSRS Play restores the original game frame/parent,
  does **not** terminate Jagex's game, and does **not** log out.
- The local sandbox is a separate tab and remains available for
  deterministic bot tests, autonomously buying/selling *fictitious* ores.
- All log events distinguish genuine `OFFICIAL_CLIENT WINDOW_ATTACHED`
  from *local* `MINI_WORLD` actions. No official game state is emitted
  or claimed simply because a window is embedded.

## Limitations
- Windows API cross-process SetParent is not a supported Jagex
  plug-in contract. GPU accelerated windows, DPI context changes and
  focus transitions may prevent embedded interaction.
- Jagex may present browser-based OAuth/MFA or system consent pop-ups in
  separate windows. OSRS Play does **not** capture these or intercept
  browser session tokens, so 100% in-window login is unverified.
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
2. Click Anmeldung starten and verify the Launcher is visible inside OSRS Play
3. Authenticate using Jagex's actual login UI. Note whether external
   browser/MFA prompts appear
4. Press Play Now inside the embedded launcher; verify auto-transition
   to `osclient.exe` in the same panel
5. Check game render, focus, clicks, world selection and resize
6. Release and check original OSRS window remains usable
7. Review `OFFICIAL_CLIENT` diagnostics. Avoid sharing secrets.
7. For live bot automation, establish a documented and authorized
   game-state/action interface before enabling automated actions

## References
https://oldschool.runescape.wiki/w/Jagex_Launcher
https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setparent
https://legal.jagex.com/docs/rules/rules-of-runescape
