# Weltkarte als Lokalisierungshilfe

**Fortschreibung:** In `world-map-12` wurden Spielermarker, Oberflächenposition 3155/3202 und bestätigtes Schließen live nachgewiesen. Die anschließende Minimap-Zuordnung bleibt gesperrt. Die folgenden Abschnitte dokumentieren den älteren Stand bis `world-map-08`; [aktuelle Korrekturen und Ergebnisse](WELTKARTEN-KORREKTUREN-2026-10-26.md).

Stand: 26.10.2026. Die Routine zum Öffnen, Lesen und bestätigten Schließen der großen Weltkarte ist implementiert. Das automatische Öffnen wurde im offiziellen Client nachgewiesen. Eine vollständige Live-Lokalisierung mit anschließendem Wegschritt ist noch nicht nachgewiesen.

## Ablauf

Nach bestätigtem Charakter und vollständigen Skillwerten versucht die App zunächst die vorhandene Minimap-Lokalisierung. Scheitert sie, folgt die Weltkarte vor der Zoomkalibrierung. Ein erfolgreicher Minimap-Treffer benötigt diesen Umweg nicht.

Die geometrisch erwartete Position des Globus erlaubt nur einen Hover. Ein Klick benötigt zusätzlich das erkannte Globussymbol oder einen exakten Tooltip im zugehörigen Bildbereich: `Open Floating panel` beziehungsweise `Ctrl+M to open`. Der rote Shortcut erhält eine eigene OCR-Aufbereitung. Nach einer veralteten Aufnahme wird auch das konkrete Eingabeziel erneut geprüft.

`Game features`/`You are here`, `Search` und `Gielinor Surface` identifizieren das native Kartenpanel. Die Schließkontrolle wird getrennt anhand ihrer sichtbaren Pixel geprüft. Eine unlesbare Schließkontrolle verhindert somit keine passive Kartenanalyse, erlaubt aber auch keinen angenommenen Schließklick.

Ein einzelner goldener sternförmiger Marker muss eindeutig sein. Kompakte goldene Gebäude und mehrere plausible Marker werden abgelehnt. Die SIFT-Zuordnung verlangt räumlich verteilte Merkmale, RANSAC-Konsens, nordorientierte Geometrie und unabhängige Übereinstimmung der Bildkanten. Verwendet wird die Position des Spielermarkers, nicht die Mitte des Kartenausschnitts.

Zwei zeitlich unabhängige Aufnahmen müssen dieselbe Oberflächenposition ergeben. Erst nach einem geprüften Schließklick und zwei frischen Aufnahmen ohne Panel und Schließsymbol wird der Hinweis übernommen. F8 unterbindet weitere Eingaben. Kartenbilder und Charaktertexte werden nicht gespeichert.

Der Hinweis begrenzt die zusätzliche Minimap-Suche auf einen Bereich von ungefähr 128 × 128 Spielkacheln. Alle verfügbaren Stockwerke bleiben Kandidaten: `Gielinor Surface` beweist das Stockwerk der Spielfigur nicht. Vorhandene gültige oder mehrdeutige globale Treffer werden nicht überstimmt. Lokale Bildursprünge werden in Welt- und Klickkoordinaten zurückgerechnet.

Der Hinweis gilt höchstens fünf Minuten und wird bei Sitzungswechsel gelöscht. Ein automatisches Nachlokalisieren für längere Reisen ist noch offen. Der Sitzungszustand wird bereits vor der Lokalisierung übernommen, damit der erste Aufgabenzyklus den neuen Hinweis nicht wieder verwirft.

## Live-Belege und Grenzen

Am 25.10. blieben frühe Testläufe bei der Tooltip-Erkennung hängen. Die Weltlisten-Seite 2 lieferte außerdem zu wenig bestätigte Zellen. Für einzelne damalige Navigationstests wurde die Spielsitzung manuell vorbereitet; diese Läufe beweisen keinen autonomen Gesamteinstieg. Später war auch die Charakterzeile im Chat zeitweise unlesbar. Diese Beobachtungsgrenzen bleiben relevant.

Am 26.10.2026 erreichte `world-map-08` den offiziellen Spieleinstieg eigenständig:

| Lokale Logzeit | Beobachtung |
| --- | --- |
| 16:18:19 | Launcher-Startknopf über Fensternachricht ausgelöst |
| 16:18:30 | Welt 399 anhand unabhängiger Footer-/Play-Aufnahmen bestätigt |
| 16:18:36 | Spieleinstieg ausgelöst |
| 16:18:40 | Charakter und 24 Skillwerte bestätigt, Kartenroutine gestartet |
| 16:18:45 | Roter Shortcut erkannt, Globusklick ausgelöst |
| 16:18:46 | Kartenbeschriftungen erkannt; Schließsymbol nicht bestätigt |
| 16:18:50 | `WORLD_MAP_POSITION_NOT_CONFIRMED`, keine Bewegungsfreigabe |

Die Karte wurde somit durch das Programm geöffnet. Die komplette Kette aus verifizierter Position, bestätigtem Schließen, Minimap-Zuordnung und Reise bleibt offen. Die abschließende Änderung trennt passive Panelanalyse und Schließkontrolle; sie ist durch Regressionstests geprüft, aber noch nicht in einem weiteren Live-Durchlauf bestätigt. Der neue konkrete Abbruchgrund für die unbestätigte Schließkontrolle lautet `WORLD_MAP_CLOSE_CONTROL_NOT_VERIFIED`.

## Prüfung des Quellstands

- Release-Build: keine Fehler oder Warnungen.
- `--self-test`, einschließlich `--world-map-self-test`: erfolgreich. Geprüft werden unter anderem Globus/rotes Fremdsymbol, Tooltip-Ort, doppelte Marker, alte Frames, fehlende Schließkontrolle, Zielwechsel nach Aufnahmeerneuerung, bestätigtes Schließen und Pause.
- `--ocr-self-test`: erfolgreich, einschließlich getrennter Kartenbeschriftungen in derselben Werkzeugleistenzeile.
- `--navigation-self-test`: erfolgreich; der große Referenzausschnitt mit versetztem Spielermarker erreicht 807 Inlier. Regionale Koordinaten und Stockwerk-Mehrdeutigkeit werden separat geprüft.
- `--layout-self-test`: erfolgreich, unveränderte 1000 × 600 Spielansicht.
- `--background-self-test`: erfolgreicher verdeckter Testfenstertransport mit beobachteter Bildänderung. Der Vergleich von Cursor/Fokus blieb wegen gleichzeitiger Benutzereingaben unbestimmt.

Die Kartenfixtures stammen aus öffentlichen Referenzbildern. Sie beweisen Rechenweg und Schutzbedingungen, nicht die Übereinstimmung mit der Live-Kartendarstellung. Ein Aufgaben-Vorschlag ist weiterhin kein Trainings- oder Levelnachweis.
