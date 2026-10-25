# Bekannte Fehler und offene Live-Grenzen – OSRS Play 1.7.2

Stand: 25.10.2026, ausgewertete Live-Tests bis 22:13 Uhr, lokaler Testlauf `live-calibration-recovery-15` auf Basis von `main` nach PR #26. Alle Zeiten sind lokale Windows-Zeiten. Implementierung und Belege: [Live-Recovery und Zoom](LIVE-RECOVERY-UND-ZOOM-2026-10-25.md).

## Bestätigter Fortschritt

- Welt 399 und Spieleinstieg wurden unabhängig visuell bestätigt; 24/24 Skillwerte und Gesamtlevel 131 wurden gelesen.
- Die zuvor blockierte Zoomprüfung erreichte um 22:10:07 erstmals `MINIMAP_FULLY_ZOOMED_OUT_VISUALLY_CONFIRMED`: ein echter Skalenwechsel von 0,6486 mit 24 Inliern, danach zwei Anschlagsmessungen von ungefähr 1,0 mit 83 bzw. 88 Inliern.
- Die Inventarrunde wurde um 22:11:32 abgeschlossen: 17 frisch untersuchte Items, 11 leere Plätze, etwa 84 Sekunden. Einzelne OCR-Schreibweisen bleiben korrekt von der Cache-Freigabe ausgeschlossen.
- Der bisher unbekannte echte Disconnect-Dialog wurde erkannt. Ein automatischer, frisch bestätigter Retry-Klick erreichte die Play-Now-Seite. Zwischenzustände beenden die Wiederverbindung nicht mehr fälschlich als erfolgreichen Spielbeitritt.

## P0 – Weltlokalisierung und autonome Bewegung bleiben offen

**Bestätigt:** Nach erfolgreicher Zoom- und Inventarprüfung um 22:11:36: `NAVIGATION_LOCALIZATION_NO_VERIFIED_MAP_MATCH features=120 candidates=0 inliers=0 best_ratio_matches=0 phase=initial no_input=true`. Eine spätere Wiederholung bei gedrehter Kamera blieb ebenfalls ohne Match. Ergebnis: `AUTOMATIC_WORLD_POSITION_NOT_CONFIDENT`, keine Bewegungsfreigabe.

**Zusätzlicher Code, noch kein Live-Bewegungsnachweis:** Bei fehlendem nativen Match kann die Merkmalsextraktion einmal gleichmäßig vergrößert wiederholt werden. Die transformierten Punkte werden in ursprünglichen Koordinaten geprüft. Vorhandene gültige native Matches bleiben erhalten, mehrdeutige Matches werden nicht durch einen zweiten Versuch überstimmt. Die Referenztests bestätigen Geometrie, Routing und Klickkoordinaten; die Zuverlässigkeit am echten Client ist damit nicht bewiesen.

**Nächster Lokalisierungsweg:** Die große Weltkarte wurde im offiziellen Client geöffnet und zeigt den Spielermarker sowie größere Geländeabschnitte. Automatisches Öffnen/Schließen, Bildbereichserkennung, eindeutige Spielerzuordnung und die unabhängig bestätigte Übergabe an den Routenplaner fehlen noch. Die optische Ähnlichkeit zu den Referenzkacheln ist ein Hinweis, kein numerischer Positionsbeleg.

Die bisherigen Navigationsfixtures stammen aus der Referenzkarte selbst. Sie können einen Darstellungsunterschied zwischen echter Minimap und Referenzkarte nicht ausschließen. Kartentreffer und Bewegung dürfen deshalb weiterhin nur aus tatsächlichen Live-Belegen freigegeben werden.

## P1 – Vollständige Aktionsketten und F2P-Maxing nicht bestätigt

Reise, Bank/GE, Kampf, Sammeln, Produktion und Fortschritt über längere Zeit sind noch nicht durchgehend autonom live bestätigt. Eine vollständige Inventarrunde und ein Aufgaben-Vorschlag sind kein Nachweis für Trainingserfolg oder Level 99.

Nach verifizierter Weltposition muss zunächst ein kurzer Weg mit neuer Positionsbestätigung funktionieren; anschließend ein einzelner Sammel-/Produktionsvorgang mit echtem Inventar- oder XP-Beleg. Journale und Schutzregeln für unklare Ergebnisse bleiben erforderlich.

## Behobene Startblockaden und verbleibende Beobachtung

Die frühere harte Ablehnung schon einer fehlenden Nach-Scroll-Minimap wurde durch begrenzte passive Wiederholungen und getrennte Gründe ersetzt. Die Radius-Schwankung der Kreiserkennung wird durch identische, unskalierte Bildschirmbereiche abgefangen. Die widersprüchliche Zwölf-Matches-Vorbedingung für eine Zehn-/Elf-Inlier-Bestätigung ist entfernt. Native Weltziffern und zusätzliche Footer-OCR überwanden den beobachteten Footer-Block bei Welt 455.

Diese Änderungen wurden mit Regressionen und einzelnen Live-Läufen geprüft; sie garantieren keine beliebige Clientgeometrie oder dauerhaft fehlerfreie Erkennung. F8/Pause, native Geometrie, frische Aufnahmen und bestätigte Bildwirkung bleiben verbindlich. Ein zugestelltes Windows-Ereignis allein belegt keine ausgeführte Spielaktion.

## Hinweise für weitere Logs

Vollständigen bereinigten Log mit `build_commit` und `build_run` aufbewahren. Zuerst `WORLD_VISUALLY_CONFIRMED` und `MINIMAP_ZOOM RESULT` prüfen, danach `INVENTORY_READ`, `NAVIGATION_LOCALIZATION...` und tatsächliche Fortschrittsbelege. Keine Zugangsdaten oder privaten Screenshots veröffentlichen. Die Versionsnummer 1.7.2 allein identifiziert keinen PR-Testbuild.
