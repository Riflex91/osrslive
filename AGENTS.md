# Weiterarbeit an OSRS Play

Vor Änderungen zuerst `docs/HANDOFF-RUNELITE-2026-10-27.md` und den aktuellen
Abschnitt in `docs/AKTUELLE-FEHLER.md` lesen. Dort stehen die tatsächlichen
Live-Ergebnisse und die lokal eingerichteten RuneLite-Einstellungen.

- Zielclient ist reguläres RuneLite, gestartet über den offiziellen Jagex
  Launcher. Der separate Entwicklungsclient wurde vom Benutzer verworfen.
- Standardstart bleibt ohne Spieleingaben. Der inzwischen API-basierte
  Companion bietet ab 1.8.22 einen nur explizit gestarteten Autowalk-
  Test mit Nutzer-Minimapkalibrierung. 1.8.23 repariert die im Live-Log
  erkannte Erstklick-Sperre bei einem stationären Spieler. Die Annahme
  der gezielten Win32-Fensternachrichten durch echtes RuneLite ist
  weiterhin **nicht live bewiesen**. Alle Schritte und Grenzen stehen
  in der Übergabe; keine automatische Konto-/Loginbedienung.
- Alle Weltkoordinaten stammen vom geprüften lokalen Plugin-Hub-Export;
  keine Screenshot-Positionserkennung oder geratenen Ziffern freigeben.
- Das lokale RuneLite-Profil `auto` liegt nicht im Git-Checkout. Keine Konto-,
  Authentifizierungs-, Profilrohdateien oder Bildschirmbilder einchecken.
- Details zu Tests, Dateien, Plugins und nächsten Schritten stehen in der
  Übergabe. Weitere Arbeit und Ergebnisse dort nachvollziehbar ergänzen.
