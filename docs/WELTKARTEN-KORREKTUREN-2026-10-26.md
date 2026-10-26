# Bestätigte Weltkarten-Lokalisierung und verbleibende Minimap-Grenze

Stand: 26.10.2026, Live-Lauf `world-map-12`, Logzeiten in Europe/Berlin.

## Korrekturen

Das native Kartenpanel reicht weiter nach rechts als zunächst angenommen. Der geprüfte Kartenausschnitt ist jetzt `(192,4,508,386)`, die Schließkontrolle liegt bei `(671,8,28,25)`. Das orange X wird anhand beider Diagonalen und ausgesparter Seitenbereiche erkannt. Damit ist auch das reale dickere X mit 306 orangefarbenen Pixeln zulässig; ein gefülltes orangefarbenes Rechteck bleibt ausgeschlossen.

Der Spielermarker dreht sich. Seine dunklen Konturen teilen die goldenen Bildpixel in vier Komponenten. Ein räumlich begrenztes morphologisches Schließen verbindet diese Teile vor der bestehenden Form-/Flächenprüfung. Solide Gebäude und mehrere plausible Marker bleiben gesperrt.

Kreisförmige Kartensymbole werden bei Merkmals- und Kantenprüfung ausgeblendet. Merkmalszuordnungen müssen außerdem in beide Richtungen eindeutig sein. Die Mindestanforderungen an RANSAC-Anteil, räumliche Verteilung, Orientierung und Kantenüberdeckung wurden nicht abgesenkt. Unterschiedliche Abbruchgründe unterscheiden jetzt Konsens, Verteilung, Orientierung und Bildkanten.

Eine vorhandene Gameplay-Sitzung darf Navigationssperren nicht umgehen, wenn die aktuelle Aufnahme den Skills-Tab vorübergehend nicht bestätigt. Diese Freigabe wird nun vor jedem Aufgabenzyklus geprüft. Ein während einer Aufgabe erkanntes Kartenpanel blockiert den Ausführungstreiber zusätzlich.

Auf der Weltlisten-Seite 2 konnten sechs grob erkannte Zellen den präziseren nativen Lesedurchlauf verhindern. Es fehlte dann der Spaltenabstand für die alternative Raster-OCR; die Seite erschien fälschlich unzureichend gelesen. Ein verifiziertes natives Raster erhält jetzt immer den nativen Lesedurchlauf. Ein Test mit Seite 326–496, nur sechs groben Zellen und einer zusätzlichen leeren Negativaufnahme prüft diese Korrektur. Ein vollständiger neuer Live-Einstieg über diese Seite wurde noch nicht separat bestätigt.

## Live-Ergebnis

`world-map-10` erkannte den einzelnen Spielermarker und das X im offiziellen Client und schloss die Karte selbstständig. Die damaligen 31 Inlier bei 52 Paaren reichten noch nicht zur Positionsfreigabe.

`world-map-12` öffnete die Karte automatisch. Nach Ausblenden der kreisförmigen Symbole ergaben frische Aufnahmen rund 816–820 Merkmale, 30 gegenseitige Zuordnungen und 20 RANSAC-Inlier. Nach einem vorübergehend als Sitzungswechsel interpretierten Schließübergang wurde die Prüfung wiederholt. Um **16:47:01** wurde die Oberflächenposition **3155/3202** aus zwei unabhängigen Aufnahmen und bestätigtem Schließen übernommen. Das Spielerstockwerk ist damit weiterhin nicht bewiesen.

Um **16:47:08** folgte die bestätigte Zoomkalibrierung: Skalenwechsel `0,6592`, anschließend `1,0002` mit 69 beziehungsweise 66 Inliern. Um **16:48:21** endete die Inventarrunde mit 17 untersuchten Items und 11 leeren Plätzen nach etwa 72 Sekunden. Die nächste Runde nutzte 14 Cachetreffer und untersuchte nur drei Items erneut.

Der Reiseversuch um **16:48:23** blieb stehen: `NAVIGATION_LOCALIZATION_NO_VERIFIED_MAP_MATCH features=262 candidates=0 inliers=0 best_ratio_matches=3`. Der regionale Suchhinweis genügt noch nicht zur Zuordnung der tatsächlichen Minimap zu den Referenzkacheln. Kein Wegschritt wurde freigegeben. Weitere Versuche bestätigten denselben Blocker. Eine einzelne Inventar-Tab-Erkennung schlug zwischenzeitlich fehl, die nächste Runde konnte wieder abgeschlossen werden.

Der isolierte Abgleich in `world-map-11` benutzte eine manuell geöffnete Karte; er ist kein Beleg für automatisches Öffnen. `world-map-12` lieferte die anschließend beschriebene automatische Kartenkette. Die Tooltip-Erkennung kann bei gleichzeitigen Mausbewegungen weiterhin ausbleiben; ein ungeprüfter Ersatzklick erfolgt nicht.

## Tests und nächster Schritt

Release-Build ohne Warnungen; Selbsttests, Windows-OCR-Fixtures, Karten-/Navigationstests, Layout und verdeckter Hintergrundtransport erfolgreich. Die Kartenreferenzfixture erreicht 679 Inlier. Regressionen prüfen zusätzlich das dicke X, orange Fremdflächen, fragmentierte Marker, die fortbestehende Sperre ohne aktuelle Skillbestätigung und die Weltlisten-Seite 2.

Als Nächstes muss der Darstellungsunterschied zwischen echter Minimap und Referenzkarte untersucht werden. Die Weltkartenposition ist ein regionaler Suchhinweis, kein Ersatz für eine kalibrierte Minimap-Klicktransformation. Der Hinweis verfällt weiterhin nach fünf Minuten. Ein bestätigter kurzer Weg und anschließend ein Arbeitsvorgang mit Inventar-/XP-Beleg bleiben offen.
