# RuneLite-Plugins: Auswahl für OSRS Play

Stand: 27.10.2026. Vollständig erfasst wurden **2.810 Hub-Einträge** sowie
**131 eingebaute Plugins aus RuneLite 1.13.4**. Namen und Funktionsbeschreibungen
wurden automatisch nach Einsatzbereichen durchsucht. Die unten erläuterten
Kandidaten wurden zusätzlich anhand ihrer Dokumentation bzw. relevanter
Quellcodedateien geprüft. Das ist keine vollständige Quellcode-, Sicherheits-
oder Live-Prüfung aller 2.941 Plugins. Vorhanden, aktiviert und mit unserem
Programm getestet sind unterschiedliche Zustände.

Durchsuchbare Übersichten mit Links zur jeweiligen Funktionsbeschreibung:

- [Alle Hub-Plugins](plugin-review/hub-catalog.html), [CSV](plugin-review/hub-catalog.csv)
- [Alle eingebauten Plugins](plugin-review/core-catalog.html), [CSV](plugin-review/core-catalog.csv)
- [Quelle, Anzahl und Prüfsummen](plugin-review/catalog-provenance.json)

Die Kategorisierung erzeugt Prüfkandidaten, keine Installationsliste. Sie
enthält Mehrfachzuordnungen und kann Funktionen übersehen oder falsch zuordnen.
1.212 Hub-Einträge ohne Schlüsselworttreffer bleiben ausdrücklich als weitere
Spezialfunktionen zur manuellen Prüfung markiert. Der Hub ist umfangreicher
als unser F2P-Ziel; die Kategorien stellen keine F2P-Verfügbarkeitsprüfung dar.
Reproduktion: `python tools/review_runelite_catalog.py` (Netzwerk erforderlich).

## Konkrete Auswahl

| Plugin | Funktion / Nutzen | Empfehlung für unseren Stand |
|---|---|---|
| **World Location** (Hub) | Grid Info zeichnet X/Y/Ebene des eigenen Spielers aus `getLocalPlayer().getWorldLocation()` sowie Chunk/Region. | Zuerst live prüfen. Numerische Anzeige statt Atlasabgleich; noch keine externe strukturierte Datenschnittstelle. |
| **Shortest Path** (Hub) | Berechnet und zeichnet Wege zu einem gewählten Ziel; besitzt Nachrichten für andere RuneLite-Plugins. | Hoher Nutzen für Wege und Abgleich. Läuft den Weg nicht selbst. Plugin-Nachrichten sind kein externer HTTP-Zugang. |
| **Quest Helper** (Hub) | Questschritte, Voraussetzungen, benötigte Items, Zielpfeile und NPC-/Objekt-/Kachelmarkierungen. Kann Shortest Path verwenden. | Hoher Nutzen für Questplanung; jeweils nur die aktive Aufgabe einblenden. Die Anzeige löst die Schritte nicht automatisch aus. |
| **Not Enough Runes** (Hub) | Itemquellen und Produkte; kann Quest-Itemanforderungen nachschlagen und Shops/Spawns an Shortest Path übergeben. | Sinnvoll für Beschaffung und spätere Itemdatenbank. Angezeigte Quelle garantiert weder Bestand noch erreichbaren Zugang. Keine Inventar- oder Kontextmenü-Schnittstelle. |
| **Inventory Setups** (Hub) | Speichert Inventar-/Ausrüstungszusammenstellungen, zeigt Abweichungen und filtert Bankansichten. | Später für Tätigkeitspakete wie Holzfällen, Angeln und Crafting. Stellt das Inventar nicht selbst zusammen. |
| **Clue Scroll** (eingebaut) | Hinweise, Zielpfeile und besondere Hinweisarten; Quellcode enthält auch Beginner-Map-Clues. | Optional bei konkretem Clue-Auftrag. Automatische Identifikation funktioniert laut Konfiguration nicht für Beginner/Master; Hinweise benötigen einen passenden Zustand. |
| **Tile Indicators** (eingebaut) | Markiert unter anderem das Bewegungsziel. | Hoher Nutzen beim Prüfen von Wegschritten und Ankunft; ergänzt absolute Koordinaten. |
| **XP Tracker** (eingebaut) | Verfolgt Erfahrungsfortschritt und stellt ein Fortschrittspanel bereit. | Für tatsächlichen Trainingserfolg. Ein Klick allein gilt nicht als Erfolg; unser Programm muss XP/Items weiterhin selbst verifizieren. |
| **Idle Notifier** (eingebaut) | Meldet unter anderem inaktive Animation, Interaktion und kritische Ressourcen. | Hilfreiche Zusatzkontrolle bei unterbrochenem Sammeln/Kampf; Benachrichtigung ist noch kein strukturiertes Bot-Ereignis. |
| **NPC Indicators / Object Markers** (eingebaut) | Markierungen ausgewählter Ziele. | Aufgabenspezifisch nutzen; keine pauschalen Vollbildmarkierungen. |
| **Ground Items / Inventory Tags / Bank Tags** (eingebaut) | Beschriftung und Ordnung von Gegenständen. | Nach Positionstest getrennt prüfen; Farben/Labels dürfen bestehende Erkennung nicht verdecken. |
| **Menu Entry Swapper** (eingebaut) | Ändert die Reihenfolge von Aktionen für Items, NPCs und Objekte. | Zunächst aus. Später nur feste dokumentierte Konfiguration: Ein Item-/Menücache muss diese Konfiguration berücksichtigen; Standard-Linksklick darf nicht geraten werden. |
| **Custom Cursor** (eingebaut) | Ersetzt das Cursorbild, auch durch eigene Bilder bzw. Ausrüstungsbilder. | Zunächst aus. Keine zusätzlichen Welt-, Ziel- oder Positionsdaten. Ob ein Cursor im Capture sichtbar ist, hängt zudem vom Aufnahmeweg ab. |
| **Low Detail** (eingebaut) | Entfernt Bodendetails und bestimmte Texturen, reduziert Speicherbedarf. | Sparsames Startprofil; live prüfen, ob benötigte Objekte und Overlay lesbar bleiben. |
| **FPS Control** (eingebaut) | Globale und getrennte Hintergrund-Bildratenbegrenzung. | Messvorschlag: 30 FPS aktiv / 10–15 im Hintergrund. Noch keine gemessene optimale Einstellung; OCR-Frische mitprüfen. |
| **GPU** (eingebaut) | Hardwaregestütztes Rendering mit eigenen Darstellungseinstellungen. | CPU/GPU/Lesbarkeit bei identischer Szene vergleichen. Abschalten spart nicht zwangsläufig Gesamtressourcen. |
| **Entity Hider** (eingebaut) | Blendet wählbare Entitäten aus. | Optional andere Spieler/Haustiere; eigene Figur, Ziele und erforderliche NPCs sichtbar lassen. |
| **117 HD / Resource Packs / Animation Smoothing** | Verändern bzw. erweitern die Darstellung. | Für die erste Positions- und Lastmessung aus lassen. Keine Voraussetzung für unsere Aufgaben. |
| **HTTP API** (Hub) | Die geprüfte Implementierung bietet `/stats`, `/inv`, `/equip`. | Löst den Positionsblock nicht. Nicht allein wegen des Namens als vollständige Bot-Schnittstelle installieren. |

## Reihenfolge der Einrichtung

1. Reguläres RuneLite ausschließlich über den offiziellen Jagex Launcher.
   World Location: **Grid Info an**, **Grid Info Type = Unique ID**. Anzeige
   oben links frei halten. In Instanzen kann die Einstellung für Template-
   Koordinaten die Bedeutung verändern; deren Navigation ist noch offen.
2. Echte Koordinaten bei Stillstand, manueller Bewegung und Logout prüfen.
   Der aktuelle Companion-Entwurf liest Ziffern aus dem Overlay mittels OCR.
   Das ist keine direkte API-Verbindung zwischen den Prozessen. Die Live-Probe
   bleibt wegen falsch gelesener Ziffern/Interpunktion gesperrt. Reine Tests und
   die unzweideutige Ebene-2-OCR-Fixture bestehen; die bekannte Ebene-0-Grenze
   wird im Test separat ausgewiesen. Keine Spielfreigabe.
3. Low Detail/FPS Control einzeln messen, danach Shortest Path und Tile
   Indicators. Genau eine aktive Quest-/Skill-Anzeige hinzunehmen.
4. Erst nach stabiler Position und Fortschrittsprüfung folgen Aufgabenprofile,
   Inventarvorbereitung und eine Ausführung über dasselbe RuneLite-Fenster.

Aus den Plugin-Funktionen folgt **noch keine Bereitschaft für autonomes
F2P-Training**. Der Katalog-Scan selbst installiert keine Plugins. Anschließend wurde auf
Benutzerwunsch das Profil auto mit den fünf ausgewählten Hub-Plugins und
relevanten Einstellungen eingerichtet. [Konfiguration und Live-Befund](HANDOFF-RUNELITE-2026-10-27.md).

## Primärquellen

- [Offizieller Hub-Katalog](https://runelite.net/plugin-hub/)
- [Eingebaute Plugins, RuneLite 1.13.4](https://github.com/runelite/runelite/tree/runelite-parent-1.13.4/runelite-client/src/main/java/net/runelite/client/plugins)
- [World Location: Player-Overlay](https://github.com/Skretzo/runelite-plugins/blob/fc7aebe8de366c513b91f7d38164d763166d4ebf/src/main/java/com/worldlocation/InfoOverlay.java)
- [Shortest Path](https://github.com/Skretzo/shortest-path)
- [Quest Helper: Funktionen und Integrationen](https://github.com/Zoinkwiz/quest-helper#readme)
- [Not Enough Runes](https://github.com/Hannah-GBS/notenoughrunes/tree/e4dfcfa479bb05db51e901820312419c58b14a42)
- [Inventory Setups](https://github.com/dillydill123/inventory-setups#readme)
- [Clue Scroll](https://github.com/runelite/runelite/tree/master/runelite-client/src/main/java/net/runelite/client/plugins/cluescrolls)
- [Custom Cursor](https://github.com/runelite/runelite/blob/master/runelite-client/src/main/java/net/runelite/client/plugins/customcursor/CustomCursorPlugin.java)
- [Low Detail](https://github.com/runelite/runelite/blob/master/runelite-client/src/main/java/net/runelite/client/plugins/lowmemory/LowMemoryPlugin.java)
- [FPS Control](https://github.com/runelite/runelite/blob/master/runelite-client/src/main/java/net/runelite/client/plugins/fps/FpsConfig.java)
- [HTTP API: tatsächliche Endpunkte](https://github.com/Adam-/runelite-plugins/blob/c435ceffff60edf38d6f94281c97de35ab2d53ae/src/main/java/info/sigterm/plugins/httpserver/HttpServerPlugin.java)
