# OSRS Play 0.9.0 – Live-only-Migration

## In der ausführbaren EXE

- Nur offizieller Jagex Launcher und osclient.exe, eingebettet mittels Win32 SetParent
- Kein Sandbox-Reiter, kein lokaler TCP-Spielserver, keine Offline-Agenten oder Kampagnen
- Keine simulierten Game-Ticks oder künstlichen Inventar-/GP-Werte im Log
- F2P-Ping, öffentliche Jagex HiScores, Diagnose, Updatefunktion bleiben
- --scenario-tests wurde deaktiviert (Exit 2)
- Live-Bot-Steuerung ist mangels tatsächlicher offizieller Spielzustands- und Aktionsschnittstelle deaktiviert

## Verifizierbar

Windows-Selbsttests prüfen Host-Prozessfilter, Fensterzustandsübergänge, Passwort-Redaktion, offizielle F2P-Filter, öffentliche Hiscore-Parser und Updater-Manifest. Ein echtes Jagex-Login, ein echter OSRS-Game-Tick oder eine ausgeführte Botaktion sind durch diese Tests nicht nachgewiesen.

## Altdateien

Vorherige lokal gespeicherte synthetische Checkpoints in %LOCALAPPDATA%/OSRSLiveDiagnostics werden nicht automatisch gelöscht, damit keine vorhandenen Dateien unaufgefordert verloren gehen. Der neue Client lädt oder verwendet sie nicht.
