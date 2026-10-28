# OSRS Play 1.8.29 – Echte Plugin-Bewegung im sicheren Minimap-Korridor verfolgen

## 1.8.29: Große API-Abtastschritte und seitliche Wegabweichungen

Ein neuer, 553 Zeilen langer Live-Test mit OSRS Play 1.8.28 vom
28.10.2026 (20:50–20:52 Uhr) bestätigt die automatische
Kalibrierung des weißen Minimap-Spielerpunkts bei **(645,111)**,
den Minimap-Rechtsklick zum Zoom-Reset und zahlreiche echte
Positionen nach `SendInput`-Klicks aus dem RuneLite-Plugin.
Ein Lauf bestätigte drei automatische Klicks und mindestens zwei
erreichte Wegpunkte; beim dritten Klick bestätigte das Plugin
`(3241,3197)`, dann `(3240,3198)` als kurze Abweichung.
Die neue `WAYPOINT_CORRIDOR_REPLAN`-Logik startete sofort einen
weiteren Klick und stoppte bei `(3239,3200)` erneut, obwohl die
Figur dem Ziel weiter näher kam. Andere Klicks wurden zu Unrecht
schon bei der **ersten** neuen Plugin-Kachel angehalten: bis zu
**vier Kacheln** zwischen zwei Positionsereignissen sind real.

1.8.29 verfolgt deshalb den laufenden Klick, solange alle folgenden
Kontrollen erfüllt bleiben:

- Eine **neue, zeitlich nach dem Klick** vom Plugin gelieferte
  Weltposition ist höchstens vier Kacheln von der letzten
  bestätigten Position entfernt.
- Jede seitlich vom exakten A*-Prefix abweichende Position liegt
  höchstens zwei Kacheln vom bevorstehenden Prefix und kommt dem
  angeklickten Wegpunkt **strikt näher**; Ebene und erlaubte
  Bewegungsachsen bleiben konsistent.
- Bis zu vier aufeinanderfolgende Korridorereignisse bestätigen
  Fortschritt, **ohne** einen zusätzlichen Klick zu senden. Erst
  der Wegpunkt bzw. maximal fünf solcher Ereignisse erlaubt die
  nächste *frisch berechnete* Kollisionsroute. Maximal zwei
  vorzeitige Neuplanungen je bewusster Sitzung.
- Ein Sprung über vier Kacheln, Rückschritt, entfernte Abweichung,
  veraltete Pluginmeldung, Focusverlust oder nicht erlaubtes
  Eingabefenster stoppt die Automatik weiterhin.

Wichtig für die Fehleranalyse: **Jeder gesondert gestartete und
fehlgeschlagene Autowalk-Versuch** kopiert nun die bis dahin
vollständige, fortgeschriebene Logdatei erneut, auch wenn
bereits ein früherer Versuch mit demselben Fehlercode gestoppt
hat. Unverändert wird eine dauerhaft wiederholte allgemeine
Fehlermeldung nur einmal kopiert, damit sie die Zwischenablage
nicht ständig überschreibt.

Die Diagnose protokolliert jetzt für jeden Klick auch alle
Kollision-Prefix-Kacheln sowie für echte Korridorschritte
`tile_jump`, `closest_prefix_distance`, Wegpunktabstand,
Zeitabstände und Zähler. Das Log wird bei Fehlern automatisch
kopiert und die GUI bleibt rot. **Ein vollständiger Durchlauf
bis Lumbridge ist weiter nicht live bestätigt.**

## Vorherige Version: 1.8.28 – Enger Korridor mit sofortiger Neuplanung

## 1.8.28: Echtes Gehen bestätigt, streng begrenzte Korridor-Neuplanung

Der Live-Test der Version 1.8.27 am 28.10.2026, 20:37 Uhr,
bestätigt erstmals die korrigierte automatische Minimap-Kalibrierung:
`AUTO_FIXED_LAYOUT_WHITE_DOT` bei **(645,111)**. Der Minimap-Zoom-
Reset wurde ausgeführt. RuneLite führte daraufhin in zwei Versuchen
mehrere Schritte auf dem geplanten Weg aus. Im zweiten Versuch
folgten auf den ersten bestätigten Wegpunkt bereits ein **zweiter
automatischer Klick** und weitere frische, passende Plugin-Kacheln.

Im ersten Versuch zeigte das Log noch eine kleine Abweichung des
RuneLite-internen Laufwegs von der statischen A*-Zwischenroute:
Nach dem bestätigten Kachelstand (3244,3192) kam (3243,3193),
also **eine benachbarte Kachel näher zum selben Wegpunkt**.
Die alte Prüfung beendete dies sofort als `UNEXPECTED_API_TILE`.

Version 1.8.28 akzeptiert nur diesen eng definierten Sonderfall:

- Zuerst müssen mindestens zwei tatsächlich durch das Plugin
  bestätigte Wegschritte auf dem zuvor berechneten Pfad vorliegen.
- Die Abweichung darf **höchstens eine benachbarte Kachel** sein,
  muss in Richtung des Wegpunkts führen und nahe an den nächsten
  vorgesehenen Kacheln liegen; Ebenenwechsel bleiben ausgeschlossen.
- Der alte Klick wird abgeschlossen. **Vor jedem weiteren Klick**
  wird vom aktuellen Plugin-Tile eine vollständig neue Kollisions-
  route berechnet. Maximal **zwei** solche Neuplanungen pro Test;
  jede weitere Abweichung bleibt ein harter Fehler.
- Neue Logs `WAYPOINT_CORRIDOR_REPLAN` erklären den exakten
  letzten bestätigten Punkt, die Abweichung, den Wegpunkt und
  das Neuplanungslimit.

Der zweite Test endete mit `RUNELITE_FOREGROUND_LOST`, nachdem die
Routenposition bereits mehrfach fortgeschritten war. **Das ist
eine gewollte Sicherheitsabschaltung**: Wenn ein anderes Programm
den Vordergrund übernimmt, werden keinerlei weiteren Spielklicks
gesendet. Bitte RuneLite für den gesamten Test im Vordergrund lassen.

Ein vollständiger Lauf zum Ziel ist weiterhin **nicht** verifiziert.
Die gesamte Ortsbestimmung stammt vom RuneLite-Plugin; der weiße
Minimap-Spielerpunkt dient ausschließlich der Klickgeometrie.

## Vorherige Version: 1.8.27 – Weißer Spielerpunkt bestätigt

## 1.8.27: Nutzerbild erneut vermessen und Kalibrierung abgesichert

**Korrektur eines Fehlers in 1.8.26:** Ich hatte die roten Linien im
Originalbild falsch gelesen. Sie schneiden sich **bei etwa x640/y110**
in der 768 Pixel breiten Originalaufnahme, **nicht bei x665/y137**.
Im neuen 773×534-RuneLite-Client liegt der kleine **weiße
Spielerpunkt etwa bei x645/y111**. Die falsche gelbe Markierung
in 1.8.26 lag etwa 20 Pixel zu weit rechts. Der von dir eingereichte
Live-Log bestätigt `MINIMAP_ANCHOR_CALIBRATED center_x=666` und einen
`UNEXPECTED_API_TILE`-Stopp nach dem ersten Eingabeklick.

Der Minimap-Mittelpunkt wird nun beim bekannten Layout aus der
Fensterbreite berechnet (**x = Breite − 128, y = 111**). Anders als
zuvor muss zusätzlich der **kleine weiße Spielerpunkt** innerhalb
weniger Pixel dieser Position in der aktuellen Aufnahme sichtbar sein.
Ohne diesen Nachweis darf die Automatik **nicht starten**. Auch bei
manueller Auswahl muss im bekannten Layout der weiße Punkt getroffen
werden und die Wahl darf höchstens 3 Pixel vom festen Mittelpunkt
abweichen. Kartenicons, das früher fehlgedeutete Ringsymbol und die
falschen Punkte x665–679 werden abgewiesen. Die Aufnahme dient
weiterhin ausschließlich der Klickgeometrie; die Weltkoordinaten
kommen nur aus dem RuneLite-Plugin.

Der automatische **Minimap-Reset auf 4 Pixel/Kachel** aus 1.8.26
bleibt bestehen; die 3D-Kamera bleibt unverändert. Der tatsächliche
Zoomwert kann mangels Plugin-Export noch nicht direkt gemessen werden.

**Spieltest:** Nach Update auf `v1.8.27` soll der gelbe Ring direkt
auf dem **kleinen weißen Punkt** in der Mitte der Minimap sitzen.
Kontrolliere vor der Einwilligung. Wenn er abweicht, nicht starten,
sondern den Screenshot und das neue Log senden. Live-Erfolg einer
kompletten Route ist noch nicht bestätigt.

## Vorherige Version: 1.8.26 – falsch vermessener fester Mittelpunkt

## Neu in 1.8.26: Richtiger Minimap-Mittelpunkt und Zoom-Reset

Der Test von 1.8.25 hat einen Fehler sichtbar gemacht: Der gelbe Kreis
stand auf einem runden **Kartenicon** bei (679,138) anstelle des
**stationären Spielerpunkts**. Auf dem vom Nutzer markierten echten
RuneLite-Bild liegt der Spielerpunkt bei (665,137) einschließlich
Fenster-Titelleiste, entsprechend (665,110) im Clientbild.

Bei der bestätigten Standard-Fenstergeometrie 773×534 verwendet OSRS
Play jetzt **diesen festen Minimap-Mittelpunkt**. Die Suche nach dunklen
Ringen wurde entfernt; bewegliche Map-Icons werden nicht mehr mit dem
Spieler verwechselt. Ein unpassend vergrößertes oder anders angeordnetes
Fenster braucht weiterhin eine überprüfte manuelle Kalibrierung. Im
bekannten Layout verhindert eine zusätzliche Plausibilitätsgrenze die
zuvor falsch gewählten Punkte (644,113) und (679,138).

OSRS Play stellt **nicht mehr den 3D-Kamera-Zoom** ein. Nach deiner
ausdrücklichen Zustimmung wird RuneLite in den Vordergrund geholt,
ein kontrollierter **Rechtsklick auf die Minimap** ausgeführt und ein
mögliches Kontextmenü mit Escape geschlossen. RuneLites Minimap-
Zoom-Plugin setzt beim Rechtsklick auf den **Standard von 4 Pixeln
pro Weltkachel** zurück. Ohne aktive Minimap-Zoom-Option gilt ohnehin
die normale Standard-Skalierung. Der installierte OSRS Toolkit Exporter
liefert jedoch noch keinen tatsächlichen Minimap-Zoomwert; das Log
unterscheidet ausdrücklich zwischen **gesendeter Reset-Eingabe** und
**nicht messbarer tatsächlicher Zoomstufe**. Die Spielposition in
Weltkacheln stammt weiterhin ausschließlich aus dem Plugin-Export.

**So testen:** Aktuelle `v1.8.26` installieren (oder Update abwarten),
RuneLite über den Jagex Launcher im Profil `auto` starten, einloggen,
Minimap auf Norden ausrichten, einen Schritt manuell gehen und im
OSRS Play „Automatisch gehen (Test)“ wählen. Prüfe den gelben Kreis
**genau auf dem Spielerpunkt in der Minimap** und bestätige die
Nordaustrichtung sowie die Vordergrund-Mausfreigabe. Im neuen Log
stehen `MINIMAP_ANCHOR_CALIBRATED`, `ZOOM_RESET_INPUT_SENT` und
anschließend nur bei echtem Plugin-Fortschritt die Wegpunktmeldungen.
Bei einem Fehler erscheint weiterhin die rote Verbesserungsmeldung
und das vollständige Protokoll wird in die Zwischenablage kopiert.

## Vorherige Version: 1.8.25 (fehlerhafte Ring- und 3D-Zoom-Erkennung)

## Historisch: 1.8.25 – echte Bewegung bestätigt, Kalibrierung nachträglich widerlegt

Das neue 1.8.24-Live-Log belegt erstmals tatsächliche
automatische Schritte im regulären RuneLite:
`FOREGROUND_INPUT_INSERTED` → `PROGRESS_CONFIRMED` →
`WAYPOINT_CONFIRMED`. Der Spieler bewegte sich auf dem
geprüften Weg, die RuneLite-API dokumentierte die
Kacheln. Der gesamte Weg bis zum Ziel ist damit noch
nicht verifiziert.

Zwei vorzeitige Sicherheitsstopps sind repariert:

- Ein unveränderter Plugin-Export wird nicht erneut
  als neue Wegbewegung ausgewertet. Wiederholtes
  Melden derselben Kachel führt nicht zu `OFF_PATH`.
- Wenn nach mehreren echten Wegschritten eine Kachel
  direkt neben dem angeklickten Wegpunkt erreicht wird,
  kann die Route einmalig von dort neu berechnet werden.
  Unbegründete Seitensprünge bleiben streng gesperrt.

Auf deinem markierten Screenshot sitzt der kleine
runde Spielerindikator etwa bei **x666/y137 im Fenster**
und ungefähr **x666/y111 im Clientbild**. Die bisher
manuell gewählten x643–645 lagen deutlich daneben.
1.8.25 **versuchte** dieses **15-Pixel-Ringmuster automatisch** zu finden;
der Folgetest widerlegte die Annahme, siehe 1.8.26 oben. Die damalige
Erkennung wurde entfernt. Historisch wurde das Ringmuster
auf einer frischen, nur im RAM angezeigten Aufnahme.
Der gelbe Kreis wird zur Kontrolle gezeigt. Erkennt
die Suche nichts Eindeutiges, kannst du weiterhin
manuell markieren. Aus Bildern werden niemals
Spiel-Weltkoordinaten berechnet.

Im gleichen Fenster wählst du den gewünschten
**3D-Kamera-Zoom**: „Weit heraus“ (Vorgabe), „Mittel“,
„Nah“ oder „Unverändert“. Das Programm stellt die
relative Kameraansicht mittels begrenzter, sichtbarer
Mausrad-Schritte im geprüften RuneLite-Vordergrund ein.
Der tatsächlich erreichte numerische Kamera-Zoom
bleibt ohne Export dieses Plugin-Werts **nicht messbar**.
Das ist von der **Minimap-Skalierung** getrennt, für
welche wir momentan RuneLites Standard **4 px/Tile**
verwenden. Minimap-Norden oben und Standard-Zoom
müssen weiterhin bestätigt werden.

**Historische, durch 1.8.26 ersetzte Testanweisung:** OSRS Play auf 1.8.25
aktualisieren, RuneLite im Profil `auto` geöffnet lassen,
einmal eine Kachel bewegen, Lumbridge auswählen,
„Automatisch gehen (Test)“ starten. Prüfe den automatisch
vorgeschlagenen gelben Spielerpunkt, wähle deinen
Kamera-Zoom, bestätige Minimap-Ausrichtung und Maus-
Einwilligung. Das neue Protokoll enthält
`MINIMAP_MARKER_CALIBRATED`, `ZOOM_INPUT_SENT`,
`FOREGROUND_INPUT_INSERTED`, bestätigte Wegschritte
und nötigenfalls automatische Clipboard-Fehlerlogs.

## Vorherige Version: 1.8.24

## Neu in 1.8.24: Vordergrund-Eingabe nur mit Einwilligung

Der 1.8.23-Live-Test hat drei automatische Klickversuche
protokolliert, aber **keinen nachweislich erfolgreichen
Wegpunkt**. Die ersten zwei wurden als Win32-
`PostMessage`-Nachrichten zugestellt; beim ersten kam
innerhalb von neun Sekunden kein Positionsereignis.
Beim zweiten meldete das Plugin zwar einen späteren
Schritt, aber **außerhalb** der berechneten Route.
Beim dritten endete die Protokollaufnahme bereits 3,3s
nach dem Klickversuch. Die RuneLite-Position selbst
ist weiterhin zuverlässig aus dem Plugin verfügbar.

1.8.24 benutzt für den ausdrücklich aktivierten
Bewegungstest die native Windows-Funktion `SendInput`,
die tatsächlich Mausereignisse in Windows einfügt.
Dies geschieht **nur im geprüften RuneLite-Vordergrund**,
mit separater Einwilligung in der Minimap-Kalibrierung.
Der erste Klick holt RuneLite bei erlaubter Windows-
Fokusaktivierung nach vorne. Danach gilt: Sobald ein
anderes Fenster aktiv oder das Minimapziel verdeckt ist,
**kein neuer Klick**. Auch Fenstergröße, Mausknöpfe,
Bildschirmauflösung und die geprüfte RuneLite-Fenster-
Identität werden vor jeder Eingabe berücksichtigt.

**Bitte so testen:**

1. RuneLite via Jagex Launcher im Profil `auto` öffnen,
   mit OSRS Toolkit Exporter einloggen und einen Schritt
   manuell gehen, damit ein API-Ereignis vorliegt.
2. In OSRS Play **Lumbridge** auswählen und
   **„Automatisch gehen (Test)“** drücken.
3. Im aktuellen Bild den weißen Spielerpunkt auf der
   Minimap auswählen; Norden oben/Zoom heraus sowie
   die **zusätzliche Mausfreigabe** ausdrücklich anhaken.
4. Kalibrierung bestätigen. RuneLite sollte jetzt im
   Vordergrund sein. Dort bleiben und Maus-/Tastatur-
   Bedienung während des Tests vermeiden.
5. Nach `RUNELITE_AUTOWALK FOREGROUND_INPUT_INSERTED`
   auf `PROGRESS_CONFIRMED` oder `WAYPOINT_CONFIRMED`
   warten. Ohne Bewegung stoppt der Client mit roter
   Meldung und kopiert das vollständige Sitzungslog.
   `FOREGROUND_CLICK_NOT_SENT reason=...` erklärt eine
   Sicherheitsblockade noch **vor** dem Spieleingriff.

Ein gesendetes Windows-Ereignis zählt weiterhin **nicht**
als erfolgreiche Spielfortbewegung. Weltpositionen stammen
ausschließlich vom offiziellen RuneLite-Plugin-Hub-Export,
niemals aus Bildschirmaufnahmen. Die neue Eingabe muss
erst durch deinen nächsten Live-Test bestätigt werden.

## Vorherige Version: 1.8.23

## Neu in 1.8.23: Kalibrierter Erstklick bei stationärem Charakter

Der reale 1.8.22-Live-Test am 28.10.2026, 19:07–19:09 Uhr
bestätigt die automatische Installation von 1.8.22,
den aktuellen Manifestcheck, direkte RuneLite-API-
Positionsereignisse und zwei erfolgreich bestätigte
Minimap-Kalibrierungen (Fenster 773×534, Mittelpunkt
645/112). Beide Kalibrierungen armierten die Automatik,
**aber kein Klick wurde gesendet**. Ursache: Beim
ersten Start war das letzte echte RuneLite-Positionsevent
12 beziehungsweise 33 Sekunden alt. Da das offizielle
Plugin keine stationären Heartbeats exportiert,
verhinderte der bisherige 3-Sekunden-Filter gerade
den ersten Versuch trotz bewusster Nutzerfreigabe.

1.8.23 gestattet nach einer **ausdrücklich bestätigten
aktuellen Minimap-Kalibrierung** exakt **einen einzigen
ersten Klick** vom letzten **in dieser laufenden
OSRS-Play-Sitzung bereits akzeptierten** API-Tile,
sofern das Ereignis höchstens 60 Sekunden alt ist und
der Klick binnen zwölf Sekunden nach der Bestätigung
erfolgt. Der Click bleibt auf den kleinen geprüften
Minimap-Radius begrenzt. Das ist eine einmalige
Start-Ausnahme, **kein fiktiver Plugin-Heartbeat**.
Danach gelten unverändert die strengen Regeln:
Jeder weitere Klick benötigt ein neues höchstens
drei Sekunden altes API-Ereignis, das die vorige
Route in vorwärts gerichteter Reihenfolge bestätigt.
Bleibt diese Bewegung aus, stoppt der Test nach
neun Sekunden mit rotem Fehlerhinweis und vollständiger
Log-Kopie in der Zwischenablage. Ohne vorher jemals
akzeptiertes API-Ereignis wird weiterhin überhaupt
nicht geklickt.

**Erneut im Spiel testen:** Alte OSRS Play EXE über
„Jetzt Updates prüfen“ auf 1.8.23 bringen. Im
eingeloggten RuneLite-Profil `auto` mindestens einmal
eine Kachel manuell gehen, danach Ziel Lumbridge wählen
und den Button „Automatisch gehen (Test)“ benutzen.
Minimap-Norden oben und Zoom heraus, weißen Punkt
markieren und zügig bestätigen. Nun sollte ein
`RUNELITE_AUTOWALK CLICK_INTENT` mit
`manually_calibrated_first_click=true` und danach
`WINDOW_MESSAGES_POSTED` erscheinen. Erst wenn RuneLite
wirklich läuft, folgt `PROGRESS_CONFIRMED` beziehungsweise
`WAYPOINT_CONFIRMED`. Ist keine neue Bewegung sichtbar,
bitte die kopierte Logdatei schicken. Falls der Erstklick
noch nicht freigegeben werden darf, protokolliert
`RUNELITE_AUTOWALK CLICK_DEFERRED` explizit das
Eventalter und die Blockierbedingungen. Die Annahme von
Fensternachrichten im offiziellen RuneLite ist **noch
nicht live bestätigt**.

## Vorherige Version: 1.8.22 – Erster optionaler Autowalk-Test

## Neu in 1.8.22: Automatisch zum ausgewählten Ziel (erster Bewegungstest)

Der Nutzer-Live-Lauf von 1.8.21 am 28.10.2026 um
18:40–18:41 Uhr bestätigte automatische Installation,
13 API-gemeldete Spielpositionen mit zwölf Änderungen,
14 erfolgreiche neue Lumbridge-Kollisionsrouten sowie
korrekte Vorwärts- und Rückwärts-Fortschritte von
72 auf 59 und zurück auf 68 Restschritte. Kein OCR,
kein neues Positionsproblem. Die einzige rote Meldung
war die zuvor gewollte nicht implementierte Automatik.

Ab 1.8.22 gibt es zwei neue GUI-Knöpfe:
**„Automatisch gehen (Test)“** und **„AUTOMATIK STOPP“**.
Der Standard bleibt AUS; OSRS Play klickt niemals beim
Start selbstständig ins Spiel. Vor dem ersten Klick
wird eine nur im Arbeitsspeicher gehaltene aktuelle
Fensteraufnahme für die **manuelle Minimap-Kalibrierung**
angezeigt. Der Nutzer muss im regulären RuneLite aus
dem Jagex Launcher eingeloggt sein, Minimap nach
Norden ausrichten, vollständig herauszoomen, den
weißen Spielerpunkt anklicken, die geschätzte
Pixel-pro-Kachel-Skalierung prüfen und ausdrücklich
bestätigen. Die Aufnahme dient **nur der Klickgeometrie**:
die Spielerposition stammt unverändert ausschließlich
aus dem offiziellen RuneLite-Plugin-Hub-Export.
Es werden keine Screenshots auf Datenträger geschrieben.

OSRS Play sucht auf der kollisionsgeprüften A*-Route
einen kurzen Wegpunkt innerhalb eines kleinen,
beschränkten Minimap-Radius. Ein Mausklick wird gezielt
als Windows-Fensternachricht an das validierte RuneLite-
Fenster geschickt. **Das Absenden allein zählt nie als
erfolgreicher Spielschritt.** Erst ein neueres, tatsächlich
auf dem zuvor geplanten Weg vorwärts gelegenes
Plugin-Positionsereignis bestätigt Fortschritt oder
den Wegpunkt. Vor dem nächsten Klick wird neu geplant.
Unverifizierte Transfers, Türen, Treppen und Ebenen-
wechsel sind im ersten Test ausdrücklich blockiert.

Weitere Schutzgrenzen: Nur frische API-Ereignisse,
höchstens 40 Klicks oder fünf Minuten je Lauf,
Positions-/Wegpunkt-Timeout neun Sekunden, manuelles
STOPP, Pause, Zielwechsel, Fensterwechsel, minimiertes
Fenster, fehlender Export oder abweichender Live-Pfad
stoppen weitere Eingaben. Blockaden erzeugen ein
ausführliches, automatisch in die Zwischenablage
kopiertes Fehlerlog und die rote GUI-Meldung.
**Ein bereits gesendeter Minimap-Klick kann noch eine
laufende Ingame-Bewegung auslösen; STOPP verhindert
weitere Klicks, beendet diese vorhandene Bewegung aber
nicht rückwirkend.**

### Live-Test

1. RuneLite-Profil `auto` durch den Jagex Launcher starten,
   einloggen und OSRS Toolkit Exporter aktiviert lassen.
2. In OSRS Play **Lumbridge** oder ein nahes Ziel wählen.
3. In RuneLite Minimap nach Norden drehen und vollständig
   herauszoomen. „Automatisch gehen (Test)“ drücken.
4. Im angezeigten aktuellen RuneLite-Bild den weißen
   Spielerpunkt der Minimap anklicken; ungefähr 4 Pixel
   je Kachel ist **nur ein Startwert**, kein live bewiesener
   Kalibrierwert. Kalibrierung bestätigen.
5. Bei Bedarf manuell eine Kachel gehen, damit der
   positionsereignisbasierte Export frisch ist.
6. Beobachten, ob sich der Charakter selbst auf einen
   Wegpunkt bewegt. `RUNELITE_AUTOWALK CLICK_INTENT`,
   `WINDOW_MESSAGES_POSTED`, `PROGRESS_CONFIRMED`
   und `WAYPOINT_CONFIRMED` sind getrennt geloggt.
   Falls der Klick nicht angenommen wird, folgt
   `POSITION_EVENT_TIMEOUT` samt rotem Fehlerhinweis
   und Clipboard-Log. **Diese Live-Bestätigung steht aus.**

## Vorherige Version: 1.8.21 – Routenverlauf und Diagonalen

## Neu in 1.8.21: Echte Live-Routen bestätigt, Bewegungsdiagnose präzisiert

Der zweite Nutzer-Live-Lauf von 1.8.20 am 28.10.2026,
18:20–18:21 Uhr Europe/Berlin bestätigt **zehn** erfolgreiche
Neuberechnungen einer kollisionsbasierten Lumbridge-Route
**während** der RuneLite-Charakter tatsächlich lief. Die
Routenlänge fiel von **92 auf 73** verbleibende Schritte.
Die Spielposition kam aus dem offiziellen API-Exporter,
ohne Screenshot-OCR und ohne neue Positionsfehler.
Die einzige rote Meldung `AUTOMATION_NOT_IMPLEMENTED`
bezeichnet weiterhin bewusst nicht vorhandene automatische
Spiel-Eingaben; der Client blieb rein lesend.

Kleine, aber wichtige Diagnosekorrektur: OSRS erlaubt
**acht benachbarte Felder**, auch diagonale. Ein Wechsel
`dx=1,dy=-1` wurde bisher wegen der Manhattan-Distanz
von zwei fälschlich als nicht benachbart protokolliert.
Ab 1.8.21 verwendet `non_adjacent_observation` die
**Chebyshev-Distanz** (größere absolute Achsänderung)
plus Ebenenwechsel. `manhattan_tiles` bleibt als eigene
Messgröße im Log, neu kommt `chebyshev_tiles` hinzu.
Der Zeitabstand zwischen echten RuneLite-Exportereignissen
heißt nun exakt `event_gap_ms` statt irreführend
`capture_gap_ms`. Regressionstests prüfen eine
einzelne Diagonale gegen einen echten Zwei-Tile-Sprung.

Die API-Ereignisquelle und die live bestätigte,
lesende Kollisionsnavigation aus 1.8.20 bleiben unverändert.

**Direkt im Spiel prüfbarer nächster Schritt:** Die GUI führt
jetzt ein eigenes, pro gewähltem Ziel zurücksetzbares
`Routenverlauf`-Protokoll. Beim ersten A*-Pfad entsteht ein
Startpunkt. Bei jedem weiteren **neuen API-Tile-Ereignis**
vergleicht der Client die gemeldete aktuelle Kachel mit
der **vorherigen vollständigen Route** und berechnet
die neue Reststrecke. Er zeigt und protokolliert:

- `ON_PREVIOUS_ROUTE`: Spieler gelangte auf ein Feld des
  zuvor berechneten Wegs;
- `OFF_PREVIOUS_ROUTE`: Spieler steht außerhalb des
  alten Wegs; eine neue Route wird berechnet;
- Restschritte-Veränderung (näher, weiter oder gleich);
- `ARRIVED` erst bei einem API-Stand innerhalb des
  eingestellten Zielradius mit leerer A*-Route;
- `LEFT_GOAL`, wenn der Charakter das erreichte
  Ziel wieder verlässt.

Wegen asynchroner Plugin-Ereignisse wird **nie** behauptet,
dass alle Kacheln zwischen zwei Stichproben durchlaufen
wurden. Beim Zielwechsel, Pause oder Quellenverlust
beginnt ein neuer unabhängiger Vergleich. Ein kontrollierter
Test: Ziel Lumbridge wählen, zunächst auf dem angezeigten
Weg laufen, dann kurz abweichen, Restschritte und
`RUNELITE_ROUTE PROGRESS` prüfen. Automatische Spiel-
Eingaben sind weiterhin nicht aktiviert.

## Vorherige Version: 1.8.20 – API-Ereignisse und Kollisionsroute

## Neu in 1.8.20: Keine Bildschirmprüfung mehr für RuneLite-Positionen

Die echte 1.8.19-Sitzung vom 28.10.2026, 17:56–17:58 Uhr,
zeigte **44** direkte API-Auswertungen, aber **89** zu alten
Positionsdateien, **91** Vierfach-OCR-Versuche und **46** nicht
lesbare Grid-Info-Positionen. Das bestätigte den Architekturfehler:
Der offizielle RuneLite-Plugin-Hub-Exporter schreibt `position.json`
bei **jedem Kachelwechsel**, nicht beim Stillstehen. Das Alter des
letzten Positionsereignisses ist kein Beweis für eine falsche Kachel.
Die temporäre Pixel-Fingerprint-Umgehung aus 1.8.19 war im Live-Test
keine zuverlässige Dauerlösung.

Die RuneLite-GUI und der Positions-Probe-Modus benutzen deshalb jetzt
`RuneLiteApiPositionSource` statt Bildschirm-Capture/OCR: exakt an den
Fenstertitel gebundene, prüfsummengeprüfte JSON-Koordinaten werden
unmittelbar als **API-Positionsereignis** verarbeitet. Ein neuer
Zeitstempel löst genau ein Bewegungsereignis aus; die wiederholte
Abfrage derselben Datei erfindet keine weiteren Bewegungen.
Nach drei Sekunden Stillstand heißt der Zustand sichtbar
`API_POSITION_LAST_KNOWN` und behält die zuletzt gemeldeten X/Y bei.
Der Client nennt dies **nicht** einen unabhängigen Live-Beweis:
Der unveränderte Drittanbieter-Exporter stellt noch keinen Heartbeat
für Logout-/Plugin-Ausfall-Erkennung bereit. Beim Programmstart muss
ein API-Ereignis höchstens zehn Sekunden alt sein; liegt nur eine
alte Datei aus einer möglichen früheren RuneLite-Sitzung vor, zeigt
OSRS Play `API_WAITING_FOR_NEW_EVENT`: **bitte im Spiel ein Feld
bewegen**. Falsche Zuordnung, ungültige Prüfsummen oder nicht
monotone Zeitstempel bleiben gesperrt. Keine Screenshots/OCR,
keine Manipulation des Jagex-Launchers.

**Nächste Entwicklung direkt im Spiel testen:** Wähle im RuneLite-
Begleitfenster ein F2P-Navigationsziel und gehe manuell ein paar
Kacheln. Zusätzlich zur Luftlinie lädt OSRS Play nur die an
verifizierte Commits gebundenen öffentlichen Kollisionsdaten
(keine Kartenbild-Downloads) und zeigt die Länge einer tatsächlich
berechneten A*-Route sowie die **nächste begehbare Kachel**.
Bei jeder echten Positionsänderung wird neu gerechnet, am Ziel wird
der Zielradius gemeldet. Es werden weiterhin **keine Spielaktionen
ausgeführt**; Türen/Treppen werden nur angezeigt. Nicht vorhandene
Kollisionen oder Netzwerkfehler bleiben sichtbar, ohne einen Weg
zu erfinden. Automatische Spielführung und ein direkter RuneLite-
Heartbeat erfordern weitere Entwicklung bzw. eine Aktualisierung
des separat installierten Plugin-Hub-Plugins.

## Vorherige Version: 1.8.19 – Pixel-Fallback (durch API-Ereignisse ersetzt)

## Neu in 1.8.19: Stationäre Position ohne unbekannte Ziffern zu erraten

Der nächste echte RuneLite-Live-Test vom 28.10.2026,
17:33:17–17:34:26 Uhr Europe/Berlin bestätigt die automatische
Installation von 1.8.18, 40 bestätigte Positionen in 86 Proben,
31 direkte API-Auswertungen, die neue F2P-Navigationszielauswahl
und acht von neun erfolgreich eingelesene Zusatzexportdateien
(eine Datei ist noch nicht vorhanden, kein ungültiger Export).
Der neue `WAITING_FOR_EXPORT_FRAME`-Zustand unterdrückte sechsmal
korrekt die OCR eines nachweislich vor dem neuen JSON-Export
entstandenen Bildes.

**Echte verbleibende Blockade:** Beim Stillstand auf 3176/3198/0
und später 3202/3193/0 liefert der Plugin Hub nach einigen Sekunden
keinen neuen Export, denn das Plugin schreibt nur bei Kachelwechseln.
Auch vier vollständig passende Chunk-/Region-/X/Y-Zahlenlesungen
können am ersten Ort fehlende unabhängige Pixelvorlagen für `6`
und `7` nicht ersetzen; später verhindert `32Ø2` sogar vollständige
X/Y-OCR-Stimmen. 21 Messungen wurden vorübergehend nicht freigegeben;
nach vier und zehn Sekunden wurden die geforderten roten Warnungen
und Clipboard-Logs ausgelöst. Die API und der Updater selbst liefen.

**Neue Lösung ohne Positionsraten:** Zwei echte, zeitlich verschiedene,
zuvor über den offiziellen RuneLite-API-Export bestätigte Aufnahmen
dürfen gemeinsam einen flüchtigen **Pixelabdruck des gesamten
World-Location-Zahlenrasters** autorisieren: acht X/Y-Ziffern,
die Ebene, sechs Chunk- und fünf Region-Ziffern sowie zwei
wirklich sichtbare Kommas. Nur ein späterer **exakter Treffer**
dieser Pixelmuster im aktuellen RuneLite-Fenster darf denselben
alten API-Ort erneut als Kandidaten liefern. Auch dann bleiben
zwei aktuelle, eigenständige Fensteraufnahmen Pflicht.

Ein anderes Feld **innerhalb desselben Chunks**, eine andere Ebene,
fehlende Kommas, andere Bildgröße/ein anderes Fenster oder ein neuer
API-Ort invalidieren den alten Abdruck. Es werden weder OCR-Zeichen
geschätzt noch Screenshots oder Fingerprint-Hashes gespeichert,
ausgegeben oder ins Log kopiert. Der Abdruck existiert höchstens
flüchtig im Arbeitsspeicher und läuft ohne echte neue Bildbeweise ab.
`stage=GRID_FINGERPRINT`, `TRUSTED_AFTER_TWO_CONFIRMED_FRAMES`,
`EXACT_CURRENT_PANEL_MATCH`, `CURRENT_PANEL_DIFFERENT` und
`NO_TRUSTED_REFERENCE` zeigen im Log die Beweiskette mit Zählern.

Die zusätzlichen lokalen Exportübersichten und die rein lesende
Navigation aus 1.8.18 bleiben bestehen. Gameplay-Automation und
echte begehbare Routen werden weiterhin nicht behauptet. Der
1.8.19-Windows-Test und ein neuer Nutzer-Live-Test stehen noch aus.

## Vorherige Version: 1.8.18 – Live-Positionswechsel, Lese-Navigation und lokale Exportdaten

## Neu in 1.8.18: Positionsnavigation und Zusatzdaten aus dem echten Client

Der echte Benutzer-Live-Test von **1.8.17** am 28.10.2026,
17:12:48–17:13:29 Uhr Europe/Berlin, belegt ein funktionierendes
automatisches Release-Update (`PREVIOUS_UPDATE`, `ALREADY_CURRENT`),
36 direkte RuneLite-API-Lesungen (`DIRECT_CLIENT_API`),
30 bestätigte Positionsproben von 52 sowie zehn tatsächlich beobachtete
Positionswechsel. Bei fünf alten Positionsexporten wurde zusätzlich
`POSITION_EXPORT_SCREEN_CROSSCHECK OCR_CORROBORATED` mit echten Pixeln
erfolgreich verwendet. Bei vier Proben kam `GRID_INFO_NOT_READABLE`
unmittelbar während eines Tilewechsels. Diese Fehler waren zeitlich
begrenzt und bedeuteten nicht, dass der Plugin-Hub-Exporter fehlte.

**Neuer Übergangsschutz:** Ist ein gültiger, höchstens drei Sekunden
alter `position.json`-Export neuer als die letzte Windows-Grafikaufnahme,
wartet der Client bewusst auf das nächste Bild (`WAITING_FOR_EXPORT_FRAME`)
und führt **keine** OCR-Prüfung auf dem veralteten Bild durch.
Der bewährte Schutz aus zwei zeitlich unabhängigen Aufnahmen und
der Vergleich der Zeitstempel bleiben unverändert. Es werden keine
vermuteten Zwischenpositionen erzeugt.

**Neue lesende Navigation:** Die RuneLite-GUI bietet eine Auswahl der
bereits im Projekt bekannten F2P-Orte. Erst für eine bestätigte
Spielerposition werden X/Y-Differenz und die maximale Koordinatenabweichung
angezeigt, ausdrücklich als **Luftlinie, nicht als begehbare Route**.
Die echten Hindernisse und Transportwege sind in diesem Companion
noch nicht mit dem Nutzerziel verbunden; es werden keine Klicks oder
Bewegungen an RuneLite gesendet.

**Optionale lokale Datenansicht:** Da der Benutzer im offiziellen
`OSRS Toolkit Exporter` alle Exporte aktiviert hat, liest OSRS Play nun
auch dessen `character.json`, `quests.json`, `diaries.json`,
`combat_achievements.json`, `inventory.json`, `equipment.json`,
`bank.json`, `ge_offers.json` und `game_quests.json`. Die Übersicht
zeigt nur kleine, anhand des tatsächlichen Pluginformats geprüfte
Zusammenfassungen (beispielsweise Gesamtlevel, Quest- und
Inventarzähler). Sie unterscheidet jüngere und ältere gespeicherte
Snapshots. Bankdaten sind **nicht automatisch ein aktuelles
Inventar**. Private Namen, Gegenstand-IDs, Mengen, Guthaben,
Angebotspreise und die JSON-Inhalte werden weder in das Log noch in
die Zwischenablage übernommen. Die Zuordnung erfolgt weiterhin nur
zum exakt passenden offiziellen RuneLite-Fenster. Kein Netzwerk-Upload,
kein fremder Account, keine Spielautomatisierung.

Windows-Selbsttests decken ungültige/fremde/alte Exportdateien,
Feld-Prüfsummen, das Warten auf neuere Aufnahmen, Ziel-Ebenen,
Navigation ohne Spieleingaben und Datenschutz ab. Die reale
1.8.18-Runde mit deinem laufenden RuneLite steht noch aus.

## Vorherige Version: 1.8.17 – Direkte RuneLite-Position

## Neu in 1.8.17: Präzise Koordinaten ohne modifizierten RuneLite-Client

Der echte 1.8.16-Live-Test vom 28.10.2026 um 16:27–16:28 Uhr
(Europe/Berlin) belegt die automatische Installation und **sieben**
erfolgreiche `SINGLE_CHUNK_CROSSCHECK_VERIFIED`-Ereignisse. Der
Pixelweg verifizierte acht ID-basierte Kandidaten; 13 Aufnahmen
wurden durch zwei frische Frames bestätigt. Danach fielen bei
Bewegungen Chunk-ID-Ziffern vollständig aus, und am Ort um
3155/3186 fehlte die Ziffer `6` in sämtlichen ID-Pixelvorlagen.
Trotz vier passender X/Y- und vier Region-OCR-Stimmen ging die
Position verloren. Weitere OCR-Ratereien sind keine verlässliche
Grundlage für eigenständige Navigation.

Es gibt bereits einen im **regulären RuneLite Plugin Hub** gelisteten,
unveränderten Positionsdatenexport:
[OSRS Toolkit Exporter](https://github.com/Gorsokk/position-exporter).
Er nutzt intern RuneLites `client.getLocalPlayer().getWorldLocation()`
und schreibt bei jedem erkannten Kachelwechsel lokal eine
`position.json`. Der Jagex Launcher und das RuneLite-Profil `auto`
bleiben unverändert. **Weder ein modifizierter Client noch ein eigener
Plugin-Loader wird installiert.**

**Einmalige Einrichtung in RuneLite:**

1. RuneLite wie bisher über den **Jagex Launcher** mit Profil `auto` öffnen.
2. Einstellungen → Plugin Hub → **OSRS Toolkit Exporter** installieren.
3. In dessen Einstellungen **Export position** einschalten.
4. Aus Datenschutzgründen nach Möglichkeit **Export GE offers**,
   **Export character progress**, **Export inventory and equipment**,
   **Export bank** und **Export game quest data** ausschalten.
5. Wie bisher das World-Location-Overlay mit **Grid Info** sichtbar lassen.
   Bewege die Spielfigur einmal, falls der Export noch keine Datei erzeugt hat.

OSRS Play liest **ausschließlich** die positionsbezogene Datei
`position.json` unter dem vom Plugin angelegten RuneLite-Datenordner.
Sie enthält zwar intern auch den Charakternamen, dieser wird aber
weder angezeigt noch in Logs übernommen. Die Dateizuordnung muss zum
**vollständigen Namen im Titel genau eines offiziellen RuneLite-Fensters**
passen. Mehrdeutige Dateien, falsche Prüfsummen, ungültige Ebenen,
falsche Accountzuordnung, beschädigte JSON-Daten und zukünftige
Zeitstempel werden ohne Positionsfreigabe verworfen.

Bei einer höchstens drei Sekunden alten Position nutzt OSRS Play
direkt die vom RuneLite-Client berechnete Position, verlangt aber
weiterhin **zwei frische, unabhängige Fensteraufnahmen**. Das Plugin
schreibt im Stillstand nicht periodisch neu: Ist die Datei älter,
verlangt OSRS Play deshalb mindestens drei zum Export passende
X/Y- und Region-Zahlenlesungen aus dem **jetzigen Bildschirm** sowie
eine eindeutige echte Pixelprüfung aller Feldraster, Kommas und der
Ebene. Eine bislang nicht in den IDs enthaltene einzelne Ziffer darf
**nur der ohnehin von RuneLite direkt bezeichnete Ort** kennzeichnen,
wenn der neue Pixelglyph auch tatsächlich sichtbar und eindeutig
verschieden ist. Danach kann dieser Glyph aus zwei bestätigten
Aufnahmen in die bisherige kurzlebige Glyphhistorie gelangen.
Bei fehlendem Plugin bleibt die bestehende vorsichtige OCR-Prüfung
aktiv; es werden keine Positionen erfunden.

Diagnosen: `POSITION_EXPORT`, `DIRECT_CLIENT_API`,
`STALE_REQUIRES_LIVE_SCREEN_CROSSCHECK`,
`POSITION_EXPORT_SCREEN_CROSSCHECK`,
`CLIENT_EXPORT_PLUS_SCREEN_PIXELS`, `client_export_labelled_glyphs`.
Die RuneLite-Oberfläche und gespeicherte Screenshots werden nicht
verändert; private Profil-/Charakterdateien werden nicht eingecheckt.
Das ist bis zum nächsten Benutzer-Live-Test nur synthetisch erprobt.
Die automatische RuneLite-Spielsteuerung bleibt unimplementiert.

## Bisherige Versionen

**Neu in 1.8.16 – echte OCR-Lücke aus dem 1.8.15-Live-Test geschlossen:**
Das echte Live-Protokoll vom 27.10.2026, 23:11–23:12 Uhr bestätigt
`version=1.8.15`, `PREVIOUS_UPDATE`, `ALREADY_CURRENT`,
automatisch kopierte Fehlerlogs und neue bestätigte Bewegungen.
In den Proben 7, 10, 11, 26, 28, 35, 37, 38 und 44 erscheint
die **Chunk-ID nur in einem OCR-Durchlauf vollständig**, aber zwei
bis vier OCR-Varianten stimmen beim X/Y-Paar überein. Der für genau
eine X/Y-Lesung eingerichtete Sonderweg aus 1.8.15 lehnte diese
zusätzlichen unabhängigen Belege bislang ab (`attempted=false`).

1.8.16 akzeptiert in diesem **streng begrenzten Sonderweg** auch
mehrere numerische X/Y-Lesungen – nur wenn **alle identisch** sind,
mindestens eine aus einem **anderen OCR-Pass als die einzige
Chunk-ID** stammt und **mindestens drei** identische Region-IDs
vorliegen. Die Chunk-/Region-Prüfsummen, höchstens 64 rechnerisch
passende Felder, aus realen Pixeln benannte Ziffern, sichtbare
Kommas, tatsächliche Ebene, eine eindeutige Position und zwei
zeitlich getrennte Aufnahmen bleiben unverändert Pflicht.
`xy_distinct_values`, `cross_pass_xy_votes` und `id_xy_votes`
zeigen im Diagnoseprotokoll, welche Quellen den Kandidaten tragen.
Widersprechende OCR-Paare, fehlende Pixel oder unbekannte
Einzelglyphen sperren weiter.

Unabhängige 1.8.15-Live-Aufnahmen bestätigten weiterhin mehrere
Positionen, aber ein zeitweiser `GRID_INFO_NOT_READABLE`-Fehler
blieb auch bei zwei regulären Chunk-Stimmen bestehen, weil die
Pixelprüfung eine unbekannte, nur einmal vorkommende Tile-Ziffer
nicht benennen konnte. Das wird **nicht geraten**. Die neue
1.8.16-Regel ist synthetisch geprüft; ein Live-Test steht noch aus.
Automatische RuneLite-Spielaktionen sind weiterhin nicht vorhanden.

## Vorherige Verbesserungen

**Neu in 1.8.15 – Chunk-ID nur einmal lesbar, XY und Region getrennt:**
Der echte 1.8.14-Live-Test bestätigt erneut das automatische Update,
die automatische Logdatei-Kopie und die neue Bewegungschronik:
`RUNELITE_MOTION OBSERVATION` enthält Richtung, Zeit- und
Probenabstände, einschließlich der ausdrücklich unbekannten
Zwischenroute. Der Client konnte zahlreiche manuelle Bewegungen
nachvollziehen. Bei Probe 69–75 um **3163/3200/0** wird aber nur
die Chunk-ID `809360` in einer OCR-Variante vollständig gelesen;
eine andere Variante erkennt das X/Y-Paar und drei weitere
OCR-Aufbereitungen bestätigen die Region `12594`. Der bisherige
ID-/Pixel-Ausweichweg verlangt mindestens zwei numerische
Chunk-ID-Stimmen und startet deshalb keine Pixelprüfung.

1.8.15 erlaubt **ausschließlich** in diesem Muster einen weiteren
schwach gewichteten, separat protokollierten Kandidaten: Genau
eine vollständig gelesene Chunk-ID muss von einem **anderen**
OCR-Pass mit genau einem vollständigen X/Y-Zahlenpaar sowie
mindestens **drei** gleichen Region-ID-Stimmen gestützt werden.
Alle Zahlen müssen rechnerisch zusammenpassen. Danach bleiben
höchstens 64 Chunk-Felder für die strikte Rasterprüfung; jede
der acht X/Y-Ziffern braucht eine tatsächlich vorhandene,
unabhängig benannte Pixelvorlage. Echte Kommas, Ebene, eine
eindeutige Pixelposition, exakter Vergleich mit dem einzelnen
X/Y-Paar und eine zweite frische Aufnahme bleiben Pflicht.
Der Logeintrag `SINGLE_CHUNK_CROSSCHECK_VERIFIED` mit
`single_chunk_assisted=true` unterscheidet diesen Pfad von
echten doppelten Chunk-ID-Stimmen. Ein falscher Einzelwert,
fehlende Region-Mehrheit, zwei widersprechende Chunk-Werte,
fehlende Pixel oder nur ein Bild bleiben gesperrt.
Diese Erweiterung ist zunächst nur synthetisch getestet; eine
echte 1.8.15-Live-Bestätigung steht aus. Spielaktionen bleiben aus.

**Neu in 1.8.14 – Bewegungschronik als Vorbereitung für sichere Navigation:**
Das echte 1.8.13-Live-Protokoll bestätigt automatisches Update,
`clipboard=COPIED` und viele unabhängig bestätigte Positionen.
Insbesondere Probe 42 zeigt bei exakt einer gültigen X/Y-OCR-Lesung
`ID_ONLY_QUORUM IDS_VERIFIED`, `single_xy_matches_pixels=true`,
`PIXEL_GRID_VERIFIED` und `OVERLAY_CONFIRMED` für **3142/3202/0**.
Die bis dahin offene Einzel-OCR-Fehlerklasse ist damit **live gelöst**.

Der Companion protokolliert ab 1.8.14 bei tatsächlich bestätigter
Position `RUNELITE_MOTION OBSERVATION`: Startkoordinate sowie bei
jeder Änderung vorheriges und neues Tile, `dx`, `dy`, Ebenendifferenz,
Manhattan-Distanz, Abstand der bestätigten Aufnahmezeitpunkte und
fehlende Proben. Zwischenpositionen werden **nicht** rekonstruiert;
`non_adjacent_observation=true` bedeutet ausdrücklich nur, dass die
beiden beobachteten Positionen nicht benachbart sind. Ein separater
Gesundheitszähler erfasst bestätigte, stabile und geänderte Positionen.
Pausen und Verlust der RuneLite-Quelle löschen die Bewegungschronik;
unbestätigte OCR-Zahlen dürfen sie nicht verändern. Der Pixel-Logwert
`single_xy_matches_pixels` ist jetzt `not_applicable` für gewöhnliche
OCR-Quoren statt irreführend `true`; beim Einzel-OCR-Ausweichweg
wird auch `ocr_agreements=1` korrekt ausgewiesen. Windows-Selbsttests
prüfen Bewegung, Sprünge, Ebenenwechsel, veraltete Zeitstempel und
Reset. Keine Spielaktion oder automatische Wegplanung wird gestartet;
ein echter 1.8.14-Live-Test steht aus.

**Neu in 1.8.13 – ein OCR-Zahlenpaar soll Pixelbeweise nicht blockieren:**
Der echte 1.8.12-Live-Test bestätigt die automatische Installation
und den neuen Ausweichweg: `ID_ONLY_PIXEL_ENUMERATION` erkannte die
Position **3150/3193/0** trotz `Tile315Ø` in sämtlichen Varianten,
mit `full_matches=4`, `distinct_positions=1` und wiederholter
`OVERLAY_CONFIRMED`-Zweibildbestätigung. Auch Bewegung nach
3157/3199, 3153/3200 und 3148/3206 wurde bestätigt.
Bei Probe 12 wurde dagegen eine einzige gültige X/Y-Zeile gelesen,
die drei anderen waren beschädigt. Weil weder das normale
Zweierstimmen-Quorum noch die bislang nur bei **null** numerischen
Paaren zulässige ID-Ausweichroute griff, unterblieb die Pixelprüfung.
Die streng begrenzte Ergänzung erlaubt maximal **eine** vollständige
X/Y-OCR-Lesung, aber nur wenn sie zu den jeweils mindestens zweifach
bestätigten Chunk-/Region-IDs passt. Alle 64 Felder werden weiterhin
aus echten, eindeutig benannten Ziffernglyphen pixelweise geprüft;
die **einzige** Pixelposition muss zusätzlich exakt mit dem einzelnen
OCR-Paar übereinstimmen. Zwei Zahlenpaare benutzen das normale
Feldquorum; widersprechende IDs, Chunk-Checksummen oder Pixel lassen
keine Position zu. Der neue Logwert `single_xy_matches_pixels`
erläutert die Entscheidung. Synthetische Positiv-/Negativtests
prüfen diese Erweiterung; eine echte 1.8.13-Live-Messung steht aus.
Automatische RuneLite-Spielaktionen sind weiterhin nicht implementiert.

**Neu in 1.8.12 – beschädigte X/Y-Ziffern nicht blind ersetzen:**
Das echte 1.8.11-Live-Log bestätigt die Tooltip-Suffix-Korrektur:
Mit `region_ignored_suffixes=3` gelang in mehreren aufeinanderfolgenden
Bildern die Pixel- und Zweibildfreigabe für Position 3145/3196/0.
Manche andere Positionen bleiben aber blockiert, weil alle OCR-
Aufbereitungen z. B. `315Ø` statt vier numerischer X-Ziffern lesen.
Wenn **keine** OCR-Aufbereitung ein vollständiges numerisches X/Y-Paar
liefert, jedoch Chunk und Region jeweils zweimal identisch erkannt
werden, kann der Leser den Suchraum nun rein mathematisch auf die
64 Felder eines Chunks beschränken. Er muss danach für **jede einzelne
X/Y-Ziffer** einen zugewiesenen realen Pixel-Glyphen aus dem aktuellen
ID-Bild oder mindestens zwei früher bestätigten Aufnahmen nachweisen.
Weder wiederholte unbenannte Zeichen noch spekulative `Ø`→`0`-
Ersetzungen werden freigegeben. Eindeutige Pixelposition, zwei echte
Kommas, visuell belegte Ebene, Chunk-/Regionsprüfsummen und eine
zweite unabhängige frische Aufnahme bleiben Pflicht. Neue
`stage=ID_ONLY_QUORUM`-/`ID_ONLY_PIXEL_ENUMERATION`-Diagnose und
synthetische Negativtests begleiteten diesen inzwischen **live
bestätigten** Ausweichweg. Gameplay-Eingaben bleiben aus.

**Neu in 1.8.11 – HUD-/Tooltiptext neben Chunk- und Region-IDs:**
Der 1.8.10-Live-Test bestätigt die automatische Installation und die
**vollständigen** Pixelprotokolle einschließlich Ergebnisfeldern.
Neue OCR-Proben zeigen aber, dass Tooltiptexte wie `Pot (GE: 12 gp)`
manchmal in dieselbe Zeile wie `Region ID 12593` oder `Chunk ID 807311`
geraten. Der Feldquorum-Parser akzeptiert nun die feste, exakt sechs-
bzw. fünfstellige ID auch mit **durch Leerraum getrenntem Suffix**;
aneinanderhängende weitere Ziffern, widersprüchliche ID-Quoren,
falsche Chunk-/Region-Checksummen und nicht bestätigte Pixel werden
weiterhin abgelehnt. Separate Diagnosezähler
`chunk_ignored_suffixes`/`region_ignored_suffixes` dokumentieren
jede solche Toleranz. Ein neuer synthetischer Test reproduziert die
echte Probe 3152/3197 und prüft widersprüchliche Präfixe.
Automatische RuneLite-Spielaktionen bleiben gesperrt.

**Neu in 1.8.10 – vollständige statt abgeschnittene Diagnosezeilen:**
Der echte 1.8.9-Live-Test hat die automatische Installation bestätigt und
auf dem bewegten RuneLite-Charakter viele unabhängige Positionen gelesen,
darunter 3147/3208, 3157/3198 und 3148/3199. Der neue, auf zuvor
bestätigten Bildern beruhende Glyphenspeicher wurde wiederholt benutzt.
Die eigentliche vollständige Datei-/Zwischenablagekopie funktionierte,
aber ein allgemeines **480-Zeichen-Limit pro Logereignis** schnitt in 58
Pixelzeilen gerade `full_matches`, `distinct_positions`, `elapsed_ms`
und das Ergebnisfeld ab. 1.8.10 behält diese Felder jetzt vollständig;
pro Ereignis gelten bis zu 16.384 bereinigte Zeichen. Ein noch längerer
Eintrag markiert den absichtlich ausgelassenen Rest samt Zeichenzahl.
Die Redigierung von Zugangsdaten bleibt erhalten. Der Windows-Selbsttest
prüft die ausführliche Logdatei und identische Zwischenablagekopie.
RuneLite-Spielaktionen sind weiterhin nicht implementiert.

**Neu in 1.8.9 – bestätigte Pixelziffern sitzungsintern wiederverwenden:**
Ziffern aus mindestens zwei früheren, erfolgreich bestätigten RuneLite-
Bildaufnahmen können eine heute nur einmal sichtbare Ziffer belegen, wenn
mindestens fünf gegenwärtige ID-Ziffern mit dem historischen Schriftbild
übereinstimmen. Alter, Fensterbindung, Rasterabstände und Widersprüche
bleiben konservativ geprüft. Das Live-Log von 1.8.9 bestätigt erstmals
mehrere Bewegungspositionen mit `historical_glyph_matches>0` und
`OVERLAY_CONFIRMED`; ein Kaltstart bei einem Ort ohne passende Vorlagen
bleibt erwartungsgemäß noch gesperrt.

Die folgenden Versionsabschnitte dokumentieren jeweils ihren damaligen Teststand.

**Neu in 1.8.8 – unabhängig bestätigte OCR-Zahlenfelder:** Das echte
1.8.7-Live-Log zeigt, dass bei der Bewegung die Region `12594` von
manchen Bildaufbereitungen als `12534` gelesen wird, während andere
Varianten Chunk und Tile-Zahlen unvollständig lesen. 1.8.8 bestätigt
deshalb **jedes vollständige Zahlenfeld getrennt** in mindestens zwei
OCR-Aufbereitungen: X/Y als zusammengehöriges Paar, Chunk-ID und Region-ID.
Die Ergebnisse dürfen nur dann gemeinsam in die Pixelprüfung einfließen,
wenn jedes Feld eine **eindeutige** Stimmenmehrheit hat und beide IDs
mathematisch zu X/Y passen. Die eigentlichen Kommas, Zeichenformen und
Ebenenziffer müssen weiterhin im selben Bild pixelbasiert bestätigt
werden; widersprüchliche gültige Positionen bleiben gesperrt. Separate
`FIELD_QUORUM`-Logeinträge zählen Feldstimmen, Ausreißer und Blockgründe.
Ein erfolgreicher echter 1.8.8-Live-Test steht noch aus. **Keine
automatische RuneLite-Spielaktion wird dadurch freigeschaltet.**

**Neu in 1.8.7 – Bewegungsziffern und unabhängiger Updatekanal:**
Das erste 1.8.6-Bewegungslog zeigte gute RuneLite-Frames mit X=3144/Y=3211,
aber kein gültiges Pixelraster: Die neuen IDs `805265`/`12594` enthalten
keine Ziffer `3`, obwohl sie in der Tile-Zeile zweimal auftritt. Der Leser
prüft jetzt solche fehlenden X/Y-Ziffern nur, wenn zwei Vorkommen derselben
Ziffer **im selben Bild pixelgleich** und von sämtlichen ID-Vorlagen
eindeutig verschieden sind. Einmalig sichtbare unbekannte Ziffern,
unklare Kommas, OCR-Widersprüche und eine nicht belegte Ebene bleiben
strikt gesperrt. Das Log zählt entsprechende Prüfungen und Ablehnungen.
Zusätzlich vergleicht der Updater das Raw-Manifest mit dem **offiziellen
GitHub-Release-API-Endpunkt** einschließlich veröffentlichter Asset-SHA-256.
Ist das Release neuer, wird ausschließlich dessen geprüfter Versions-/
Hashdatensatz verwendet; bei widersprüchlichen Hashes wird abgebrochen.
Fällt die API aus, bleibt das vorhandene, geprüfte Manifest als Fallback.
**Ein Live-Nachweis dieser neuen Version fehlt noch; keine Game-Automatik.**

**Neu in 1.8.6 – zuverlässigerer Update-Manifestabruf:** Das erste
1.8.5-Live-Protokoll bestätigt die Pixelposition über zwei unabhängige
RuneLite-Aufnahmen und durchgehend weitere `OVERLAY_CONFIRMED`-Proben.
Die Update-Abfrage erhielt unmittelbar nach dem Release jedoch noch
`remote_version=1.8.4` bei lokal installiertem 1.8.5, obwohl das GitHub-
Manifest schon 1.8.5 enthielt. Um zwischengespeicherte alte Antworten
zu vermeiden, holt der Updater die öffentliche Manifestdatei mit einer
einmaligen URL-Kennung sowie `Cache-Control`- und `Pragma`-Headern. Eine
ältere Remote-Version wird separat als `MANIFEST_BEHIND_LOCAL` geloggt und
niemals installiert. Der Vergleich behandelt `.0`-Assemblyrevisionen
und dreiteilige Manifestversionen korrekt als gleich. Offline-Tests prüfen
zwei unterschiedliche URLs und die Cache-Header. **RuneLite-Spielaktionen
bleiben nicht implementiert.**

**Neu in 1.8.5 – Positionsfenster statt Ausschnittsursprung:** Die Raster-
Ziffernprüfung suchte zuvor nur in den ersten 240×120 Bildpunkten und
ließ darin mögliche Panel-Anker bis x=80/y=55 zu. Das echte 1.8.4-Log
lieferte zwar übereinstimmende Chunk-/Region-/X/Y-OCR-Zahlen, aber in
4.536 Versuchen **kein einziges Kommapaar**. Das zweite vollständige
Fensterbild bestätigte einen tatsächlichen Panel-Versatz von +14/+53
Pixel gegenüber dem zugeschnittenen Ausschnitt (dessen binäre Pixel
exakt übereinstimmen). Deshalb sucht 1.8.5 nun im vollständigen 350×220-
OCR-Ausschnitt. Zusätzliche Logwerte zeigen die Helligkeitspixel,
Kommakandidaten, Abstands-/Leerstellenprüfung, Treffer und Dauer, um
Layoutversatz von einer Schrift-/Helligkeitsabweichung zu unterscheiden.
Die Lesefreigabe-Regeln bleiben unverändert konservativ; ein echter
WGC-Live-Test mit Profil `auto` steht weiter aus.

**Neu in 1.8.4 – bildgestützte Prüfung der World-Location-Ziffern:** Ein
kleiner zusätzlicher Rasterleser nutzt das feste 7×11-Pixel-Schriftbild der
lokal eingerichteten `auto`-Anzeige. Er lernt Ziffernformen ausschließlich
aus den im selben Bild vorhandenen, mindestens zweimal per OCR identisch
erkannten Chunk-/Region-IDs und gleicht anschließend die Tile-Zeile ab.
Zwei tatsächlich sichtbare passende Kommas und leere Trennfelder sind
Pflicht; es gibt keinerlei blindes `o`→`0` oder `»`→`,`.
Unbekannte Ziffern, abweichende Schrift oder widersprüchliche OCR-
Ergebnisse werden verworfen. Ein zweites frisches Fensterbild ist weiterhin
notwendig; daraus entsteht **noch keine RuneLite-Spielautomatik**. Die erste
Live-Bestätigung dieser neuen Lesemethode steht aus.

**Neu in 1.8.3 – Auto-Update des RuneLite-Companions:** Der Standardclient
prüft beim Start, alle 30 Minuten und auf Knopfdruck das Release-Manifest
(SHA-256-Dateiintegrität) und installiert neue verifizierte EXE-Releases.
Nur OSRS Play wird dafür beendet und neu gestartet; RuneLite bleibt offen.
Jeder Update-Schritt und jede Fehlermeldung landet im Sitzungslog; ein
Updatefehler löst die automatische Log-Kopie und rote Verbesserungsmeldung aus.
**Wichtig:** Ältere RuneLite-Standardclients (einschließlich 1.8.2) haben den
Update-Check noch nicht und benötigen einmalig die manuelle 1.8.3-Installation.
Die SHA-256-Prüfung schützt gegen Übertragungsfehler, ist keine unabhängige
kryptografische Signatur des Herausgebers.

**Neu in 1.8.2 – ausführliche Fehlerprotokolle:** Jeder Start erzeugt ein
fortlaufendes lokales Sitzungslog mit Buildkennung, Zeitstempeln, OCR-/Capture-
Details und Sperrgründen. Bei einem Fehler oder einer nicht ausgeführten Aktion
wird die vollständige Logdatei automatisch in die Zwischenablage kopiert; die
GUI meldet in Rot **„VERBESSERUNG MUSS EINGEBAUT WERDEN“**. Der manuelle
Kopierknopf bleibt verfügbar. [Verhalten, Logpfad, Datenschutz und Tests](docs/LOGGING-1.8.2.md).

**RuneLite-Umstellung in Arbeit:** Der Standardstart ist ein nur lesender Companion für reguläres RuneLite aus dem offiziellen Jagex Launcher. Vorgesehen ist das Spielerkoordinaten-Overlay des Hub-Plugins World Location; unser Programm liest dessen Zahlen mittels OCR und prüft Chunk/Region sowie zwei frische Aufnahmen. Offline-Fixtures bestehen mit ausdrücklich dokumentierter Ebene-0-Grenze. Die echte Positionsprobe bleibt wegen falsch gelesener Ziffern/Interpunktion blockiert. Der separate Entwicklungsclient wurde auf Benutzerwunsch verworfen. Automatische RuneLite-Aufgaben sind noch nicht implementiert. Die alte C++-Client-Automatik bleibt ausdrücklich mit `--legacy-official` erreichbar. [Übergabe für die nächste KI](docs/HANDOFF-RUNELITE-2026-10-27.md), [aktueller Prüfstand](docs/RUNELITE-MIGRATION-2026-10-27.md), [Plugin-Prüfung mit vollständigen Katalogen](docs/RUNELITE-PLUGIN-PRUEFUNG-2026-10-27.md).

**Historischer Prüfstand des bisherigen visuellen Backends:**

**Neu in 1.7.18:** Der vollständige 1.7.17-Live-Test vom 27.10. um 00:13 bestätigte die Weltkartenposition `(3141,3212)` erneut unabhängig in zwei Aufnahmen mit 14/20 und 14/19 Inliern, die Nordausrichtung und den maximalen Minimap-Zoom-out. Zwei mindestens 550 ms getrennte Terrain-Atlasabgleiche fanden **denselben** unverifizierten Kandidaten auf Ebene 0, jeweils nur **ein Feld** von der bestätigten Weltkarte entfernt; `best_score=0.436` vs. `runner_up=0.243` und `0.432` vs. `0.238`. Die alte SIFT-Minimap-Erkennung bleibt trotz dieses vielversprechenden Abgleichs ohne genügend Ratio-Paare. Die nächste Frage ist, ob die Kandidatengeometrie auch *Bildpixel zu hypothetischen Minimap-Klickpositionen* korrekt transformiert. 1.7.18 berechnet deshalb aus dem exakten Fundpunkt des Atlas-Template-Matchs, der jeweiligen Referenzskalierung und den **nativen** Minimap-Bounds die erwarteten Positionen für acht Felder nach Osten, Westen, Norden und Süden. Die passive Diagnose `TERRAIN_PROJECTION_PREFLIGHT` vergleicht beide frischen Aufnahmen, den Weltkartenhinweis, die Score-Differenz, die Nordausrichtung, die sichere Minimap-Randzone und die Bildstabilität. Sie protokolliert nur relative Pixelabstände und eine Fehlerkategorie, **keine Bewegung und keine Freigabe**. Der separate Navigationsverifier, die Weltkarten-/Kompassprüfung und sämtliche F8-/Input-Sperren bleiben unverändert. Synthetische Regressionen prüfen Zoom-out, 150px/142px-Minis, Richtungsachsen sowie fehlende, widersprüchliche und zeitlich abhängige Belege. Ein weiterer echter Live-Test muss zuerst den Projektionsbefund liefern, bevor ein einzelner kontrollierter Wegschritt überhaupt erwogen wird.

**Neu in 1.7.17:** Der 1.7.16-Live-Test vom 26.10. um 23:55 bestätigte die Weltkarte auf zwei frischen Bildern wieder eindeutig bei `(3141,3212)` (19/28 und 19/29 SIFT-Inlier), schloss sie korrekt und kalibrierte die Minimap auf maximalen Zoom-out. Die alte absolute SIFT-Minimap-Erkennung blieb bei nur 1–2 Ratio-Paaren gesperrt. Erstmals lieferte die separate maskierte Terrain-Probe einen starken globalen Bildtreffer (`best_score=0.550`, `runner_up=0.362`, Skalierung `1.95`) über vier Kartenebenen. Es fehlten jedoch der **Abstand dieses unsicheren Kandidaten zur unabhängig bestätigten Weltkartenposition** und eine erneute Messung. 1.7.17 protokolliert nun nur diese relative X/Y-Abweichung (`surface_hint_xy_delta_tiles`) samt ausdrücklich *unverifiziertem* Kandidaten-Floor und wertet ein zweites, mindestens 550 ms später aufgenommenes Bild mit eigenem globalem Atlasabgleich aus (`TERRAIN_PROBE_REPEAT`, `candidate_shift_xy_tiles`, `same_unverified_plane`). Alle Auswertungen bleiben rein passiv und beeinflussen weder den SIFT-Positionserkenner noch Klickrechte, Laufwege oder Spielspeicherzugriff. Ein gleicher Kandidat auf zwei Screenshots ist weiterhin kein hinreichender Beweis für eine sichere Bewegungssteuerung.

**Neu in 1.7.16:** In 1.7.15 wurden Welt W399, InGame, alle 24 Skillwerte und die Nordausrichtung des Kompasses vollständig bestätigt. Die große Weltkarte ließ sich öffnen und schließen, aber alle vier Bildproben unterschritten mit `r65:27/16` beziehungsweise `r65:28/17` knapp die unverändert erforderlichen 65 % geometrischen SIFT-Konsens. Deshalb blieb die unbekannte Weltposition zu Recht gesperrt und die neue Minimap-Terrain-Probe wurde noch nicht erreicht. Als weitere reine Bilddiagnose bewertet 1.7.16 bei einem solchen *abgelehnten* Karten-Fit nun zusätzlich die räumliche Verteilung, die Nordausrichtung, die Konturen und die maskierte Grauwertekorrelation der sichtbaren Landschaft zur öffentlichen Atlasreferenz (`r65_near_miss`, `r60_near_miss`, ...). Die Werte werden nur im bestehenden `WORLD_MAP_SAMPLE`-Log protokolliert; unsichere Positionskoordinaten werden nicht freigegeben. **Sämtliche SIFT-, Zweibild- und Eingabe-Sicherheitsgrenzen bleiben unverändert**; es gibt weder einen zusätzlichen Klick noch einen Spielspeicherzugriff. Ein älterer Screenshot mit bereits unabhängig bestätigter Spielerposition zeigte offline einen deutlichen Landschafts-Korrelationstreffer bei der passenden Referenzregion, ist allein aber kein Beweis für eine neue Autorisierungslogik.

**Neu in 1.7.15:** Der 1.7.14-Live-Log vom 26.10. um 23:22 bestätigte Welt W553, InGame und alle 24 Skillwerte, stoppte jedoch vor der Weltkarte und vor `TERRAIN_PROBE` mit `COMPASS_PIXEL_NEEDLE_NOT_VERIFIED`: die Minimap wurde in der ersten Kompassaufnahme nicht erkannt. Die Kompassroutine wartet bei fehlenden Nadelpixeln nun bis zu acht weitere frische, rein passive Screenshots ab (etwa 4,4 Sekunden zusätzliche Beobachtung), protokolliert anonymisierte `minimap`-/`compass`-Erkennungshinweise und sendet **keinen** Klick ohne verifizierte Nadel. Bleiben die Pixel verschwunden, bleibt die Sperre bestehen. Taucht in einer späteren InGame-Aufnahme eine erneut pixelverifizierte Nadel auf, darf *nur dieser* vorherige Missing-Pixel-Block automatisch zu einer komplett neuen Kompass-Zweibildprüfung zurückkehren; weder Tooltip- noch Eingabeschutz-Fehler werden automatisch zurückgesetzt. Der neue Terrain-Vergleich aus 1.7.14 bleibt diagnostisch und ohne Bewegungserlaubnis. Es werden keine Spielspeicherwerte ausgelesen oder Screenshots gespeichert.

**Neu in 1.7.14:** Der Live-Test von 1.7.13 bestätigte maximalen Zoom-out; die bewährte SIFT-Lokalisierung blieb trotzdem bei `NO_VERIFIED_MAP_MATCH` (84 Merkmale und 2 Ratio-Paare vor dem Zoom versus 283 Merkmale und 4 Paare danach). Ein anschließend bereitgestellter Screenshot mit herausgezoomter Minimap zeigte bei einem *rein lokalen, nachträglichen Vergleich* zur öffentlichen Referenzkarte einen auffälligen, eindeutigen Oberflächen-Korrelationstreffer (`0,455`) nahe der zuvor unabhängig auf der Weltkarte bestätigten Position; der beste räumlich getrennte Alternativtreffer derselben Oberflächenkarte lag bei etwa `0,253`. **Das ist noch keine gültige Echtzeit-Positionsbestätigung.** Die neue read-only `NAVIGATION TERRAIN_PROBE` testet deshalb bei einer weiterhin blockierten Neutätigkeit einmalig über vier Kartenebenen und drei Zoom-Skalen, ob sich die visuelle Trennschärfe im tatsächlichen Capture wiederholt. Sie protokolliert nur gerundete Ähnlichkeits- und Konkurrenzwerte, nicht die unsichere Weltposition oder einen Screenshot. Kein neuer Klick, keine Gameplay-Freigabe und kein Lesen des Spielspeichers. Maximaler Zoom-out bleibt die bevorzugte Einstellung. Der zuvor widersprüchliche `requested_limit`-Wert in der Zoom-Ergebniszeile wurde korrigiert.

**Neu in 1.7.13:** Im 1.7.12-Live-Test vom 26.10. um 22:25 wurden `(3141,3212)` auf zwei unabhängigen Weltkartenaufnahmen mit je 22/33 Inliern bestätigt; die Karte schloss korrekt. Die Minimap wurde nach gemessener Vergrößerung um 1,1426 und zwei bestätigten Anschlagsschritten maximal **hinein** gezoomt. Trotzdem blieb die eigenständige absolute Minimap-Positionserkennung gesperrt (`AUTOMATIC_WORLD_POSITION_NOT_CONFIDENT`). Der Referenzatlas verwendet 4 Pixel pro Spielfeld; auf dem maximal hinein gezoomten Bild ist wenig Umgebung sichtbar, während der Bildabgleich eine größere Skalentransformation braucht. Zum Vergleich ergaben ältere Zoom-out-/Zoom-in-Tests 240/3 beziehungsweise 137/1 Feature-/Deskriptor-Treffer (verschiedene Aufnahmen, kein kontrollierter A/B-Vergleich). 1.7.13 bevorzugt daher **maximal herauszoomen** und schreibt für ein und dieselbe Live-Sitzung eine rein lesende `NAVIGATION LOCALIZATION_PREFLIGHT`-Bilanz `before_zoom` und `after_zoom` mit Status, Merkmalen, Kandidaten, Inliern und besten Ratio-Paaren. Bei unzureichenden Treffern bleibt jedes neue Gameplay unverändert gesperrt. Die vorläufige relative Distanzschätzung von 11,25 Pixeln pro Feld bleibt bei Zoom-out ausdrücklich deaktiviert; nur bei nachgewiesenem maximalem Zoom-in war sie bisher zulässig. Ob Zoom-out mehr echte Terrainübereinstimmung bringt, wird erst der nächste Live-Test zeigen. Kein Lesen von Spielspeicher oder zusätzliche vergleichende Zoomrunden.

**Vorheriger Stand (1.7.12):**

**Neu in 1.7.12:** Der Live-Test von 1.7.11 (26.10., 22:02–22:03 Uhr) bestätigte Anmeldung, 24/24 Skills und Nordkompass. Die Weltkartenroutine stoppte vor der ersten Eingabe, weil sich die Bildschirmposition des unverändert 1000×600 Pixel großen Spielfensters zwischen zwei Aufnahmen geändert hatte (`GAME_VIEWPORT_CHANGED`, `origin_changed=True`). Daher wurden die neuen Minimap-Diagnosen noch nicht erreicht. Nur beim **erstmaligen passiven Beobachten der Weltkarte, vor jeglichem Eingabeversuch**, darf 1.7.12 einen neuen Ursprung akzeptieren, wenn zwei unabhängige, frische InGame-Bilder dieselbe neue Bildschirmposition, Fensterkennung und Größe bestätigen. Sobald eine Eingabe auch nur versucht wurde, bleibt jede Veränderung des Viewports eine harte Sperre. Widersprüchliche, veraltete und unlesbare Bilder sperren ebenfalls. Keine zusätzlichen Klicks, keine Speicheranalyse, keine Lockerung der Weltpositions- oder Minimap-Prüfungen. Der Effekt auf dem echten Client muss noch live getestet werden.

**Vorheriger Stand (1.7.11):**

**Release-Stand:** Der 1.7.10-Live-Test bestätigte erstmals die Weltposition **3141/3212** in zwei unabhängig registrierten großen Kartenaufnahmen mit 17/26 und 17/25 Inliern, ohne die alten Konsens- oder Bildkontrollen zu lockern. Danach blieb die Minimap-Skala bei zwei nach oben gerichteten Hintergrund-Radimpulsen praktisch unverändert (1,0023/1,0000). Erst der entgegengesetzte Probeimpuls ließ die Hough-/Spielerzentrum-Minimaperkennung in drei frischen Frames ausfallen. 1.7.11 wartet in genau diesem Fall maximal sechs unabhängige Frames ab, ohne einen weiteren Radimpuls zu senden, und protokolliert nur anonymisierte Bildmerkmale (`hough_raw`, `inside_roi`, `white_centres`, `verified_unique`). Eindeutiges Spiel-HUD, gültiger Minimaprahmen und geometrisch gemessene Skalierung bleiben für jeden weiteren Schritt zwingend; wenn die Minimap nicht wiederkehrt, stoppt das Programm unverändert. Die Ursache des Live-Ausfalls ist ohne Screenshot während des Ereignisses noch nicht abschließend belegt.

**Vorheriger Stand (1.7.10):**

**Release-Stand:** Der 1.7.9-Live-Test bestätigt die neue sichere Charakterübersicht: Name, 24/24 Fertigkeiten und Nordausrichtung wurden ohne Prozessspeicherzugriff bestätigt. Der Kartenabgleich wurde mit 27/42 beziehungsweise 25/43 RANSAC-Inliern jedoch weiterhin zu Recht gesperrt. 1.7.10 versucht neben dem unveränderten reziproken SIFT-Matcher mit 0,65-Verhältnis auch **strengere** Descriptor-Auswahlen von 0,60 und 0,55 aus demselben Screenshot. Die bisherigen Mindestkonsenswerte, Kartenkanten-, Orientierung-, Verteilungs-, Eindeutigkeits- und unabhängigen Zwei-Bild-Prüfungen bleiben bestehen; widersprüchliche Positionskandidaten aller Auswahlen sperren die Navigation. Zusätzliche Profile werden mit Paar-/Inlier-Zahlen protokolliert. Ein endgültiger Kartenblock stoppt künftig auch unnötige periodische Charakterübersichts-Klicks bis zum bewusst ausgelösten F8-Neuversuch oder einem frischen Spielfenster. Ob die strengere Auswahl genügend Kartenmerkmale gewinnt, muss ein weiterer Live-Test zeigen.

**Vorheriger Stand (1.7.9):**

**Release-Stand:** Im Live-Log von 1.7.8 am 26.10. um 20:58–20:59 wurden Spiel und F2P-Welt 380 erkannt sowie 24/24 Fertigkeiten mit Gesamtlevel und Prüfsumme 131 gelesen; dennoch verharrte der Start in `IN_GAME_CHARACTER_PROMPT_NOT_READABLE`. 1.7.9 ergänzt einen vorsichtigen Ausweichweg: Erst wenn zwei zeitlich unabhängige, vollständig geprüfte Skills-Ansichten und das visuell erkannte Symbol der Charakterübersicht vorliegen, darf ausschließlich dieser Reiter geöffnet werden. Zwei unabhängige Namenserkennungen in der offiziellen Charakterübersicht müssen anschließend mit den Skills zusammengeführt werden; andernfalls bleibt der Spielauftrag gesperrt. Kein Kontoname wird geschätzt, keine Speicheranalyse verwendet, keine Navigation dadurch ungeprüft freigeschaltet.

**Vorheriger Stand (1.7.8):**

**Release-Stand:** Zwei echte Minimap-Screenshots vor/nach einer manuellen Bewegung zeigen eine Terrainverschiebung von etwa 49 Pixeln nach rechts und 42 nach unten. Eine unabhängig auf die frühere Kartenaufnahme registrierte Weltkarte (343/353 Bildpaare) deutet auf rund 4,4 Felder westlich und 3,7 nördlich; daraus ergibt sich vorläufig ein Maßstab von **11,25 Pixeln je Feld**. 1.7.8 führt damit eine **ausschließlich passive** relative Wegbilanz ein: nach bestätigter großer Weltkarte, Nordausrichtung und maximalem Zoom werden verschobene Bildpunkte mit angenommenem Maßstab als `LIVE_MINIMAP_RELATIVE_DISTANCE` gemeldet. Größere Bewegungen werden zeitnah protokolliert, auch wenn sie zwischen den normalen Fünf-Sekunden-Diagnosen liegen. Unterbrochene Bildketten verwerfen die Bilanz. **Keine dieser Schätzungen erzeugt eine Weltposition oder erlaubt einen automatischen Klick.** Außerdem nennt `VISUAL_FRAME/GUARD` ab sofort den konkreten Grund einer zusammengefassten Fenster-/Frischesperre, ohne die Sperre zu lockern. [Diagnose und Grenzen](docs/LIVE-MINIMAP-MOTION-2026-10-26.md).

**Vorheriger Stand (1.7.7):** Der Kartenkonsens kann bei mindestens 30 RANSAC-Inliern bis 60 % fallen, bevor unveränderte Zusatzprüfungen greifen. Der nächste Live-Test bestätigte die Kartenposition 3153/3202 mit 30 von 40 Paaren; der anschließende Minimap-Atlas-Abgleich blieb mangels einer verifizierten Weltposition blockiert.

**Vorheriger Stand (1.7.6):** 1.7.6 ergänzt eine passive Navigationsprüfung **vor** einer neuen Inventar-/Trainingsroutine. Der 1.7.5-Live-Test zeigte bei unverändert ungeklärter Position alle rund 30 Sekunden erneute Inventarmenü-Klicks; neue Aktivitäten werden nun ohne diese Eingaben blockiert und nach 60 Sekunden wieder passiv geprüft. Bereits offene Transaktionsjournale werden weiterhin separat vor diesem Gate bearbeitet. Ohne verifizierte Weltposition bleibt jede neue automatische Reise gesperrt. [Hintergrund](docs/LIVE-MINIMAP-MOTION-2026-10-26.md).

**Vorheriger Stand (1.7.5):** Der passive Minimap-Bewegungsabgleich korrigiert unterschiedliche Ausschnittgrenzen. Im anschließenden Live-Log betrug die scheinbare Bewegung nach der Zoomroutine nur noch etwa 0,03 Pixel statt zuvor rund vier Pixel.

**Vorheriger Stand (1.7.3/1.7.4):** Weltkarten- und Kompassprüfung, eigene Minimap-Referenzen, maximaler Zoom und erste Live-Bewegungsdiagnose. Diese Maßnahmen allein konnten die Weltlokalisierung im Live-Spiel noch nicht herstellen.

**Minimap mit maximalem Zoom (26.10.2026):** Die Live-Kalibrierung zoomt jetzt bis zum oberen Anschlag hinein und bestätigt ihn anhand tatsächlicher Bildwirkung. Der Live-Test erreichte diesen Zustand um 17:56:37. Für die Minimap sind vier eigene, geprüfte Referenzatlanten eingebettet; die große Weltkarte verwendet weiterhin ihre separaten Referenzen. Der anschließende Live-Abgleich blieb mit 137 Merkmalen und einem passenden Paar blockiert. Maximaler Zoom allein behebt die Lokalisierung hier nicht; autonomes Training ist weiterhin nicht bestätigt. [Umsetzung, Tests und Grenzen](docs/MINIMAP-REFERENZEN-UND-MAXZOOM-2026-10-26.md).

**Nordausrichtung ergänzt (26.10.2026):** Vor dem Kartenabgleich prüft die App nun die Kompassnadel und kann eine gedrehte Ansicht über den bestätigten „Look North“-Knopf zurückstellen. Zwei frische Nordbilder sind erforderlich; nach Pause und erkannter Richtungsänderung wird erneut geprüft. Im Live-Test stand die Kamera bereits bei 0,00°. Die Minimap-Zuordnung blieb trotzdem ohne gültigen Match. [Verhalten, Live-Beleg und Grenzen](docs/KOMPASS-NORD-2026-10-26.md).

**Aktueller Live-Stand (26.10.2026):** Die große Weltkarte wird automatisch geöffnet, ihr animierter Spielermarker erkannt und die Oberflächenposition 3155/3202 aus zwei unabhängigen Bildern bestätigt. Das Panel wurde selbstständig geschlossen; Zoomkalibrierung und Inventarprüfung mit 17 Items/11 leeren Plätzen liefen anschließend durch. Die Minimap liefert weiterhin keine gültige Welttransformation, deshalb bleibt der Wegversuch gesperrt. Korrigiert wurden außerdem die Weltlisten-Seite 2 und eine mögliche Umgehung der Navigationssperre ohne frische Skillbestätigung. [Korrekturen, Tests und Live-Belege](docs/WELTKARTEN-KORREKTUREN-2026-10-26.md) · [Aktuelle Fehler](docs/AKTUELLE-FEHLER.md).

**Nächster PR-26-Livetest (25.10.2026):** Die Inventarrunde wurde abgeschlossen (8 aus dem Cache, 9 frisch untersucht, 11 leer). Die erste Weltlokalisierung scheiterte trotz 214 Minimapmerkmalen ohne verifizierten Kartenmatch. Die Automatik prüft nun unmittelbar nach der bestätigten Anmeldung, ob die Minimap des offiziellen C++-Clients tatsächlich bis zum unteren Zoomanschlag gebracht werden kann: gezielte Mausradnachrichten, unabhängiger Maßstabsvergleich zwischen frischen Minimapbildern und zwei bestätigte wirkungslose Abwärtsstufen nach einem zuvor beobachteten Zoom-Effekt. Ignorierte Eingaben und unbekannte Zoomzustände sperren das automatische Gameplay mit konkreter Warnung; F8 und Beobachtungsmodus bleiben wirksam. [Analyse, Grenzen und Tests](docs/MINIMAP-ZOOM-UND-LOKALISIERUNG-2026-10-25.md).

**Live-Log vom 25.10.2026 (PR #26):** Bei der Inventarprüfung werden identische unbestätigte OCR-Identitäten nicht mehr in jedem Aufgabenzyklus erneut als Warnung gemeldet. Ein frischer, ausreichend vollständiger Wiki-Referenzcache wird jetzt explizit als solcher angezeigt; unvollständige Referenztabellen lösen eine erneute Aktualisierung aus. Vor einer Reise beobachtet das Programm eine fehlende Minimap höchstens zweimal passiv nach und meldet getrennt, ob die Bildquelle, Merkmale, Kartenübereinstimmung oder Eindeutigkeit fehlt. Eine einzigartige pixelverifizierte Minimap kann auch dann übernommen werden, wenn die Inventar-OCR ihr Label auslässt, aber den nativen Spiel-HUD erkennt. Es werden dadurch keine unbestätigten Weltpositionen oder Eingaben freigegeben. [Loganalyse und Regressionstests](docs/LIVE-LOG-2026-10-25.md).

**Item-Datenbank:** Eine lokale SQLite-Datenbank speichert bestätigte Itemnamen, Kontextaktionen und vollständige Bildsignaturen. Unveränderte Items werden innerhalb derselben Sitzung über Aufgabenzyklen hinweg wiedererkannt; Mengen und ausführbare Menüaktionen benötigen weiterhin frische Bestätigung. [Verhalten, Sitzungsgrenzen und Tests](docs/ITEM-DATENBANK-2026-10-25.md).

**Weiterentwicklung der Aufgabenautomatik:** Versorgte Tätigkeiten erhalten Vorrang vor Aufgaben mit fehlendem Bedarf. Bestätigte Beschaffungs-/Zielblockaden stellen die betroffene Aufgabe mit begrenztem Backoff zurück und erlauben verfügbare Ausweicharbeit. Gewählte Reiseziele bleiben über Fortschrittsschritte bestehen; offene Journale und unklare Ergebnisse behalten ihre Sperren. [Verhalten, Tests und Grenzen](docs/AUTOMATISCHE-AUFGABENPLANUNG-2026-10-25.md).

**Neu in 1.7.2:** Häufigere, weiterhin serielle Bildschirmbeobachtung und sofortige Weiterverarbeitung neu erkannter Login-Schritte. Wiederholte Klicks auf denselben Bildschirm bleiben zeitlich begrenzt. Ein Session-Controller erkennt Logout/Verbindungsverlust, stoppt Spielaktionen, prüft die Spielwelt-Verbindung und setzt den bekannten Einstieg mit Backoff fort. Die native Spielansicht kann zusätzlich über Minimap, Skills-Symbol und mehrere Bereiche der Chat-Bedienleiste erkannt werden. Charakter und vollständige Skillwerte müssen anschließend weiterhin neu bestätigt werden. [Ursache des Stillstands, Verhalten und Grenzen](docs/LOGIN-UND-RECONNECT-1.7.2.md).

**Neu in 1.7.1:** Der manuelle Aufgabenbutton ist entfernt. Die native HUD-Schrift wird in isolierten, kontrastverstärkten Ausschnitten gelesen; Chat-Schaltflächen werden getrennt von ihren On/Off-Anzeigen verarbeitet. Das behebt die im 1.7.0-Log belegte OCR-Schwachstelle nach dem Spieleinstieg. Die Wirkung im vollständigen Spielablauf wird separat geprüft; unbekannte oder unvollständige Skills geben weiterhin keine Spielaktionen frei. [Analyse und Prüfstand](docs/HUD-ERKENNUNG-1.7.1.md).

**Neu in 1.7.0:** Automatische Minimap-Erkennung und Zuordnung zu öffentlichen Karten, A*-Wegsuche auf Kollisionsdaten, Treppenverbindungen und begrenzte Tür-/Torbehandlung. Keine vom Benutzer aufgezeichneten Wege oder Wegpunkt-Kalibrierung. Die Automatik reist selbst zu eingebauten F2P-Zielen und beschafft Kampfbedarf über Bank/GE. Nahkampf, Fernkampf und Wind Strike gegen Level-1-Hühner prüfen Lebenspunkte, Nahrung, Ausrüstung und tatsächlichen Skill-XP-Zuwachs. [Bedienung, Datenquellen, Tests und verbleibende Grenzen](docs/KAMPF-UND-AUTOMATISCHE-NAVIGATION-1.7.0.md).

**Neu in 1.6.7:** Die zweite Spieleinstiegsseite wird über das Pergamentbanner, die drei Informationskacheln und den rot-weißen „CLICK HERE TO PLAY“-Button auch dann identifiziert, wenn Windows OCR den grafischen Text nicht lesen kann. Erst nach zwei zeitlich unabhängigen Bestätigungen und zuvor geprüfter Welt folgt ein Eingabeversuch; Passwort- und unbekannte Bildschirme bleiben gesperrt. [Analyse, Testgrenzen und neue Logereignisse](docs/NATIVE-SECOND-ENTRY-1.6.7.md).

**Neu in 1.6.6:** Der auf den bestätigten Play-Now-Klick folgende Bildschirm wird durch zusätzliche, eng begrenzte OCR auf erkennbare Welcome-/Connecting-/Click-here-to-play-Texte geprüft. Bei unklarer Anzeige werden ausschließlich datensparsame Statusinformationen protokolliert und keine angenommenen UI-Ziele geklickt. Ein klar erkannter zweiter Spieleinstieg verlangt erneut zwei unabhängige Bestätigungen und die vorherige Weltkontrolle. [Erklärung und Live-Testgrenzen](docs/POST-PLAY-OBSERVATION-1.6.6.md).

**Neu in 1.6.5:** Der auf Welt 553 gezeigte „Play Now“-Button kann bei fehlender Vollbild-OCR zusätzlich durch Bildkontur, weißen Buttontext und getrennten gelben Untertext erkannt werden. Es bleiben die Prüfung der empfohlenen Welt, zwei voneinander unabhängige Bildschirmbeobachtungen und F8-Pause erforderlich. [Details zur Play-Button-Erkennung](docs/PLAY-NOW-VISUAL-FALLBACK-1.6.5.md).

**Neu in 1.6.4:** Der anhand eines echten Weltlisten-Screenshots bestätigte Zeilenabstand beträgt 19 Pixel statt 18,25. Das Programm liest die dunkle native Ziffernschrift zusätzlich mit einem bildgestützten Font-Matcher. Erforderlich bleiben die aufsteigend validierte Spalte, eine eindeutige Welt und zwei unabhängige Bildschirmbeobachtungen vor einem Klick. [Ursache, Umsetzung und Prüfgrenzen](docs/NATIVE-WORLD-GRID-1.6.4.md).

**Neu in 1.6.3:** Die native F2P-Weltliste erhält eine separate Schwarz-auf-Weiß-Ziffern-OCR für die Spielansicht 1000×600. Der visuell bestätigte Eintrag W399 liegt bereits auf der ersten sichtbaren Seite; es wird daher nicht blind nach rechts geblättert. Vor einer Weltwahl werden verifizierte aufsteigende Spalten und zwei zeitlich unabhängige Beobachtungen derselben Zielzelle verlangt. [Analyse und Grenzen](docs/NATIVE-WORLD-GRID-OCR-1.6.3.md).

**Neu in 1.6.2:** Stabilere Erkennung von Weltlisten auch bei unvollständiger fokussierter OCR, reduzierter Mindestumfang für die bildgestützte Seitennavigation und strukturierte Weltlisten-Diagnose. Die App sucht weiterhin beim Start nach einem neuen GitHub-Release und prüft zusätzlich alle 30 Minuten im Leerlauf. Eine laufende Jagex- oder OSRS-Sitzung wird nicht durch das Update unterbrochen. Ein erfolgreicher Main-Release publiziert EXE und SHA-256-Manifest. [Änderungen und Grenzen](docs/UPDATE-AND-WORLD-DIAGNOSTICS-1.6.2.md).

**Neu in 1.6.1:** Das Programm prüft bei fehlendem Fenster erneut, ob der offizielle Jagex Launcher läuft, startet bzw. öffnet ihn bei Bedarf und stellt einen minimierten Launcher für die Play-Button-Erkennung wieder her. Der Launcher bleibt nach dem Clientstart geöffnet und erhält erst nach frischer, wiederholt konsistenter visueller Charakter- und 24-Skills-Erkennung einen regulären Schließauftrag. Kein erzwungenes Beenden; bei fehlender Schließbestätigung wird gewarnt. [Details und Live-Testgrenzen](docs/LAUNCHER-LIFECYCLE-1.6.1.md).

**Downloads:** Erfolgreiche Pull-Request-Läufe veröffentlichen ein zeitlich befristetes EXE-Testartefakt unter GitHub Actions. Der freigegebene `main`-Release wird nach dem Merge von der Windows-CI gebaut, inklusive Auto-Update-Manifest.

Windows-Oberfläche für den unveränderten offiziellen Jagex Launcher und `osclient.exe`.
Die Anwendung ist **noch kein vollständig selbstständig spielender Maxing-Agent**.

Neu: An den Fenstertransport angeschlossene Routinen für Sammeln, Kochen, Firemaking, Lederverarbeitung, Smithing, Prayer, Runecraft, einzelne Rezepte und GE-Aufträge. Die vollständige Funktion auf dem offiziellen Client ist noch nicht Ende-zu-Ende bestätigt. [Bedienung, unterstützte Abläufe und Grenzen](docs/VISUELLE-AUSFUEHRUNG-1.6.0.md).

## Fenster und Log

Die Spielansicht bleibt fest **1000 × 600 Pixel** groß. Das Hauptfenster lässt sich verschieben und minimieren, aber nicht frei skalieren oder maximieren. **Log ausblenden / einblenden** klappt unterhalb des Spiels einen 180-Pixel-Bereich zu oder auf; nur die Gesamthöhe des Fensters ändert sich. Auf kleinen Desktops kann das Gesamtfenster über den sichtbaren Bereich hinausragen.

**Log kopieren** und **Strg+Umschalt+C** kopieren denselben bereinigten Bericht mit bis zu 10.000 gepufferten Ereignissen, auch bei verborgenem Log. Die Vorschau zeigt die letzten 420 Ereignisse. Überläufe werden genannt. Bei belegter Zwischenablage gibt es begrenzte Wiederholungen und eine sichtbare Wiederholungsmöglichkeit.

**Automatik pausieren**, **F8**: Eingaben anhalten. F8 wird als globaler Hotkey registriert; wenn die Registrierung bereits durch eine andere Instanz belegt ist, erscheint eine Warnung und der Pause-Button bleibt verfügbar.

## Hintergrundbetrieb: umgesetzt und noch zu prüfen

Windows Graphics Capture liest die Compositor-Oberfläche des eigenen Hostfensters und schneidet daraus das offizielle Spielfenster aus. Eine davorliegende fremde App wird nicht als Spielbild gelesen. Kein `CopyFromScreen`, kein notwendiger Vordergrund und keine Speicherung von Screenshots.

Eingaben werden als Mausnachrichten an das ermittelte Fenster bzw. dessen Render-Child geschickt. Der Transport verwendet weder `SendInput` noch `SetCursorPos` noch `SetForegroundWindow`. Ein Versand gilt nicht als Erfolg; erforderlich ist eine passende neue Beobachtung. Die feste Geometrie wird vor dem Versand geprüft, F8/Pause/Abbruch werden berücksichtigt, und ein gedrückter Knopf wird auch bei Abbruch losgelassen.

**Der Hintergrundtransport wurde an einem verdeckten Testfenster einschließlich beobachteter Bildänderung geprüft. Am echten Client öffnete eine Fensternachricht die Weltenauswahl. Der vollständige automatische Login und die Annahme aller Aktionen bei verdecktem Client sind noch nicht bestätigt.** Eine Anwendung kann Fensternachrichten ignorieren oder eigene Fokusregeln anwenden. Es gibt keinen stillen Rückfall auf den globalen Mauszeiger.

Hintergrund bedeutet hier: hinter anderen Anwendungen oder verdeckt. **Minimiert pausiert die Bildaufnahme**, weil Windows dort keine frischen Renderframes garantiert. Veraltete Frames geben keine Aktionen frei. Statische Oberflächen erhalten bei Bedarf eine neue Capture-Sitzung. Capture-Aufräumarbeiten blockieren nicht den UI-Thread.

## Einstieg und Spielzustand

Im Jagex Launcher den gewünschten Charakter und **Offizieller Client** wählen und gegebenenfalls anmelden/MFA abschließen. Vorhandene Auswahl wird übernommen; die App gibt keine Zugangsdaten ein.

Der Launcher bleibt ab 1.5.0 im Originalfenster, damit dessen größere Mindestbreite den Play-Button nicht abschneidet. Nur der Spielclient wird eingebettet.

Der Startablauf erkennt den Launcher-Play-Button über Accessibility und bekannte Spielbildschirme mit lokaler Windows OCR. Die F2P-Prüfung lädt die offizielle Weltliste und vergleicht erreichbare uneingeschränkte freie Welten anhand mehrerer TCP-Proben. Ein erkannter passender Welteintrag wird gewählt und danach erneut beobachtet. Ein zusätzlicher vergrößerter Footer-Durchlauf erkennt die Welt auch dann, wenn OCR das Wort „World“ verliert, aber Weltzahl und Switch-Hinweis eindeutig zuordnen kann. Zahlenausschnitte verbessern das Lesen des Weltrasters. Für die feste native Geometrie ist eine begrenzte, bildgestützte Seitennavigation implementiert; sie benötigt noch den echten Ende-zu-Ende-Test.

In der Spielansicht erkennt der Leser Chat-Bedienleiste und Charakter-Prompt. Die Skilltafel wird über „Total level“ lokalisiert, und die winzigen Basislevel werden in vergrößerten Ausschnitten gelesen. Es müssen alle 24 Werte zur Gesamtstufe passen und in zwei zeitlich getrennten Beobachtungen übereinstimmen. Wiederverwendung eines Frames reicht nicht. Fremde Charakterdaten, unvollständige oder veraltete Lesungen geben keinen Zielvorschlag frei. Sichtbare OCR ist kein serverseitiger Authentifizierungsnachweis.

Falls die Skilltafel geschlossen ist, kann die Automatik bei der festen Standardgeometrie einen Hover auf den Skill-Tab versuchen und erst nach erkanntem „Skills“-Tooltip klicken. Dieser Ablauf benötigt noch eine Bestätigung am echten Client.

## Ziele, Fortschritt und Beschaffung

Der Zielplaner schlägt den nächsten zugänglichen F2P-Skill unter 99 vor; erst bei allen 24 bekannten Skills auf 99 wechselt er zu Vermögen/Ausrüstung. Mitglieder-Skills werden auf F2P nicht als abgeschlossen behandelt. Bestätigte visuelle Levelstände werden unter `%LOCALAPPDATA%\OSRSPlay\progress` gespeichert. Der Dateischlüssel ist ein Hash des Charakternamens; der Klarname wird dort nicht gespeichert. Ein gespeicherter Stand ersetzt keine frische Sitzungsbeobachtung.

Der neue F2P-Katalog enthält Basismethoden für alle 15 freien Skills und jeden trainierbaren Levelbereich, neun Quest-XP-Gelegenheiten, Werkzeug-Upgrades und erste Rezeptketten. Aus bestätigten Skills entsteht ein Aktivitätsvorschlag mit offen ausgewiesenen Voraussetzungen. Unbekannte Inventar-/Bank-/Questdaten werden nicht als erfüllt behandelt.

Die Ausführer untersuchen Inventarslots über Bildsignaturen, Mengen-OCR und Kontextmenüs. Nach einer Eingabe benötigen sie ein neueres Bild und passende Bestandsänderungen. Ressourcen, Produktionsdialoge, Bank und GE werden über lesbare Aktionshinweise bedient; unbekannte oder mehrdeutige Anzeigen halten den Ablauf an.

Die Beschaffungspolicy bevorzugt vorhandene Gegenstände, bindet Angebote an Produkt und fehlende Menge, prüft frische Gesamtkosten und die Münzreserve. Die neue Laufzeit kann ausgewählte Produkte ablegen, Material entnehmen und nach bestätigter leerer Banksuche einen nötigen Gegenstand kaufen. Aktuelle F2P-Preise kommen von der Wiki-Preis-API. Ein dauerhaftes Angebotsjournal verhindert blindes Wiedereinstellen bei Teilfüllung oder Neustart. Ein vollständiger automatischer Vergleich aller Beschaffungswege ist noch nicht angebunden.

Die Automatik wählt selbst eine unterstützte Aktivität und deren Reiseziel. Es gibt keinen manuellen Aufgabenbutton mehr. Die benötigten Referenzkarten werden automatisch heruntergeladen und lokal zwischengespeichert. Der Kontotyp ist eine Einstellung, keine automatische Ironman-Erkennung; die anfängliche Münzreserve beträgt 500.

**Noch offen:** Der vollständige Live-Test auf dem offiziellen Client, weitere Kampfziele, Quest-Ausführung, zuverlässiger Umgang mit allen Gefahren sowie Navigation außerhalb des unterstützten F2P-Festlands. Die automatische Kartenlokalisierung ist mit gedrehten/gezoomten Referenzbildern getestet; das ersetzt keinen erfolgreichen Reise- oder Kampflauf im Spiel. Minimiert bleibt die Aufnahme pausiert. Die Anwendung wird weiterhin nicht als fertiger Langzeit-Maxing-Agent ausgegeben.

[Gründliche F2P-Auswertung, konkrete Loginbefunde, Quellen und Implementierungsgrenzen](docs/F2P-PROGRESSION-UND-LOGIN-2026-10-24.md).

## Diagnose und Entwicklung

Logs: `%LOCALAPPDATA%\OSRSPlay\live-logs\last-session.log`; Beobachtungsmodus: `observation-session.log`; UI-Test: `ui-smoke-test.log`. Übliche Passwort-/Token-Felder und E-Mails werden redigiert. OCR-Volltext und Bilder werden nicht exportiert.

Windows x64, .NET 10 SDK, Windows Graphics Capture und lokale OCR-Sprachunterstützung:

```powershell
dotnet build native/OSRSLiveDiagnostics.csproj -c Release
dotnet run --project native/OSRSLiveDiagnostics.csproj -c Release --no-build -- --self-test
dotnet run --project native/OSRSLiveDiagnostics.csproj -c Release --no-build -- --ocr-self-test
dotnet run --project native/OSRSLiveDiagnostics.csproj -c Release --no-build -- --layout-self-test
dotnet run --project native/OSRSLiveDiagnostics.csproj -c Release --no-build -- --background-self-test
dotnet run --project native/OSRSLiveDiagnostics.csproj -c Release --no-build -- --observe-only
dotnet publish native/OSRSLiveDiagnostics.csproj -c Release -r win-x64 -o publish
```

`--observe-only` startet keinen Launcher und keine Updates und sendet keine Spieleingaben; ein vorhandenes offizielles Fenster kann zum Lesen eingebettet werden. `--ui-smoke-test` startet zusätzlich keine Spielbeobachtung. Der Hintergrundtest verwendet ausschließlich eigene Testfenster und kein Spielkonto. Ein nicht verfügbarer OCR-/Capture-Unterbau wird gesondert gemeldet; ein übersprungener Plattformtest ist kein Nachweis für Hintergrundbetrieb.

[RuneMate-Recherche, Prüfungen und verbleibende Grenzen](docs/OSRS-BACKGROUND-AND-DOCKED-LOG-2026-10-24.md).
