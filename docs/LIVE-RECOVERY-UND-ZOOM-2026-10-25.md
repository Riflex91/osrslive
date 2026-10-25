# Live-Korrekturen: Weltanzeige, Wiederverbindung und Minimap

Stand: 25.10.2026. Fortsetzung auf Basis von `main` nach dem Merge von PR #26. Ziel dieses Schritts ist, die in `AKTUELLE-FEHLER.md` beschriebenen Startblockaden tatsächlich zu überwinden. Die vorhandenen F2P-Routinen und der Aufgabenplaner werden dadurch erreichbar; ein durchgehend erfolgreicher Maxing-Agent ist damit noch nicht nachgewiesen.

## Korrigierte Ursachen

- Nach einem Mausradimpuls verwirft die Zoomroutine eine vorübergehend fehlende Minimap nicht mehr sofort. Sie beobachtet höchstens drei Aufnahmen desselben Schritts, ohne weitere Eingabe. Der Log trennt fehlende Minimap, unbekannten Spielbildschirm, fehlenden HUD, ungültige Kartenabmessungen und wiederholte Zeitstempel. Fensterwechsel, geänderte Fensterposition/-größe und erkannte Sitzungswechsel stoppen unmittelbar.
- Die Kreisdetektion kann bei gleichbleibendem Mittelpunkt einen anderen Radius schätzen. Im Live-Test wechselte der Ausschnitt von `796,4,156` auf `801,9,146`. Vorher blockierte schon die Ungleichheit der Rechtecke. Jetzt werden dieselben Bildschirmbereiche aus der Schnittmenge verglichen, ohne einen Ausschnitt auf die Größe des anderen zu skalieren. Beide müssen weiterhin native, bildgestützt verifizierte Karten sein; Mittelpunktabweichungen über vier Pixel bleiben gesperrt.
- Der alte Zwölf-Matches-Vorfilter war mit der vorhandenen Zehn-/Elf-Inlier-Regel unvereinbar. Zehn räumlich verteilte Inlier erreichen nun die vorgesehene zusätzliche Bestätigung durch eine zweite unabhängige Aufnahme. Neun Inlier reichen weiterhin nicht.
- Die SIFT-Zuordnung berücksichtigt beide Vergleichsrichtungen mit unverändertem Ratiofilter. Doppelte Punktpaare zählen einmal; widersprüchliche Zuordnungen werden entfernt. Bei schwacher nativer Geometrie werden beide Bilder zusätzlich mit demselben Faktor zwei verarbeitet. RANSAC und die Verteilungsprüfung arbeiten anschließend wieder in ursprünglichen Pixelkoordinaten. Widersprechende gültige Skalen sperren den Schritt.
- Die synthetische Zoomfixture schrieb zwei Nullwerte als `int` in eine `CV_64FC1`-Matrix. Dadurch blieb ein Teil des nativen Speichers uninitialisiert und Tests konnten zufällig scheitern. Die Werte werden jetzt korrekt als `double` geschrieben.
- Eine fehlende Welt-Fußzeile löst höchstens zwei zusätzliche passive Aufnahmen pro Beobachtungsrunde aus. Die Diagnose wird auf eine Runde je zehn Sekunden begrenzt. Ein Kontrast-/Bicubic-Durchlauf ergänzt die ursprüngliche Footer-OCR.
- Die weißen Weltziffern können zusätzlich mit den bereits vorhandenen nativen Ziffernmustern gelesen werden. Erforderlich sind eine eindeutig per OCR gelesene Switch-Beschriftung und drei **exakt** passende Ziffernraster an der zugehörigen Position. Beschädigte Ziffern, mehrere Marker und widersprechende Weltwerte sperren die Übernahme. Der Leser erhält die gewünschte Welt nicht als Parameter.
- Der echte Dialogtext `You were disconnected from the server.` fehlte in der Wiederverbindungserkennung. Ein fokussierter, kontrastverstärkter OCR-Durchlauf mit begrenztem glattem Zweitdurchlauf liest ausschließlich bekannte Meldungen im mittleren Dialogbereich. Leerzeichen und Satzendpunkte dürfen variieren; Buchstaben werden nicht geraten oder ersetzt.
- Wiederverbindung bleibt auch dann aktiv, wenn sie bereits beim Programmstart begann und eine folgende Aufnahme kurz unbekannt ist. In diesem Zustand erfolgen keine Eingaben. Die Meldung `RETURNED_TO_GAME` verlangt jetzt tatsächlich einen erkannten Spielbildschirm.
- Fokussierte Erkennung bleibt während der Wiederverbindung verfügbar. Ein wiedergewonnener vollständiger Welcome-Anker erhält dieselbe Footer-/Play-Prüfung wie Vollbild-OCR. Die Zustandsmaschine berücksichtigt die vorhandenen pixelverifizierten Play-/Enter-Buttons. Überlappende gleiche OCR-Buttonlabels werden nicht als zwei verschiedene Buttons gezählt.

## Verifizierte Live-Belege

Die Tests wurden mit dem Computer-Use-Skill an der Windows-Anwendung beobachtet. Start, Pause und reguläres Schließen wurden darüber bedient. Welt-, Zoom- und Inventaraktionen liefen durch den eigenen Hintergrundtransport des Programms. Ein erfolgreicher Versand allein gilt weiterhin nicht als ausgeführte Aktion.

- 21:39:20–22: Eine zusätzliche Footer-Aufnahme und danach zwei unabhängige Bestätigungen führten zu `WORLD_VISUALLY_CONFIRMED world=553`.
- 22:01:26: Der Wiederverbindungscontroller bestätigte selbstständig den sichtbaren Retry-Button und erreichte anschließend die Play-Now-Seite. Dies war noch kein vollständiger Spieleinstieg; die dabei aufgedeckten Folgezustandsfehler wurden separat korrigiert.
- 22:09:51: Der folgende Test bestätigte die gewünschte Welt 399 und startete das Spiel selbstständig. Charakter, 24/24 Skillwerte und Gesamtlevel 131 wurden erneut gelesen.
- 22:10:03–07: Die Zoomroutine bestätigte `scale=0.6486 inliers=24 matches=36 sampling=2`, anschließend `scale=1.0000 inliers=83` und `scale=0.9997 inliers=88`. Ergebnis: `MINIMAP_FULLY_ZOOMED_OUT_VISUALLY_CONFIRMED`, drei Abwärtsschritte.
- 22:11:32: Vollständige Inventarrunde: `cached_items=0 scanned_items=17 empty_slots=11 duration_ms=83991`.
- 22:11:36: Die nun erreichbare Navigation stoppte mit `NO_VERIFIED_MAP_MATCH features=120 candidates=0 inliers=0 best_ratio_matches=0`; keine Bewegung wurde gesendet. Eine spätere Wiederholung bei gedrehter Kamera blieb ebenfalls gesperrt.

## Verbleibende Weltlokalisierung und große Weltkarte

Bei fehlenden Minimapmerkmalen bzw. fehlendem verifizierten Kartenmatch prüft der Navigator zusätzlich eine gleichmäßig vergrößerte Merkmalsquelle. Koordinaten werden vor dem Fit zurückgerechnet; RANSAC-, Verteilungs-, Maßstabs-, Eindeutigkeits- und native Kartenkantenprüfung bleiben erhalten. Ein bereits gültiger oder mehrdeutiger nativer Match wird nicht ersetzt. Die Referenztests bestätigen korrekte Welt- und Klickkoordinaten bei passenden Beispielen; ein anderes rotiertes Beispiel bleibt beim vergrößerten Zweig unbestätigt und verwendet weiterhin seinen gültigen nativen Match. **Dieser zusätzliche Lokalisierungszweig wurde nach dem letzten Live-Log ergänzt; er ist kein bestätigter autonomer Wegschritt.**

Auf die Frage des Benutzers wurde die große Weltkarte über Computer Use im offiziellen Client geöffnet. Sie zeigt einen sichtbaren `You are here`-Marker, größere Geländeausschnitte und Ortsnamen. Die Darstellung ähnelt optisch den verwendeten Explv-Referenzkacheln stärker als die kleine Live-Minimap. Das ist eine technische Beobachtung, kein numerisch verifizierter Positionsmatch.

**Noch nicht implementiert:** automatisches Öffnen/Schließen der großen Karte, zuverlässige Erkennung ihres sichtbaren Kartenbereichs und Spielermarkers sowie eine unabhängig bestätigte Übergabe an den Routenplaner. Die große Karte ist damit ein konkreter nächster Lokalisierungsweg, aber keine bereits ausführbare Funktion dieser Änderung. Ein bloßer Klick auf die große Karte ersetzt weder Kollisionsrouting noch bestätigte Minimap-/Wegschritte.

## Tests und Grenzen

Der abschließende lokale Build lief ohne Warnungen und Fehler. `--self-test`, `--ocr-self-test`, `--navigation-self-test`, `--layout-self-test` und `--background-self-test` endeten jeweils mit Exitcode 0. Der Hintergrundtest prüft eigene verdeckte Testfenster und ersetzt keinen vollständigen autonomen Spielablauf.

Die Regressionen prüfen vorübergehende und dauerhafte fehlende Karten/HUDs, wiederholte Aufnahmen, Sitzungs-/Fensterwechsel, Pause während einer Wiederholung, exakte Zehn-Punkt-Geometrie, zu geringe Verteilung, konfliktfreie Merkmalszuordnung, gleichmäßige Bildvergrößerung, veränderte Kreisradien, unveränderte Bilder sowie den begrenzten Footer-Wiederholungsablauf. Native Footer-Tests beschädigen gezielt ein Glyphenpixel und entfernen bzw. verdoppeln den unabhängigen Marker. Wiederverbindungstests prüfen auch eine unbekannte Zwischenaufnahme vor dem ersten erfolgreichen Login und pixelbestätigte Einstiegsseiten.

Die Referenzkartentests erzeugen ihre Bildausschnitte aus der öffentlichen Referenzkarte. Sie prüfen Geometrie und Routing, aber nicht die Übereinstimmung mit echten Aufnahmen des offiziellen Clients. Diese Grenze darf nicht als bestandener Live-Navigationstest dargestellt werden.

Keine neue Eingabemethode, kein globaler Cursor-Fallback, keine Lockerung der Schutzregeln für unbekannte Weltpositionen, Gesundheit, Journale oder unklare Transaktionsergebnisse. Die aktive Item-Wiedererkennung bleibt wie zuvor sitzungsgebunden. Vollständige Bewegung, Bank-/Handelskette, Kampf, Produktion und alle F2P-Skills bis 99 müssen anhand tatsächlicher Fortschrittsbelege separat nachgewiesen werden.
