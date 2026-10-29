# Bekannte Fehler und offene Live-Grenzen – OSRS Play 1.8.37

**1.8.37 – Live v1.8.36: Korridorbewegung bis drei Kacheln
vor dem Wegpunkt, danach fehlende Neuplanung.** Vier echte
Vordergrund-SendInput-Klickfolgen nach Lumbridge; beim
vierten Klick `(3234,3171)->(3238,3178)` wurden
`(3234,3173)` als Präfix und `(3235,3175)` als
Korridorfortschritt durch das Plugin bestätigt.
Danach keine Positionsevents; die Automatik wartete
weiterhin, weil `nearWaypoint` bisher nur bei
Wegpunktdistanz 2 replante, nicht bei 3. Der letzte
bestätigte Tile war im ruhigen Fenster noch
aktuell genug für eine kurze Wiederaufnahme.
Ein späterer aktiver-Fensterwechsel führte
korrekt zu `RUNELITE_FOREGROUND_LOST`.
1.8.37 ergänzt nur im engen Fall von echtem
Präfix- UND Korridorfortschritt, Endzielannäherung,
1,6s ohne neue Position und höchstens 3s alter
API-Position den begrenzten
`CORRIDOR_THREE_TILE_QUIET_REPLAN`.
Danach ist ein neuer, verifizierter Kollisionsweg
mit maximal sechs Schritten zulässig, weiterhin
höchstens vier Replans pro Start. Kein
Hintergrundinput, keine automatische Fortsetzung
bei Fokusverlust und keine tatsächliche Stillstands-
behauptung ohne Plugin-Heartbeat. Realer Langstrecken-
Erfolg bleibt erneut zu testen.



**1.8.36 – Live v1.8.35: Seitwärtsschritte wurden zweimal fälschlich verworfen.**
Der neue Log vom 29.10.2026, 18:17–18:20 Uhr belegt zwei Laufversuche
mit 7 und 5 erfolgreichen Vordergrund-SendInput-Klickfolgen.
Sie endeten mit `UNEXPECTED_API_TILE` bei `(3229,3220)`
aus `(3230,3219)` (Wegpunkt `(3228,3218)`, Distanz 2)
und bei `(3238,3184)` aus `(3238,3183)` (Wegpunkt
`(3241,3186)`, Distanz 3). Vor beiden Seitwärtsschritten
hatte der tatsächliche RuneLite-Export mehrere eindeutige
Vorwärts-/Korridorbewegungen bestätigt.
V1.8.36 beobachtet einen einzelnen neuen Seitwärtsschritt
in unmittelbarer Nähe der verifizierten Route, sofern
die Entfernung zum finalen Ziel sinkt, und sendet beim
bloßen Seitwärtsschritt KEINEN neuen Klick.
Nur nach einem ruhigen Intervall und bei höchstens
3 Sekunden alter API-Position wird die
neu geplante Kollisionsroute mit maximal sechs
Wegschritten geklickt, wobei Fokus- und Eingabesperren
unverändert bleiben. Die konkrete Erfolgslauf-Quote
muss erneut im realen Spiel gemessen werden.

**Weiterer Befund:** Direkt beim Start schwankte das
RuneLite-Launcher-/Plugin-Profil kurz zwischen
`RUNELITE_NOT_RUNNING` und `PLUGIN_EXPORT_UNAVAILABLE`;
dies ist nicht die Ursache der späteren Autowalk-Fehler.
Die anschließend erkannte historische Position
(15 Minuten alt) wurde korrekt nicht für Spielklicks
freigegeben.



**1.8.35 – Vier Klicks, dann Umweg ohne automatische Rückkehr zum Wegplan:** 
Im Log v1.8.34 vom 29.10.2026, 18:02 Uhr, folgten dem vierten Klick
`(3257,3245)->(3259,3237)` die drei bestätigten RuneLite-Tiles
`(3255,3244)`, `(3254,3240)`, `(3255,3239)`. Das Programm konnte
diesen Spielumweg bisher nur beobachten, nicht kontrolliert aus der
neuen Position fortsetzen. Ein späterer Fokuswechsel beendete den
Lauf sicher mit `RUNELITE_FOREGROUND_LOST` – kein fehlerhafter
Game-Klick und kein Anlass, die Foreground-Policy zu lockern.
1.8.35 ergänzt einen begrenzten, API-frischen Quiet-Replan erst nach
drei solchen Umweg-Events, nach 1,6 Sekunden ohne weiteren Export,
nur bei Fortschritt sowohl zum Zwischen- als auch zum Endziel.
Er benutzt danach einen maximal sechs Tiles langen geprüften
Wegabschnitt. Der kumulative Session-Replan-Grenzwert ist vier.
Unverändert: kein Weiterlaufen nach Fokusverlust, kein altes
Positionsereignis als freier Input, kein unbegrenztes Neuplanen und
keine Garantie ohne neuen echten Live-Test.



**1.8.34 – Live-Erfolg und Eingabekonflikt:** Das 29.10.-Log zu
1.8.33 bestätigt im zweiten Versuch `TARGET_REACHED` bei
(3226,3220,0) mit 10 SendInput-Klickfolgen und neuer RuneLite-API-
Bestätigung; anschließend blieb (3226,3222,0) im Zielradius.
Der erste Versuch endete korrekt durch `PHYSICAL_MOUSE_BUTTON_HELD`
bei 0 Input-Events für den dritten angeforderten Befehl; zwei vorherige
Befehle waren erfolgreich gesendet. 1.8.34 meldet diese freiwillige
Sicherheitsunterbrechung ohne falschen roten Fehleralarm, prüft die
Maustaste erneut unmittelbar vor Click-Down und trennt versuchte von
wirklich eingefügten Klickfolgen im Stoppbericht. Keine automatische
Fortsetzung, keine Hintergrundklicks, kein Live-Beweis für 1.8.34.

**1.8.33 – Zwei echte v1.8.32-Frühstopps vor dem Wegpunkt:**
Das 461-Zeilen-Log vom 28.10.2026, 22:21:58–22:23:14
beweist, dass die veröffentlichte v1.8.32 (`build_run=40770678261`)
automatisch installiert wurde. Ein aus altem Export vorgemerkter
`Varrock_Westbank`-Start öffnete nach neuem Positionsevent korrekt
die Kalibrierung. Vier `FOREGROUND_INPUT_INSERTED`-Klicks
erzeugten echten RuneLite-Positionsfortschritt. Der vierte Klick
hatte das Kollisionspräfix bis `(3241,3271)`; nach dem exakten
Tile `(3240,3269)` meldete der Exporter `(3239,3269)` –
**eine Kachel seitlich, zwei vor Wegpunkt**, mit Restdistanz 2.
Der alte `after<before`- und Achsenkorridortest stoppte
`UNEXPECTED_API_TILE`, obwohl die Bewegung ansonsten echt war.
Bei einem weiteren Ziel `Grand_Exchange` passierte dasselbe:
nach zwei Klicks und dem echten beobachteten `(3238,3295)` kam
`(3237,3296)` vor Wegpunkt `(3239,3297)`, wieder nur ein
seitlicher Tilewechsel bei gleicher Restdistanz. Der zweite
`Lumbridge_Hühner`-Test bestätigte zwei Klicks und
API-Fortschritt, endete aber mit `RUNELITE_FOREGROUND_LOST` –
dieser Schutz ist richtig und wird nicht aufgeweicht.

Die v1.8.33-Erweiterung `NEAR_WAYPOINT_LATERAL_OBSERVED`
beobachtet maximal **ein** neues seitliches Tile pro Klick,
nur nach langem bereits bestätigtem Präfix, bei Tile-Sprung 1,
Wegpunktdistanz genau 2 vorher/nachher, maximal zwei Tile
Entfernung vom Kollisionspräfix, maximal fünf Sekunden nach
Klick und maximal zwei Sekunden Eventabstand. Das Tile gilt
**nicht** als Ziel-/Routenbestätigung und löst keinen neuen
Spielinput aus. Erst ein späteres wirklich verifiziertes
Wegpunkt-Event oder die bestehende beschränkte 1,6s-/3s-
Ruhe- und API-Aktualitätsprüfung mit neuem Kollisionsweg
(`WAYPOINT_LATERAL_QUIET_REPLAN`) ermöglicht eine
Fortsetzung. Der Wiederaufnahme-Zähler bleibt auf zwei;
weitere Seitwärtsbewegungen, lange Sprünge, falsche Ebenen,
alte API-Daten und Fokusverlust bleiben harte Stopper.
Exakte Live-Tiles und Negativfälle sind in den Selbsttests
enthalten. Echte Ankunft am gewählten Ziel ist noch offen.

**1.8.32 – Kein Buttondefekt, sondern fehlende frische
Pluginposition:** Im 1.8.31-Live-Log (49 Zeilen) wurde
`Lumbridge_Eichen` gewählt, anschließend `START_REQUIRES_PLUGIN_POSITION`
um 22:04:54 ausgelöst. Die Position war beim Start
**1347 Sekunden alt** (`HISTORICAL_POSITION_PREVIEW=3251,3229`)
und durfte nicht zu einem Bewegungsklick führen.
Die Fehlerbehandlung kopierte das komplette Log korrekt,
aber der Benutzer sah keinen hilfreichen Wartemodus.
1.8.32 lässt „Automatisch gehen (Test)“ einen Start
vormerken. Erst ein **neues Post-Request-Pluginereignis**
öffnet die Kalibrierung – **nicht** das Spiel selbst.
Die wartende GUI ist auffällig markiert und abbrechbar.
Nach maximal zwei Minuten, Ziel-/Fenster-/Sitzungswechsel
oder Pause wird der Wartestatus verworfen; nach
Zeitüberschreitung mit Fehler-Log-Kopie. Auch bereits
akzeptierte, aber für den Erstklick >60 Sekunden alte
API-Positionen erzeugen nur eine Vormerkung. Keine
Aufweichung von Quellen-, Zoom- oder Vordergrundprüfungen.

**1.8.31 – Konkrete Fehler aus dem v1.8.30-Live-Test (800 Zeilen):**
Der automatische Update-Start war erfolgreich. In der ersten
Sitzung wurden sieben echte Spielklicks und zwei begrenzte
`WAYPOINT_QUIET_REPLAN`-Erholungen nachgewiesen. Nach einer
folgenden `WAYPOINT_NEAR_CONFIRMED`-Meldung blieb nur noch
ein A*-Kollisionsschritt `(3227,3219)->(3226,3218)` bis
zum **Radius 4** von Lumbridge. Dieser Schritt projiziert
sich nur 5,7 Pixel weit und war bisher mit `radius<7`
gesperrt. 1.8.31 erlaubt eine gesondert geprüfte
`ProjectFinalGoalStep`-Projektion nur für einen einzelnen
validen, direkt angrenzenden, transportfreien Schritt in
den Zielradius. Erfolg weiterhin nur nach neuer API-Position.

Die dritte Sitzung bestätigte den Wegpunkt `(3257,3227)`
und schickte den nächsten Klick nach `(3249,3226)`, doch
die folgenden realen RuneLite-Tiles liefen zunächst in die
entgegengesetzte Richtung: `(3258,3229)->(3257,3230)->
(3253,3230)->(3251,3229)`. Vorige A*-Off-Path-Prüfung
stoppte sofort, obwohl der vorherige Klick danach noch
im Spiel weiterlief. `TRANSITION_DETOUR_OBSERVED`
beobachtet nach zuvor bestätigtem Wegpunkt nur maximal fünf
maximal vier Tile vom geplanten Präfix entfernte Events,
höchstens vier Sekunden nach Klick und ohne neue Eingabe.
Die normale 9-Sekunden-Frist gilt unverändert; ein später
bestätigter Wegpunkt ist zwingend. Wiederholte, lange oder
weit entfernte Umwege bleiben abgebrochen.

Der separate manuelle Eingabeschutz war auf RuneLite
**aktiv**; ein Test unterdrückte zehn physische Mausereignisse
und wurde mit `Esc` freigegeben. Keine real gemessene
Hintergrundsteuerung. Startup-Positionsdaten stammten erneut
aus einer ~25 Minuten alten Datei, wurden nur als historische
Vorschau angezeigt und erst nach manuellem Tilewechsel
als aktuelle Pluginposition übernommen. Das benötigt
weiterhin eine neue Exporter-Funktion beim Login/Start.

**1.8.30 – Korridor-Fix live bestätigt, Stillstand vor Wegpunkt offen:**
Im echten 1.8.29-Log vom 28.10.2026, 21:14–21:15, kamen zwei
Wegpunkte und jeweils ein zweiter Spielklick ohne `OFF_PATH`/
`UNEXPECTED_API_TILE` zustande. Es gab je einen frischen
Stillstand **zwei Tiles** vor dem Wegpunkt (3229,3170 bzw.
3235,3182). Der Nutzer wechselte daraufhin den Vordergrund;
`RUNELITE_FOREGROUND_LOST` schützt absichtlich weiter.
1.8.30 probiert bei mindestens zwei bestätigten Schritten,
exakt zwei Rest-Tiles und einer 1,6-Sekunden-Ruhephase
einen höchstens zweimaligen **neu geplanten** Wiederklick
vom letzten noch <=3s alten Plugin-Tile. Echte Stillstand-
Verifikation ohne Exporter-Heartbeat bleibt ausgeschlossen.

Startproblem: Nur ein 22 Minuten altes `position.json` war
vorhanden; als Live-Position darf es niemals übernommen werden.
1.8.30 zeigt sie sofort als **UNBESTÄTIGTE historische
Position**, lässt aber automatische Eingaben davon aus.

Neue optionale Eingabesperre per Windows-Low-Level-Hooks
blockiert physische Spielklicks/Tasten bei aktivem RuneLite-
Vordergrund-Test; Esc, Alt-Tab und Systemausstieg bleiben.
Die Wirkung des Hooks ist auf echtem Windows/RuneLite noch
zu prüfen. **Hintergrundspielklicks sind mit dem bisherigen
SendInput-/read-only-Exporter-Weg weiterhin nicht möglich**;
der gewollte Fokus-Stopp wird nicht abgeschafft.

**1.8.29 – geklärt: 1.8.28-Stopps trotz echter Wegbewegung:**
Das neue Live-Log vom 28.10.2026, 20:50–20:52, bestätigt
erfolgreiche Klicks, den korrekten weißen Punkt `(645,111)` und
von OSRS-Plugin-Events beobachteten Fortschritt. Vier-Lauf-Test:
`(3230,3167)->(3230,3169)`, `(3232,3176)->(3233,3178)`,
`(3241,3197)->(3240,3198)` mit voreiligem erneuten
Klick, sowie `(3216,3163)->(3220,3163)` wurden allesamt
als Abweichungen gestoppt, obwohl der Wegpunkt näher kam.
Eine Abfrage kann **2–4 Kacheln** echter RuneLite-Bewegung
repräsentieren; 1.8.28 erlaubte nur einen Einzel-Nachbarschritt
und klickte dann sofort erneut.

1.8.29 akzeptiert nur neue, zielnähere API-Kacheln im höchstens
zwei Kacheln breiten **Vorwärtskorridor** bei maximal vier
Kacheln Abstand zur vorigen Beobachtung. Dabei bleibt der
laufende Klick aktiv; erst der erreichte oder beinahe erreichte
Wegpunkt bzw. nach fünf solchen Ereignissen eine begrenzte
Neuplanung erlaubt weitere Eingaben. Alle Achsen-,
Ebenen-, Rückwärts-, Timeout- und Vordergrundstopps bleiben.
Exakte `collision_prefix`-Koordinaten, Korridorabstand,
Zeitintervalle und Sprungweite stehen im neuen Log.
Zusätzlich wird ein **neuer gescheiterter Gehversuch** bei
gleichem Fehlercode nicht mehr vom Clipboard-Deduplikator
unterdrückt; jedes erneute Nutzer-Armieren kann genau einen
aktuellen vollständigen Log-Kopiervorgang erzeugen.
**Der Live-Test von 1.8.29 und das Erreichen von Lumbridge
sind noch offen.**

**1.8.28 – bestätigter Fortschritt, zwei verschiedene Stopps in 1.8.27:**
Das neue Nutzerlog vom 28.10.2026, 20:37, zeigt zweimal den
geprüften **weißen Spielerpunkt (645,111)** und mehrere echte, nach
Win32-SendInput vom RuneLite-Exporter gemeldete Kachelwechsel.
Im zweiten Versuch wurden sogar **zwei** aufeinanderfolgende
Automatik-Klicks ausgeführt, ein Wegpunkt bestätigt und ein weiterer
Wegverlauf beobachtet. Kein vollständiger Zielerfolg bislang.

Der erste Versuch stoppte nach (3244,3192) auf der einen benachbarten
abweichenden Kachel (3243,3193), die näher zum angeklickten
Wegpunkt lag. 1.8.28 ermöglicht nur nach mindestens zwei
wirklich bestätigten Prefix-Schritten eine strikt begrenzte
`WAYPOINT_CORRIDOR_REPLAN`-Neuplanung aus einer benachbarten,
nachweislich näher liegenden Kachel; maximal zwei pro Sitzung,
nie ein ungeprüfter Folgeklick ohne neu berechneten Kollisionsweg.
Alle anderen Abweichungen bleiben harte Fehler.

Der zweite Versuch stoppte, als RuneLite den **Vordergrund verlor**.
Diese Sicherheitsregel ist beabsichtigt und wird **nicht**
abgeschwächt. Für den nächsten Test RuneLite die ganze Zeit
aktiv halten. Keine automatischen Klicks in ein anderes Fenster.

Die folgenden 1.8.27-Notizen dokumentieren nun eine behobene
Fehlposition, nicht den aktuellen Status.

**1.8.27 – schwerer Koordinatenfehler in 1.8.26:** Der gelbe
Kalibrierungsring traf den kleinen weißen Spielerpunkt **erneut nicht**.
Das ursprüngliche Kreuz der roten Linien liegt bei (640,110), nicht
bei den zuvor angegebenen (665,137). Der derzeitige 773×534-Client
benötigt etwa (645,111), nicht (665,110). Der 1.8.26-Live-Log vom
28.10.2026, 20:19, bestätigt den falschen Mittelpunkt x666/y114
und einen nicht zur A*-Route passenden Tile-Fortschritt nach dem
ersten Klick (`UNEXPECTED_API_TILE`). 1.8.27 korrigiert die
Layoutgeometrie auf `width-128,111`, fordert für automatische und
manuelle Kalibrierung den sichtbaren **weißen Spielerpunkt** und
begrenzt manuelle Abweichungen auf 3 Pixel. Das Programm darf mit
einer leeren/falsch ausgerichteten Minimap **keine neuen
Autowalk-Klicks** senden. Der Minimap-Rechtsklickreset bleibt; der
tatsächliche Zoomwert bleibt mangels Exporter-Feld unverifiziert.
Ein erneuter **Spieltest** ist zwingend erforderlich, bevor die
Bewegungsprojektion als live-geprüft gelten darf.

Die folgende 1.8.26-Beschreibung ist historisch widerlegt.

**1.8.26 – Live-Fehler 1.8.25:** Der automatische 15px-Ring-
Detektor sah einen Kartenmarker bei x679/y138 als Spielerpunkt.
Der Charakter steht aber dauerhaft im Mittelpunkt der Minimap,
laut Nutzer-Screenshot bei x665/y137 inklusive Titelleiste, also
Client-x665/y110. Manuelle x644–645 waren ebenfalls falsch.
Der neue feste Layoutanker wird bei bekanntem 768–773×528–534-
Client verwendet; unbekannte Fensteranordnungen benötigen manuelle
Kalibrierung und im bekannten Layout dürfen falsche manuelle
Positionen nicht mehr übernommen werden. Eine echte Zoom-
Steuerung per 3D-Kamera-Mausrad wurde vom Nutzer nicht verlangt:
1.8.26 setzt stattdessen mit einem streng geprüften Minimap-
Rechtsklick RuneLites Standard 4 Pixel/Kachel zurück und schließt
ein mögliches Kontextmenü mit Escape. Ein echter Minimap-Zoom-
Export fehlt bislang: Der Reset-Input ist überprüfbar, aber die
Zoomstufe **nicht unabhängig messbar**. Die neue Kombination muss
im Spiel getestet werden, bevor der Weg bis zum Ziel als verifiziert
gelten darf. Das Log aus 1.8.25 enthält unter anderem den Fehler
`CAMERA_ZOOM_NOT_COMPLETED` nach 33 Eingabeereignissen; diese
3D-Wheel-Schleife ist entfernt.

Die folgenden 1.8.25-Notizen sind historische, teilweise durch den
Nutzertest widerlegte Annahmen.

**1.8.25 – neue Echtdaten aus 1.8.24:** RuneLite reagiert
jetzt nachweislich auf die gezielte Vordergrund-Mauseingabe:
Echte Plugin-Kachelfolgen auf dem A*-Pfad und ein
`WAYPOINT_CONFIRMED` folgen dem `FOREGROUND_INPUT_INSERTED`.
Die zwei Fehler, die den Lauf bislang vorzeitig stoppten:
`API_POSITION_UPDATED` wiederholt eine bereits gelesene
Position mit identischer Exportzeitmarke (`new_event=false`)
und wurde trotzdem an `Observe` weitergegeben; sowie
eine echte Kachel **einen Schritt** neben einem fast
erreichten Wegpunkt nach ausreichend früherem Pfad-
Fortschritt. 1.8.25 ignoriert alte API-Events und
unveränderte Tiles; eine eng begrenzte, gleich ebene,
vorwärts abgesicherte 1-Tile-Endpunkttoleranz löst
Neuberechnung aus, statt fälschlich zu scheitern.

**Neuer Mini-Map-/Zoom-Test:** Ein Screenshot mit
rot markiertem Spielerpunkt zeigt ein charakteristisches
15px-Kreiszeichen (clientbezogen etwa x666/y111) –
die manuellen Punkte x643–645 lagen daneben.
Ein eindeutiger dunkler Ring mit hellem Zentrum
wird jetzt automatisch im WGC-RAM-Bild gesucht;
keine automatische Bestätigung, solange mehrere
Kandidaten erkannt werden. Das Bild dient **nicht**
zur WorldTile-Lokalisierung, nur zur Klickgeometrie.
Die manuelle Korrektur bleibt möglich. Ein auswählbarer
3D-Kamera-Zoom wird mit bewusster Vordergrundfreigabe
per Mausrad in relative Presets gefahren. **Exakte
Kamera-/Minimap-Zoomwerte kann der aktuelle Exporter
nicht bestätigen**; der getrennte Minimap-Zoom muss
weiterhin auf den RuneLite-Standard 4px/Tile eingestellt
sein. Nächster Live-Test soll die tatsächliche Ring-
Erkennung und Zoomänderung belegen.

**1.8.24 – PostMessage erzeugt weiterhin keine bestätigte
automatische RuneLite-Bewegung:** Drei `CLICK_INTENT`-
Ereignisse sind im echten 1.8.23-Nutzerlog nachweisbar.
Der erste PostMessage-Klickversuch endet nach 9 Sekunden
ohne Plugin-Bewegung (`POSITION_EVENT_TIMEOUT`). Nach
dem zweiten erreicht ein API-Tile die Position 3215/3197,
aber **außerhalb** des berechneten Wegs Richtung
3222/3198: korrekter `UNEXPECTED_API_TILE`-Stopp.
Beim dritten Klick ist die Logdatei nach nur 3,3s
manuell kopiert worden. Der eigentliche Java-/AWT-
Maus-Input wurde nie bestätigt; eine erfolgreiche
PostMessage-Queue reicht nicht als Beweis.

1.8.24 führt für den ausdrücklichen Bewegungstest
`SendInput` statt PostMessage ein. Das ist sichtbare
Vordergrund-Maussteuerung und deshalb nur mit zusätzlicher
Einwilligung nach Nutzerkalibrierung möglich. Der erste
Klick versucht RuneLite regulär in den Vordergrund zu
holen, weitere Klicks tun das **nicht** erneut. Vor jedem
Klick müssen RuneLite-Prozess, Fenstergeometrie, aktueller
Vordergrund, minimapbezogenes Bildschirmziel und
unbelegte Maustasten stimmen. Sonst sofortiger roter
`FOREGROUND_CLICK_NOT_SENT`-Fehler samt vollständigem Log,
ohne weiteren Spielinput. Auch erfolgreiche SendInput-
Übermittlung benötigt später einen **neuen** bestätigenden
Plugin-Wegschritt. Echte Liveannahme weiterhin **offen**.

**1.8.23 – live bestätigte Autowalk-Startblockade:**
Die echte Sitzung 19:07–19:09 Uhr am 28.10.2026
bestätigte erfolgreiche autoaktualisierte 1.8.22,
Plugin-Koordinaten, A*-Routen und zwei akzeptierte
Minimap-Kalibrierungen. Es wurde **kein** Gameklick
versucht, weil bei beiden `ARMED`-Ereignissen das
letzte akzeptierte Exportereignis 12/33 Sekunden alt
war. `CanDispatch <=3s` verhinderte den Erstklick
trotz Nutzerbestätigung. 1.8.23 erlaubt binnen zwölf
Sekunden nach manueller Kalibrierung einen einzigen
ersten Kurz-Wegpunkt-Klick von einem in der aktuellen
Session bereits akzeptierten höchstens 60s alten
API-Tile. Danach bleibt jedes weitere Kommando ohne
neues <=3s-Plugin-Ereignis verboten. Der eigentliche
Inputtransport nach RuneLite ist weiter **live offen**;
ein erfolgreicher Windows-CI-Build kann das nicht
ersetzen. Erste ungültige Startanfrage ohne
Sessionposition wurde im Log korrekt blockiert und
das Fehlerprotokoll automatisch kopiert.

**1.8.22 – erster automatischer Bewegungsklick eingebaut,
noch nicht live bestätigt:** Der 1.8.21-Nutzerlauf bestätigte
14 verifizierte Wege aus 13 RuneLite-API-Spielpositionen.
Der Benutzer wünscht jetzt den ersten echten Eingabetest.
Neue GUI-Buttons aktivieren die Automatik ausdrücklich,
lassen den Minimap-Mittelpunkt in einem temporär angezeigten
RuneLite-Bild wählen und senden danach jeweils nur einen
kurzen A*-Wegpunkt-Klick. Die Spielerposition wird weiterhin
aus dem Plugin Hub gelesen, **nie** aus dem Bild berechnet.
Nur neue API-Positionsereignisse auf dem geplanten Pfad
erlauben den nächsten Klick. Abweichung, fehlende Antwort,
Pause, Stopp, ungültige Daten und Fensteränderung sperren.
Der PostMessage-Transport zur Java-Game-Canvas sowie die
manuelle Pixelmaßstab-Kalibrierung sind die wichtigsten
offenen Live-Unbekannten. Vor dem Live-Test keine Zusage,
dass RuneLite tatsächlich losläuft. Türen/Transporte/
Treppen sind explizit nicht automatisiert. Fehlermeldungen
und Clipboardprotokoll bleiben aktiv.

**1.8.21 – zweite reale 1.8.20-Routenprobe bestanden,
Diagnosefehler gefunden:** Im Nutzerlog vom 28.10.2026,
18:20:50–18:21:22 Uhr Europe/Berlin sind 42
API-Positionsproben, zehn echte neue API-Ereignisse
und zehn erfolgreiche `RUNELITE_ROUTE ROUTE_RECALCULATED`
verzeichnet. Die Lumbridge-Route verbesserte sich von
92 auf 73 verbleibende Schritte, `ROUTING_ONLY_READY`
verifizierte 72 Kollisionsregionen und 421 Transporte.
Kein OCR-Bild, kein Positionsausfall, kein Wegfehler.
Die einzige rote Meldung `AUTOMATION_NOT_IMPLEMENTED`
entspricht weiterhin dem dokumentierten Read-only-Modus.

**Neuer manuell testbarer Routenverlauf:** Eine
zustandsbehaftete, rein lokale Auswertung vergleicht
das nächste echte RuneLite-API-Tile mit den Kacheln
der **vorherigen A*-Route** und vermerkt
`BASELINE`, `ON_PREVIOUS_ROUTE`,
`OFF_PREVIOUS_ROUTE`, `ARRIVED`, `LEFT_GOAL`
und `step_delta` zur vorherigen Restlänge.
Ein übersprungener Bildschirm-/Plugin-Zwischenstand
wird nicht als tatsächlich gegangener Weg erfunden.
Zielwechsel, Pause, Quellenverlust oder Routenfehler
setzen die Verlaufsreferenz zurück. Die GUI bekommt
einen sichtbar eigenen Routenverlauf; erster Live-Test
von Abweichung/Rückkehr steht noch aus.

**Korrigierter Logfehler:** Ein OSRS-Diagonalschritt
(`dx=1,dy=1` oder Vorzeichenvarianten) ist ein
benachbartes Feld. Die alte Logformel `manhattan_tiles>1`
meldete ihn fälschlich als `non_adjacent_observation=true`.
Nun entscheidet `chebyshev_tiles>1` (oder Ebenenwechsel)
und `event_gap_ms` bezeichnet den Abstand zweier
API-Exportzeiten. Ein Zwei-Felder-Sprung bleibt ein
nicht benachbartes Ereignis, keine rekonstruierte Route.
Windows-CI und neuer Nutzer-Live-Test stehen noch aus.

**1.8.20 – neue Architektur nach 1.8.19-Live-Test:**
1.8.19 wurde am 28.10.2026, 17:56–17:58 Uhr live mit bereits
installiertem Toolkit Exporter getestet. 44 direkte API-Auswertungen
und der aktuelle Updater waren gut; aber 89 als `STALE` bezeichnete
eigentlich normale Positionsereignisse erzeugten 91 OCR-Runden,
46 nicht lesbare Positionen und zusätzliche Fehlerprotokolle.
Der Pixel-Fingerprint konnte das nicht zuverlässig abfangen.

**Korrektur:** In der regulären RuneLite-GUI und im Probe-Modus
werden Koordinaten nur noch aus den verifizierten lokalen
RuneLite-API-Ereignissen gelesen. Kein WGC/OCR/Pixel-Gate für
Positionen. Aktuelle Änderungen werden einmalig als Bewegung
übernommen, ein unveränderter Wert ausdrücklich als
`API_POSITION_LAST_KNOWN` angezeigt. Ein altes Exportereignis
beim ersten Programmstart belegt keinen aktuellen Login:
`API_WAITING_FOR_NEW_EVENT` bittet um einen echten Kachelwechsel.
Fehlendes Plugin, fremdes Profil, Rückwärts-Zeitstempel und
inkonsistente Region-Koordinaten bleiben blockiert.

**Offene Grenze:** Der unveränderte offizielle Plugin-Hub-Exporter
schreibt bei einem Kachelwechsel. Er bietet keinen periodischen
Liveness-Heartbeat; der letzte Stand allein kann deshalb keine
fortbestehende Verbindung garantieren. Eine entsprechende Änderung
des Drittanbieter-Plugins ist nicht durch ein OSRS-Play-EXE-Update
installierbar.

**Nächster testbarer Schritt:** Aus dem API-Ort und einem frei
wählbaren F2P-Ziel wird eine Kollisionsroute aus gepinnten
Skretzo-Daten berechnet. OSRS Play zeigt nächsten Schritt,
Restlänge, Zielradius bzw. sauber benannte Ablehnungen. Die
Routenführung ist rein lesend; manuelles Gehen und Nutzer-Live-Test
stehen noch aus.

**1.8.19 – bekannter Stillstandsfehler des 1.8.18-Live-Tests:**
Das echte Nutzerprotokoll vom 28.10.2026, 17:33:17–17:34:26 Uhr
Europe/Berlin belegt `version=1.8.18`, `PREVIOUS_UPDATE`,
`ALREADY_CURRENT`, 40 bestätigte Positionen und 31 direkte
API-Aufnahmen sowie sechs sichere `WAITING_FOR_EXPORT_FRAME`.
Das neue Navigationsziel „Lumbridge“ erzeugt bestätigte
Luftlinienhinweise; acht Zusatzexport-Kategorien sind lesbar,
ein optionaler Snapshot fehlt, **null** wurden verworfen.

Verbleibende Positionserkennungs-Blockade: `3176,3198,0` wurde
in drei aufeinander folgenden **frischen API-Frames** bestätigt,
aber nach Ablauf des drei Sekunden alten Positionsexports
verhindern unbekannte Einzelglyphen `6`/`7` eine weitere
Pixel-Freigabe (`client_export_labelled_glyphs=8`,
`full_matches=0`). Später wurden `3202,3193,0` in mindestens
zwei API-Frames bestätigt, doch die OCR liest `32Ø2` ohne
vollständiges X/Y, deshalb `ID_ONLY_PIXEL_ENUMERATION`
und `NOT_CORROBORATED`. Es folgten 21 vorübergehend
`GRID_INFO_NOT_READABLE` und echte rote Warnungen inklusive
`POSITION_NOT_CONFIRMED_TIMEOUT` mit Clipboard-Kopie.

1.8.19 setzt an der tatsächlichen Fehlerquelle an:
Ein kurzlebiger, nur im Arbeitsspeicher gehaltener Pixelabdruck
aller Ziffern des **bereits zweifach bestätigten API-Orts** wird
erst nach zwei eigenen unabhängigen, zeitlich getrennten
erfolgreichen API-Frames vertraut. Ein späteres aktuelles Bild
des alten JSON-Ortes muss den gesamten Abdruck exakt zeigen,
einschließlich Ebene, beider Kommas und aller Chunk/Region-/X/Y-
Ziffern. Zwei unabhängige aktuelle Frames bleiben für die
Freigabe nötig. Widerspruch, ein geänderter Tile im selben Chunk,
eine andere Ebene, verändertes Layout oder ein anderes Fenster
werden abgelehnt. Keine gespeicherten Screenshots, kein
Positionsraten. Der Windows- und erste neue Live-Test stehen aus.

**1.8.18 – 1.8.17-RuneLite-Plugin-Hub-Integration live bestätigt:**
Das echte Nutzerprotokoll vom 28.10.2026 um 17:12:48–17:13:29 Uhr
Europe/Berlin dokumentiert die automatische Installation der 1.8.17
(`PREVIOUS_UPDATE`), erfolgreiche release-/manifest-Prüfung
(`ALREADY_CURRENT`), 36 direkte API-Lesungen und 30 zweifach bestätigte
Positionsproben von 52; zehn echte Tilewechsel sind im Motion-Log.
Bei älteren Positionsexporten kommen zusätzliche OCR-/Pixelprüfungen
zum Einsatz und wurden fünfmal erfolgreich bestätigt. Viermal
erschien kurzzeitig `GRID_INFO_NOT_READABLE`, weil der offizielle
Exporter seine Kachel bereits gewechselt hatte, das angeforderte
WGC-Bild aber noch den früheren Zustand zeigen konnte.

1.8.18 gibt in genau diesem Fall den benannten Wartezustand
`WAITING_FOR_EXPORT_FRAME` aus, setzt die bisherige Zweibildprüfung
zurück und lässt die OCR des nachweislich veralteten Bildes weg.
Live-Diagnose und Release-Hinweis bleiben strikt getrennt:
keine Positionsfreigabe für unverifizierte Zeitstempel, keine
automatische Spieleingabe. Neu hinzu kommen wählbare F2P-Luftlinien-
Ziele aus bestätigter Position und lokal gehaltene, schema-/zeit-
geprüfte Aggregatübersichten der neun offiziell exportierten
JSON-Dateien. Ältere Bank-/Questdateien dürfen nicht als frische
Spielzustände ausgegeben werden. Kein persönlicher Rohdatenexport
in Log/Clipboard. Der erste Live-Test von 1.8.18 steht aus.

**Weiterhin offen:** Kollisionen und Navigationstransporte wurden im
RuneLite-Companion noch nicht mit live Spielerpositionen verknüpft;
kein laufendes Pathfinding und keine RuneLite-Spieleingaben.

**1.8.17 – bestätigter OCR-Ausfall trotz erfolgreichem 1.8.16-Fix:**
Das echte 1.8.16-Live-Protokoll bestätigt siebenmal die neue
Ein-Chunk-Route, acht erfolgreiche ID-Pixelprüfungen und 13
zweifach bestätigte Positionsaufnahmen. Danach bleibt die Position
ab Probe 26 unzuverlässig: Einmal sind sämtliche Chunk-OCR-Werte
unvollständig, danach fehlt am Ort 3155/3186/0 eine unabhängige
Pixelvorlage für die nur einmal vorkommende Ziffer `6`. Weder
vier X/Y- noch vier Region-Stimmen reichen bisher allein aus.
Die beabsichtigten roten Warnungen und Clipboard-Kopien
funktionieren auch bei `POSITION_NOT_CONFIRMED_TIMEOUT`.

Ein offizieller RuneLite-Plugin-Hub-Export ist gefunden:
`Gorsokk/position-exporter` schreibt beim Tilewechsel einen
`position.json`-Snapshot aus der **echten RuneLite-API** lokal,
ohne eigenen Client oder Netzwerkübertragung. 1.8.17 ergänzt
eine optionale lesende Schnittstelle; bei frischem Export wird
die Position mit Original-Checksummen, genauem Window-Title-Match
und zwei WGC-Bildern bestätigt. Ältere Exporte benötigen aktuelle
eindeutig passende OCR und reale Pixelmuster, darunter Ebene und
Kommas. Erst damit darf der vom Client benannte Glyph `6` als
echte historische Ziffernvorgabe gelernt werden. Andere
Accountdateien, veraltete Werte ohne live sichtbare Bestätigung,
falsche Checksummen und fehlende Fensterbilder werden abgelehnt.

**Noch offen:** Ein bestätigter echter Live-Test mit installiertem
`OSRS Toolkit Exporter`; das Plugin muss der Benutzer im offiziellen
RuneLite Plugin Hub installieren und alle unnötigen Exporte
deaktivieren. Ohne Plugin bleibt das bisherige vorsichtige OCR.
Auf Spielautomation wird weiterhin ausdrücklich verzichtet.

**1.8.16 – mehrfach erkannte X/Y-Werte bei einzelner Chunk-ID:**
Das echte 1.8.15-Protokoll (27.10.2026, 23:11–23:12 Uhr)
belegt die automatische Updateinstallation, passende Release-
Metadaten, mehrfach bestätigte Bewegungskoordinaten und die
automatische Kopie der Fehlerlogdatei. Die neue Ein-Chunk-Route
aus 1.8.15 wurde nicht aktiviert, obwohl mehrere Proben nur
**eine** Chunk-ID, **drei bis vier** Region-ID-Lesungen und
**zwei bis vier übereinstimmende** X/Y-Paare enthielten.
Ursache war die alte Bedingung `xyReadings.Count==1`, die
mehrere übereinstimmende Werte ebenfalls ablehnte.

1.8.16 erlaubt in dieser Ausnahme mehrere X/Y-Lesungen, aber
nur bei einem **einzigen identischen** Wert, mindestens einer
Lesung aus einem vom Chunk unabhängigen OCR-Pass und mindestens
drei gleichen Region-ID-Werten. Jede konkurrierende X/Y-Lesung,
ein falscher Chunk, fehlende Pixelziffer oder Trennzeichen und
die fehlende frische Zweitaufnahme sperren weiterhin.
`xy_distinct_values`, `cross_pass_xy_votes` und `id_xy_votes`
zeigen die tatsächliche Evidenz; die echte Windows-CI-/Live-
Bestätigung steht noch aus.

**Separat offen:** Probe 42 des 1.8.15-Laufs besaß volle Feldquoren,
aber nur unzureichend benannte Ziffernvorlagen im Pixelraster
(`unknown_singleton_rejects=6`, `full_matches=0`). Der Client
verweigerte daher berechtigt die Position, meldete nach Timeout
rot `GRID_INFO_NOT_READABLE` und kopierte das Log. Die genuine
fehlende Pixelbeweiskette und die bislang nicht implementierte
Gameplay-Automation bleiben offene Punkte; es wird nichts geraten.

**1.8.15 – eine einzige Chunk-ID-Stimme trotz ausreichender anderer Hinweise:**
Das echte 1.8.14-Live-Log vom 27.10.2026, 22:54–22:55 Uhr
(Europe/Berlin) bestätigt die automatische Installation
(`PREVIOUS_UPDATE`, `ALREADY_CURRENT`), korrekt funktionierende
Bewegungsereignisse und die automatische Zwischenablagekopie.
Zwölf Positionswechsel werden als `RUNELITE_MOTION OBSERVATION`
einschließlich Aufnahmeabstand und Richtungsdifferenz protokolliert.
Bei Probe 69–75 liegt eine neue reale Blockade vor: `Chunk ID 809360`
wird nur von `nearest_2x` vollständig gelesen, während
`nearest_3x` unter anderem `Tile3163» 3200» o` und eine beschädigte
Chunk-ID liefert. `Region ID 12594` wird in mindestens drei
Bildaufbereitungen vollständig bestätigt. Die seit 1.8.12 nötigen
zwei Chunk-Stimmen fehlen (`chunk_readings=1`, `chunk_quorums=0`),
also wird keine unabhängige Pixelprüfung gestartet. Der Status
`GRID_INFO_NOT_READABLE` löst nach vier Sekunden wie vorgesehen
den roten Hinweis samt neuer vollständiger Logkopie aus.

Der 1.8.15-Sonderweg setzt eine **einzelne** sechsstellige Chunk-ID,
**eine** komplette X/Y-Zahlenablesung eines **anderen** OCR-Passes,
mindestens **drei** übereinstimmende Region-IDs und alle beiden
mathematischen Checksummen voraus. Unter `single_chunk_assisted=true`
werden die 64 mathematisch möglichen Felder ausschließlich mit
sichtbaren, unabhängig benannten Pixelziffern verifiziert. Die eine
OCR-X/Y-Lesung muss dem eindeutigen Pixelort entsprechen; die Ebene,
echte Trennzeichen und ein zweiter frischer Frame bleiben zwingend.
Fehlende Unabhängigkeit, eine zweite abweichende Chunk-ID, Region-
oder Pixelmangel und falsche Checksummen sperren die Freigabe.
**Noch kein 1.8.15-Live-Test.** Die eigentliche RuneLite-Automation
bleibt ausdrücklich unimplementiert.

**1.8.14 – Bewegungsfolge zwischen bestätigten Positionen bisher unsichtbar:**
Der 1.8.13-Live-Lauf vom 27.10.2026, 22:40–22:41 Uhr bestätigte
Update, Logkopie und insbesondere den neuen Fall mit genau einer
X/Y-OCR-Lesung in Probe 42 (`ID_ONLY_PIXEL_ENUMERATION`,
`single_xy_matches_pixels=true`, `OVERLAY_CONFIRMED 3142/3202/0`).
Die erkannte eigene Spielerposition ändert sich an vielen weiteren
Stellen. Die Logdatei zeigt aber nur einzelne bestätigte Standorte,
noch keine direkte Bewegungsänderung mit zeitlicher Lücke.

1.8.14 protokolliert deshalb ausschließlich **bestätigte**
Koordinatenwechsel als `RUNELITE_MOTION OBSERVATION` mit
ΔX/ΔY/ΔEbene, Proben- und Aufnahmeabstand und einem ausdrücklich
nicht als tatsächliche Route interpretierten Nicht-Nachbarschaftsflag.
Periodische Health-Events zählen bestätigte/stabile/wechselnde
Positionen. Bei Pause und Quellenverlust wird die Timeline verworfen.
Fehlende oder nur einfach erkannte Positionen dürfen nie in diese
Historie gelangen. Reine Feldquoren loggen nun nicht mehr fälschlich
`single_xy_matches_pixels=true`, sondern `not_applicable`; beim
Einzel-X/Y-Pfad wird die eine komplette OCR-Beobachtung korrekt gezählt.
Das ist **Diagnose**, weder Eingabetransport noch echte Navigation.
Ein Live-Test der 1.8.14-Protokollwerte steht aus.

**1.8.13 – exakt eine vollständige X/Y-OCR-Lesung blockiert den Pixelweg:**
Das echte 1.8.12-Live-Log vom 27.10.2026, 22:24–22:25 Uhr
(Europe/Berlin) bestätigt den Start nach automatischer Installation
(`PREVIOUS_UPDATE`, `version=1.8.12`, `ALREADY_CURRENT`). In Probe
13 und 14 lesen alle vier OCR-Varianten `315Ø` statt `3150`;
Chunk 805263 und Region 12593 werden ausreichend oft erkannt.
Die neue reine ID-/Pixelprüfung findet 64 Kandidaten, vier echte
Rastermatches und exakt einen Ort: **3150/3193/0**. Probe 14
bestätigt ein unabhängig frisches zweites Bild. Viele weitere
Proben und verschiedene Bewegungspositionen werden bestätigt.
Probe 12 liest dagegen in einer Variante `Tile3150» 3193» o`;
das Feldquorum hat nur eine statt zwei X/Y-Stimmen und die reine
ID-Route lehnt die Probe wegen `xy_readings=1` ab.

1.8.13 erlaubt diesen Sonderfall **nur mit einer einzigen**
vollständig numerischen X/Y-Lesung, wenn deren Zahlen bereits zum
Chunk und zur Region aus je mindestens zwei OCR-Pässen passen.
Jede X/Y-Glyphe muss weiterhin unabhängig in echten Pixeln
benannt sein, genau eine der 64 Positionen darf entstehen und die
Pixelposition muss mit dem Einzel-OCR-Wert identisch sein; sonst
`single_xy_matches_pixels=false` und keine Freigabe. Mehrere
komplette OCR-Paare, widersprechende IDs, ungültige Pixel und
fehlende Zweibildbestätigung bleiben gesperrt. Ein echter
1.8.13-Live-Test steht aus. Die rote GUI-Meldung zu
`AUTOMATION_NOT_IMPLEMENTED` ist sachlich unverändert richtig.

**1.8.12 – kompletter numerischer X/Y-Ausfall trotz sauberer IDs:**
Das echte 1.8.11-Live-Log bestätigt die neue Feldsuffixregel:
`region_ignored_suffixes=3` und anschließend
`PIXEL_GRID_VERIFIED` sowie `OVERLAY_CONFIRMED` für 3145/3196.
Andere Proben mit `Tile315Ø`/`Tile314Ø` in allen vier OCR-Pässen
bleiben gesperrt, weil die X/Y-Feldstimmen fehlen. 1.8.12 lässt
**nur in dieser speziellen Lage** nach zwei eindeutigen, rechnerisch
passenden Chunk-/Region-ID-Quoren die maximal 64 möglichen Tile-Felder
pixelweise prüfen. Jede der acht Ziffern benötigt einen unabhängig
benannten Glyphbeleg aus aktuellen IDs oder früheren bestätigten
Frames; OCR-Zeichenersatz oder unbenannte Wiederholung sind
ausgeschlossen. Kommas, Ebene, Eindeutigkeit und zwei frische Frames
bleiben Pflicht. Keine Freigabe bei widersprechendem OCR-X/Y,
fehlender ID, ungültigen Pixeln oder falschen Checksummen. Der
Live-Test dieser neuen strengeren Ausweichroute steht aus.

**1.8.11 – fremder HUD-Text teilt OCR-Zeile mit einer ID:**
Der echte 1.8.10-Live-Test bestätigt den Updateweg, vollständige
mehrhundertstellige Pixel-Logeinträge und mehrere bewegte Positionen
mit `OVERLAY_CONFIRMED`. In einigen Frames verschmilzt jedoch
`Pot (GE: 12 gp)` mit `Region ID 12593` oder `Chunk ID 805263`,
z. B. `Region ID 12593 12`. Ein bisher strenger Zeilen-Endanker
verwirft dann die ganze ID, obwohl Zahl und mathematische Prüfsumme
passen. 1.8.11 entnimmt im Feldquorum nur die **genau** fünf bzw.
sechs Ziffern vor einem durch Leerraum abgetrennten OCR-Suffix;
adjazente Extraziffern, widersprechende Quoren, Checksumme- oder
Pixelverweigerung bleiben Sperrgründe. Ein neuer Zähler zeigt pro
Frame, wie oft ein Suffix übersprungen wurde. Kein Live-Test der
Änderung und weiterhin keine automatisch ausgeführten Spielaktionen.

**1.8.10 – vollständige Fehlerereignisse statt 480-Zeichen-Abschneidung:**
Das echte 1.8.9-Log belegt das automatische Update, die unabhängige
Bestätigung von mindestens neun unterschiedlichen Positionen während
manueller Bewegung und den wirksamen sitzungsinternen Ziffernspeicher,
u. a. bei der früher gesperrten 3147/3208/0. Kaltstart ohne bereits
gelernte Vorlagen bleibt fail-closed. Der eigentliche neue Befund
ist ein Diagnoseverlust: 58 `RUNELITE_PIXEL`-Ereignisse schneiden
ihre Schlussfelder mit `...[gekürzt]` ab, weil `Diagnostics.Add`
den `Sanitize`-Standard von 480 Zeichen auch für ganze Detailtexte
übernahm. 1.8.10 erhöht dieses Feldlimit gezielt auf 16.384 Zeichen,
kennzeichnet seltene weitere Auslassungen samt Zeichenzahl und fügt
einen Dateikopie-/Zwischenablage-Regressionsfall hinzu. Geheime Token
und E-Mail-Adressen werden weiter herausgefiltert. Die GUI bleibt
bei nicht implementierter Automation rot; kein Gameplay-Code wurde
freigegeben. Die Behebung ist noch nicht live getestet.

**1.8.9 – schon unabhängig bestätigte ID-Ziffern auch nach Bewegung nutzen:**
Das echte 1.8.8-Live-Log bestätigt das Feldquorum und die Position
3144/3212/0 über viele Proben. Beim Wechsel auf 3139/3216/0 fehlt
eine ID-Vorlage der einmalig sichtbaren Y-Ziffer `6`, sodass die
Pixelprüfung erwartungsgemäß gesperrt blieb. Ein neuer, maximal fünf
Minuten lebender, an das konkrete RuneLite-Fenster und dessen Größe
gebundener Vorlagenspeicher übernimmt Pixelziffern nur aus getrennten
**vollständig bestätigten** Bildern. Erst nach zwei bestätigten
Aufnahmetimestamps und mindestens fünf exakt vergleichbaren aktuellen
ID-Ziffern dürfen diese Vorlagen eine heute einzelne Ziffer belegen.
Unbestätigte Frames, abweichende Fonts/IDs, andere Ziffernglyphen,
OCR-Widersprüche und fehlende Pixel bleiben blockiert. Die Release-API
erkennt trotz zeitweilig veraltetem CDN-Manifest die aktuelle Version;
Fehlerlog-Zwischenablage und roter Hinweis wurden erneut live belegt.
1.8.9 wurde noch nicht live getestet; Gameplay-Automatik fehlt weiterhin.

**1.8.8 – pro Feld übereinstimmende OCR statt nur vollständige Zeilen:**
Das echte 1.8.7-Log bestätigt die Release-API und Raw-Manifest bei Start
und manueller Prüfung jeweils auf 1.8.7. Der Pixel-Leser erkannte in
Probe 17 die zusätzliche Ziffernregel erfolgreich (`full_matches=4`,
`distinct_positions=1`, `repeated_unknown_layouts=4`), aber die zweite
frische Aufnahme konnte die Position nicht bestätigen. Bei Positionen
um 3145/3210 liefern die Nearest-OCR-Varianten gleiche Tile-X/Y- und
Chunkwerte, jedoch `Region 12594` gegen `12534`; die bicubischen
Varianten erkennen die richtige Region, aber nicht alle Chunk-/Y-Ziffern.
1.8.8 bildet pro Feld einen Quorumwert aus mindestens zwei vollständigen
Beobachtungen, prüft Region-/Chunkchecksummen und lässt dann erst
den unverändert konservativen Pixel-Leser entscheiden. Zwei verschiedene
Quoren für dasselbe Feld, gültig widersprechende Positionen und
unklare Ziffern/Kommas bleiben blockiert. Ein echter Bewegungsnachweis
mit 1.8.8 fehlt noch. Die Log-Zwischenablage funktioniert weiter.

**1.8.7 – neuer Live-Ort belegt fehlende Vorlagen:** Das echte 1.8.6-Log
vom 27.10., 20:28–20:29 (Europe/Berlin) zeigte bereits in Probe 1
`Tile3144`/`3211` mit übereinstimmenden Chunk `805265` und Region
`12594` in zwei OCR-Varianten. Die Pixelsuche fand passende Kommas und
ID-Ziffernvorlagen, aber `full_matches=0`: In diesen IDs fehlt die
Ziffer `3` aus den Tile-Werten. Beim späteren Weg über z. B.
`3147`/`3208` kommt außerdem die singuläre unbekannte `7` hinzu.
1.8.7 erlaubt nur **in derselben Tile-Zeile mehrfach pixelgleich
nachgewiesene** fehlende Ziffern; einzelne nicht trainierte Ziffern
werden ausdrücklich mit `unknown_singleton_rejects` verweigert.
Keine Position wurde im echten 1.8.6-Lauf fälschlich freigegeben;
die rote GUI-Meldung und `clipboard=COPIED` sind im Log bestätigt.
Das Update-Manifest wurde trotz 1.8.6-Cache-Headern kurz nach dem
Release noch als 1.8.5 beantwortet. 1.8.7 prüft daher zusätzlich
die aktuellste offizielle GitHub-Release-Asset-SHA-256 und nutzt bei
älterem Raw-Manifest den neueren verifizierten Release-Datensatz.
**Beide Änderungen benötigen neue Live-Bestätigung.**

**1.8.6 – Updater-Cache und tatsächlicher Live-Durchbruch:** Das neue
1.8.5-Sitzungslog vom 27.10. um 20:11 (Europe/Berlin) bestätigt bereits ab
der zweiten unabhängigen WGC-Aufnahme `OVERLAY_CONFIRMED` und anschließend
neun weitere stabile Ortsproben für X=3143/Y=3210/Ebene=0.
`RUNELITE_PIXEL` meldet jedes Mal `PIXEL_GRID_VERIFIED`, 4 identische
Rastertreffer und eine eindeutige Position; rund 11–12 ms nach der ersten
Probe. **Die zuvor blockierte Live-Position ist damit an diesem einen Ort
bestätigt.** RuneLite-Spielaktionen bleiben ausstehend und lösen
`AUTOMATION_NOT_IMPLEMENTED` samt Log-Kopie/roter GUI-Meldung aus.
Gleichzeitig lieferte die Update-Anfrage im 1.8.5-Client noch `remote_version=1.8.4`,
obwohl Version 1.8.5 bereits veröffentlicht war. 1.8.6 fügt pro Abfrage
einmalige URL-Kennungen, Cache-Vermeidungs-Header und `MANIFEST_BEHIND_LOCAL`
hinzu; die Wirksamkeit gegen die reale CDN-Zwischenspeicherung benötigt
einen weiteren Live-Lauf.

**1.8.5 – erweiterte Suche nach World-Location-Pixeln:** Das erste echte
1.8.4-Log zeigt in jeder Aufnahme zwei übereinstimmende OCR-ID-/X/Y-Beobachtungen,
aber `comma_layouts=0` bei 4.536 geprüften Rasterankern. Die auf den
zugeschnittenen Screenshot beschränkte Ankersuche wird auf den vollständigen
350×220-OCR-Bereich erweitert. Ein später bereitgestelltes vollständiges
RuneLite-Fensterbild bestätigte die Panelverschiebung um +14/+53 Pixel
gegenüber dem Ausschnitt bei **identischen** binären Panelpixeln. Eine
neue stufenweise Diagnose trennt fehlende
helle Pixel, nichtpassende Kommas/Leerstellen und Ziffernmuster. Der spätere
1.8.5-Live-Lauf bestätigte damit die Positionslesung; Spielaktionen bleiben gesperrt.

**1.8.4 – bisher ungelöste Live-Position:** Ein vom Benutzer zugeschnittener
World-Location-Ausschnitt zeigte eine feste monospaced Rasterstruktur und
sichtbare Kommas/Null, die Windows-OCR nicht zuverlässig erkannte. Ein neuer
streng geprüfter, bildgestützter Leser gleicht Tile-Ziffern mit Vorlagen
aus den bereits konsistenten Chunk-/Region-ID-Zeilen derselben Aufnahme ab.
Die analoge Auswertung des Benutzerbilds ist plausibel; der echte
WGC-/RuneLite-Lauf von 1.8.4 scheiterte allerdings an der zu eng begrenzten
Ankersuche und wurde in 1.8.5 adressiert. Keine ungesicherten
Ziffernersetzungen, keine Freigabe von Spieleingaben.

**1.8.3 – Auto-Update im RuneLite-Modus:** Der automatische GitHub-Check war
im älteren `MainWindow`, nicht im Standard-`RuneLiteWindow` verdrahtet.
Version 1.8.3 integriert Startup-, 30-Minuten- und manuelle Updateprüfungen
inklusive Log-/Clipboard-Fehlerdiagnose. Frühere Builds müssen einmalig
manuell durch 1.8.3 ersetzt werden. Das vom Benutzer angehängte 1.8.2-Log
bestätigt weiterhin unlesbare Ebene-0-Tile-Zeilen bei gültigen Capture-Frames;
an der fail-closed OCR-Sperre ändert dieser Fix nichts.

**1.8.2 – Diagnose statt stiller Sperre:** Ausführliche lokale Ereignislogs,
automatische Zwischenablage-Kopie bei Fehler/nicht ausgeführter Aktion und
roter Verbesserungsmeldung in beiden GUI-Modi sind ergänzt. Es gibt noch
keinen neuen echten Positions- oder Bewegungstest; die bekannte Ebene-0-
OCR-Sperre gilt unverändert. [Logging-Details](LOGGING-1.8.2.md).

**1.8.1 (noch ohne neuen Live-Test):** Zusätzliche Bildaufbereitung für die
World-Location-OCR und strengere Ablehnung widersprüchlicher gültiger
Koordinaten aus demselben Bild sind ergänzt. Die Ebene-0-Lesbarkeit ist dadurch
noch nicht nachgewiesen; die letzte bekannte Live-Sperre gilt weiterhin.

**Architekturwechsel am 27.10.:** RuneLite muss über den offiziellen Jagex Launcher starten. Der eigene Entwicklungsclient samt privatem Export-Plugin wurde daher verworfen und aus dem aktuellen Standardstart entfernt. Der nur lesende Companion-Entwurf soll stattdessen das Spielerkoordinaten-Overlay von World Location per OCR lesen. Das lokale Profil auto samt fünf Hub-Plugins ist eingerichtet. Die Live-Probe liest unter anderem `Tile3142» 3212» o` statt 3142/3212/0 und verwirft die Position korrekt. Reine Tests und unzweideutige Ebene-2-OCR-Fixtures bestehen; die bekannte Ebene-0-Ablehnung wird ausdrücklich als Grenze ausgewiesen. Automatische RuneLite-Spielaktionen sind nicht implementiert. [Vollständige Übergabe](HANDOFF-RUNELITE-2026-10-27.md). [Migration](RUNELITE-MIGRATION-2026-10-27.md), [vollständiges Plugin-Screening und Auswahl](RUNELITE-PLUGIN-PRUEFUNG-2026-10-27.md).

**Die folgenden Befunde beschreiben das bisherige visuelle Backend.**

**1.7.17-Live-Test (27.10., 00:13):** Weltkarte zweimal auf `(3141,3212)` bestätigt (14/20, 14/19), Kompass nach Norden bestätigt und Minimap vollständig herausgezoomt. SIFT-Matching der kleinen Minimap bleibt unzuverlässig. Die maskierte Atlas-Terrain-Erkennung lieferte `0.436/0.243` und `0.432/0.238` (Bestwert/Zweitplatzierter) bei gleicher Skala `1.95`, gleichem Oberflächen-Kandidaten, maximal einem Feld Abstand zur separat bestätigten Weltkarte und über zwei unabhängige Aufnahmen ohne Kandidatenverschiebung. Das ist ein starker Plausibilitätsbeleg **an genau diesem Live-Ort**, aber noch kein Test von Projektion, Klick, Wegwirkung, Neulokalisierung nach Bewegung oder fehlerhaften Matchfällen. 1.7.18 prüft die resultierenden vier hypothetischen Minimap-Schrittpositionen auf native Crop-Geometrie, Kompassachsen, sicher klickbare innere Randzone und Zwei-Bild-Stabilität; alle Werte bleiben ausdrücklich unverifiziert und führen zu keinem Input. Der unveränderte `AUTOMATIC_WORLD_POSITION_NOT_CONFIDENT`-Block verhindert noch automatische Bewegung.

**1.7.16-Live-Test (26.10., 23:55–23:57):** Weltkartenstart erfolgreich, zwei unabhängige korrekte Lokalisierungen `(3141,3212)` mit 19/28 und 19/29 Inliern sowie sicherer Kartenschluss. Minimap-Zoom-out in fünf Schritten bis zum visuell bestätigten unteren Anschlag. SIFT-Minimap-Position weiterhin `NO_VERIFIED_MAP_MATCH` (325 Features/2 Ratio-Paare vor Zoom; 337/1 danach). Der erstmals ausgeführte diagnostische `TERRAIN_PROBE` zeigte `best_score=0.550`, `runner_up=0.362`, `reference_scale=1.95` und 67 geprüfte Atlas-Peaks auf vier Ebenen. Ohne bestätigten Abstand zur unabhängigen Weltkarte ist nicht belegbar, ob der beste Treffer tatsächlich den Spieler beschreibt. 1.7.17 protokolliert daher den X/Y-Abstand zum höchstens fünf Minuten alten Weltkarten-Hinweis sowie Stabilität einer zweiten, ausreichend später aufgenommenen vollständigen Terraindetektor-Probe; Floor und Kandidaten bleiben unverifiziert, echte Koordinaten werden nicht im Log ausgegeben. Keine neue Navigationserlaubnis oder Eingabe, solange der verifizierende SIFT-Pfad scheitert.

**1.7.15-Live-Test (26.10., 23:38):** Die Kompass-Nadel wurde sofort pixelverifiziert (`map=801,11,150`, rot 35/blau 16, 0° Nord) und durch zwei frische Nordaufnahmen ohne Kameraklick bestätigt. Offizielle Welt W399, Skillwerte 24/24 und Stufe 131 wurden beobachtet. Weltkarten-Tooltip, Öffnen und Schließen wurden unabhängig bestätigt. Alle vier Kartenproben hatten etwa 1064–1069 SIFT-Merkmale und 27–28 reziproke Ratio-Paare, davon 16–17 RANSAC-Inlier (`r65:27/16`, `r65:28/17`, `r60:24/14`, `r55:12/9`). Bei maximal 60,7 % unterhalb der erforderlichen 65 % wurde der Standort korrekt abgelehnt und die Karte geschlossen (`WORLD_MAP_POSITION_NOT_CONFIRMED`). 1.7.16 protokolliert an dieser Stelle ergänzend die **nicht autorisierende** geometrische und pixelbasierte Qualität der knappen Kandidaten. Das kann zeigen, ob der Atlasinhalt trotz SIFT-Ausreißern zum sichtbaren Terrain passt; ohne Live-Belege findet weiterhin keinerlei automatische Reise statt. Die Minimap-Zoom-out-/Terrain-Probe aus 1.7.14 steht noch aus, weil die große Karte zuerst sicher lokalisiert sein muss.

**1.7.14-Live-Test (26.10., 23:22–23:24):** Version und Build wurden bestätigt. Offizieller Start auf W553, erfolgreiche InGame- und Skill-24/24-Erkennung sowie Levelsumme 131. Um 23:22:56 stellte die Kompassroutine `COMPASS_PIXEL_EVIDENCE minimap_missing` fest, blockierte sofort mit `COMPASS_PIXEL_NEEDLE_NOT_VERIFIED`, und über eine Minute spätere Hintergrundaufnahmen konnten die fest gespeicherte Sperre nicht mehr aufheben. Die Weltkarte, Zoom-out-Prüfung und `TERRAIN_PROBE` wurden in dieser Sitzung deshalb **nicht** erreicht. Aus dem Log allein ergibt sich nicht, ob die Kreisdetektion auf einem Übergangsbild oder dauerhaft versagte. 1.7.15 ergänzt begrenzte passive Wiederholungsaufnahmen mit Hough-/Pixel-Hinweisen und eine ausschließlich an spätere unabhängige Kompasspixel geknüpfte Freigabe dieses einen Blockgrunds zur erneuten vollständigen Kalibrierung. Kameraeingaben, Weltkartenklicks, SIFT-Positionshürden und Gameplay bleiben ohne passende visuelle Beweise gesperrt. Bei anhaltendem `minimap_missing` wird ein Screenshot **der betroffenen 1.7.15-Sitzung** zur Fehlersuche benötigt.

**1.7.13-Live-Test (26.10., ca. 22:45):** Die Weltkarte bestätigte `(3141,3212)` (24/35 RANSAC-Inlier in unabhängigen Frames). Danach erreichte die Minimap acht `WheelDown`-Eingaben und den nachgemessenen unteren Zoom-Anschlag, ohne unabhängige absolute Position: `LOCALIZATION_PREFLIGHT before_zoom` 84 Features/2 Ratio-Paare und `after_zoom` 283/4, beide 0 verifizierte Inlier. Nachträgliche, rein lesende Grauwert-Korrelation eines vom Nutzer bereitgestellten Screenshots ergab bei maximalem Zoom-out einen globalen Oberflächenkarten-Peak bei etwa 0,455 nahe der Weltkartenposition, konkurrierende räumlich getrennte Peak circa 0,253 (geprüfte Skalen 1,6–2,6 Referenzpixel pro Screenshotpixel). Für die volle reale Anwendung sind diese Einzelwerte nicht belastbar genug. 1.7.14 misst daher in der Live-Sitzung zusätzlich einen **nicht autorisierenden** maskierten Terrain-Bildabgleich in drei begrenzten Skalierungen auf allen vier Ebenen, einmal pro Subjekt. `TERRAIN_PROBE` enthält nur Scores und verweigert weiterhin jeden neuen Gameplay-Klick, solange `AUTOMATIC_WORLD_POSITION_NOT_CONFIDENT` gilt. Ein Screenshot von ca. 23:01 zeigt zudem `BLOCKED_WORLD_MAP_INPUT_TARGET_NOT_VERIFIED_AFTER_REFRESH` in einer anderen Sitzung; ohne den zugehörigen vollständigen Log kann nicht festgestellt werden, ob dort ein echter UI-Zielwechsel oder eine fehlerhafte Schaltflächenerkennung vorlag. Die nachträgliche visuelle Analyse behebt diese getrennte Worldmap-Eingabesperre nicht.

**Live-Test 1.7.12, 26.10. um 22:25:** Der vorherige Fensterpositionsblock trat nicht wieder auf. Weltkarte aus zwei voneinander unabhängigen Bildern mit je 22/33 SIFT-Inliern eindeutig bei `(3141,3212)` registriert und bestätigt geschlossen. Die Minimap-Zoomkalibrierung registrierte zunächst unveränderte obere Anschlagsmessungen, danach bei der gegenläufigen Probe ein tatsächliches Herauszoomen auf `scale=0.8707`, erneutes Hineinzoomen mit `scale=1.1426` und zwei bestätigte unveränderte weitere obere Anschlagsschritte. `MINIMAP_FULLY_ZOOMED_IN_VISUALLY_CONFIRMED` war erfolgreich. Eine absolute Minimapweltposition fehlte weiterhin; der neue Trainingsauftrag wurde folgerichtig **ohne neue Inventar-/Reiseklicks** mit `AUTOMATIC_WORLD_POSITION_NOT_CONFIDENT` blockiert. 1.7.12 protokollierte die unmittelbare Positions-Vorabprüfung noch nicht detailliert. 1.7.13 wechselt als begrenzter Vergleich zum maximalen Zoom-out und protokolliert in derselben Sitzung die Matcher-Qualität vor und nach Zoom (alle Bildbelege bleiben lokal/ungespeichert). Frühere Maximal-Zoom-out- und Zoom-in-Tests hatten 240 Features/3 Ratio-Paare bzw. 137/1, jedoch an nicht identischen Szenen; daraus folgt **noch keine nachgewiesene** Überlegenheit. Schwellen, Atlas-Plane-Eindeutigkeit, Zweibildprüfung vor realen Schritten und Fail-Closed-Logik bleiben identisch. Die 11,25-Pixel-pro-Feld-Distanzschätzung von Zoom-in wird unter Zoom-out nicht benutzt.

**Live-Test 1.7.11, 26.10. um 22:02–22:03:** Charakterbestätigung, 24/24 Skills mit Gesamtlevel 131 und Kompass-Nordprüfung waren erfolgreich. Unmittelbar vor dem ersten Weltkarten-Eingabeversuch wechselte jedoch die Bildschirmposition des Spiel-Viewports bei identischem 1000×600-Format und gleicher Fensterkennung. Die Bildroutine stoppte als `WORLD_MAP_FRAME_WINDOW_GEOMETRY_OR_FRESHNESS_CHANGED` nach `GAME_VIEWPORT_CHANGED origin_changed=True`. Da die Weltkarte noch nicht geöffnet wurde, wurden weder Minimap-Zoom noch Hough-Diagnosen erreicht. Die genaue Ursache der Positionsverschiebung ist im textbasierten Log nicht sichtbar. 1.7.12 erlaubt ausschließlich vor dem ersten Eingabeversuch eine Neukalibrierung nach zwei unabhängigen frischen InGame-Aufnahmen am gleichen neuen Ursprung. Wechselnde Fenster, andere Größen, widersprüchliche Positionen, unlesbare oder alte Bilder sowie jede Verschiebung nach begonnenen Eingaben bleiben gesperrt. Die Weltkarte, Minimap und spätere Navigation erfordern weiterhin ihre separaten strengen visuellen Prüfungen.

**Live-Test 1.7.10, 26.10. um 21:45:** Charakter, 24 Skills, Nordkompass und F2P-Welt bestätigt; aus zwei Kartenbildern wurde `(3141,3212)` mit `r65:26/17` und `r65:25/17` unabhängig geprüft. Die strengeren neuen Ratio-Profile waren dabei nicht entscheidend. Die nachfolgende Minimap-Zoomkalibrierung registrierte bei WheelUp/WindowWheelUp nur Skalen 1,0023 und 1,0000; nach der umgekehrten WheelDown-Probe ergaben drei frische InGame/HUD-Frames keine pixelverifizierte Minimap (`MINIMAP_ZOOM_POST_SCROLL_MINIMAP_MISSING`). Fenstermarke, 1000×600-Geometrie und Bildfrische waren weiter gültig. Die Ursache des Minimap-Verlusts ist nicht nachgewiesen. 1.7.11 ergänzt ausschließlich passive Beobachtungen vier bis sechs sowie eine differenzierte Hough-/Spielerzentrum-Diagnose; ohne bestätigte Minimap und messbare Skalenänderung bleibt Zoom-/Navigationsfreigabe gesperrt.

**Live-Test 1.7.9, 26.10. um 21:26:** Der pixelbestätigte Öffnungsweg der Charakterübersicht und die unabhängige Identitätsbestätigung funktionieren; Ranged wurde als Trainingsziel vorgeschlagen. Die Kompasskalibrierung bestätigte die Nordausrichtung und die Weltkarte konnte mit bestätigter Schließung geöffnet/geschlossen werden. Ihre vier Bildsätze enthielten 27/42 bzw. 25/43 SIFT-Inlier; keine Probe erreichte den unveränderten Konsens, deshalb `BLOCKED_WORLD_MAP_POSITION_NOT_CONFIRMED`. Etwa 14 Sekunden nach dem Block führte die kurze 30-s-Identitätslease zu einem unnötigen neuen Character-Summary-Klick, obwohl die Navigation bereits gesperrt war. In 1.7.10 werden schwächere Merkmale aus identischen Frame-Descriptors bei strengeren Verhältnissen 0,60/0,55 ausgeschlossen und ausschließlich Kandidaten mit unverändertem Konsens und allen zusätzlichen visuellen Prüfungen zugelassen. Außerdem frieren terminale Karten-/Zoom-/Kompasssperren die periodische Summary-Aktualisierung bis zum F8-Neuversuch ein. Kein positiver Ausgang dieser Änderungen ist ohne neuen Live-Test behauptet.

**Live-Test 1.7.8, 26.10. um 20:58–20:59:** Die Welt 380 war visuell bestätigt, 24/24 Skills und beide Summen betrugen 131. Die Chat-Eingabe lieferte jedoch keinen gültigen Charakternamen (`focused_prompt_candidates=0`). Dadurch blieb `Game.Confirmed` leer, alle Folgekalibrierungen und die passive relative Wegbilanz wurden erwartungsgemäß nicht gestartet. Das erneute Öffnen des Skills-Reiters nach etwa einer Minute änderte daran nichts. In 1.7.9 darf ein vollständig bildbestätigtes Skills-Raster die ausschließlich lesende Charakterübersicht unter zusätzlichen Symbol- und Zwei-Bild-Prüfungen anfordern. Ein fehlender oder widersprüchlicher Charaktername blockiert weiterhin sämtliche Spielaktionen. Ob der neue Ausweichweg auf diesem Client funktioniert, ist bis zum nächsten Live-Test offen.

**Neue Bildbelege, 26.10. um 20:20–20:21:** Eine tatsächlich manuell zurückgelegte Strecke wurde erstmals aus zwei Minimapbildern mit etwa +49/+42 Terrainpixeln registriert (je nach Bildausschnitt 10–12 RANSAC-Inlier). Die unabhängige Weltkartenregistrierung zu einer älteren Aufnahme erreicht 343/353 Inlier und weist auf ungefähr −4,4/+3,7 Weltfelder relativ zum älteren Ort hin. Diese Schätzung von rund 11,25 Pixeln je Feld ist **noch nicht zweimal unabhängig live kalibriert** und darf nur als Diagnose dienen. Ein Screenshot zeigt außerdem `BLOCKED_WORLD_MAP_FRAME_WINDOW_GEOMETRY_OR_FRESHNESS_CHANGED`; ob dabei der Spielprozess, die Position des 1000×600-Viewports oder die Frische der OCR-Aufnahme wechselte, ist anhand von Screenshots nicht erkennbar. 1.7.8 protokolliert den jeweiligen Grund getrennt und bleibt eingabeseitig gesperrt.

**Live-Test 1.7.6, 26.10. um 19:44:** Anmeldung, Skills (24/24, Gesamtlevel 131) und Weltkartenöffnung funktionierten. Vier Weltkartenbilder ergaben jeweils 49 Feature-Paare mit 31 geometrischen Inliern, aber die damalige 65-%-Konsensschwelle verwarf die 63,3 % sofort. Die Karte wurde nach erkannter Schließkontrolle wieder geschlossen. Danach gab es neun Minuten lang keine erneuten Inventar-, Reise- oder Trainingsklicks: der Vorab-Positionsblock aus 1.7.6 greift. In 1.7.7 dürfen hohe absolute Trefferzahlen (mindestens 30 Inlier) mit mindestens 60 % Konsens zu den weiterhin unveränderten zusätzlichen Bildprüfungen vordringen. Ob dieser konkrete Fall auch Orientierung, Kantenvergleich und unabhängige Positionsbestätigung besteht, ist offen. Der nachfolgende Minimap-/Atlas-Abgleich bleibt ungelöst.

Release 1.7.6 stoppt die beobachtete Folge wiederholter Inventarkontextmenüs, wenn bereits vor Beginn der nächsten Aktivität keine verifizierte Weltposition vorliegt. Bestehende Journale mit möglichen ausstehenden Aktionen bleiben vor dieser Sperre priorisiert. 1.7.5 hatte die passive Verschiebungsmessung um wechselnde Ausschnittursprünge korrigiert; der Live-Test ergab danach nahe null Pixel scheinbare Bewegung. Eine absolute Minimap-Weltposition ist weiterhin gesperrt. Zwei unabhängig bestätigte Weltkartenpositionen nach manueller Bewegung werden für die Maßstabskalibrierung benötigt. [Diagnose und Grenzen](LIVE-MINIMAP-MOTION-2026-10-26.md).

Die untenstehenden Live-Messungen dokumentieren die weiterhin offenen Grenzen von 1.7.3 und älteren Testbuilds; die Versionsnummer ist kein Nachweis behobener Weltlokalisierung.

**Neuester Test:** Eigene Minimap-Referenzen sind ergänzt. Mit Zoom-out blieb `minimap-01` um 17:51:54 blockiert (240 Merkmale, drei passende Paare). Auf Benutzerwunsch kalibriert die App jetzt zum maximalen Zoom hinein; `minimap-02-maxzoom` bestätigte den oberen Anschlag um 17:56:37 anhand echter Vergrößerung und zwei unveränderten Schritten. Der anschließende Positionstest um 17:58:00 blieb mit 137 Merkmalen, einem passenden Paar und null Inliern blockiert. Kein Bewegungsnachweis; maximaler Zoom allein löst den Matchfehler hier nicht. [Details](MINIMAP-REFERENZEN-UND-MAXZOOM-2026-10-26.md).

**Aktualisierung zur Kamerarichtung:** Eine explizite Nordkalibrierung ist ergänzt. `compass-04` bestätigte am 26.10. um 17:19:53 zwei unabhängige Bilder mit 0,00°; der folgende Wegversuch blieb mit 246 Merkmalen und nur vier passenden Referenzpaaren blockiert. In diesem Nordzustand genügt eine Kameradrehung zur Behebung des Matchfehlers nicht. Der Live-Klickfall aus einer tatsächlich gedrehten Ausgangslage bleibt separat zu bestätigen. [Details und Tests](KOMPASS-NORD-2026-10-26.md).

## Aktuell bestätigt – `world-map-12`, 26.10.2026

Die große Weltkarte wird automatisch geöffnet, der animierte Spielermarker erkannt und das Panel bestätigt geschlossen. Um 16:47:01 wurde die Oberflächenposition 3155/3202 aus zwei unabhängigen Bildern übernommen. Zoomkalibrierung und Inventarrunde (17 Items, 11 leere Plätze) liefen danach durch.

**Der aktuelle P0-Blocker ist die anschließende Minimap-Zuordnung:** 262 Merkmale, nur drei passende Referenzpaare, keine gültige Transformation. `AUTOMATIC_WORLD_POSITION_NOT_CONFIDENT` verhindert weiterhin Bewegung. Ein Weltkartenhinweis allein erlaubt keinen Wegschritt. Zeitweilige Tooltip-/Inventar-Tab-Ausfälle und ein als Sitzungswechsel interpretierter Schließübergang bleiben Beobachtungspunkte.

Die falsche Kartenbreite, die X-Erkennung, die vierteilige Markersegmentierung und eine mögliche Umgehung der Navigationssperre bei fehlender frischer Skillbestätigung wurden korrigiert. Die Weltlisten-Seite 2 erhält auch bei sechs groben OCR-Zellen wieder ihre native Gesamtprüfung; diese Änderung ist durch Fixtures, noch nicht durch einen getrennten Gesamteinstieg geprüft. [Änderungen, konkrete Live-Belege und nächste Schritte](WELTKARTEN-KORREKTUREN-2026-10-26.md).

## Vorheriger Prüfstand bis `world-map-08`

Stand: 26.10.2026, einschließlich `world-map-08` um 16:18 Uhr. Alle Zeiten sind lokale Windows-Zeiten. Belege: [Weltkartenroutine](WELTKARTEN-NAVIGATION-2026-10-26.md), [vorherige Live-Recovery und Zoom](LIVE-RECOVERY-UND-ZOOM-2026-10-25.md).

## Bestätigter Fortschritt

- Welt 399 und Spieleinstieg wurden unabhängig visuell bestätigt; 24/24 Skillwerte und Gesamtlevel 131 wurden gelesen.
- Die zuvor blockierte Zoomprüfung erreichte um 22:10:07 erstmals `MINIMAP_FULLY_ZOOMED_OUT_VISUALLY_CONFIRMED`: ein echter Skalenwechsel von 0,6486 mit 24 Inliern, danach zwei Anschlagsmessungen von ungefähr 1,0 mit 83 bzw. 88 Inliern.
- Die Inventarrunde wurde um 22:11:32 abgeschlossen: 17 frisch untersuchte Items, 11 leere Plätze, etwa 84 Sekunden. Einzelne OCR-Schreibweisen bleiben korrekt von der Cache-Freigabe ausgeschlossen.
- Der bisher unbekannte echte Disconnect-Dialog wurde erkannt. Ein automatischer, frisch bestätigter Retry-Klick erreichte die Play-Now-Seite. Zwischenzustände beenden die Wiederverbindung nicht mehr fälschlich als erfolgreichen Spielbeitritt.

## P0 – Weltlokalisierung und autonome Bewegung bleiben offen

**Bestätigt:** Nach erfolgreicher Zoom- und Inventarprüfung um 22:11:36: `NAVIGATION_LOCALIZATION_NO_VERIFIED_MAP_MATCH features=120 candidates=0 inliers=0 best_ratio_matches=0 phase=initial no_input=true`. Eine spätere Wiederholung bei gedrehter Kamera blieb ebenfalls ohne Match. Ergebnis: `AUTOMATIC_WORLD_POSITION_NOT_CONFIDENT`, keine Bewegungsfreigabe.

**Zusätzlicher Code, noch kein Live-Bewegungsnachweis:** Bei fehlendem nativen Match kann die Merkmalsextraktion einmal gleichmäßig vergrößert wiederholt werden. Die transformierten Punkte werden in ursprünglichen Koordinaten geprüft. Vorhandene gültige native Matches bleiben erhalten, mehrdeutige Matches werden nicht durch einen zweiten Versuch überstimmt. Die Referenztests bestätigen Geometrie, Routing und Klickkoordinaten; die Zuverlässigkeit am echten Client ist damit nicht bewiesen.

**Weltkartenroutine implementiert, Live-Kette noch offen:** Am 26.10. erfolgten Launcherstart, Weltwahl 399, Spieleinstieg und das Öffnen der großen Weltkarte automatisch. Der exakte rote Shortcut bestätigte den Globusklick. Die Kartenbeschriftungen wurden erkannt, die Schließkontrolle jedoch nicht; `world-map-08` blockierte mit `WORLD_MAP_POSITION_NOT_CONFIRMED`. Die abschließende Korrektur trennt passive Kartenanalyse von der geprüften Schließkontrolle und meldet gegebenenfalls `WORLD_MAP_CLOSE_CONTROL_NOT_VERIFIED`. Ihr vollständiger Live-Durchlauf steht noch aus.

Markerauswertung, zwei unabhängige Positionsbelege, bestätigtes Schließen und regionale Minimap-Suche sind im Code vorhanden und durch Fixtures geprüft. Diese liefern noch keinen Live-Wegschritt. Der Oberflächenhinweis beweist kein Spielerstockwerk und gilt nur fünf Minuten; längere Reisen benötigen künftig eine erneute Lokalisierung.

Die bisherigen Navigationsfixtures stammen aus der Referenzkarte selbst. Sie können einen Darstellungsunterschied zwischen echter Minimap und Referenzkarte nicht ausschließen. Kartentreffer und Bewegung dürfen deshalb weiterhin nur aus tatsächlichen Live-Belegen freigegeben werden.

## P1 – Vollständige Aktionsketten und F2P-Maxing nicht bestätigt

Reise, Bank/GE, Kampf, Sammeln, Produktion und Fortschritt über längere Zeit sind noch nicht durchgehend autonom live bestätigt. Eine vollständige Inventarrunde und ein Aufgaben-Vorschlag sind kein Nachweis für Trainingserfolg oder Level 99.

Nach verifizierter Weltposition muss zunächst ein kurzer Weg mit neuer Positionsbestätigung funktionieren; anschließend ein einzelner Sammel-/Produktionsvorgang mit echtem Inventar- oder XP-Beleg. Journale und Schutzregeln für unklare Ergebnisse bleiben erforderlich.

## Behobene Startblockaden und verbleibende Beobachtung

Der erneute Gesamteinstieg gelang am 26.10. auf Welt 399. Nicht jede Weltlisten-Seite ist damit abgedeckt: In einem vorherigen Lauf lieferte Seite 2 zu wenige bestätigte Weltzellen, um zur empfohlenen Welt 553 zu blättern. Die fokussierte Play-Now-Erkennung wurde ergänzt, weil bei Wiederverbindungen die gelbe Charakterbeschriftung fehlen kann. Die Charakterzeile im Chat war zeitweise nicht zuverlässig lesbar; die Identitätsprüfung wird deshalb nicht gelockert.

Die frühere harte Ablehnung schon einer fehlenden Nach-Scroll-Minimap wurde durch begrenzte passive Wiederholungen und getrennte Gründe ersetzt. Die Radius-Schwankung der Kreiserkennung wird durch identische, unskalierte Bildschirmbereiche abgefangen. Die widersprüchliche Zwölf-Matches-Vorbedingung für eine Zehn-/Elf-Inlier-Bestätigung ist entfernt. Native Weltziffern und zusätzliche Footer-OCR überwanden den beobachteten Footer-Block bei Welt 455.

Diese Änderungen wurden mit Regressionen und einzelnen Live-Läufen geprüft; sie garantieren keine beliebige Clientgeometrie oder dauerhaft fehlerfreie Erkennung. F8/Pause, native Geometrie, frische Aufnahmen und bestätigte Bildwirkung bleiben verbindlich. Ein zugestelltes Windows-Ereignis allein belegt keine ausgeführte Spielaktion.

## Hinweise für weitere Logs

Vollständigen bereinigten Log mit `build_commit` und `build_run` aufbewahren. Zuerst `WORLD_VISUALLY_CONFIRMED` und `MINIMAP_ZOOM RESULT` prüfen, danach `INVENTORY_READ`, `NAVIGATION_LOCALIZATION...` und tatsächliche Fortschrittsbelege. Keine Zugangsdaten oder privaten Screenshots veröffentlichen. Die Versionsnummer 1.7.2 allein identifiziert keinen PR-Testbuild.
