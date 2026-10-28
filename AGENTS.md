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
  neue 1.8.28-Liveprobe zeigte jedoch bis zu vier Kacheln pro
  API-Beobachtung und mehrere falsche Off-Path-Stopps; 1.8.29
  beobachtet einen maximal zwei Kacheln breiten und nur näher zum
  Wegpunkt führenden Korridor, ohne bei jedem Zwischenereignis
  erneut zu klicken. Es protokolliert jedes Klick-Prefix.
  Der echte 1.8.29-Test bestätigt diese Reparatur und zwei jeweils
  automatisch erreichte Wegpunkte, aber zweimal Stillstand zwei
  Kacheln vor dem nächsten Wegpunkt. 1.8.30 erlaubt nur mit
  still-rezentem verifiziertem Plugin-Tile eine zweimal begrenzte
  Wiederaufnahme nach 1,6s ohne neue Position. Es zeigt alte
  Startup-Exports ausschließlich als unbestätigte Vorschau,
  und bietet optional einen manuellen Vordergrund-Eingabeschutz
  mit Esc-Notausstieg. Keine Hintergrundautomatik behaupten:
  SendInput geht an den Windows-Eingabestrom, nicht gezielt an
  inaktive RuneLite-Fenster; der Exporter gibt keine Game-Befehle.
  Der neue echte 1.8.30-Test bestätigt automatische Installation,
  sieben Folge-Klicks, zwei funktionierende Quiet-Replans und
  zehn unterdrückte physische Mausereignisse mit Esc-Hookfreigabe.
  Ein letzter Kollisionsschritt in Lumbridges Zielradius wurde
  durch den allgemeinen 7-Pixel-Mindestabstand fälschlich
  gesperrt. 1.8.31 erlaubt nur in diesem validen Ein-Schritt-
  Zielfall eine Minimap-Projektion von 4–6 Pixel. Außerdem
  wurde ein Spielumweg erst nach einem bestätigten Wegpunkt
  sichtbar; 1.8.31 beobachtet höchstens fünf stark begrenzte
  Start-Umweg-Kacheln, sendet dabei KEINEN Folgeklick und
  verlängert keine Timeout-Grenze. Vollständiger Zielerfolg
  steht weiterhin aus.
  1.8.31 wurde erfolgreich automatisch installiert; der
  neue 49-Zeilen-Log vom 28.10.2026, 22:04:54 belegt,
  dass „Automatisch gehen (Test)“ bei einem rund 22 Minuten
  alten Export mit `START_REQUIRES_PLUGIN_POSITION` abwies.
  1.8.32 ergänzt nur einen befristeten, sichtbar abbrechbaren
  VORMERKUNGS-Modus. Erst eine **nach dem Nutzer-Klick**
  frisch exportierte API-Tile öffnet automatisch die
  Kalibrierung; bis zur erneuten ausdrücklichen Freigabe
  sendet der Client keinen Klick. Ohne Exporter-Heartbeat
  erfordert das weiterhin einen manuell ausgelösten
  ersten Tilewechsel. Keine stille Akzeptanz alter Datei.
  Die strikte
  `RUNELITE_FOREGROUND_LOST`-Sperre bleibt bestehen. Alle Schritte
  und Grenzen stehen
  in der Übergabe; keine automatische Konto-/Loginbedienung.
- Alle Weltkoordinaten stammen vom geprüften lokalen Plugin-Hub-Export;
  keine Screenshot-Positionserkennung oder geratenen Ziffern freigeben.
- Das lokale RuneLite-Profil `auto` liegt nicht im Git-Checkout. Keine Konto-,
  Authentifizierungs-, Profilrohdateien oder Bildschirmbilder einchecken.
- Details zu Tests, Dateien, Plugins und nächsten Schritten stehen in der
  Übergabe. Weitere Arbeit und Ergebnisse dort nachvollziehbar ergänzen.
