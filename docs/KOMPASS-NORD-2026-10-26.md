# Geprüfte Nordausrichtung der Minimap

Stand: 26.10.2026.

## Warum dieser Schritt ergänzt wurde

Der bisherige Matcher verwendete SIFT und affine Bildtransformationen, um gedrehte und gezoomte Minimap-Ausschnitte zuzuordnen. Die Referenztests prüfen diese Eigenschaft. Eine explizite Prüfung oder Korrektur der Kamerarichtung fehlte jedoch. Die neue Kompasskalibrierung beseitigt diesen fehlenden Vorbereitungsschritt, ohne aus einem Kompassklick bereits eine Weltposition abzuleiten.

## Verhalten

Nach Bestätigung der Spielsitzung und vor dem Kartenabgleich wird die rote/blaue Kompassnadel in der unterstützten nativen Ansicht erkannt. Die benachbarte bronzefarbene Umrandung wird nicht als rote Nadelhälfte gewertet. Bereits bestätigte Nordrichtung benötigt keinen Klick: Zwei neue, unabhängige Bilder müssen höchstens 7,5° Abweichung ergeben.

Bei gedrehter Nadel wird zuerst über dem bildlich erkannten Kompass geschwebt. Erst die exakte Beschriftung `Look North`, gegebenenfalls mit dem bekannten Zusatz `/ n more options`, erlaubt einen einzelnen Klick. Anschließend werden erneut zwei frische Nordbilder verlangt. Fehlender Tooltip, ignorierter Klick, alte Frames, verschwundener Kompass oder F8 erlauben keinen angenommenen Erfolg und keine blinde Klickfolge. Vor einer Eingabe wird das Ziel nach einer Aufnahmeerneuerung erneut geprüft.

Nach Pause/Wiederaufnahme wird die Richtung neu geprüft. Eine später erkannte Abweichung verwirft die bisherige Freigabe. Während einer laufenden Gameplay-Routine stoppt eine erkannte Richtungsänderung weitere Eingaben; die nächste Vorbereitung kann die Ausrichtung erneut prüfen. Dies funktioniert auch mit einer noch gültigen Spielsitzung bei geöffnetem Inventar, ohne eine fehlende aktuelle Skilltafel als Richtungsbeleg zu verwenden.

Ein bereits offenes Kartenpanel wird zuerst über seine vorhandene Routine bearbeitet; der Kompass wird danach geprüft. Nordrichtung ersetzt weder Zoomkalibrierung noch Stockwerk-, Positions- oder Wegbestätigung. Es werden keine Spielspeicherwerte oder Zugangsdaten gelesen.

## Live-Befund

Im Lauf `compass-04` am 26.10.2026 wurden um **17:19:51** 35 rote Nadelpixel und 16 blaue Pixel im offiziellen Client erkannt. Die Richtung betrug **0,00°**. Zwei unabhängige Nordbilder führten um **17:19:53** zu `COMPASS_ALREADY_NORTH_CONFIRMED`. Ein Kompassklick war in dieser Szene nicht nötig.

Danach wurden die Weltkartenposition und der Zoomanschlag bestätigt. Der Reiseversuch um **17:22:50** blieb trotz Nordrichtung bei `NAVIGATION_LOCALIZATION_NO_VERIFIED_MAP_MATCH features=246 candidates=0 inliers=0 best_ratio_matches=4` stehen. Die Drehung ist somit in diesem bestätigten Nordzustand nicht die Ursache des fehlenden Kartenmatches. Darstellung und Referenzquelle bleiben zu untersuchen. Die bestehenden öffentlichen Referenzkacheln sind als [Weltkartenkacheln dokumentiert](https://github.com/Explv/osrs_map_tiles).

Die Rückstellung einer tatsächlich gedrehten Kamera ist in Treiberfixtures geprüft; ein vollständiger Live-Nachweis dieses Klickfalls steht noch aus. Manuelle Versuche zur Herstellung einer gedrehten Ausgangslage lieferten keinen verlässlich bestätigten Richtungswechsel und werden nicht als Beleg gezählt. Die anschließenden Ergänzungen zur Wiederaufnahme und späteren Richtungsänderung sind durch den Quellstand und die Tests, noch nicht durch einen getrennten Live-Durchlauf geprüft.

## Tests

`--compass-self-test` ist zusätzlich in `--self-test` enthalten. Geprüft werden Nord/Ost/Süd/diagonale Nadelrichtungen, bronzeartige Randpixel, fehlende blaue Nadel, erkannter Tooltip, fehlender Tooltip, ein wirkungsloser Klick, bereits vorhandene Nordrichtung ohne Eingabe, unabhängige Bilder, alte Aufnahmen, verschwundenes Eingabeziel, geöffnetes Kartenpanel und Pause. Die übrigen OCR-, Karten-, Fenster- und Hintergrundprüfungen bleiben erforderlich.

Der lokale Release-Build, vollständige Selbsttests, Windows-OCR-Fixtures, Karten-/Navigationstests, Layout und Hintergrundtransport wurden erfolgreich abgeschlossen. Der Cursor-/Fokusvergleich des Hintergrundtests blieb wegen gleichzeitiger Benutzereingaben unbestimmt.
