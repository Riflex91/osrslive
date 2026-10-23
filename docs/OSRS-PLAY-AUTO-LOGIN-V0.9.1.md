# OSRS Play 0.9.1 – Auto-Start der offiziellen Jagex-Anmeldung

## Fehler aus Windows-Praxislog
v0.9.0 am 23. Oktober 2026, 21:36:31–21:36:51:
`HOST_READY`, `UPDATER CHECK_FINISHED` und Performance-Proben
wurden ausgegeben; **kein** `LAUNCHER_STARTED`,
`LOGIN_WINDOW_READY` oder `GAME_WINDOW_READY`.

## Ursache
Die `Shown`-Routine führte nur `Updater.CheckAndUpdateAsync` aus.
`OpenJagexLogin` war nur am UI-Button angebunden. Zusätzlich
startete `autoHostOfficial=false` und es gab somit keine automatische
Fensterübernahme.

## Korrektur
Nach dem Update-Check wird einmal pro `MainWindow` der offizielle
Jagex-Anmeldeablauf gestartet. Bei automatischem Update mit Neustart
erfolgt kein Launcherstart mehr im alten Prozess.

Entscheidungsmatrix `OfficialStartupPolicy.Decide`:
- bereits eingebettetes originales Fenster: beibehalten
- vorhandenes originales Spiel: einbetten
- vorhandener Launcher: einbetten
- mehrere passende Fenster: Nutzer muss wählen
- kein vorhandenes Fenster: installierten JagexLauncher.exe starten
- nicht installiert: Fehlerstatus protokollieren; kein Fake-Login

## Tests
`OfficialStartupPolicyTests` prüft sämtliche Fälle der Entscheidungsmatrix.
Der Windows-CI-Build prüft weiterhin den Original-Window-Host, F2P und
die Live-only-Diagnostik. CI kann keine authentifizierte Kontositzung
oder ein echtes Jagex-Fenster darstellen.

## Abnahme auf Windows
1. OSRS Play v0.9.1 starten, **keine** Jagex-Schaltfläche betätigen
2. Nach Updateprüfung `AUTO_LOGIN_START` im Log prüfen
3. Automatischen Start bzw. Einbettung des Jagex Launcher prüfen
4. Dort authentifizieren; nach `Play Now` das originale `osclient.exe`
   im selben OSRS-Play-Bereich beobachten
5. Kein ständiger Wechsel Game/Launcher, keine Offline-Events
6. Werden mehrere Launcher gefunden: keine zusätzlichen Prozesse öffnen

Automatisches Einloggen ohne Jagex-Eingabe/MFA ist nicht implementiert
und wegen Jagex-Authentifizierung auch nicht vorgesehen.
