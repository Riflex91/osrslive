# OSRS Play v0.9.0 – nur offizielles OSRS

Ab dieser Version enthält **OSRS Play keine Offline-Sitzungen** mehr. Die EXE startet ausschließlich die Darstellung des unveränderten offiziellen Jagex Launchers beziehungsweise der offiziellen osclient.exe.

## Funktionen

- Anmeldung starten: Original-Jagex-Launcher innerhalb des OSRS-Play-Fensters öffnen
- Login/Spiel verbinden: bereits laufendes originales OSRS-Spiel einbetten
- Fenster freigeben: offizielles Spielfenster aus der Einbettung lösen, ohne es zu beenden
- F2P-Ping testen: offizielle freie Standardwelten auf Netzwerkebene vergleichen
- Echte OSRS-Skills: öffentliche Jagex-HiScores ohne Passworteingabe abfragen
- Logs kopieren und als JSON/ZIP exportieren sowie automatisches Update

## Entfernt

Lokaler MiniWorld-TCP-Spielserver, Sandbox-Tab, Simulationstimer, Offline-Agent, BotController, Mining/GE-Szenarien, OfflineCampaign und zugehörige Offline-Tests. Der alte Kommandozeilenmodus --scenario-tests gibt einen Fehlercode zurück und startet nichts.

## Grenze

Der **Live-Bot** ist ausdrücklich deaktiviert. OSRS Play bettet das Originalspiel als Windows-Fenster ein, verarbeitet aber weder authentifizierte OSRS-Weltzustände noch die offiziellen Spielaktionen. Daher führen wir keine künstlichen Fortschritte als echte aus. Für autonome Aktionen auf den offiziellen Servern fehlen ein zulässiger Game-State- und Aktionsadapter. Eine offizielle OSRS-Botverbindung wurde nicht implementiert.

Die Anmeldung einschließlich MFA geschieht weiterhin ausschließlich in der unveränderten Jagex-Software. Browser-Sicherheitsfenster können gesondert erscheinen. Windows-DPI-/GPU-Einstellungen können die Cross-Process-Einbettung beeinflussen.

## Validierung

Windows-CI kompiliert die eine OSRS-Play.exe und prüft Prozessfensterfilter, Launcher-Game-Übergang, F2P-Weltfilter, öffentliche Skill-Auswertung, Updater und Log-Redaktion. Kein Login auf einem realen Konto lässt sich auf dem CI-Rechner nachweisen.
