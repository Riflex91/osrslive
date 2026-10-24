# OSRS Play 1.6.4 – Korrektur der Weltlisten-Zeilen und Ziffern

## Reproduzierter Live-Befund am 24.10.2026

Version 1.6.3 startete den Jagex Launcher und den offiziellen Spielclient korrekt. Die Weltliste wurde geöffnet, aber die Anzeige verblieb bei `TARGET_WORLD_NOT_UNAMBIGUOUSLY_VISIBLE`.

`WORLD_GRID_DIAGNOSTIC` meldete: `validated_cells=0`, `raw_world_like_numbers=7`, `native_ocr=raster_verified=true_isolated_numbers=16_verified_world_cells=0`. Die zugehörige 1000×600-Spielansicht zeigt Zielwelt **399** bereits in der **fünften Spalte, zweiten Zeile**. Auf den rechten Pfeil zu klicken wäre die falsche Aktion.

**Ursache:** Die native Tabelle verwendet 24 Zeilen mit genau **19 Pixel** Zeilenabstand. Version 1.6.3 verwendete beim Cropping `18.25` Pixel und untersuchte bis zu 25 Zeilen. Der kumulative Versatz zerstörte im unteren Tabellenbereich die Ziffernausschnitte.

## Fix

- `WorldGridOcr.NativeNumberRegion` verwendet jetzt 19 Pixel Abstand und 24 native Zeilen sowie einen an die Ziffern angepassten Rechteckzuschnitt. Das beschneidet nicht mehr die ersten Zeichenpixel.
- Vor der allgemeinen Windows-OCR wird ein rein bildgestützter Decoder für die nativen 7×11-Pixelziffern verwendet. Schwarz-Pixel auf hellen bzw. goldenen Tabellenzeilen werden über ein kalibriertes Font-Muster mit maximal einer Pixelspalte und zwei Pixelzeilen Toleranz ausgewertet.
- Beschädigte oder mehrdeutige Ziffern werden nicht geraten: Maximal acht unterschiedliche Mustereinheiten und mindestens zwei Einheiten Vorsprung vor dem zweitbesten Zeichen sind erforderlich. Einzige Zahlen beweisen keine Welt.
- Ein erkannter dreistelliger Eintrag wird nur dann zur `WorldCell`, wenn die gesamte Spalte anhand aufsteigender Weltzahlen unabhängig validiert werden konnte. Bei fehlender Pixel-Schrift-Erkennung bleibt die vorhandene Windows-OCR als konservativer Fallback.
- `VisualStartup` verlangt vor dem Klick auf eine eindeutige Zielwelt weiterhin zwei zeitlich verschiedene Bildschirmbeobachtungen derselben Zahl an derselben Position. Es gibt weiterhin **keinen** Klick auf einen unbestätigten Seitenpfeil oder eine aus ihrer Position angenommene Weltnummer.

## Prüfung

Ein lokaler pixelbasierter Abgleich mit dem vom Anwender bereitgestellten Screenshot dekodierte 167 von 168 sichtbaren Rasterpositionen gemäß den Bildziffern. Die farblich abweichende rote Welt 345 wurde als unsicher verworfen. Dies ist eine **Offline-Bildauswertung**, kein Nachweis für einen angenommenen Live-Klick.

Die `--self-test`-Regression erzeugt eine vollständige synthetische 1000×600-Weltliste aus den Glyph-Masken und prüft die Dekodierung der Welt 399 (Spalte 5, Zeile 2), das 19-Pixel-Raster und die Ablehnung eines leeren Bildes. Windows-CI prüft zusätzlich Build, OCR-Fixtures, Fenstertransport, UI-Layout und die veröffentlichte Ein-Datei-EXE.

Der tatsächliche Klick auf den offiziellen Jagex-Client und die anschließende Übernahme von Welt 399 bleiben live zu bestätigen. Relevante Logzeilen sind `WORLD_GRID_DIAGNOSTIC`, `AWAITING_INDEPENDENT_WORLD_CELL_CONFIRMATION`, `SELECT_F2P_WORLD_REQUESTED` und die nachfolgende Spielphase.

## Automatische Updates

Nach erfolgreich geprüftem Merge nach `main` veröffentlicht die Windows-CI den neuen Release und die zugehörige SHA-256-Manifestversion. Version 1.6.3 sollte 1.6.4 beim nächsten Programmstart automatisch erkennen und bei bestätigter Prüfsumme installieren. Während eines aktiven Spiels wird nicht erzwungen neu gestartet.
