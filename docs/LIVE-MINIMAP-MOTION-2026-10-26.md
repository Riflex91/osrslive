# Echte Minimap: passiver Bewegungsabgleich (26.10.2026)

## Warum

Die ausgelieferten statischen Minimap-Referenzen werden anders gerendert als die
sichtbare Minimap des offiziellen Windows-Clients. Ein gültiger Test mit einem
aus dem Referenzatlas erzeugten Bild belegt daher noch keinen Live-Abgleich.
Die Grenzwerte für die globale Weltposition werden **nicht** abgesenkt.

## Implementierter erster Schritt

`LiveMinimapMotionVision.Compare` vergleicht zwei *echte* Minimap-Ausschnitte
derselben Sitzung. Das Verfahren verwendet reziproke SIFT-Paare, RANSAC,
flächige Landmarken, Prüfung auf unveränderten Maßstab/Nordausrichtung und
einen unabhängigen Kantenvergleich. Eine veränderte Fenstergeometrie,
Weltkartenansicht, eine fehlende bzw. leere Karte oder eine nicht verifizierte
Abbildung verwirft die Messung.

Der laufende Beobachter protokolliert höchstens alle fünf Sekunden
`LIVE_MINIMAP_MOTION` mit `terrain_dx_px`, `terrain_dy_px`, `pairs`, `inliers`
und dem genauen `reason`. Er speichert keine Bildschirmfotos dauerhaft und
erzeugt **keinerlei Eingabe** ins Spiel. Bei Reset oder geöffneter Weltkarte
wird der vorherige Bildausschnitt vergessen.

**Ergänzung 1.7.8 nach echter Bewegung:** Die zwei Benutzerbilder vom 26.10.,
20:20:42 und 20:21:07, erlauben erstmals einen stabilen relativen Terrainversatz
von etwa (+49,+42) Pixeln. Die anschließende große Weltkarte wurde mit einer
älteren Aufnahme (343/353 reziproke RANSAC-Inlier, Skalierung nahe 1) zur Deckung
gebracht. Der Spielermarker liegt danach etwa 4,4 Felder weiter westlich und
3,7 Felder nördlicher. Daraus entsteht **vorläufig** ein Maßstab von 11,25
Minimappixeln je Feld am bestätigten maximalen Zoom. Die Bilddaten stammen
teilweise aus verschiedenen Sitzungen; Markerpuls und eine nicht unabhängig
bestätigte Spielebene begrenzen ihre Verlässlichkeit.

Darum verwendet 1.7.8 die Zahl nur für `LIVE_MINIMAP_RELATIVE_DISTANCE`:
`east_tiles_provisional`, `north_tiles_provisional`, `samples`,
`pixels_per_tile_assumed`, `not_a_world_position=true`, `no_input=true`.
Die passive Bilanz beginnt erst nach erfolgreicher Weltkarte, Kompass- und
Zoomprüfung; keine Eingabe, keine absolute Position und kein Client-Speicherzugriff.
Stale Frames, unpassende Transformationen, Kartenöffnen, Fensterwechsel und
Beobachtungslücken über zwölf Sekunden verwerfen die bisherige Bilanz.
Bewegungen werden ohne den bisherigen fünfsekündigen Stillstands-Takt
zeitnah zusammengefasst (maximal alle zwei Sekunden). Das bestehende
`LIVE_MINIMAP_MOTION` bleibt für stationäre Diagnosen erhalten.

Die Bildschirmfotos vom 20:20–20:21 enthalten außerdem die Sperre
`WORLD_MAP_FRAME_WINDOW_GEOMETRY_OR_FRESHNESS_CHANGED`. Ab 1.7.8 zeigt ein
separater `VISUAL_FRAME/GUARD`-Eintrag, ob Aufnahme, Fensteridentität,
Viewportgeometrie oder Frische nach drei passiven Versuchen scheiterte.
Die bestehenden Schutzgrenzen und das Eingabeverbot bei Unsicherheit bleiben.

**Nach Auswertung des ersten Live-Logs:** Beim Wechsel der per Hough erkannten
Minimap von 150 auf 144 Pixel verschiebt sich die Mitte des Bildausschnitts.
Der ursprünglich lokal gemessene Versatz von rund vier Pixeln direkt nach
der Zoomroutine war deshalb kein Nachweis für tatsächliche Bewegung. Die
Messung wird nun am Bildzentrum ausgewertet und in Koordinaten der
unveränderten Spielansicht zurückgerechnet. Ein Wechsel des Spielfensters
oder der Bildschirmgeometrie verwirft die alte Referenz vollständig. Die
neuen Offline-Tests decken gerade diesen scheinbaren Versatz ab.

Die gemessene Verschiebung ist ausschließlich relativ und in Bildschirmpixeln.
Insbesondere gilt **nicht**: `terrain_dx_px` sei eine Weltkoordinate, eine
Spielerposition oder ein Navigationsbeleg. Der bestehende Wegplaner akzeptiert
solche Messungen nicht als Position und stoppt weiter, wenn die
Weltlokalisierung nicht unabhängig gelingt.

## Nächster, noch nicht freigegebener Schritt

**Eingabe-Schutz ab 1.7.6:** Der passive 1.7.5-Live-Test bestätigte nach der
Zoomkalibrierung unverändert nur ein einziges Atlas-Match. Trotzdem wurden
mehrmals Inventarmenüs geöffnet, weil die Aufgabenplanung den Navigationsblock
erst nach dem Inventarscan feststellte. Neue Aktivitäten prüfen die verfügbare
Weltposition deshalb jetzt vor dem ersten Inventarmenü und lassen bei fehlender
Position alle Eingaben aus. Sie wiederholen nur diese passive Prüfung mit
60 Sekunden Abstand; ausstehende Trade-/Combat-Journale werden davor gesondert
abgewickelt. Dies ist keine Freigabe der automatischen Navigation.

1. Nach bestätigter Weltkartenposition einen Minimap-Referenzframe erfassen.
2. Den Charakter manuell einige Kacheln bewegen; Zoom und Kompass gleich
   lassen. Die neuen Frames nachweisbar wieder mit der echten Minimap
   registrieren.
3. Zweite **unabhängig** bestätigte Weltkartenposition aufnehmen. Nur daraus
   die tatsächlichen Pixel pro Weltkachel messen (mit Achsen-/Plausibilitäts-
   und Mehrfachmessung).
4. Erst nach echten Live-Validierungen entscheiden, ob der Kalibrierwert für
   `MapLocation.ClickFor` sicher genug ist. Bis dahin kein automatischer
   Positions-Fallback und kein blindes Klicken.

Zum Auswerten werden `LIVE_MINIMAP_MOTION` und `WORLD_MAP_POSITION_CONFIRMED`
benötigt; keine Passwörter oder vollständigen Screenshots in GitHub-Issues
veröffentlichen.
