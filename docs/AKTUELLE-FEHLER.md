# Bekannte Fehler und offene Live-Grenzen – OSRS Play 1.7.2

Stand: 25.10.2026, letzter ausgewerteter Windows-Live-Test: `Eingefügter Text(20261025-202434).txt`, Build `462b9444ebd20ab2a9ee25df5ae565d73da62fc9` aus PR #26 (CI-Lauf `40230287323`). Die Bezeichnungen **bestätigt**, **vermutet** und **nicht geprüft** trennen Log-Beweise von Annahmen. Diese Liste ist kein Nachweis für einen abgeschlossenen autonomen Spielablauf.

## Zuletzt beobachteter Status

- Der offizielle Jagex Launcher und `osclient.exe` starteten, der visuelle Spieleinstieg gelang. 24/24 Skillwerte und Gesamtlevel 131 wurden konsistent erkannt; serverseitige Authentifizierung ist damit nicht bewiesen.
- Das Log enthält **90 Ereignisse, 2 Warnungen, 0 Fehler**, keinen Pufferüberlauf und rund 320 MiB Arbeitsspeicher.
- Der Aufgabenplaner schlug `RANGED_CHICKENS` vor. Es erfolgte **kein** bestätigter autonomer Wegschritt, weil die Minimap-Zoomprüfung vorher blockierte.
- Build, Selbsttests, OCR-, Navigations- und Hintergrundtransporttests liefen auf Windows-CI `40230287323` erfolgreich. Das garantiert keine vollständige Eingabewirkung unter allen realen Spielbildschirmen.

## Offene Fehler (nach Priorität)

### P0 – Zoom-Kalibrierung stoppt bei ungültigem Frame nach Mausradimpuls

- **Bestätigt:** Nach `MINIMAP_ZOOM_WheelDown_REQUESTED` um 21:24:01.326 meldete die Spielbeobachtung um 21:24:02.285 `navigation_available=False`. Darauf folgte um 21:24:02.897 `MINIMAP_ZOOM_POST_SCROLL_FRAME_NOT_VERIFIED`, nach nur einer Abwärtsstufe und **ohne** `[MINIMAP_ZOOM] STEP`-Skalenmessung.
- **Auswirkung:** Kalibrierung und anschließende automatische Navigation bleiben gesperrt. Dies ist ein sicheres Stoppen, aber verhindert einen produktiven Durchlauf.
- **Ursache unklar:** Ein neues Bild kann vorübergehend die Minimap/HUD-Evidenz verlieren, die Fenstergeometrie wechseln oder die Frischebedingung verletzen. Der aktuelle Grundcode unterscheidet diese Fälle noch nicht im Log. Die zuvor ergänzte passive Wiederholung greift derzeit bei schwacher SIFT-Geometrie, **nicht** bei einem schon als unbrauchbar abgewiesenen Nach-Scroll-Frame.
- **Nächste Korrektur:** Fehlende Minimap, HUD-/Spielstatus, Fenster-/Größenwechsel und Zeitstempel separat protokollieren; bei einer vorübergehenden unbrauchbaren Aufnahme maximal wenige neue **passive** Frames abwarten. Keinen weiteren Mausradimpuls senden, bis der Zustand geklärt ist. Nach wie vor niemals Navigation aufgrund eines unbestätigten Kartenbildes freigeben.
- **Reproduktion:** Offiziellen Client per OSRS Play starten, visuelle Skill-Bestätigung abwarten, erster Zoom-Schritt in der eingebetteten 1000×600-Spielansicht. Der Fehler trat im genannten Lauf auf und ist nicht bei jedem vorherigen Lauf identisch aufgetreten.

### P1 – Weltanzeige wird gelegentlich nicht per OCR verifiziert

- **Bestätigt:** Um 21:23:50.343: `[VISUAL_STARTUP] WORLD_FOOTER_OCR | world_candidates=0 switch_marker=False`; Zustand `WORLD_LABEL_NOT_RECOGNIZED`.
- **Auswirkung:** Die F2P-Pingliste empfahl Welt **553** mit 6,5 ms, doch die tatsächliche Anwendung dieser Empfehlung im Spiel ist durch diesen OCR-Schritt **nicht** nachgewiesen. Der anschließende Spieleinstieg gelang trotzdem; diese Warnung blockierte den Ablauf in dem Lauf nicht dauerhaft.
- **Nächste Korrektur:** Der vorhandene visuelle Weltseiten-Leser soll bei unlesbarer Fußzeile einen begrenzten frischen Screenshot abwarten und die Bestätigung klar von der bloßen Pingempfehlung trennen. Unbestätigte Weltwahl nicht als erfolgreich gemeldet oder für nachfolgende weltabhängige Aktionen vorausgesetzt werden.

### P1 – Weltposition und vollständige autonome Navigation nicht live bestätigt

- **Zuletzt früher belegt:** Ein vorausgegangener Live-Test meldete `NAVIGATION_LOCALIZATION_NO_VERIFIED_MAP_MATCH features=214 candidates=0 inliers=0`. Obwohl ein Minimap-Ausschnitt erkannt wurde, fehlte ein eindeutiger Kartenmatch.
- **Aktueller Stand:** In diesem neuesten Log **nicht erneut getestet**, weil die Zoom-Vorprüfung vorher stoppte. Die erfolgreiche automatische Minimap-Verkleinerung nach manuellem Hineinzoomen wurde in einem früheren Kontrolltest anhand von `scale=0.8753`, `0.8536`, `0.8381` im echten OSRS-Client beobachtet. Das bestätigt weder den endgültigen Zoomanschlag noch eine Weltkoordinate.
- **Nächster Schritt:** Erst nach `MINIMAP_FULLY_ZOOMED_OUT_VISUALLY_CONFIRMED` die Kartenlokalisierung erneut prüfen; Weltposition nur bei ausreichenden Merkmalen/Inliern und eindeutiger Referenz übernehmen.

### P2 – Volle Spielaktionen-/Inventar-Kette noch nicht Ende zu Ende verifiziert

- **Historische Grenze, in diesem kurzen Start-Log nicht neu geprüft:** Inventar-Kontextmenüs, Erkennung von Inventaränderungen, Reise, Bank/GE und Kampf haben Selbsttests und einzelne Live-Beobachtungen, aber keinen durchgehend bestätigten autonomen Live-Durchlauf.
- **Nächster Schritt:** Nach erfolgreicher Zoom-/Positionsprüfung erst die vorhandenen Einzelaktionen anhand frischer OCR-/Pixelbelege testen; Journale und Eingabesicherungen nicht aufweichen.

## Bewusste technische Grenzen (keine durch das Log bewiesenen Defekte)

- Ein offiziell gerenderter Clientzustand mit bestätigten Skillwerten beweist **keine** serverseitige Authentifizierung.
- `PostMessage`-Erfolg beweist nur die Zustellung an ein Windows-Fenster, nicht die Annahme einer Eingabe durch den Spielclient. Die Render-Child-Mausradroute wurde im Kontrolltest zwar visuell bestätigt, kann bei anderen Zuständen jedoch erneut geprüft werden müssen.
- Verdeckte Fenster lassen sich beobachten; bei minimiertem Fenster pausiert die Aufnahme. Der globale Mauszeiger und der Fensterfokus werden für die Hintergrundroutine nicht verändert.
- Die Version `1.7.2` und der Update-Check beziehen sich auf den offiziellen Release-Kanal; ein PR-Test-Build enthält zusätzliche Änderungen und wird separat anhand von `build_commit`/`build_run` identifiziert.

## Hinweise für nächste Logs

1. Den vollständigen kopierten Log mit `build_commit` und `build_run` aufbewahren, keine Zugangsdaten/Screenshots mit privaten Inhalten veröffentlichen.
2. Auf `MINIMAP_ZOOM STEP` (Skalierung, Inlier, Matches, Aufnahme) und `MINIMAP_ZOOM RESULT` achten. Ein fehlender `STEP`-Eintrag bei einem `POST_SCROLL_FRAME_NOT_VERIFIED`-Block ist ein separater Aufnahme-/Erkennungsfehler.
3. Erst nach bestätigtem Minimalzoom `NAVIGATION_LOCALIZATION...` auswerten und darauf prüfen, ob ein eindeutiger Ort gefunden wird. Ein Aufgaben-*Vorschlag* ist noch keine ausgeführte Spielaktion.

Dokumentation der vorangegangenen Zoom-Tests und der Implementierung: [MINIMAP-ZOOM-UND-LOKALISIERUNG-2026-10-25.md](MINIMAP-ZOOM-UND-LOKALISIERUNG-2026-10-25.md).
