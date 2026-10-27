# OSRS Play 1.8.2 – vollständige Fehlerdiagnose

## Verhalten im laufenden Client

- Jeder Start erzeugt eine eigene **append-only**-Textdatei unter
  `%LOCALAPPDATA%\OSRSPlay\live-logs\`. Dateinamen enthalten den Backendtyp,
  Startzeit und Prozess-ID: `runelite-*.log`, `legacy-*.log`, bei einem Fehler
  vor dem GUI-Start `fatal-*.log`. Bisherige Sitzungen werden nicht automatisch
  überschrieben oder gelöscht.
- RuneLite zeigt ein scrollbares Log mit den letzten 150 Ereignissen; die Datei
  enthält **alle** Ereignisse der Sitzung. Das ältere Backend zeigt weiter
  seine letzten 420 Ereignisse an, schreibt jetzt aber ebenfalls fortlaufend.
- Bei einer neuen Fehler-/Blockursache schreibt das Programm zuerst den
  Fehler mit `IMPROVEMENT_REQUIRED` in die Datei und kopiert **den gesamten
  aktuellen Dateiinhalt** in die Windows-Zwischenablage. Die GUI zeigt rot
  `VERBESSERUNG MUSS EINGEBAUT WERDEN` und den Auslöser an.
- Derselbe dauerhaft bestehende Fehler wird weiter protokolliert, überschreibt
  die Zwischenablage aber nur einmal pro Sitzung. Jeder neue Fehlergrund löst
  eine neue Kopie aus. `Gesamte Logdatei kopieren` bzw. `Log kopieren` erneuert
  den Stand jederzeit manuell. Schlägt die Zwischenablage fehl, bleibt der
  Fehler sichtbar und der Knopf bietet einen neuen Versuch an.
- Absichtliches Pausieren ist kein Fehler. Das normale Warten auf ein zweites
  unabhängiges Bild ist ebenfalls kein Fehler, wird bei mehr als acht Sekunden
  ohne Fortschritt aber als Blockade gemeldet. Wiederholt unlesbare/mangelnde
  Aufnahmen werden nach vier Sekunden, interne Fehler und widersprüchliche
  Clients sofort gemeldet. Im Legacy-Modus wird ein geplanter, aber länger als
  30 Sekunden in einem Waiting-Zustand nicht ausgeführter Auftrag gesondert
  protokolliert (nicht während einer Nutzerpause oder im Beobachtungsmodus).
  Selbst wenn der RuneLite-OCR-Status zwischen mehreren Warteständen wechselt,
  entsteht nach zehn Sekunden ohne bestätigte Position eine zusätzliche
  `POSITION_NOT_CONFIRMED_TIMEOUT`-Diagnose.
- Der RuneLite-Companion ist noch **nur lesend**. Die fehlende automatische
  Spielausführung wird einmalig als `AUTOMATION_NOT_IMPLEMENTED` protokolliert
  und kopiert; die rote Meldung ist daher derzeit selbst bei erfolgreich
  bestätigten Positionsbildern korrekt.

## Welche Daten enthalten sind

| Kategorie | Inhalt |
|---|---|
| Start | Version, Build-Commit/-Run/-Kanal, Modus, lokale Zeitstempel |
| RuneLite-Fenster | Gefundene Prozesse, akzeptierte/abgelehnte Fenster, Clientgröße, Capture-Zustand |
| Bildaufnahme | Frame-Anzahl, Frame-Kopien, Alpha-/Crop-/Geometriestatus, Capture-Fehler, Alter in ms |
| OCR | Probe und Verfahren (`nearest_2x`, `nearest_3x`, `bicubic_4x`, `contrast_4x`), gelesene Grid-Zeilen, gültige/ungültige Parser-Entscheidung |
| Position | Chunk-/Region-Prüfung, Anzahl der gültigen OCR-Durchläufe, Zweibildbestätigung, eindeutige Koordinaten bei bestätigter Position |
| Pixelprüfung (ab 1.8.4) | `RUNELITE_PIXEL/VERIFICATION`: Übereinstimmende OCR-Beobachtungen, getestete Bildanker, sichtbare Trennzeichen, bestätigte Ziffernvorlagen, vollständige Rastertreffer und Anzahl eindeutiger Positionen. Keine Pixel oder Screenshotdaten im Log. |
| Erweitertes Pixelprotokoll (ab 1.8.5) | Größe des 350×220-Pixel-Suchbereichs, Zahl heller Bildpunkte, erste Kommaformen, passende Kommapaare, leere Trennfelder, vollständig geprüfte Layouts, Laufzeit. Damit werden Positionierungs- und Helligkeitsfehler gezielt unterscheidbar. |
| Ablauf | Statuswechsel, Pause/Fortsetzen, Launcher-Anforderung, abgelehnte oder veraltete Ergebnisse, 10-Sekunden-Gesundheitsdaten |
| Fehler | Auslöser, Sperrgrund, Exception-Typ, HResult, bereinigte Nachricht, Methoden-/IL-Stack und Zustand der Log-Kopie |
| Auto-Update (ab 1.8.3) | Prüfung beim GUI-Start, alle 30 Minuten und per Schaltfläche; Manifest-Version, Assetname, Downloadgröße, Dauer, SHA-256-Prüfergebnis, Helperstart, Ergebnis oder bereinigter Fehler. Bei Updatefehler automatische Log-Kopie mit roter Meldung. |
| Cachefeste Manifestabfrage (ab 1.8.6) | `MANIFEST_REQUEST` nennt die frische URL-/Header-Richtlinie; `MANIFEST_BEHIND_LOCAL` meldet einen älteren remote Stand, ohne private URL-Kennung oder Daten preiszugeben. Eine Downgrade-Installation ist ausgeschlossen. |
| Zweite Releasequelle (ab 1.8.7) | `RELEASE_API_REQUEST`, `RELEASE_API_VERIFIED`, `RELEASE_AHEAD_OF_MANIFEST` bzw. `RELEASE_API_UNAVAILABLE` unterscheiden bestätigte Release-Assetdaten von einem möglicherweise alten Raw-Manifest. Konfliktierende SHA-256-Werte führen zu `CHECK_FAILED`. |
| Bewegungsziffern (ab 1.8.7) | `unknown_tile_digits`, `unknown_singleton_rejects`, `repeated_unknown_layouts` zeigen, ob ID-Ziffern zur Lesung der neu erreichten Tile-Koordinaten fehlen. Einzelne unbekannte Glyphen bleiben blockiert. |
| Feldweise OCR-Übereinstimmung (ab 1.8.8) | `stage=FIELD_QUORUM`, `xy_votes`, `chunk_votes`, `region_votes`, `rejected_field_outliers`, `CHECKSUM_VERIFIED` bzw. `NOT_READY` oder `CHECKSUM_REJECTED`. Im Bitmapprotokoll wird die Feldprovenienz und die tatsächliche Anzahl vollständiger OCR-Treffer separat erfasst. |
| Sitzungsinterne Pixelvorlagen (ab 1.8.9) | `stage=GLYPH_HISTORY`, `trusted_history_frames`, `historical_glyph_matches`, `historical_contradictions` dokumentieren die Prüfung gegen unabhängig bestätigte ältere Glyphen ohne deren Rohdaten zu speichern. |
| Lange Ereignisse (ab 1.8.10) | Vollständige Pixelprüfungen überschreiten die frühere pauschale 480-Zeichen-Grenze. Detailfelder behalten jetzt bis zu 16.384 bereinigte Zeichen inklusive `full_matches`, `distinct_positions`, `elapsed_ms` und `result`. Ausnahmsweise darüber hinaus gekürzte Einträge nennen die Anzahl ausgelassener Zeichen ausdrücklich. |
| Fremde OCR-Suffixe (ab 1.8.11) | `chunk_ignored_suffixes` und `region_ignored_suffixes` zählen durch Whitespace abgesetzten zusätzlichen HUD-/Tooltiptext hinter korrekt langen Chunk-/Region-ID-Ziffern. Konkurrierende Stimmen, Checksummen und echte Pixelprüfung bleiben sichtbar und verbindlich. |
| Reine ID-/Pixelpositionsprüfung (ab 1.8.12) | `stage=ID_ONLY_QUORUM` zählt numerische X/Y-OCR-Lesungen, gültige Tile-Labels sowie ID-Stimmen; bei exakt null X/Y-Lesungen und eindeutigen IDs werden höchstens 64 Chunk-Felder streng pixelgeprüft (`evidence_source=ID_ONLY_PIXEL_ENUMERATION`, `tile_candidates=64`). Kein Glyphraten, weiterhin reale Kommas, Ebene und Zweibildbestätigung. |
| Einzelner X/Y-OCR-Treffer (ab 1.8.13) | Der ID-/Pixel-Ausweichweg darf auch **eine** vollständige, mit beiden unabhängigen IDs rechnerisch konsistente X/Y-OCR-Lesung prüfen; `xy_readings=1`, `SINGLE_XY_CHECKSUM_REJECTED` und `single_xy_matches_pixels=true/false` machen das Veto sichtbar. Fehlende Pixelübereinstimmung, widersprüchliche vollständige Beobachtung oder mehr als eine numerische X/Y-Lesung bleiben gesperrt. |
| Read-only-Bewegungschronik (ab 1.8.14) | `RUNELITE_MOTION OBSERVATION` für erste bzw. wirklich geänderte, **zweifach bestätigte** Position: X/Y/Ebene vorher/nachher, `dx`, `dy`, `plane_delta`, `manhattan_tiles`, `sample_gap`, `capture_gap_ms`, `non_adjacent_observation`, `route_between_samples_unknown`, `autonomous_input=false`. Health-Zähler `confirmed_position_frames`, `stable_frames`, `observed_tile_changes`, `non_adjacent_observations`. Keine Zwischenposition geraten; Pause und Quellenverlust setzen die Folge zurück. |
| Präzisierte Einzel-X/Y-Diagnose (ab 1.8.14) | Bei gewöhnlichem Feldquorum ist `single_xy_matches_pixels=not_applicable`; nur die eng begrenzte Einzelstimmen-Route protokolliert `true` oder `false`. `ocr_agreements` enthält nun die wirklich passende einzelne vollständige OCR-Beobachtung, auch wenn bei der ID-basierten 64-Zellen-Suche die erwartete Tile-Position zunächst unbekannt ist. |
| Altes Backend | Zusätzlich bestehende Render-, Login-, Welt-, Inventar-, Navigations-, Kampf- und Aktivitätsereignisse sowie blockierte Executor-Zustände |

Die Datei speichert **keine Bildschirmbilder**, Prozessspeicher, Jagex-
Anmeldedaten oder RuneLite-Profilrohdateien. Ereignistexte werden vor dem
Schreiben bereinigt (bekannte Token-/Passwort-/Cookie-Muster und E-Mail-
Adressen). Die drei Grid-Zeilen sowie bestätigte X/Y/Ebene können enthalten
sein, weil die konkreten OCR-Zeichen für die Fehlersuche entscheidend sind.
Das Log ist daher ein **privates Diagnoseartefakt** und sollte vor öffentlicher
Weitergabe trotzdem kontrolliert werden. Es wird weder automatisch versendet
noch ins Git-Repository hochgeladen.

## Tests und offene Grenze

`--self-test` prüft Append-Datei, tatsächlichen kopierten Dateiinhalt,
manuelle Nachkopie, Duplikatunterdrückung und die simulierte belegte
Zwischenablage. Die bestehenden `--ocr-self-test`-, `--navigation-self-test`-
und Windows-Build-Prüfungen bleiben erforderlich.

Das Logging schafft Belege für einen gezielten Fix, behebt aber nicht von sich
aus die bekannte unlesbare Ebene-0-World-Location-OCR. Ein echter Lauf des
neuen Clients mit dem regulären RuneLite-Profil `auto` bleibt notwendig.

**Update-Hinweis 1.8.3:** Der 1.8.2-RuneLite-Companion rief die vorhandene
Updatefunktion gar nicht auf. Deshalb muss 1.8.3 einmalig manuell installiert
werden; danach laufen die automatischen Prüfungen auch im RuneLite-Modus.
Bei einer neuen verifizierten Version startet nur OSRS Play neu, nicht RuneLite.
