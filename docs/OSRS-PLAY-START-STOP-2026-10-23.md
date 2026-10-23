# OSRS Play – Bot-Lifecycle im Windows-Client
Stand: 23. Oktober 2026 | v0.6.0

## Implementiert
- Windows-Single-EXE `OSRS-Play.exe`; bisheriges GitHub-Repository bleibt erhalten.
- Start/Stop startet und beendet den bereits vorhandenen autonomen **Offline**-Agenten.
- Status `LÄUFT (OFFLINE)` / `GESTOPPT`; `BOT STARTED`, `BOT STOPPED` im Log.
- Idempotente Start-/Stop-Operationen mit Restart-Test in `--self-test`.
- Mining-zu-GE-Tests, Strategie-Kampagne, Telemetrie, Export und Update beibehalten.
- Release liefert byteidentische Legacy-EXE zur Überbrückung der alten Update-URL.

## Abgrenzung
Diese Version enthält **keinen** eigenständigen Jagex-Spielclient, keine
echten Skill-, Item- oder Positionsdaten und keine Tastatur-/Maussteuerung
für OSRS. Der Bot läuft innerhalb der simulierten Umgebung. Damit ist
Start/Stop in der EXE nachweisbar, reales Mining oder Handel aber noch nicht.

## Abnahme
- Build in GitHub Actions `windows-latest`, alle Selbsttests und Szenarien bestanden.
- `publish` enthält genau `OSRS-Play.exe`.
- Release enthält die neue und die Legacy-Benennung mit identischen SHA-256-Werten.
- Updater-Manifest bleibt für ältere v0.5.x-Versionen nutzbar.
