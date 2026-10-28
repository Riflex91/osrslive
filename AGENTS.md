# Weiterarbeit an OSRS Play

Vor Änderungen zuerst `docs/HANDOFF-RUNELITE-2026-10-27.md` und den aktuellen
Abschnitt in `docs/AKTUELLE-FEHLER.md` lesen. Dort stehen die tatsächlichen
Live-Ergebnisse und die lokal eingerichteten RuneLite-Einstellungen.

- Zielclient ist reguläres RuneLite, gestartet über den offiziellen Jagex
  Launcher. Der separate Entwicklungsclient wurde vom Benutzer verworfen.
- Standardstart bleibt ohne Spieleingaben. Der inzwischen API-basierte
  Companion bietet ab 1.8.22 einen nur explizit gestarteten Autowalk-
  Test mit Nutzer-Minimapkalibrierung. 1.8.23 reparierte die Erstklick-
  Sperre; das 1.8.23-Live-Log zeigt zwei gesendete PostMessages, aber
  keinen dadurch bestätigten Wegfortschritt. 1.8.24 prüft nun echte
  Win32-SendInput-Mausklicks auf RuneLite **nur im Vordergrund**, mit
  separater ausdrücklicher Nutzerfreigabe und sofortigem Fokus-/Ziel-
  Stopp. Der 1.8.24-Live-Test bestätigt echte SendInput-Bewegung und einen
  erreichten Wegpunkt. 1.8.25 korrigiert wiederholte API-Samples und die
  tolerierte unmittelbare Endpunktüberschreitung. Seine automatische
  Ring-Erkennung bei x679/y138 **war falsch**. Auch 1.8.26 hatte noch
  den falschen Mittelpunkt x665/y110, weil die roten Linien im
  Nutzerbild falsch abgelesen wurden. Sie kreuzen sich tatsächlich
  bei x640/y110 (Bildbreite 768); im 773 Pixel breiten Client ist der
  weiße Punkt bei ungefähr x645/y111. 1.8.27 verwendet width-128/y111
  und verlangt zusätzlich einen weißen Spielerpunkt im überprüften
  Bild; selbst manuelle Abweichungen sind dort auf drei Pixel
  begrenzt. Das Update setzt weiterhin
  **Minimap-** statt 3D-Kamera-Zoom über einen geprüften Minimap-
  Rechtsklick auf RuneLites Standard 4 Pixel/Tile zurück. Die echte
  Zoomstufe wird vom Exporter nicht mitgeliefert; nur der Input ist
  prüfbar. Das 1.8.27-Live-Log bestätigt die weiße Mitte und mindestens
  zwei automatische SendInput-Klicks mit echtem API-Wegefortschritt.
  1.8.28 erlaubt bis zu zwei eng begrenzte Vorwärtsabweichungen von
  einer Kachel vom genauen A*-Prefix, nur nach früherem Plugin-
  Fortschritt und mit erneuter Kollisionsplanung. Die strikte
  `RUNELITE_FOREGROUND_LOST`-Sperre bleibt bestehen. Alle Schritte
  und Grenzen stehen
  in der Übergabe; keine automatische Konto-/Loginbedienung.
- Alle Weltkoordinaten stammen vom geprüften lokalen Plugin-Hub-Export;
  keine Screenshot-Positionserkennung oder geratenen Ziffern freigeben.
- Das lokale RuneLite-Profil `auto` liegt nicht im Git-Checkout. Keine Konto-,
  Authentifizierungs-, Profilrohdateien oder Bildschirmbilder einchecken.
- Details zu Tests, Dateien, Plugins und nächsten Schritten stehen in der
  Übergabe. Weitere Arbeit und Ergebnisse dort nachvollziehbar ergänzen.
