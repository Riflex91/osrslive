# OSRS Play 1.6.3 – Native Weltraster-Ziffern erkennen

## Befund vom 24.10.2026

Das Windows-Log von 1.6.2 bestätigte Launcherstart, `osclient.exe`, Hintergrundaufnahme und das Öffnen von „Select a world“. Der anschließende Zustand `TARGET_WORLD_NOT_UNAMBIGUOUSLY_VISIBLE` protokollierte `validated_cells=0 raw_world_like_numbers=7 next_arrows=0`. Auf dem bereitgestellten Screenshot ist die gewünschte **Welt 399 bereits sichtbar: fünfte Spalte, zweite Reihe**. Ein Klick auf den rechten Pfeil würde die Seite verlassen und ist keine begründete Abhilfe.

## Implementierung

- Zusätzlicher OCR-Durchlauf für das feste native 1000×600-Weltlistenlayout. Jede Zelle wird nur als mögliche Suchregion verwendet, nicht als Beweis für eine bestimmte Weltnummer.
- Vor der Extraktion prüft eine Bildmaske mindestens fünf Spalten mit je elf farblich plausiblen Zeilen. Bei nicht passender Bildgeometrie, fehlendem „Select a world“-Titel oder schwarzem/leeren Bild erfolgt kein Layout-Fallback.
- Die dunklen Ziffern auf grau/goldenen Welt-Schaltflächen werden von Flaggen und Populationen getrennt und als Schwarz-auf-Weiß-Kontaktbogen an Windows OCR gegeben. Ergebnisse werden den visuellen Positionen zugeordnet.
- Eine Zahl allein rechtfertigt niemals einen Klick. Erst die unabhängige Validierung einer aufsteigenden Spalte mit mindestens sechs Weltzahlen erzeugt `WorldCell`-Treffer. Unerkannte und widersprüchliche Zellen bleiben unbekannt.
- Die vorhandene Weltwahl-Policy bevorzugt die **eindeutig** gelesene Zielwelt auf der aktuellen Seite. Seitenpfeile sind nur zugelassen, wenn der gewünschte Server nach bestätigter Zahlenfolge außerhalb der sichtbaren Spannweite liegt und der Pfeil bildgestützt bestätigt wurde.
- Vor dem tatsächlichen Zielwelt-Klick benötigt die Runtime dieselbe Nummer **und** Zellengeometrie aus einem weiteren unabhängigen Frame, dessen Zeitstempel mindestens 500 ms später liegt.
- `WORLD_GRID_DIAGNOSTIC` enthält zusätzlich `native_ocr=...` mit ausschließlich zusammengefassten Zählergebnissen (Raster erkannt, isolierte Nummern, validierte Weltzellen). Rohtext, Chat, Screenshots und Charakteridentität werden nicht ausgegeben.

## Grenzen und Tests

Unit- und Regressionstests prüfen, dass eine schwarze/nicht passende Szene das native Raster nicht freigibt, eine visuell passende synthetische Tabelle den OCR-Suchmodus aktiviert, und dass die kalibrierte Region der im Screenshot sichtbaren Zelle 399 entspricht. Die bereits vorhandenen Regeln für uneindeutige Welten, Seitenbegrenzung und Frame-Frische bleiben bestehen. Die Windows CI baut und prüft den Selfcontained-Release.

**Der endgültige Beleg, dass Windows OCR im echten Spielbild den Eintrag 399 tatsächlich erkennt und dass der offizielle Client den Fensterklick annimmt, kann nur aus einem neuen Live-Test stammen.** Wird das Raster nicht erkannt, hält der Agent weiterhin an. Weder blindes „Next“ noch eine ungeprüfte Auswahl erfolgen.

## Manuelle Diagnose

Bis die automatische Auswahl im echten Client bestätigt ist, kann Welt 399 auf der sichtbaren Seite direkt manuell gewählt werden. Für den nächsten Test sind die Logeinträge `WORLD_GRID_DIAGNOSTIC`, `SELECT_F2P_WORLD_REQUESTED` und der anschließende Bildschirmzustand relevant. Nach der späteren Bestätigung bitte den vollständig gesäuberten Sitzungslog senden.
