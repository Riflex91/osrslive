# Minimap-Referenzen und maximaler Zoom

Die große Weltkarte und die Minimap verwendeten bisher dieselben Explv-
Weltkartenbilder. Deren Symbole und Objektplatzierung unterscheiden sich vom
Minimap-Renderer. Die Navigation verwendet jetzt vier eigene, eingebettete
Minimap-Atlanten mit 4 Pixeln pro Kachel. Die große Weltkarte bleibt bei ihren
bisherigen Referenzen. Abdeckung: x 2880–3455, y 3072–3583, Ebenen 0–3.
SHA-256 und Bildgröße werden beim Laden geprüft; eine fehlende Ebene sperrt
die Navigation. Regionale Ausschnitte berücksichtigen ihren absoluten Ursprung
und verfallen nach fünf Minuten auch bei direktem Aufruf der lokalen Suche.

Die Referenzen wurden aus einer Kopie der statischen offiziellen Cache-Dateien
erzeugt. Java und Cache werden vom ausgelieferten Programm nicht benötigt.
Generator, festgelegte Abhängigkeiten, Eingabeprüfsummen und Quellen stehen in
[ci/minimap-reference](../ci/minimap-reference/README.md). Es werden keine
Client-Speicherwerte oder Kontodateien gelesen. Statische Objekte, Terrainfarben
und Brücken können weiterhin vom tatsächlichen Bild abweichen.

## Zoom auf Benutzerwunsch

Die Live-Kalibrierung fährt jetzt **zum maximalen Zoom hinein**. Sie prüft echte
relative Vergrößerung und anschließend zwei weitere wirkungslose Schritte am
oberen Anschlag. Bleiben die ersten Schritte wirkungslos, wird die andere
Fensterroute einzeln geprüft und bei Bedarf ein begrenzter Gegenversuch nach
außen ausgeführt. Erst die beobachtete Wirkung und das anschließende erneute
Erreichen des oberen Anschlags bestätigen maximalen Zoom. Ignorierte Eingaben
gelten weiterhin nicht als Anschlag. F8/Pause und frische Aufnahmen bleiben
erforderlich. Der neue Ergebniscode lautet
`MINIMAP_FULLY_ZOOMED_IN_VISUALLY_CONFIRMED`; der alte Zoom-out-Code gilt nur
noch für die explizite Gegenrichtungsprüfung in Regressionen.

## Tests und Live-Stand

- Build ohne Fehler oder Warnungen.
- Selbsttests mit mittlerem und bereits maximalem Zoom, beiden Fensterrouten,
  ignorierten Eingaben, Pause und passiven Wiederholungen bestanden.
- Navigationstests: vier Ebenen, beschädigte Assets, verschobener Atlasursprung,
  abgelaufener Hinweis, mehrdeutige Stockwerke, rotierte/vergrößerte Ausschnitte
  sowie Kollisionswege und Klickkoordinaten bestanden.
- Erster Live-Vergleich `minimap-01`: Wiederverbindung, Skillwerte, Weltkarte,
  Zoom-out und Inventarprüfung erfolgreich. Um 17:51:54 jedoch weiterhin nur
  drei passende Paare bei 240 Merkmalen, keine Transformation und keine Bewegung.
  Die andere Symbolanordnung allein beseitigt den Fehler nicht.
- `minimap-02-maxzoom`: Um 17:56:37 wurde der obere Anschlag live bestätigt.
  Beobachtete Vergrößerungen umfassten 1,5118 / 1,3208 / 1,2452; abschließend
  zwei unveränderte Schritte mit 1,0003 und 1,0000 (je 26 Inlier). Insgesamt
  neun Vorwärtsschritte. Weltkartenposition 3155/3202 wurde zuvor um 17:56:19
  aus zwei unabhängigen Bildern bestätigt (33 Inlier). Die Inventarrunde las
  17 Items/11 leere Plätze in 79 Sekunden. Um 17:58:00 blieb der anschließende
  Kartenmatch mit 137 Merkmalen, einem passenden Paar und null Inliern blockiert.
  Maximaler Zoom verbessert die Lokalisierung an dieser Stelle damit nicht;
  kein Wegschritt wurde freigegeben. Die Einstellung bleibt wie angefordert
  maximal; als nächster Fehler bleibt der Darstellungs-/Merkmalsabgleich offen.

Referenzfixtures stammen weiterhin aus den Referenzbildern selbst. Sie sind
kein Nachweis einer tatsächlichen Reise. Vor Bewegung bleiben ein geometrisch
und über Bildkanten bestätigter eindeutiger Match sowie zwei frische stationäre
Positionen erforderlich. Vollständiges autonomes Training ist noch nicht
bestätigt.
