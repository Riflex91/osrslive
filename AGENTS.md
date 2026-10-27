# Weiterarbeit an OSRS Play

Vor Änderungen zuerst `docs/HANDOFF-RUNELITE-2026-10-27.md` und den aktuellen
Abschnitt in `docs/AKTUELLE-FEHLER.md` lesen. Dort stehen die tatsächlichen
Live-Ergebnisse und die lokal eingerichteten RuneLite-Einstellungen.

- Zielclient ist reguläres RuneLite, gestartet über den offiziellen Jagex
  Launcher. Der separate Entwicklungsclient wurde vom Benutzer verworfen.
- Standardstart ist derzeit ein nur lesender Companion. Automatische RuneLite-
  Aufgaben sind noch nicht implementiert. Die eigenständige Pixelprüfung hat
  die Position live in 1.8.5 bestätigt. Der erste Bewegungs-Live-Lauf in
  1.8.6 scheiterte an fehlenden Ziffernvorlagen der neuen Chunk-ID;
  Navigation und Spieleingaben bleiben unimplementiert.
- Erfolgreiche Offline-Fixtures belegen keine funktionierende Live-Navigation.
  Bekannte OCR-Mehrdeutigkeiten nicht durch geratenen Ziffernersatz freigeben.
- Das lokale RuneLite-Profil `auto` liegt nicht im Git-Checkout. Keine Konto-,
  Authentifizierungs-, Profilrohdateien oder Bildschirmbilder einchecken.
- Details zu Tests, Dateien, Plugins und nächsten Schritten stehen in der
  Übergabe. Weitere Arbeit und Ergebnisse dort nachvollziehbar ergänzen.
