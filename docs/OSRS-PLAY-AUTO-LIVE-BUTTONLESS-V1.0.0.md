# OSRS Play v1.0.0 – Auto-Live ohne OSRS-Play-Schaltflächen

## Änderungen

- komplette WinForms-Oberfläche ohne eigene Buttons, Eingabefelder oder Tabs; einzig originales Jagex-Fenster, Status und Diagnose
- verifizierte Updateprüfung vor Starten/Übernahme des Jagex Launcher
- bestehendes Game-Fenster priorisiert; ansonsten bestehender Launcher oder neuer Launcher
- bei mehreren Fenstern deterministische Auswahl ohne weitere UI-Dialoge; Ambiguität protokolliert
- unabhängiger, automatisch gestarteter F2P-TCP-Pingtest gegen offizielle unbeschränkte Gratiswelten
- Live-Log wird regelmäßig lokal persistent gespeichert und kann per Ctrl+Shift+C kopiert werden
- Start der Live-Bot-Automation fail-closed, bis ein echter verifizierter Game-State- und Action-Adapter existiert
- keine Offline-Sitzungen oder synthetische Spielzustände

## Beweis- und Zustandsgrenzen

`WINDOW_ATTACHED` beweist nur Windows-Fenstereinbettung.
`LOGIN_WINDOW_READY` beweist nicht erfolgreiche Jagex-Authentifizierung.
`GAME_WINDOW_READY` beweist ein sichtbares Original-Gamefenster, nicht eine aktive Spielsitzung.
`F2P_PING BEST_WORLD` ist nur Netzwerkempfehlung, kein bestätigter World-Hop.
`LIVE_AGENT START_BLOCKED` bedeutet **keine** Spielautomation.

## Ausstehende Fähigkeiten

- offizielle Anmeldung ohne Eingriff bei nicht bestehender Jagex-Sitzung (Launcher kontrolliert MFA/SSO)
- tatsächliche Auswahl des gemessenen besten F2P-Servers innerhalb des Spiels
- verifizierter Zugriff auf Player-Position, Inventar, Objekte und serverbestätigte Spielaktionen
- autonome Agentsteuerung (kein Adapter vorhanden)

Der offizielle Jagex-Spielclient wird weder gepatcht noch modifiziert.

## Testerwartungen

Windows Self-Test, Release-Build, Single-EXE und unveränderte Update-Signatur/Hash.
Live-Login, F2P-Weltenwahl und Botaktionen können auf GitHub CI nicht getestet oder behauptet werden.
