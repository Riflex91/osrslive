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

## Folge-Log und verbesserte Hintergrund-Zustellung

Der Build `4ec2565` aus PR #26 wurde erneut live getestet (20:38–20:39 Uhr). Einloggen, 24 Skillwerte und Charakterbestätigung funktionierten. Die Minimap-Kalibrierung sendete einen `WheelDown`- und einen `WheelUp`-Impuls per Fensternachricht, blockierte jedoch anschließend mit `MINIMAP_ZOOM_WHEEL_EFFECT_NOT_VERIFIED`. Der Benutzer hat unabhängig überprüft, dass **manuelles Scrollen direkt über der Minimap die Zoomstufe verändert**. Damit ist die grundsätzliche Zoomfunktion des Clients bestätigt, nicht jedoch seine Annahme von Hintergrundnachrichten oder die Zuverlässigkeit des Bildvergleichs.

Nach Microsoft Win32 wird `WM_MOUSEWHEEL` an das fokussierte Fenster zugestellt und gegebenenfalls vom Standardfensterprozedere an Elternfenster weitergereicht. Ein gezielt an ein Render-Child gepostetes Ereignis ist daher nicht automatisch äquivalent zum realen Mausrad. Quelle: [Microsoft WM_MOUSEWHEEL](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-mousewheel).

Die Kalibrierung erprobt jetzt **zwei separate, durch Screenshots getrennte Routen**: zuerst das rendernde OSRS-Child-HWND und nur bei ausbleibendem messbaren Effekt das offizielle OSRS-Spiel-HWND (`osclient.exe`). Sobald eine Route eine verifizierte Verkleinerung bzw. beim unteren Anschlag einen reversiblen Zoom-in zeigt, wird nur diese Route für weitere Abwärtsschritte verwendet. Die Ziel-PID muss weiterhin zur Spiel-PID passen. Kein Weg verwendet `SendInput`, bewegt den globalen Cursor oder aktiviert das Spielfenster.

Jeder Versuch meldet `[MINIMAP_ZOOM] STEP | input=... target=RENDER_CHILD|GAME_WINDOW scale=... inliers=... evidence=...`. Bleiben alle vier begrenzten Richtungs-/Routenproben unverändert, stoppt die Automatik mit `MINIMAP_ZOOM_BACKGROUND_WHEEL_NOT_OBSERVED`, statt eine erfolgreich herausgezoomte Minimap vorzutäuschen. Ein zusätzlicher synthetischer Test deckt beide Routen einschließlich des Falls „bereits ganz herausgezoomt“ ab. Der echte OSRS-Client muss danach erneut mit einem neuen Windows-Build getestet werden.

## Manueller Zoom-Kontrolltest: Hintergrund-Eingabe funktioniert

Im Live-Log vom 25.10.2026, 21:07–21:08 Uhr (Build `5e0f0ee`, PR #26) wurde der Minimap-Zoom nach manueller F8-/Button-Pause sichtbar hineingezoomt und nach dem Fortsetzen neu kalibriert. Diesmal zeigten drei aufeinanderfolgende `RENDER_CHILD`-Abwärtsimpulse echte, geometrisch verifizierte Änderungen: `scale=0.8753 inliers=15`, `0.8536/15`, `0.8381/12`. Damit ist die Hintergrund-Zustellung an das OSRS-Renderfenster **im echten Spiel nachgewiesen**. Die vierte Abwärtsstufe konnte mit `inliers=11` nicht über die alte starre Untergrenze von zwölf Merkmalen bestätigt werden (`MINIMAP_ZOOM_GEOMETRY_UNVERIFIED`). Die Kalibrierung blockierte korrekt statt vorschnell einen Zoomanschlag zu behaupten.

Die Bildprüfung verwendet nun die folgenden eng begrenzten Ergänzungen:

- Bei fehlender Bildgeometrie wird ohne erneute Eingabe bis zu zweimal passiv nachbeobachtet. Jede Aufnahme wird mit einer neuen Zeitmarke und demselben unabhängigen Vorherbild verglichen.
- Bereits zehn oder elf räumlich verteilte RANSAC-Inlier können eine **vorläufige** Skalenmessung liefern, müssen aber in einer zweiten unabhängigen Aufnahme mit höchstens `0.035` Skalenabweichung erneut bestätigt werden; andernfalls bleibt der Schritt ungeprüft. Ab zwölf Inliern gelten die bisherigen strengen Geometrieprüfungen.
- Der Schritt-Log enthält künftig `sample` und `matches` sowie `scale`, `inliers` und die Evidenzursache. Pausen, Sitzungsgrenzen und das Verbot von Bewegung ohne verifizierte Position bleiben unverändert.

Bei einem weiteren Test sollen die letzten Abwärtsimpulse `scale≈1.0` zeigen und erst nach einem vorher bestätigten Herauszoomen und zwei solchen stabilen Anschlagsbelegen `MINIMAP_FULLY_ZOOMED_OUT_VISUALLY_CONFIRMED` auslösen. Der Win32-Fallback bleibt für andere Clients als Reserve erhalten, wird aber beim beobachtet funktionierenden `RENDER_CHILD` nicht verwendet.
