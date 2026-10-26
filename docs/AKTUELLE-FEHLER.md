# Bekannte Fehler und offene Live-Grenzen – OSRS Play 1.7.4

Release 1.7.4 ergänzt einen passiven Abgleich zwischen echten Minimapbildern und ermöglicht das automatische Update von 1.7.3. Eine daraus abgeleitete absolute Weltposition ist weiterhin ausdrücklich gesperrt. Zwei unabhängig bestätigte Weltkartenpositionen nach manueller Bewegung werden für die Maßstabskalibrierung benötigt. [Neue Diagnose und Grenzen](LIVE-MINIMAP-MOTION-2026-10-26.md).

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
