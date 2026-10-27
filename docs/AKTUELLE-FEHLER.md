# Bekannte Fehler und offene Live-Grenzen – OSRS Play 1.8.11

**1.8.11 – fremder HUD-Text teilt OCR-Zeile mit einer ID:**
Der echte 1.8.10-Live-Test bestätigt den Updateweg, vollständige
mehrhundertstellige Pixel-Logeinträge und mehrere bewegte Positionen
mit `OVERLAY_CONFIRMED`. In einigen Frames verschmilzt jedoch
`Pot (GE: 12 gp)` mit `Region ID 12593` oder `Chunk ID 805263`,
z. B. `Region ID 12593 12`. Ein bisher strenger Zeilen-Endanker
verwirft dann die ganze ID, obwohl Zahl und mathematische Prüfsumme
passen. 1.8.11 entnimmt im Feldquorum nur die **genau** fünf bzw.
sechs Ziffern vor einem durch Leerraum abgetrennten OCR-Suffix;
adjazente Extraziffern, widersprechende Quoren, Checksumme- oder
Pixelverweigerung bleiben Sperrgründe. Ein neuer Zähler zeigt pro
Frame, wie oft ein Suffix übersprungen wurde. Kein Live-Test der
Änderung und weiterhin keine automatisch ausgeführten Spielaktionen.

**1.8.10 – vollständige Fehlerereignisse statt 480-Zeichen-Abschneidung:**
Das echte 1.8.9-Log belegt das automatische Update, die unabhängige
Bestätigung von mindestens neun unterschiedlichen Positionen während
manueller Bewegung und den wirksamen sitzungsinternen Ziffernspeicher,
u. a. bei der früher gesperrten 3147/3208/0. Kaltstart ohne bereits
gelernte Vorlagen bleibt fail-closed. Der eigentliche neue Befund
ist ein Diagnoseverlust: 58 `RUNELITE_PIXEL`-Ereignisse schneiden
ihre Schlussfelder mit `...[gekürzt]` ab, weil `Diagnostics.Add`
den `Sanitize`-Standard von 480 Zeichen auch für ganze Detailtexte
übernahm. 1.8.10 erhöht dieses Feldlimit gezielt auf 16.384 Zeichen,
kennzeichnet seltene weitere Auslassungen samt Zeichenzahl und fügt
einen Dateikopie-/Zwischenablage-Regressionsfall hinzu. Geheime Token
und E-Mail-Adressen werden weiter herausgefiltert. Die GUI bleibt
bei nicht implementierter Automation rot; kein Gameplay-Code wurde
freigegeben. Die Behebung ist noch nicht live getestet.

**1.8.9 – schon unabhängig bestätigte ID-Ziffern auch nach Bewegung nutzen:**
Das echte 1.8.8-Live-Log bestätigt das Feldquorum und die Position
3144/3212/0 über viele Proben. Beim Wechsel auf 3139/3216/0 fehlt
eine ID-Vorlage der einmalig sichtbaren Y-Ziffer `6`, sodass die
Pixelprüfung erwartungsgemäß gesperrt blieb. Ein neuer, maximal fünf
Minuten lebender, an das konkrete RuneLite-Fenster und dessen Größe
gebundener Vorlagenspeicher übernimmt Pixelziffern nur aus getrennten
**vollständig bestätigten** Bildern. Erst nach zwei bestätigten
Aufnahmetimestamps und mindestens fünf exakt vergleichbaren aktuellen
ID-Ziffern dürfen diese Vorlagen eine heute einzelne Ziffer belegen.
Unbestätigte Frames, abweichende Fonts/IDs, andere Ziffernglyphen,
OCR-Widersprüche und fehlende Pixel bleiben blockiert. Die Release-API
erkennt trotz zeitweilig veraltetem CDN-Manifest die aktuelle Version;
Fehlerlog-Zwischenablage und roter Hinweis wurden erneut live belegt.
1.8.9 wurde noch nicht live getestet; Gameplay-Automatik fehlt weiterhin.

**1.8.8 – pro Feld übereinstimmende OCR statt nur vollständige Zeilen:**
Das echte 1.8.7-Log bestätigt die Release-API und Raw-Manifest bei Start
und manueller Prüfung jeweils auf 1.8.7. Der Pixel-Leser erkannte in
Probe 17 die zusätzliche Ziffernregel erfolgreich (`full_matches=4`,
`distinct_positions=1`, `repeated_unknown_layouts=4`), aber die zweite
frische Aufnahme konnte die Position nicht bestätigen. Bei Positionen
um 3145/3210 liefern die Nearest-OCR-Varianten gleiche Tile-X/Y- und
Chunkwerte, jedoch `Region 12594` gegen `12534`; die bicubischen
Varianten erkennen die richtige Region, aber nicht alle Chunk-/Y-Ziffern.
1.8.8 bildet pro Feld einen Quorumwert aus mindestens zwei vollständigen
Beobachtungen, prüft Region-/Chunkchecksummen und lässt dann erst
den unverändert konservativen Pixel-Leser entscheiden. Zwei verschiedene
Quoren für dasselbe Feld, gültig widersprechende Positionen und
unklare Ziffern/Kommas bleiben blockiert. Ein echter Bewegungsnachweis
mit 1.8.8 fehlt noch. Die Log-Zwischenablage funktioniert weiter.

**1.8.7 – neuer Live-Ort belegt fehlende Vorlagen:** Das echte 1.8.6-Log
vom 27.10., 20:28–20:29 (Europe/Berlin) zeigte bereits in Probe 1
`Tile3144`/`3211` mit übereinstimmenden Chunk `805265` und Region
`12594` in zwei OCR-Varianten. Die Pixelsuche fand passende Kommas und
ID-Ziffernvorlagen, aber `full_matches=0`: In diesen IDs fehlt die
Ziffer `3` aus den Tile-Werten. Beim späteren Weg über z. B.
`3147`/`3208` kommt außerdem die singuläre unbekannte `7` hinzu.
1.8.7 erlaubt nur **in derselben Tile-Zeile mehrfach pixelgleich
nachgewiesene** fehlende Ziffern; einzelne nicht trainierte Ziffern
werden ausdrücklich mit `unknown_singleton_rejects` verweigert.
Keine Position wurde im echten 1.8.6-Lauf fälschlich freigegeben;
die rote GUI-Meldung und `clipboard=COPIED` sind im Log bestätigt.
Das Update-Manifest wurde trotz 1.8.6-Cache-Headern kurz nach dem
Release noch als 1.8.5 beantwortet. 1.8.7 prüft daher zusätzlich
die aktuellste offizielle GitHub-Release-Asset-SHA-256 und nutzt bei
älterem Raw-Manifest den neueren verifizierten Release-Datensatz.
**Beide Änderungen benötigen neue Live-Bestätigung.**

**1.8.6 – Updater-Cache und tatsächlicher Live-Durchbruch:** Das neue
1.8.5-Sitzungslog vom 27.10. um 20:11 (Europe/Berlin) bestätigt bereits ab
der zweiten unabhängigen WGC-Aufnahme `OVERLAY_CONFIRMED` und anschließend
neun weitere stabile Ortsproben für X=3143/Y=3210/Ebene=0.
`RUNELITE_PIXEL` meldet jedes Mal `PIXEL_GRID_VERIFIED`, 4 identische
Rastertreffer und eine eindeutige Position; rund 11–12 ms nach der ersten
Probe. **Die zuvor blockierte Live-Position ist damit an diesem einen Ort
bestätigt.** RuneLite-Spielaktionen bleiben ausstehend und lösen
`AUTOMATION_NOT_IMPLEMENTED` samt Log-Kopie/roter GUI-Meldung aus.
Gleichzeitig lieferte die Update-Anfrage im 1.8.5-Client noch `remote_version=1.8.4`,
obwohl Version 1.8.5 bereits veröffentlicht war. 1.8.6 fügt pro Abfrage
einmalige URL-Kennungen, Cache-Vermeidungs-Header und `MANIFEST_BEHIND_LOCAL`
hinzu; die Wirksamkeit gegen die reale CDN-Zwischenspeicherung benötigt
einen weiteren Live-Lauf.

**1.8.5 – erweiterte Suche nach World-Location-Pixeln:** Das erste echte
1.8.4-Log zeigt in jeder Aufnahme zwei übereinstimmende OCR-ID-/X/Y-Beobachtungen,
aber `comma_layouts=0` bei 4.536 geprüften Rasterankern. Die auf den
zugeschnittenen Screenshot beschränkte Ankersuche wird auf den vollständigen
350×220-OCR-Bereich erweitert. Ein später bereitgestelltes vollständiges
RuneLite-Fensterbild bestätigte die Panelverschiebung um +14/+53 Pixel
gegenüber dem Ausschnitt bei **identischen** binären Panelpixeln. Eine
neue stufenweise Diagnose trennt fehlende
helle Pixel, nichtpassende Kommas/Leerstellen und Ziffernmuster. Der spätere
1.8.5-Live-Lauf bestätigte damit die Positionslesung; Spielaktionen bleiben gesperrt.

**1.8.4 – bisher ungelöste Live-Position:** Ein vom Benutzer zugeschnittener
World-Location-Ausschnitt zeigte eine feste monospaced Rasterstruktur und
sichtbare Kommas/Null, die Windows-OCR nicht zuverlässig erkannte. Ein neuer
streng geprüfter, bildgestützter Leser gleicht Tile-Ziffern mit Vorlagen
aus den bereits konsistenten Chunk-/Region-ID-Zeilen derselben Aufnahme ab.
Die analoge Auswertung des Benutzerbilds ist plausibel; der echte
WGC-/RuneLite-Lauf von 1.8.4 scheiterte allerdings an der zu eng begrenzten
Ankersuche und wurde in 1.8.5 adressiert. Keine ungesicherten
Ziffernersetzungen, keine Freigabe von Spieleingaben.

**1.8.3 – Auto-Update im RuneLite-Modus:** Der automatische GitHub-Check war
im älteren `MainWindow`, nicht im Standard-`RuneLiteWindow` verdrahtet.
Version 1.8.3 integriert Startup-, 30-Minuten- und manuelle Updateprüfungen
inklusive Log-/Clipboard-Fehlerdiagnose. Frühere Builds müssen einmalig
manuell durch 1.8.3 ersetzt werden. Das vom Benutzer angehängte 1.8.2-Log
bestätigt weiterhin unlesbare Ebene-0-Tile-Zeilen bei gültigen Capture-Frames;
an der fail-closed OCR-Sperre ändert dieser Fix nichts.

**1.8.2 – Diagnose statt stiller Sperre:** Ausführliche lokale Ereignislogs,
automatische Zwischenablage-Kopie bei Fehler/nicht ausgeführter Aktion und
roter Verbesserungsmeldung in beiden GUI-Modi sind ergänzt. Es gibt noch
keinen neuen echten Positions- oder Bewegungstest; die bekannte Ebene-0-
OCR-Sperre gilt unverändert. [Logging-Details](LOGGING-1.8.2.md).

**1.8.1 (noch ohne neuen Live-Test):** Zusätzliche Bildaufbereitung für die
World-Location-OCR und strengere Ablehnung widersprüchlicher gültiger
Koordinaten aus demselben Bild sind ergänzt. Die Ebene-0-Lesbarkeit ist dadurch
noch nicht nachgewiesen; die letzte bekannte Live-Sperre gilt weiterhin.

**Architekturwechsel am 27.10.:** RuneLite muss über den offiziellen Jagex Launcher starten. Der eigene Entwicklungsclient samt privatem Export-Plugin wurde daher verworfen und aus dem aktuellen Standardstart entfernt. Der nur lesende Companion-Entwurf soll stattdessen das Spielerkoordinaten-Overlay von World Location per OCR lesen. Das lokale Profil auto samt fünf Hub-Plugins ist eingerichtet. Die Live-Probe liest unter anderem `Tile3142» 3212» o` statt 3142/3212/0 und verwirft die Position korrekt. Reine Tests und unzweideutige Ebene-2-OCR-Fixtures bestehen; die bekannte Ebene-0-Ablehnung wird ausdrücklich als Grenze ausgewiesen. Automatische RuneLite-Spielaktionen sind nicht implementiert. [Vollständige Übergabe](HANDOFF-RUNELITE-2026-10-27.md). [Migration](RUNELITE-MIGRATION-2026-10-27.md), [vollständiges Plugin-Screening und Auswahl](RUNELITE-PLUGIN-PRUEFUNG-2026-10-27.md).

**Die folgenden Befunde beschreiben das bisherige visuelle Backend.**

**1.7.17-Live-Test (27.10., 00:13):** Weltkarte zweimal auf `(3141,3212)` bestätigt (14/20, 14/19), Kompass nach Norden bestätigt und Minimap vollständig herausgezoomt. SIFT-Matching der kleinen Minimap bleibt unzuverlässig. Die maskierte Atlas-Terrain-Erkennung lieferte `0.436/0.243` und `0.432/0.238` (Bestwert/Zweitplatzierter) bei gleicher Skala `1.95`, gleichem Oberflächen-Kandidaten, maximal einem Feld Abstand zur separat bestätigten Weltkarte und über zwei unabhängige Aufnahmen ohne Kandidatenverschiebung. Das ist ein starker Plausibilitätsbeleg **an genau diesem Live-Ort**, aber noch kein Test von Projektion, Klick, Wegwirkung, Neulokalisierung nach Bewegung oder fehlerhaften Matchfällen. 1.7.18 prüft die resultierenden vier hypothetischen Minimap-Schrittpositionen auf native Crop-Geometrie, Kompassachsen, sicher klickbare innere Randzone und Zwei-Bild-Stabilität; alle Werte bleiben ausdrücklich unverifiziert und führen zu keinem Input. Der unveränderte `AUTOMATIC_WORLD_POSITION_NOT_CONFIDENT`-Block verhindert noch automatische Bewegung.

**1.7.16-Live-Test (26.10., 23:55–23:57):** Weltkartenstart erfolgreich, zwei unabhängige korrekte Lokalisierungen `(3141,3212)` mit 19/28 und 19/29 Inliern sowie sicherer Kartenschluss. Minimap-Zoom-out in fünf Schritten bis zum visuell bestätigten unteren Anschlag. SIFT-Minimap-Position weiterhin `NO_VERIFIED_MAP_MATCH` (325 Features/2 Ratio-Paare vor Zoom; 337/1 danach). Der erstmals ausgeführte diagnostische `TERRAIN_PROBE` zeigte `best_score=0.550`, `runner_up=0.362`, `reference_scale=1.95` und 67 geprüfte Atlas-Peaks auf vier Ebenen. Ohne bestätigten Abstand zur unabhängigen Weltkarte ist nicht belegbar, ob der beste Treffer tatsächlich den Spieler beschreibt. 1.7.17 protokolliert daher den X/Y-Abstand zum höchstens fünf Minuten alten Weltkarten-Hinweis sowie Stabilität einer zweiten, ausreichend später aufgenommenen vollständigen Terraindetektor-Probe; Floor und Kandidaten bleiben unverifiziert, echte Koordinaten werden nicht im Log ausgegeben. Keine neue Navigationserlaubnis oder Eingabe, solange der verifizierende SIFT-Pfad scheitert.

**1.7.15-Live-Test (26.10., 23:38):** Die Kompass-Nadel wurde sofort pixelverifiziert (`map=801,11,150`, rot 35/blau 16, 0° Nord) und durch zwei frische Nordaufnahmen ohne Kameraklick bestätigt. Offizielle Welt W399, Skillwerte 24/24 und Stufe 131 wurden beobachtet. Weltkarten-Tooltip, Öffnen und Schließen wurden unabhängig bestätigt. Alle vier Kartenproben hatten etwa 1064–1069 SIFT-Merkmale und 27–28 reziproke Ratio-Paare, davon 16–17 RANSAC-Inlier (`r65:27/16`, `r65:28/17`, `r60:24/14`, `r55:12/9`). Bei maximal 60,7 % unterhalb der erforderlichen 65 % wurde der Standort korrekt abgelehnt und die Karte geschlossen (`WORLD_MAP_POSITION_NOT_CONFIRMED`). 1.7.16 protokolliert an dieser Stelle ergänzend die **nicht autorisierende** geometrische und pixelbasierte Qualität der knappen Kandidaten. Das kann zeigen, ob der Atlasinhalt trotz SIFT-Ausreißern zum sichtbaren Terrain passt; ohne Live-Belege findet weiterhin keinerlei automatische Reise statt. Die Minimap-Zoom-out-/Terrain-Probe aus 1.7.14 steht noch aus, weil die große Karte zuerst sicher lokalisiert sein muss.

**1.7.14-Live-Test (26.10., 23:22–23:24):** Version und Build wurden bestätigt. Offizieller Start auf W553, erfolgreiche InGame- und Skill-24/24-Erkennung sowie Levelsumme 131. Um 23:22:56 stellte die Kompassroutine `COMPASS_PIXEL_EVIDENCE minimap_missing` fest, blockierte sofort mit `COMPASS_PIXEL_NEEDLE_NOT_VERIFIED`, und über eine Minute spätere Hintergrundaufnahmen konnten die fest gespeicherte Sperre nicht mehr aufheben. Die Weltkarte, Zoom-out-Prüfung und `TERRAIN_PROBE` wurden in dieser Sitzung deshalb **nicht** erreicht. Aus dem Log allein ergibt sich nicht, ob die Kreisdetektion auf einem Übergangsbild oder dauerhaft versagte. 1.7.15 ergänzt begrenzte passive Wiederholungsaufnahmen mit Hough-/Pixel-Hinweisen und eine ausschließlich an spätere unabhängige Kompasspixel geknüpfte Freigabe dieses einen Blockgrunds zur erneuten vollständigen Kalibrierung. Kameraeingaben, Weltkartenklicks, SIFT-Positionshürden und Gameplay bleiben ohne passende visuelle Beweise gesperrt. Bei anhaltendem `minimap_missing` wird ein Screenshot **der betroffenen 1.7.15-Sitzung** zur Fehlersuche benötigt.

**1.7.13-Live-Test (26.10., ca. 22:45):** Die Weltkarte bestätigte `(3141,3212)` (24/35 RANSAC-Inlier in unabhängigen Frames). Danach erreichte die Minimap acht `WheelDown`-Eingaben und den nachgemessenen unteren Zoom-Anschlag, ohne unabhängige absolute Position: `LOCALIZATION_PREFLIGHT before_zoom` 84 Features/2 Ratio-Paare und `after_zoom` 283/4, beide 0 verifizierte Inlier. Nachträgliche, rein lesende Grauwert-Korrelation eines vom Nutzer bereitgestellten Screenshots ergab bei maximalem Zoom-out einen globalen Oberflächenkarten-Peak bei etwa 0,455 nahe der Weltkartenposition, konkurrierende räumlich getrennte Peak circa 0,253 (geprüfte Skalen 1,6–2,6 Referenzpixel pro Screenshotpixel). Für die volle reale Anwendung sind diese Einzelwerte nicht belastbar genug. 1.7.14 misst daher in der Live-Sitzung zusätzlich einen **nicht autorisierenden** maskierten Terrain-Bildabgleich in drei begrenzten Skalierungen auf allen vier Ebenen, einmal pro Subjekt. `TERRAIN_PROBE` enthält nur Scores und verweigert weiterhin jeden neuen Gameplay-Klick, solange `AUTOMATIC_WORLD_POSITION_NOT_CONFIDENT` gilt. Ein Screenshot von ca. 23:01 zeigt zudem `BLOCKED_WORLD_MAP_INPUT_TARGET_NOT_VERIFIED_AFTER_REFRESH` in einer anderen Sitzung; ohne den zugehörigen vollständigen Log kann nicht festgestellt werden, ob dort ein echter UI-Zielwechsel oder eine fehlerhafte Schaltflächenerkennung vorlag. Die nachträgliche visuelle Analyse behebt diese getrennte Worldmap-Eingabesperre nicht.

**Live-Test 1.7.12, 26.10. um 22:25:** Der vorherige Fensterpositionsblock trat nicht wieder auf. Weltkarte aus zwei voneinander unabhängigen Bildern mit je 22/33 SIFT-Inliern eindeutig bei `(3141,3212)` registriert und bestätigt geschlossen. Die Minimap-Zoomkalibrierung registrierte zunächst unveränderte obere Anschlagsmessungen, danach bei der gegenläufigen Probe ein tatsächliches Herauszoomen auf `scale=0.8707`, erneutes Hineinzoomen mit `scale=1.1426` und zwei bestätigte unveränderte weitere obere Anschlagsschritte. `MINIMAP_FULLY_ZOOMED_IN_VISUALLY_CONFIRMED` war erfolgreich. Eine absolute Minimapweltposition fehlte weiterhin; der neue Trainingsauftrag wurde folgerichtig **ohne neue Inventar-/Reiseklicks** mit `AUTOMATIC_WORLD_POSITION_NOT_CONFIDENT` blockiert. 1.7.12 protokollierte die unmittelbare Positions-Vorabprüfung noch nicht detailliert. 1.7.13 wechselt als begrenzter Vergleich zum maximalen Zoom-out und protokolliert in derselben Sitzung die Matcher-Qualität vor und nach Zoom (alle Bildbelege bleiben lokal/ungespeichert). Frühere Maximal-Zoom-out- und Zoom-in-Tests hatten 240 Features/3 Ratio-Paare bzw. 137/1, jedoch an nicht identischen Szenen; daraus folgt **noch keine nachgewiesene** Überlegenheit. Schwellen, Atlas-Plane-Eindeutigkeit, Zweibildprüfung vor realen Schritten und Fail-Closed-Logik bleiben identisch. Die 11,25-Pixel-pro-Feld-Distanzschätzung von Zoom-in wird unter Zoom-out nicht benutzt.

**Live-Test 1.7.11, 26.10. um 22:02–22:03:** Charakterbestätigung, 24/24 Skills mit Gesamtlevel 131 und Kompass-Nordprüfung waren erfolgreich. Unmittelbar vor dem ersten Weltkarten-Eingabeversuch wechselte jedoch die Bildschirmposition des Spiel-Viewports bei identischem 1000×600-Format und gleicher Fensterkennung. Die Bildroutine stoppte als `WORLD_MAP_FRAME_WINDOW_GEOMETRY_OR_FRESHNESS_CHANGED` nach `GAME_VIEWPORT_CHANGED origin_changed=True`. Da die Weltkarte noch nicht geöffnet wurde, wurden weder Minimap-Zoom noch Hough-Diagnosen erreicht. Die genaue Ursache der Positionsverschiebung ist im textbasierten Log nicht sichtbar. 1.7.12 erlaubt ausschließlich vor dem ersten Eingabeversuch eine Neukalibrierung nach zwei unabhängigen frischen InGame-Aufnahmen am gleichen neuen Ursprung. Wechselnde Fenster, andere Größen, widersprüchliche Positionen, unlesbare oder alte Bilder sowie jede Verschiebung nach begonnenen Eingaben bleiben gesperrt. Die Weltkarte, Minimap und spätere Navigation erfordern weiterhin ihre separaten strengen visuellen Prüfungen.

**Live-Test 1.7.10, 26.10. um 21:45:** Charakter, 24 Skills, Nordkompass und F2P-Welt bestätigt; aus zwei Kartenbildern wurde `(3141,3212)` mit `r65:26/17` und `r65:25/17` unabhängig geprüft. Die strengeren neuen Ratio-Profile waren dabei nicht entscheidend. Die nachfolgende Minimap-Zoomkalibrierung registrierte bei WheelUp/WindowWheelUp nur Skalen 1,0023 und 1,0000; nach der umgekehrten WheelDown-Probe ergaben drei frische InGame/HUD-Frames keine pixelverifizierte Minimap (`MINIMAP_ZOOM_POST_SCROLL_MINIMAP_MISSING`). Fenstermarke, 1000×600-Geometrie und Bildfrische waren weiter gültig. Die Ursache des Minimap-Verlusts ist nicht nachgewiesen. 1.7.11 ergänzt ausschließlich passive Beobachtungen vier bis sechs sowie eine differenzierte Hough-/Spielerzentrum-Diagnose; ohne bestätigte Minimap und messbare Skalenänderung bleibt Zoom-/Navigationsfreigabe gesperrt.

**Live-Test 1.7.9, 26.10. um 21:26:** Der pixelbestätigte Öffnungsweg der Charakterübersicht und die unabhängige Identitätsbestätigung funktionieren; Ranged wurde als Trainingsziel vorgeschlagen. Die Kompasskalibrierung bestätigte die Nordausrichtung und die Weltkarte konnte mit bestätigter Schließung geöffnet/geschlossen werden. Ihre vier Bildsätze enthielten 27/42 bzw. 25/43 SIFT-Inlier; keine Probe erreichte den unveränderten Konsens, deshalb `BLOCKED_WORLD_MAP_POSITION_NOT_CONFIRMED`. Etwa 14 Sekunden nach dem Block führte die kurze 30-s-Identitätslease zu einem unnötigen neuen Character-Summary-Klick, obwohl die Navigation bereits gesperrt war. In 1.7.10 werden schwächere Merkmale aus identischen Frame-Descriptors bei strengeren Verhältnissen 0,60/0,55 ausgeschlossen und ausschließlich Kandidaten mit unverändertem Konsens und allen zusätzlichen visuellen Prüfungen zugelassen. Außerdem frieren terminale Karten-/Zoom-/Kompasssperren die periodische Summary-Aktualisierung bis zum F8-Neuversuch ein. Kein positiver Ausgang dieser Änderungen ist ohne neuen Live-Test behauptet.

**Live-Test 1.7.8, 26.10. um 20:58–20:59:** Die Welt 380 war visuell bestätigt, 24/24 Skills und beide Summen betrugen 131. Die Chat-Eingabe lieferte jedoch keinen gültigen Charakternamen (`focused_prompt_candidates=0`). Dadurch blieb `Game.Confirmed` leer, alle Folgekalibrierungen und die passive relative Wegbilanz wurden erwartungsgemäß nicht gestartet. Das erneute Öffnen des Skills-Reiters nach etwa einer Minute änderte daran nichts. In 1.7.9 darf ein vollständig bildbestätigtes Skills-Raster die ausschließlich lesende Charakterübersicht unter zusätzlichen Symbol- und Zwei-Bild-Prüfungen anfordern. Ein fehlender oder widersprüchlicher Charaktername blockiert weiterhin sämtliche Spielaktionen. Ob der neue Ausweichweg auf diesem Client funktioniert, ist bis zum nächsten Live-Test offen.

**Neue Bildbelege, 26.10. um 20:20–20:21:** Eine tatsächlich manuell zurückgelegte Strecke wurde erstmals aus zwei Minimapbildern mit etwa +49/+42 Terrainpixeln registriert (je nach Bildausschnitt 10–12 RANSAC-Inlier). Die unabhängige Weltkartenregistrierung zu einer älteren Aufnahme erreicht 343/353 Inlier und weist auf ungefähr −4,4/+3,7 Weltfelder relativ zum älteren Ort hin. Diese Schätzung von rund 11,25 Pixeln je Feld ist **noch nicht zweimal unabhängig live kalibriert** und darf nur als Diagnose dienen. Ein Screenshot zeigt außerdem `BLOCKED_WORLD_MAP_FRAME_WINDOW_GEOMETRY_OR_FRESHNESS_CHANGED`; ob dabei der Spielprozess, die Position des 1000×600-Viewports oder die Frische der OCR-Aufnahme wechselte, ist anhand von Screenshots nicht erkennbar. 1.7.8 protokolliert den jeweiligen Grund getrennt und bleibt eingabeseitig gesperrt.

**Live-Test 1.7.6, 26.10. um 19:44:** Anmeldung, Skills (24/24, Gesamtlevel 131) und Weltkartenöffnung funktionierten. Vier Weltkartenbilder ergaben jeweils 49 Feature-Paare mit 31 geometrischen Inliern, aber die damalige 65-%-Konsensschwelle verwarf die 63,3 % sofort. Die Karte wurde nach erkannter Schließkontrolle wieder geschlossen. Danach gab es neun Minuten lang keine erneuten Inventar-, Reise- oder Trainingsklicks: der Vorab-Positionsblock aus 1.7.6 greift. In 1.7.7 dürfen hohe absolute Trefferzahlen (mindestens 30 Inlier) mit mindestens 60 % Konsens zu den weiterhin unveränderten zusätzlichen Bildprüfungen vordringen. Ob dieser konkrete Fall auch Orientierung, Kantenvergleich und unabhängige Positionsbestätigung besteht, ist offen. Der nachfolgende Minimap-/Atlas-Abgleich bleibt ungelöst.

Release 1.7.6 stoppt die beobachtete Folge wiederholter Inventarkontextmenüs, wenn bereits vor Beginn der nächsten Aktivität keine verifizierte Weltposition vorliegt. Bestehende Journale mit möglichen ausstehenden Aktionen bleiben vor dieser Sperre priorisiert. 1.7.5 hatte die passive Verschiebungsmessung um wechselnde Ausschnittursprünge korrigiert; der Live-Test ergab danach nahe null Pixel scheinbare Bewegung. Eine absolute Minimap-Weltposition ist weiterhin gesperrt. Zwei unabhängig bestätigte Weltkartenpositionen nach manueller Bewegung werden für die Maßstabskalibrierung benötigt. [Diagnose und Grenzen](LIVE-MINIMAP-MOTION-2026-10-26.md).

Die untenstehenden Live-Messungen dokumentieren die weiterhin offenen Grenzen von 1.7.3 und älteren Testbuilds; die Versionsnummer ist kein Nachweis behobener Weltlokalisierung.

**Neuester Test:** Eigene Minimap-Referenzen sind ergänzt. Mit Zoom-out blieb `minimap-01` um 17:51:54 blockiert (240 Merkmale, drei passende Paare). Auf Benutzerwunsch kalibriert die App jetzt zum maximalen Zoom hinein; `minimap-02-maxzoom` bestätigte den oberen Anschlag um 17:56:37 anhand echter Vergrößerung und zwei unveränderten Schritten. Der anschließende Positionstest um 17:58:00 blieb mit 137 Merkmalen, einem passenden Paar und null Inliern blockiert. Kein Bewegungsnachweis; maximaler Zoom allein löst den Matchfehler hier nicht. [Details](MINIMAP-REFERENZEN-UND-MAXZOOM-2026-10-26.md).

**Aktualisierung zur Kamerarichtung:** Eine explizite Nordkalibrierung ist ergänzt. `compass-04` bestätigte am 26.10. um 17:19:53 zwei unabhängige Bilder mit 0,00°; der folgende Wegversuch blieb mit 246 Merkmalen und nur vier passenden Referenzpaaren blockiert. In diesem Nordzustand genügt eine Kameradrehung zur Behebung des Matchfehlers nicht. Der Live-Klickfall aus einer tatsächlich gedrehten Ausgangslage bleibt separat zu bestätigen. [Details und Tests](KOMPASS-NORD-2026-10-26.md).

## Aktuell bestätigt – `world-map-12`, 26.10.2026

Die große Weltkarte wird automatisch geöffnet, der animierte Spielermarker erkannt und das Panel bestätigt geschlossen. Um 16:47:01 wurde die Oberflächenposition 3155/3202 aus zwei unabhängigen Bildern übernommen. Zoomkalibrierung und Inventarrunde (17 Items, 11 leere Plätze) liefen danach durch.

**Der aktuelle P0-Blocker ist die anschließende Minimap-Zuordnung:** 262 Merkmale, nur drei passende Referenzpaare, keine gültige Transformation. `AUTOMATIC_WORLD_POSITION_NOT_CONFIDENT` verhindert weiterhin Bewegung. Ein Weltkartenhinweis allein erlaubt keinen Wegschritt. Zeitweilige Tooltip-/Inventar-Tab-Ausfälle und ein als Sitzungswechsel interpretierter Schließübergang bleiben Beobachtungspunkte.

Die falsche Kartenbreite, die X-Erkennung, die vierteilige Markersegmentierung und eine mögliche Umgehung der Navigationssperre bei fehlender frischer Skillbestätigung wurden korrigiert. Die Weltlisten-Seite 2 erhält auch bei sechs groben OCR-Zellen wieder ihre native Gesamtprüfung; diese Änderung ist durch Fixtures, noch nicht durch einen getrennten Gesamteinstieg geprüft. [Änderungen, konkrete Live-Belege und nächste Schritte](WELTKARTEN-KORREKTUREN-2026-10-26.md).

## Vorheriger Prüfstand bis `world-map-08`

Stand: 26.10.2026, einschließlich `world-map-08` um 16:18 Uhr. Alle Zeiten sind lokale Windows-Zeiten. Belege: [Weltkartenroutine](WELTKARTEN-NAVIGATION-2026-10-26.md), [vorherige Live-Recovery und Zoom](LIVE-RECOVERY-UND-ZOOM-2026-10-25.md).

## Bestätigter Fortschritt

- Welt 399 und Spieleinstieg wurden unabhängig visuell bestätigt; 24/24 Skillwerte und Gesamtlevel 131 wurden gelesen.
- Die zuvor blockierte Zoomprüfung erreichte um 22:10:07 erstmals `MINIMAP_FULLY_ZOOMED_OUT_VISUALLY_CONFIRMED`: ein echter Skalenwechsel von 0,6486 mit 24 Inliern, danach zwei Anschlagsmessungen von ungefähr 1,0 mit 83 bzw. 88 Inliern.
- Die Inventarrunde wurde um 22:11:32 abgeschlossen: 17 frisch untersuchte Items, 11 leere Plätze, etwa 84 Sekunden. Einzelne OCR-Schreibweisen bleiben korrekt von der Cache-Freigabe ausgeschlossen.
- Der bisher unbekannte echte Disconnect-Dialog wurde erkannt. Ein automatischer, frisch bestätigter Retry-Klick erreichte die Play-Now-Seite. Zwischenzustände beenden die Wiederverbindung nicht mehr fälschlich als erfolgreichen Spielbeitritt.

## P0 – Weltlokalisierung und autonome Bewegung bleiben offen

**Bestätigt:** Nach erfolgreicher Zoom- und Inventarprüfung um 22:11:36: `NAVIGATION_LOCALIZATION_NO_VERIFIED_MAP_MATCH features=120 candidates=0 inliers=0 best_ratio_matches=0 phase=initial no_input=true`. Eine spätere Wiederholung bei gedrehter Kamera blieb ebenfalls ohne Match. Ergebnis: `AUTOMATIC_WORLD_POSITION_NOT_CONFIDENT`, keine Bewegungsfreigabe.

**Zusätzlicher Code, noch kein Live-Bewegungsnachweis:** Bei fehlendem nativen Match kann die Merkmalsextraktion einmal gleichmäßig vergrößert wiederholt werden. Die transformierten Punkte werden in ursprünglichen Koordinaten geprüft. Vorhandene gültige native Matches bleiben erhalten, mehrdeutige Matches werden nicht durch einen zweiten Versuch überstimmt. Die Referenztests bestätigen Geometrie, Routing und Klickkoordinaten; die Zuverlässigkeit am echten Client ist damit nicht bewiesen.

**Weltkartenroutine implementiert, Live-Kette noch offen:** Am 26.10. erfolgten Launcherstart, Weltwahl 399, Spieleinstieg und das Öffnen der großen Weltkarte automatisch. Der exakte rote Shortcut bestätigte den Globusklick. Die Kartenbeschriftungen wurden erkannt, die Schließkontrolle jedoch nicht; `world-map-08` blockierte mit `WORLD_MAP_POSITION_NOT_CONFIRMED`. Die abschließende Korrektur trennt passive Kartenanalyse von der geprüften Schließkontrolle und meldet gegebenenfalls `WORLD_MAP_CLOSE_CONTROL_NOT_VERIFIED`. Ihr vollständiger Live-Durchlauf steht noch aus.

Markerauswertung, zwei unabhängige Positionsbelege, bestätigtes Schließen und regionale Minimap-Suche sind im Code vorhanden und durch Fixtures geprüft. Diese liefern noch keinen Live-Wegschritt. Der Oberflächenhinweis beweist kein Spielerstockwerk und gilt nur fünf Minuten; längere Reisen benötigen künftig eine erneute Lokalisierung.

Die bisherigen Navigationsfixtures stammen aus der Referenzkarte selbst. Sie können einen Darstellungsunterschied zwischen echter Minimap und Referenzkarte nicht ausschließen. Kartentreffer und Bewegung dürfen deshalb weiterhin nur aus tatsächlichen Live-Belegen freigegeben werden.

## P1 – Vollständige Aktionsketten und F2P-Maxing nicht bestätigt

Reise, Bank/GE, Kampf, Sammeln, Produktion und Fortschritt über längere Zeit sind noch nicht durchgehend autonom live bestätigt. Eine vollständige Inventarrunde und ein Aufgaben-Vorschlag sind kein Nachweis für Trainingserfolg oder Level 99.

Nach verifizierter Weltposition muss zunächst ein kurzer Weg mit neuer Positionsbestätigung funktionieren; anschließend ein einzelner Sammel-/Produktionsvorgang mit echtem Inventar- oder XP-Beleg. Journale und Schutzregeln für unklare Ergebnisse bleiben erforderlich.

## Behobene Startblockaden und verbleibende Beobachtung

Der erneute Gesamteinstieg gelang am 26.10. auf Welt 399. Nicht jede Weltlisten-Seite ist damit abgedeckt: In einem vorherigen Lauf lieferte Seite 2 zu wenige bestätigte Weltzellen, um zur empfohlenen Welt 553 zu blättern. Die fokussierte Play-Now-Erkennung wurde ergänzt, weil bei Wiederverbindungen die gelbe Charakterbeschriftung fehlen kann. Die Charakterzeile im Chat war zeitweise nicht zuverlässig lesbar; die Identitätsprüfung wird deshalb nicht gelockert.

Die frühere harte Ablehnung schon einer fehlenden Nach-Scroll-Minimap wurde durch begrenzte passive Wiederholungen und getrennte Gründe ersetzt. Die Radius-Schwankung der Kreiserkennung wird durch identische, unskalierte Bildschirmbereiche abgefangen. Die widersprüchliche Zwölf-Matches-Vorbedingung für eine Zehn-/Elf-Inlier-Bestätigung ist entfernt. Native Weltziffern und zusätzliche Footer-OCR überwanden den beobachteten Footer-Block bei Welt 455.

Diese Änderungen wurden mit Regressionen und einzelnen Live-Läufen geprüft; sie garantieren keine beliebige Clientgeometrie oder dauerhaft fehlerfreie Erkennung. F8/Pause, native Geometrie, frische Aufnahmen und bestätigte Bildwirkung bleiben verbindlich. Ein zugestelltes Windows-Ereignis allein belegt keine ausgeführte Spielaktion.

## Hinweise für weitere Logs

Vollständigen bereinigten Log mit `build_commit` und `build_run` aufbewahren. Zuerst `WORLD_VISUALLY_CONFIRMED` und `MINIMAP_ZOOM RESULT` prüfen, danach `INVENTORY_READ`, `NAVIGATION_LOCALIZATION...` und tatsächliche Fortschrittsbelege. Keine Zugangsdaten oder privaten Screenshots veröffentlichen. Die Versionsnummer 1.7.2 allein identifiziert keinen PR-Testbuild.
