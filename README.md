# OSRS Play v0.9.1 – automatische Jagex-Anmeldung

**Fehlerkorrektur:** Nach dem Start von OSRS Play und der Updateprüfung
wird jetzt **automatisch der installierte originale Jagex Launcher geöffnet**,
wenn noch keine offizielle Spiel-/Launcheransicht gefunden wird.
Das vorherige v0.9.0 startete nur den Fensterhost, nicht jedoch den Launcher.

- Bereits laufendes `osclient.exe` wird zuerst eingebettet, ohne einen
  neuen Launcher oder Spielprozess zu starten.
- Ein bereits geöffnetes Jagex-Launcher-Fenster wird übernommen.
- Sind mehrere offizielle Fenster sichtbar, verlangt OSRS Play eine
  **explizite manuelle Auswahl** über „Login/Spiel verbinden“.
- Ist kein offizieller Client geöffnet, startet OSRS Play den installierten
  `JagexLauncher.exe` und beobachtet dessen Fenster. Anschließend wird
  das echte OSRS-Fenster nach „Play Now“ im selben Bereich angezeigt.
- Wird bei der Updateprüfung eine neue EXE installiert, erfolgt der
  automatische Jagex-Start **erst nach dem Neustart** in der neuen Version.
- Wenn der Launcher nicht installiert/auffindbar ist, meldet der Client
  `LAUNCHER_NOT_FOUND` und zeigt eine entsprechende Meldung an;
  die bisherige manuelle Schaltfläche bleibt als Fallback vorhanden.
- Im Log erscheinen `AUTO_LOGIN_START` und danach
  `LAUNCHER_START_REQUESTED`, `LOGIN_WINDOW_READY` oder
  `GAME_WINDOW_READY`; etwaige Fehler sind ausdrücklich erkennbar.

Das ist ein automatisches **Öffnen der originalen Anmeldemaske**, keine
automatische Passworteingabe. Login, MFA und eventuell externe Browserdialoge
werden ausschließlich durch die unveränderte Jagex-Software gehandhabt.

**Live-only bleibt bestehen:** Keine lokalen Simulationen. Automatische
Botaktionen in echten OSRS-Welten sind nicht implementiert; der Live-Bot
bleibt deaktiviert, bis Spielzustand und Spielaktionen zuverlässig und
zulässig angebunden sind.

---

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
