# Weiterarbeit an OSRS Play

- 1.8.37: Das echte v1.8.36-Live-Log vom 29.10.2026
  18:36:55–18:37:50 bestätigt 4 eingefügte
  Vordergrundklicks und echte Bewegung. Der vierte Klick
  (3234,3171)->(3238,3178) erreichte zuletzt
  (3234,3173) im Kollisionspräfix und dann
  (3235,3175) im Korridor, Distanz 3 zum Wegpunkt.
  1.8.36 blieb pending, weil Quiet-Replan Distanz
  genau 2 verlangte; später korrekter Fokus-Stopp.
  1.8.37 erlaubt ausschließlich bei mindestens
  zwei bestätigten Präfixschritten, einem späteren
  gültigen Korridorschritt, Fortschritt zum geklickten
  UND finalen Ziel, bei >=1,6s ohne neues Export-
  Event und API-Alter <=3s den begrenzten
  CORRIDOR_THREE_TILE_QUIET_REPLAN mit höchstens
  6 geprüften Folgeklick-Schritten. Vier Replans,
  Vordergrund- und manuelle Stopps bleiben. Selbsttests
  replayen die echte Folge und Negativfälle.

- 1.8.36: Das v1.8.35-Live-Log vom 29.10.2026
  (18:17–18:20) belegt trotz 7+5 tatsächlicher
  Vordergrund-Klickfolgen zwei zu strikte
  `UNEXPECTED_API_TILE`-Abbrüche: nach Korridorfortschritt
  ein einzelner Seitwärtsschritt bei gleichbleibender
  Wegpunktdistanz 2 bzw. 3, aber näher zum Endziel.
  1.8.36 beobachtet höchstens einen solchen kleinen
  `CORRIDOR_LATERAL_PLATEAU_OBSERVED`-Schritt
  ohne zusätzlichen Input und erlaubt nur bei
  höchstens 3 Sekunden alter API-Position,
  1,6 Sekunden ohne neue Bewegungsbeobachtung
  und weiter geprüfter Kollisionsroute ein
  höchstens sechs Tiles langes
  `CORRIDOR_PLATEAU_QUIET_REPLAN`.
  Kumuliertes Replan-Limit bleibt 4,
  nach Fokusverlust wird nie wiederaufgenommen.
  Selbsttests modellieren beide echten Präfixe
  und Negativfälle; neuer Live-Test erforderlich.

- 1.8.35: Das echte v1.8.34-Log (29.10.2026, 18:02) zeigt nach
  vier eingefügten Vordergrund-Klickfolgen einen abweichenden,
  aber mehrfach API-bestätigten Spielweg: vom Klick
  (3257,3245)->(3259,3237) zu (3255,3244), (3254,3240),
  (3255,3239). 1.8.34 wartete trotz Fortschritt endlos auf den
  spezifischen Wegpunkt; der spätere Fokusverlust stoppte korrekt.
  1.8.35 darf ausschließlich nach drei engen Umweg-Ereignissen,
  frischer Position (<=3s) und >=1,6s ohne neuen Export aus
  dem letzten echten API-Tile eine neue geprüfte Kollisionsroute
  mit höchstens sechs Steps für den nächsten Klick planen.
  Höchstens vier Replans pro Start, alle sonstigen Stopps bleiben.
  Kein ungeprüftes echtes Ziel- oder Stillstandsversprechen.

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
  Der echte v1.8.32-Test bestätigt 8 weitere automatische
  SendInput-Klicks in drei Versuchen (4 nach Varrock,
  2 nach Lumbridge, 2 zur Grand Exchange). Zwei Versuche
  stoppten an **einer** seitlichen Kachel nahe Wegpunkt
  trotz echten Plugin-Wegfortschritts, der dritte korrekt
  bei Vordergrundverlust. v1.8.33 beobachtet höchstens
  einen späten Seitwärtsschritt statt sofort zu stoppen;
  es gibt dabei keinen neuen Klick und keinen falschen
  Wegpunkterfolg. Ein echter späterer Wegpunkt oder der
  weiterhin streng begrenzte frische API-Quiet-Replan
  ist für die Fortsetzung nötig. Vollständige Ankunft
  bleibt weiterhin ungetestet.
  Ein v1.8.33-Live-Test vom 29.10.2026 hat jetzt erstmals Lumbridges
  Zielradius vollständig erreicht (10 tatsächliche SendInput-Klickfolgen).
  Der erste Versuch stoppte sicher wegen einer bereits gedrückten
  physischen Maustaste. 1.8.34 klassifiziert diese Nutzerunterbrechung
  ohne automatisches Wiederaufnehmen oder Fehlalarm, prüft die
  Maustaste vor jedem Click-Down erneut und protokolliert versuchte
  und tatsächlich eingefügte Klickfolgen getrennt. Die reale Windows-
  Validierung von 1.8.34 bleibt offen.
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
