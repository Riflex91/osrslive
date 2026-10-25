# Neuer Live-Log: Minimap-Zoom und Kartenlokalisierung (25.10.2026)

Basis des Logs: PR #26, Commit `c5e6ffe7d5c90621bc5c341ec9204b866a59625e`, OSRS Play 1.7.2.

## Befunde

- Offizieller Jagex-Client wurde gestartet; Welt 399 gewählt und der zweite Spieleinstieg ausgelöst. Alle 24 Skills mit Gesamtlevel 131 wurden konsistent gelesen. Serverseitige Authentifizierung bleibt nicht direkt nachweisbar.
- Item-Datenbank: 4.674 Referenzeinträge aus lokalem Wiki-Cache. Drei Warnungen zu nicht im Katalog verifizierten direkten OCR-Namen blieben; diese Namen wurden nicht für Cache-Wiedererkennung freigegeben.
- Inventarscan: 8 Cachetreffer, 9 neue Untersuchungen, 11 leere Plätze, Dauer 28.740 ms. Der Scan war damit vollständig, aber noch relativ teuer.
- Die vier Referenzebenen wurden geladen. Danach: `NAVIGATION_LOCALIZATION_NO_VERIFIED_MAP_MATCH features=214 candidates=0 inliers=0`. Damit war der Minimap-Ausschnitt vorhanden, aber keine Position konnte mit ausreichend Evidenz zugeordnet werden. Es erfolgte kein verifizierter Wegschritt.
- Der Log alleine beweist **nicht**, dass ein falsch gewählter Minimap-Zoom die einzige Ursache ist. Kartenstil/Renderer, Feature-Matching und Maßstabsgrenzen können ebenso beteiligt sein.

## Implementierung

1. **Vor dem ersten Gameplay**, nach wiederholt bestätigten Charakter-/Skillwerten und sichtbar erkanntem InGame-HUD, verlangt die Automatik eine eindeutige, pixelbestätigte Minimap. Sie beobachtet eine zweite frische Bildaufnahme vor dem ersten Zoomimpuls.
2. `WM_MOUSEWHEEL` wird wie die übrigen Eingaben ausschließlich an das Spiel-eigene Render-Child-HWND gesendet. Das Windows-Protokoll benötigt hierfür Bildschirmkoordinaten im Nachrichtenparameter; der globale Mauszeiger und der Vordergrund werden nicht verändert. Zunächst wird genau eine Abwärtsstufe gesendet, niemals eine ungezielte Klickaktion.
3. Zwei Minimapbilder werden mit SIFT-Merkmalen und RANSAC-Geometrie verglichen. Nur ausreichend verteilte, übereinstimmende Bildmerkmale erlauben eine Aussage über tatsächliche Maßstabsänderung. Wird nach dem ersten Abwärtsimpuls nichts verändert, prüft ein reversibler Aufwärtsimpuls, ob der Client den Zoom überhaupt annimmt (oder bereits am unteren Anschlag war).
4. Danach werden höchstens 24 einzelne Abwärtsimpulse gesendet. Erst **nach einer beobachteten Zoomänderung und zwei späteren Abwärtsimpulsen ohne messbare weitere Maßstabsänderung** wird `MINIMAP_FULLY_ZOOMED_OUT_VISUALLY_CONFIRMED` protokolliert. Bei Mehrdeutigkeit, fehlendem neuen Frame, ignoriertem Transport, falscher Richtung oder Zeitüberschreitung wird das Gameplay blockiert. Eine Eingabenannahme wird nie aus `PostMessage` allein behauptet.
5. Der Zoomstatus gilt nur für die aktuelle visuell bestätigte Spielsitzung. Fenster-/Sitzungswechsel führen erneut zur Prüfung. F8/Pause und Observe-only unterbinden Zoom-Eingaben.
6. Der folgende `WorldNavigator`-Fehler nennt zusätzlich `best_ratio_matches`, die größte Zahl geometrisch noch ungeprüfter SIFT-Ratio-Matches aus dem Kartenvergleich. Damit lässt sich im nächsten Log unterscheiden, ob bereits die Merkmalszuordnung oder erst die spätere Geometrieprüfung scheitert.

## Grenzen und Testverfahren

Der erweiterte offizielle C++-Client bietet laut Jagex eine Minimap-Zoom-Einstellung; das Zoomen kann in den Client-Einstellungen deaktiviert sein. Ein zugestelltes Fensternachrichten-Ereignis garantiert noch nicht, dass `osclient.exe` es verarbeitet. Wenn keine Bildwirkung sichtbar ist, verhindert der Controller ausdrücklich die Behauptung eines verifizierten Minimalzooms. Quellen: [Jagex, Enhanced C++ Client Improvements (2023)](https://secure.runescape.com/m=news/wilderness-boss-rework-tweaks--more?oldschool=1) und die [OSRS-Wiki-Einstellungen](https://oldschool.runescape.wiki/w/Settings).

Die Bildprüfung ist nicht gleichbedeutend mit einem erfolgreichen Live-Test am offiziellen Client. Neue Regressionen überprüfen: gerichtete Mausradnachrichten am verdeckten Testfenster, positive und unveränderte Maßstabsänderungen, schon vollständig herausgezoomte Minimap, ignoriertes Scrollen, fehlende Minimap und Pause. Windows-CI führt zusätzlich normale OCR-, Layout-, Navigations- und Hintergrundtests aus.

**Für den nächsten Test:** Bei `MINIMAP_ZOOM.RESULT` prüfen, ob `Completed` gemeldet wird. Bei `Blocked` zuerst die Client-Einstellung „Enable minimap zoom“ kontrollieren. Nach dem manuellen Aktivieren ermöglicht F8-Pause/Fortsetzen einen neuen Verifikationsversuch. Erst bei bestätigtem Zoom und anschließend `NAVIGATION_LOCALIZATION...` lässt sich die verbleibende Kartenlokalisierung unabhängig untersuchen. Der Bot soll keinesfalls aus einem unsicheren Kartenbild eine Bewegung ableiten.
