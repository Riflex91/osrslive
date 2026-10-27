# RuneLite-Umstellung – 27.10.2026

RuneLite muss auf ausdrücklichen Benutzerwunsch über den offiziellen Jagex Launcher starten. Der experimentelle separate Entwicklungsclient mit eigenem API-Export-Plugin ist deshalb verworfen. Die eigenen lokalen Prototypdateien wurden außerhalb des Repos archiviert, die temporäre Launcher-Konfiguration zurückgenommen und der Entwicklungsstart aus dem Companion entfernt. Es werden keine Anmeldedaten ausgelesen oder übertragen.

## Aktueller Ansatz

Der Standardstart von OSRS Play 1.8.0 ist ein nur lesender RuneLite-Companion. Das reguläre Hub-Plugin **World Location** zeichnet bei aktivem **Grid Info** die X/Y-/Ebenenposition des eigenen Spielers aus der RuneLite-API sowie Chunk- und Region-ID. Das ist vom Maus-Hover-Tooltip für fremde Kacheln verschieden. Grid Info Type muss **Unique ID** sein; das Panel muss oben links lesbar sein.

OSRS Play liest diese Zahlen über Windows OCR aus frischen, nur im Arbeitsspeicher gehaltenen Fensteraufnahmen. Es prüft die aus X/Y berechneten Chunk-/Region-IDs und verlangt zwei unabhängige frische Aufnahmen derselben Position. Fensterwechsel, fehlende Anzeige und alte Frames verwerfen die Bestätigung. Das ist **keine strukturierte direkte API-Verbindung zwischen den Prozessen** und noch kein Ersatz für Zustandsdaten wie Welt, Login, Instanzen oder Charakteridentität. Instanz-/Template-Koordinaten benötigen eine eigene Prüfung vor Navigation.

Der bisherige Minimap-/Atlasvergleich war trotz mehrerer Zoom-, Kamera- und Terrainversuche nicht zuverlässig genug für Bewegung. Er wird im neuen Standardstart nicht als Ersatzquelle herangezogen. Das alte C++-Client-Backend ist nur explizit mit `--legacy-official` erreichbar.

## Tatsächlicher Teststand

Das Profil **auto** wurde im regulären RuneLite angelegt und aktiviert. Die fünf ausgewählten Hub-Plugins sind installiert; Grid Info, Low Detail, 30/15 FPS und Tile Indicators sind eingestellt. Die echte sichtbare Position 3142/3212/0 wurde noch nicht vom Companion bestätigt: OCR liest falsche Zeichen und bleibt gesperrt. Parser-/Frischetests sowie die unzweideutige Ebene-2-OCR-Fixture bestehen; die Ebene-0-Fixture dokumentiert ausdrücklich die Ablehnung als offene Grenze. Keine automatischen RuneLite-Spielaktionen implementiert.

[Ausführliche Übergabe mit exakten Einstellungen, Messwerten und nächsten Schritten](HANDOFF-RUNELITE-2026-10-27.md).

## Plugins

[Prüfbericht mit Empfehlungen und vollständigen durchsuchbaren Katalogen](RUNELITE-PLUGIN-PRUEFUNG-2026-10-27.md): 2.810 Hub-Plugins sowie 131 eingebaute Plugins aus RuneLite 1.13.4 wurden anhand ihrer Metadaten erfasst. Ausgewählte relevante Kandidaten sind anhand ihrer Dokumentation/Quellen näher geprüft.

Quellen: [World Location: eigenes Spieler-Overlay](https://github.com/Skretzo/runelite-plugins/blob/fc7aebe8de366c513b91f7d38164d763166d4ebf/src/main/java/com/worldlocation/InfoOverlay.java), [RuneLite API](https://static.runelite.net/runelite-api/apidocs/net/runelite/api/Client.html), [regulärer Launcher und Entwicklermodus](https://github.com/runelite/runelite/blob/runelite-parent-1.13.4/runelite-client/src/main/java/net/runelite/client/RuneLite.java).
