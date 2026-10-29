# Übergabe: RuneLite, Profil auto, OSRS Play 1.8.35

## 1.8.35 – Geprüfter Umweg: letzte frische API-Position neu verplanen

Der neue **v1.8.34-Live-Log vom 29.10.2026, 18:02:02–18:02:50** zeigt echte
Fortbewegung (vier eingefügte Vordergrund-SendInput-Klickfolgen).
Der 4. Klick ging von `(3257,3245,0)` nach `(3259,3237,0)`.
Die RuneLite-API meldete stattdessen die Folge
`(3255,3244)` -> `(3254,3240)` -> `(3255,3239)`, jeweils
`TRANSITION_DETOUR_OBSERVED`, ohne den ursprünglichen Wegpunkt zu
erreichen. Die alte Automatik blieb daraufhin bis zum nachfolgenden
Benutzer-Fokuswechsel in `waiting_for_waypoint=true`, obwohl der Spieler
bereits in Richtung Lumbridge weitergelaufen war. Der beobachtete
`RUNELITE_FOREGROUND_LOST`-Stopp bleibt korrekt und darf keine
Hintergrundsteuerung oder automatische Fortsetzung auslösen.

**Änderung:** Bei mindestens drei echten, streng begrenzten
Übergangs-Umwegbeobachtungen NACH einem zuvor API-bestätigten Wegpunkt,
Fortschritt zum geklickten Wegpunkt UND zum endgültigen Ziel, ohne
neueres Exportereignis seit mindestens 1,6 Sekunden und höchstens drei
Sekunden alter letzter Position, darf höchstens eine beschränkte
`TRANSITION_DETOUR_QUIET_REPLAN`-Freigabe erfolgen. Diese Freigabe
setzt keine Bewegung oder Stillstand voraus, sondern plant den nächsten
Klick ausschließlich aus der letzten realen Plugin-Position und den
geprüften öffentlichen Kollisionsdaten. Der unmittelbar nächste Klick
umfasst höchstens sechs angrenzende Kollisionstiles statt neun.
Sämtliche vorhandenen Fenster-Vordergrund-, Maustasten- und
RuneLite-Profilprüfungen bleiben bindend. Sessionweite automatische
Replan-Freigaben steigen begrenzt von zwei auf **vier**; keine
Endlosschleife: 40 Klicks/5 Minuten und neun Sekunden
Positionsfortschritts-Timeout gelten weiterhin.

**Negativtests:** ohne früheren echten Wegpunkt, bei alter/falscher
Exporterzeit, vor 1,6 Sekunden Ruhe oder bei Bewegung vom eigentlichen
Ziel weg darf kein weiterer Klick ausgelöst werden. Der bestehende
Live-v1.8.34-Fall ist mit den exakten API-Tiles als Selbsttest hinterlegt.
Der reine Windows-CI-Build weist keine garantierte autonome Navigation
im laufenden Spiel nach. Ohne Exporter-Heartbeat sind absolute
Stillstandserkennung und exakte tatsächliche Minimap-Zoomskala nicht
verfügbar. Für die endgültige Live-Bestätigung erneut im Spiel testen.



## 1.8.34 – Live v1.8.33: erster vollständiger Zielradius-Erfolg und Maus-Eingabekonflikt

Der Nutzerlog vom **29.10.2026, 17:30:54–17:31:56** belegt eine
vollständige, API-verifizierte Ankunft im Lumbridge-Zielradius. Im ersten
Anlauf wurden zwei Vordergrund-SendInput-Klickfolgen erfolgreich in den
Windows-Eingabestrom eingefügt und als Bewegung beobachtet. Der dritte
Versuch wurde noch vor dem Klick wegen `PHYSICAL_MOUSE_BUTTON_HELD`
gestoppt (null Input-Events); das ist ein korrekter Benutzereingabe-Schutz,
kein Nachweis eines Defekts oder eines erfolglosen Game-Klicks.

Nach erneutem ausdrücklichem Start wurden **zehn** Klickfolgen eingefügt;
die RuneLite-API bestätigte den Weg fortlaufend, einschließlich eng
begrenzter Korridorbeobachtungen und zwei Quiet-Replans. Um 17:31:52
wurde `TARGET_REACHED` bei `(3226,3220,0)` gemeldet; die nächste
API-Position `(3226,3222,0)` blieb im Zielradius 4 um `(3222,3218)`.
Der Lauf beweist keine Zielpunkt-Exaktheit jenseits des gewählten Radius
und keine Hintergrundsteuerung.

**Korrektur 1.8.34:** Eine manuell bereits gedrückte Maustaste beendet
den Test weiterhin sofort ohne automatische Wiederaufnahme, erscheint
aber als verständliche Benutzerunterbrechung statt als Softwarefehler.
Ein zweiter Guard direkt vor der SendInput-Maustaste verhindert eine
Kollision, falls der Benutzer die Taste erst nach der Cursorbewegung
drückt. Der Diagnoseabschluss trennt nun angeforderte Klicks von
wirklich in den Windows-Eingabestrom eingefügten Klickfolgen und setzt
`pending_verification` bei nicht gesendeten Befehlen nicht fälschlich.
Selbsttests rekonstruieren die echte letzte Lumbridge-Korridorbewegung
und die beiden möglichen Maus-Unterbrechungszeitpunkte.

**Nicht behaupten:** Ohne Windows-Runner keine lokal bestätigte
Kompilierung und ohne neuen Nutzerlauf keine Live-Validierung von 1.8.34.

## 1.8.33 – Echter v1.8.32-Test: Bewegung klappt, früher Seitenstopp zweimal falsch

Das neu übermittelte **461-Zeilen-Log** bestätigt `v1.8.32`,
`build_channel=push`, `build_run=40770678261`, Auto-Updater
`ALREADY_CURRENT` und einen funktionierenden vorgemerkten
Start von `Varrock_Westbank`: Nach dem anfangs 306 Sekunden
alten Export gab es ein neues RuneLite-Tile, welches die
Kalibrierung automatisch öffnete. Vier vom Programm in den
Vordergrund gesendete Mausbefehle wurden anhand echter
Exporter-Events als Bewegung beobachtet. Der letzte Klick
ging ab `(3241,3263)` nach `(3241,3271)`; nach `(3240,3269)`
kam `(3239,3269)`. Obwohl nur ein Seitwärtsschritt und
weiterhin zwei Tiles zum Wegpunkt, trat `UNEXPECTED_API_TILE`
auf – der alte Korridor verlangte zwingend eine kleinere
Chebyshev-Distanz zum Wegpunkt und `axisConsistent=true`.

Ein folgender `Lumbridge_Hühner`-Lauf schickte zwei Klicks
und bestätigte erneut reale API-Bewegung, wurde dann mit
`RUNELITE_FOREGROUND_LOST` abgebrochen. Das bleibt ein
absichtlicher Fail-Closed-Fokuscheck; kein Hintergrund-
Spielinput behaupten oder bei anderem aktiven Fenster
weiterklicken.

Der `Grand_Exchange`-Lauf schickte zwei Klicks und meldete
echte Prefix-/Korridorbewegung. Direkt nach `(3238,3295)`
beobachtete die API `(3237,3296)` (Wegpunkt `(3239,3297)`),
ebenfalls ein letzter seitlicher Schritt bei Distanz 2,
und der gleiche `UNEXPECTED_API_TILE`-Stopp trat auf.

**1.8.33-Fix:** `RuneLiteAutoWalk.Observe` führt für exakt
diese späten API-Tile-Muster `NEAR_WAYPOINT_LATERAL_OBSERVED`
ein: höchstens ein Seitwärtsschritt pro Klick, nach mindestens
der Hälfte des exakt geprüften Wegpräfixes und mindestens
drei bestätigten Schritten, Tile-Jump=1, Distanz zum Ziel-
Waypoint vorher/nachher exakt 2, höchstens zwei Tiles zum
verifizierten Prefix, höchstens fünf Sekunden seit dem Klick
und höchstens zwei Sekunden zwischen Beobachtungen.
Dieser Schritt wird **NICHT** als Fortschritt/Ankunft gezählt;
es wird **KEIN** Spielinput gesendet. Falls RuneLite danach
den echten Wegpunkt bestätigt, läuft die reguläre Route
weiter. Sonst erlaubt die bereits vorhandene Wiederaufnahme
erst nach mindestens 1,6 Sekunden ruhig beobachteter Phase,
bei maximal drei Sekunden altem API-Tile und höchstens zwei
solchen Replans der gesamten Sitzung eine neue, durch die
Kollisionsdaten geprüfte Route. Das Ereignis ist separat als
`WAYPOINT_LATERAL_QUIET_REPLAN` protokolliert. Die echte
Ingame-Standphase ist mangels Exporter-Heartbeat nicht
unabhängig messbar; keine verlängerten Timeouts oder
Klicks aus alter Position. Tests rekonstruieren **beide**
echten v1.8.32-Stopps und prüfen anschließend sowohl die
spätere exakte Wegpunktbestätigung als auch Nebenbewegungen,
die zwingend abgewiesen werden müssen.

**Offen:** Kein im Spiel vollständig erreichtes Ziel
bewiesen; keine Hintergrundsteuerung mit `SendInput`;
Startkoordinaten weiterhin erst bei frischem Plugin-Ereignis.

## 1.8.32 – Beim Start verweigerter Button durch alte Pluginposition

Neuer 49-Zeilen-Live-Log 28.10.2026, 22:04:50–22:04:59:

1. Der Nutzer startete die automatisch installierte v1.8.31
   (`build_channel=push`, `build_run=40767855357`,
   `build_commit=77ca4b56b4d407a76f4da1ffb1934e7bc83efd9d`),
   der Start-Updater meldete `ALREADY_CURRENT`.
2. `RuneLiteApiPositionTracker` sah von Beginn an nur eine
   **1347 Sekunden alte** Datei mit `(3251,3229,0)`.
   `HISTORICAL_POSITION_PREVIEW` zeigte sie zutreffend als
   `UNBESTÄTIGT`; bis zum Ende des 49-Zeilen-Logs kam kein
   einziges späteres Tile-Event. Die Quelle ist korrekt
   als `API_WAITING_FOR_NEW_EVENT` blockiert.
3. Um 22:04:52 wurde `Lumbridge_Eichen` gewählt. Ein
   echter Buttonklick um 22:04:54 erreichte die Start-
   Logik, wurde dort jedoch als
   `START_REQUIRES_PLUGIN_POSITION` gesperrt, worauf der
   vollständige Log automatisch in die Zwischenablage ging.
   Der Button war also nicht defekt; die interne
   Vorbedingung wurde nur zu wenig handlungsleitend
   sichtbar. Die nächsten zehn Sekunden brachten keinen
   Post-Start-Positionsexport.

**1.8.32-Fix:** Neue `RuneLiteQueuedStart`-Prüfung und
UI-Wartemodus für Nutzerklicks bei fehlender oder über
60 Sekunden alter letzter Position. Der Button loggt
`START_BUTTON_CLICKED` und `START_QUEUED_AWAITING_PLUGIN`;
die GUI zeigt auffällig gelb `GEHVERSUCH VORGEMERKT` und
nennt den nötigen einmaligen manuellen Tilewechsel. Der
rote STOPP-Button bleibt aktiv. Erst ein gegenüber der
Vormerkung **neueres** Plugin-API-Tile mit höchstens drei
Sekunden Alter, aus derselben gebundenen RuneLite-
Fensteridentität und mit unverändertem Ziel, öffnet
automatisch die bisherige Minimap-Kalibrierung für eine
**neue ausdrückliche Freigabe**. Kein automatischer
Spielinput schon beim Vormerken; keine Änderung an den
kompletten Klickprüfungen oder am Minimap-Anker.
Aus Sicherheitsgründen beträgt die Wartefrist höchstens
zwei Minuten; danach `START_QUEUE_TIMEOUT` mit
Clipboard/roter GUI. Zielwechsel und Pause annullieren
ohne Spielinput. Fenster-/Titelwechsel verwirft die
Vormerkung und meldet den Kontextfehler; Schließen
räumt sie ebenfalls auf. `RuneLiteQueuedStartTests`
belegen die erlaubten und abgewiesenen Ereignisse.

**Wichtige Grenze:** Das installierte Plugin schreibt
weiterhin nur bei Bewegung ein neues Tile. Ob der Spieler
seit dem letzten 22 Minuten alten Snapshot an derselben
Position steht, ist **nicht** nachweisbar. Deshalb bleibt
ein neuer Pluginexport erforderlich, der beim bestehenden
Exporter durch einmalige manuelle Bewegung ausgelöst
werden muss. Der Fix macht den Startklick sinnvoll und
verhindert weiteres Klicken auf denselben Button; er
erfindet keine aktuellen Koordinaten und unterstützt keine
Hintergrund-Spielklicks.

## 1.8.31 – Tatsächlicher 1.8.30-Erfolg und zwei neue Stopper

Neuer 800-Zeilen-Live-Log vom 28.10.2026, 21:40:05–21:42:26:

1. `build_run=40764663189`, `version=1.8.30` und
   `UPDATER PREVIOUS_UPDATE` bestätigen den **wirklichen
   automatischen Upgradevorgang**. `ALREADY_CURRENT` bestätigt
   danach das Release-Manifest. Am Start lag lediglich ein
   **1496 Sekunden** alter `position.json`-Export vor.
   `HISTORICAL_POSITION_PREVIEW` zeigte `(3235,3182)` nur
   `UNBESTÄTIGT`; ein frisches Plugin-Tile kam erst nach
   manueller Bewegung. Kein Fehler der Start-UI, sondern
   fehlender Exporter-Heartbeat/On-Login-Snapshot.
2. Die Eingabeschutz-Option war dreimal aktiv. Beim zweiten
   Lauf werden `suppressed_physical_mouse_events=10`,
   `suppressed_physical_keyboard_events=0` und
   `USER_ESCAPE_EMERGENCY_STOP` mit `RELEASED` protokolliert.
   Das belegt auf dieser echten Windows-Maschine die
   Hookfreigabe und abgefangene Mausereignisse, nicht ein
   generell unüberwindbares OS-Level-Sicherheitsversprechen.
3. **Erster AutoWalk nach Lumbridge:** sieben aufeinander
   folgende `SendInput`-Klicks, viele echte Plugin-Meldungen,
   bestätigte Wegpunkte, dann zwei erfolgreiche
   `WAYPOINT_QUIET_REPLAN`-Auslöser (21:40:43 und 21:40:49)
   mit jeweils neuem Klick und weiterem Wegfortschritt.
   Um 21:40:54 stand der Spieler laut Plugin bei
   `(3227,3219)`, und der frische A*-Pfad enthielt nur noch
   `(3226,3218)` als letzten Schritt in Lumbridges
   Zielradius4 um `(3222,3218)`. Der allgemeine Klickprojektor
   verlangte mindestens sieben Pixel Radius, die diagonal
   benachbarte Tile liegt bei 4 px/Tile aber nur 5,7 Pixel
   vom weißen Minimap-Punkt. Der Fehler war daher
   `WAYPOINT_PROJECTION_UNAVAILABLE`, obwohl ein einziger
   erlaubter Schritt aus dem echten Kollision-Pfad übrig war.
   1.8.31 führt `ProjectFinalGoalStep` ein: nur bei **genau
   einer** direkten, transportfreien Kollision-Tile, die den
   gewählten Zielradius erreicht, darf der verifizierte
   4–6-Pixel-Klick gesendet werden. Kein Durchklicken über
   Ziel oder unbekannte Kollision hinaus, kein Erfolg ohne
   neues API-Event. Vollständige Zielankunft weiterhin offen.
4. **Zweiter Test Richtung Varrock:** zwei Klicks, erster
   Wegpunkt bestätigt, zweiter Folgeklick, dann eine längere
   Pause und bewusster `Esc`-Stopp. Kein neuer Fix für diesen
   absichtlich gestoppten Test, keine Simulation eines Fehlers.
5. **Dritter Test Richtung Lumbridge:** erster Wegpunkt
   `(3257,3227)` bestätigt, Klick 2 nach `(3249,3226)`
   mit Präfix `3256,3226;...;3249,3226` wurde gesendet.
   Das nächste Tile `(3258,3229)` war drei Kacheln vom
   Präfix entfernt und zunächst eine Kachel **weiter** vom
   Ziel; 1.8.30 stoppte `UNEXPECTED_API_TILE` sofort.
   Aber weitere echte Events nach dem Stop belegen, dass der
   schon gesendete Spielklick weiterlief: `(3257,3230)`,
   `(3253,3230)`, `(3251,3229)` – offensichtlich nimmt
   RuneLite einen Umweg, den die öffentliche A*-Kollision
   nicht exakt vorhersagte. 1.8.31 unterscheidet daher
   `TRANSITION_DETOUR_OBSERVED`: Nur nach einem zuvor per
   Plugin bestätigten Wegpunkt, für maximal fünf
   räumlich (bis vier Prefix-Tiles und höchstens acht vom
   Klickstart) sowie zeitlich (maximal vier Sekunden nach
   Klick) begrenzte Plugin-Events, ohne weitere Klicks.
   Keine Akzeptanz als Zielerfolg; höchstens ein späterer
   tatsächlich bestätigter Waypoint erlaubt neue Eingaben.
   9-Sekunden-Timeout bleibt unverändert. Falsche Ebene,
   Sprung >4, weit entfernter oder anhaltender Umweg stoppt.

Beide v1.8.31-Fixes sind mit positiven und negativen
synthetischen Regressionstests nach exakt diesen neuen
Spielkoordinaten verknüpft. Die bestehende Maximalzahl
von zwei vorzeitigen Replans und die strenge
`RUNELITE_FOREGROUND_LOST`-Sperre bleiben.

**Offen und ausdrücklich nicht behauptet:**
Echte Hintergrund-Autowalk-Klicks und aktualisierte
Weltkoordinaten beim reinen App-/Loginstart ohne neues
Plugin-Event. Diese erfordern eine zusätzliche geprüfte
RuneLite-Schnittstelle. Keine Spielspeicheranalyse,
keine Screenshot-basierte Positionsfreigabe.

## 1.8.30 – 1.8.29-Live-Beweis und Grenzen von Startup, Hooks und Hintergrund

Neuer 266-Zeilen-Nutzerlog vom 28.10.2026, 21:14:26–21:15:16:

1. Version 1.8.29 wurde **automatisch aktualisiert**:
   `UPDATER PREVIOUS_UPDATE=Letztes Update installiert und neue EXE gestartet`,
   neue Binary `build_run=40760899794`, Startup-Manifest
   `ALREADY_CURRENT remote_version=1.8.29`.
2. Nach Kalibrierung des weißen Punkts bei `(645,111)`:
   Klick von `(3222,3160)` zu `(3228,3164)`; echte Positionen
   `(3224,3160)`, `(3226,3162)`, `(3228,3164)` zeigen
   `CORRIDOR_PROGRESS_CONFIRMED` und `WAYPOINT_CONFIRMED`.
   Klick 2 zu `(3231,3171)` verläuft über `(3230,3168)` nach
   `(3229,3170)`, dann **keine weiteren neuen API-Tiles**.
   Nach ca. 5 Sekunden `RUNELITE_FOREGROUND_LOST`.
3. Zweite ausdrücklich gestartete Sitzung: `(3229,3170)`
   über `(3229,3172)` und `(3230,3174)` zu `(3233,3177)`:
   `WAYPOINT_CONFIRMED`, danach zweiter Klick zu
   `(3237,3183)`. Neue Positionen `(3233,3179)`,
   `(3234,3181)`, `(3235,3182)` – wiederum **zwei
   Kacheln vor Wegpunkt**, dann etwa fünf Sekunden ohne
   neues Exportevent. `COPY_REQUEST origin=MANUAL` ging dem
   `RUNELITE_FOREGROUND_LOST` voraus. Die zweite automatische
   Fehler-Log-Kopie trat separat korrekt auf.
4. Keine einzige `UNEXPECTED_API_TILE`-Abweichung; die
   1.8.29-Korridorlogik funktioniert unter diesen Echtbedingungen.

**1.8.30-Nächster Schritt:** `RecoverNearWaypointAfterQuietPeriod`
gibt einen begrenzten `WAYPOINT_QUIET_REPLAN` nur frei,
wenn der **zuletzt per Plugin bestätigte** Tile auf derselben
Ebene genau zwei Kacheln vor dem Wegpunkt liegt, mindestens
zwei belegte Zwischenbewegungen existieren, seit 1,6 Sekunden
kein neuer Tile einging, die bekannte Position maximal drei
Sekunden alt ist und seit dem Klick mindestens drei Sekunden
vergangen sind. Maximal zwei vorzeitige Kollision-Neuplanungen
pro Armierung, sonst `WAYPOINT_STALL_RECOVERY_LIMIT` mit
Clipboard/roter GUI. Jeder tatsächliche weitere Klick nutzt
vollständig neue F2P-Kollisionen, verifizierten RuneLite-
Vordergrund und nur die bisherige gezielte `SendInput`-Route.
Falls nach einer solchen Neuplanung innerhalb von drei Sekunden
kein sicherer Folgeklick abgesetzt werden kann, stoppt
`QUIET_REPLAN_EXPIRED` ebenfalls mit Clipboard/roter GUI.
Keine Behauptung über gesicherte Stillstandserkennung ohne
Plugin-Heartbeat. Synthetische Negativfälle sperren altes
Event, fehlenden Postclick-Fortschritt, zu kurze Ruhephase
und dritten Versuch.

**Startup:** Beim Start war die einzige gültige Plugin-Datei
ca. **1336 Sekunden alt**. `RuneLiteApiPositionTracker` hat
dies bisher korrekt nicht als Live-Koordinate angenommen.
Die neue `HistoricalPreview` zeigt deren letzte X/Y nur
mit explizitem **UNBESTÄTIGT**, ohne sie an `lastVerifiedTile`,
Route oder Klickfreigabe zu geben. Der Exporter sendet kein
regelmäßiges Position-Heartbeat und muss vom Hersteller
beim Login/Pluginstart einen neuen Export bekommen, um ohne
manuelles Bewegen eine belastbare aktuelle Tile zu zeigen.

**Physische Eingabesperre:** Neue optional anzuklickende
`RuneLiteInputShield`-Funktion nutzt native WH_MOUSE_LL und
WH_KEYBOARD_LL nur bei bestätigtem RuneLite im Vordergrund
und während der bewussten Autowalk-Armierung. Menschliche
Spielklicks und Tasten werden gefiltert, synthetische
SendInput-Ereignisse sowie Esc/Alt-Tab/Windows-Tasten bleiben
passierbar. Kein Windows-`BlockInput`, niemals ganzer Desktop,
bei Hookfehler kein Autowalk. `Esc` löst Stop und Hookfreigabe
aus. Keine Garantie, dass Windows Low-Level-Hooks dauerhaft
installiert hält. Muss real auf Windows getestet werden.
**Hintergrundsteuerung ist noch nicht implementierbar:**
Windows-SendInput adressiert den System-Eingabestrom;
erprobtes PostMessage bewirkte im RuneLite/AWT keine
bestätigte Bewegung. Der Plugin-Hub-Exporter ist read-only.
Eine echte Hintergrundlösung bräuchte eine zusätzliche
legitime, verifizierte Steuer-Schnittstelle; diese ist nicht
vorhanden. Fokus-Stopp bleibt unbedingt bestehen.

Quelle Microsoft: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput
und https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelmouseproc.
Jagex-Regeln: https://legal.jagex.com/de-de/docs/rules/rules-of-runescape.

## 1.8.29 – Weitere 1.8.28-Echtdaten: API überspringt mehrere Kacheln, zu früher Replan

Der Nutzer lud am 28.10.2026 ein 553-Zeilen-Log der laufenden
Windows-Version 1.8.28 (20:50:46 bis 20:52:15 Europe/Berlin) hoch.
In vier explizit vom Nutzer freigegebenen Läufen bestätigte
`AUTO_FIXED_LAYOUT_WHITE_DOT` wiederholt die RuneLite-Minimap-Mitte
`(645,111)` im 773×539-Client, und `ZOOM_RESET_INPUT_SENT` meldete
den Right-Click+Escape. Der tatsächliche Minimap-Zoom wird weiter
**nicht exportiert**, also ist nur die Eingabe, nicht der Zoomwert belegt.

- Lauf 1: `(3225,3161) -> (3227,3163) -> (3229,3165) ->
  (3230,3167)` nach Klick 1, `WAYPOINT_CONFIRMED`. Danach Klick 2
  zu `(3232,3175)`, erste neue API-Kachel `(3230,3169)` und
  falscher `UNEXPECTED_API_TILE`-Stopp. Danach lief RuneLite durch
  den bereits gesendeten Klick nachweislich weiter bis `(3232,3174)`.
- Lauf 2: Klick 1 aus `(3232,3174)` zum Wegpunkt `(3236,3181)`,
  danach `(3232,3176)` (exakter Prefix Index 2), dann
  `(3233,3178)`: **vier** Kacheln seit dem Start, mehr als eine
  Kachel seit dem letzten API-Ereignis. Das ist eine Richtung
  Wegpunkt fortschreitende, nahe Korridor-Abweichung, aber 1.8.28
  stoppte.
- Lauf 3: Ab `(3236,3181)` wurden drei Klicks erfolgreich
  gesendet. Wegpunkte `(3241,3187)` und beinahe `(3242,3194)`
  wurden per Plugin bestätigt; auf Klick 3 folgten `(3241,3197)`
  (Prefix 4) und `(3240,3198)`. Letzteres löste in 1.8.28
  `WAYPOINT_CORRIDOR_REPLAN` aus, **obwohl der vorige Klick
  noch weitergehen konnte**. Der neu gesendete Klick 4 wurde
  bereits bei `(3239,3200)` als off-path gestoppt.
- Lauf 4: `(3210,3157) -> (3212,3159) -> (3214,3161) ->
  (3216,3163)` und `WAYPOINT_CONFIRMED`; nach Klick 2 zum
  Wegpunkt `(3224,3164)` meldete der Exporter `(3220,3163)`:
  ein einmaliger **Vier-Kachel-Sprung** innerhalb der zu
  erwartenden Bewegungsrichtung, aber nicht auf dem fixen A*.
  Auch hier stoppte 1.8.28 zu früh. Danach `(3222,3163)` ohne
  weitere automatische Klicks: der vorherige Klick lief nach.

**1.8.29-Fix:** Der Pending-Klick bleibt bei bis zu vier
`CORRIDOR_PROGRESS_CONFIRMED`-API-Ereignissen aktiv, sofern
gleiche Ebene, maximal vier Kacheln zwischen Beobachtungen,
höchstens zwei Kacheln Abstand vom vorausliegenden
Kollisions-Prefix, gleichbleibende Achsen bei geradlinigem
Wegpunkt, **strikt abnehmender Chebyshev-Abstand** zum
geklickten Wegpunkt sowie 9-Sekunden-Timeout eingehalten
werden. Ein weiterer Spielklick wird dadurch **nicht**
freigegeben; erst der bestätigte oder nahe Wegpunkt, bzw. nach
fünf solchen echten Zwischenereignissen maximal zweimal
eine neue Kollision-Routenplanung vom letzten Plugin-Tile.
Der v1.8.28-Log enthält bei drei separaten gleichnamigen
`UNEXPECTED_API_TILE`-Stopps nur für den ersten Versuch
`CLIPBOARD COPY_REQUEST origin=AUTO_FAILURE` (der Nutzer
kopierte am Ende zusätzlich manuell). `ClientFailureLog`
unterdrückte nämlich Wiederholungen nach `source/code` selbst
über neue Nutzer-Kalibrierungen hinweg. `StopAutoWalk` gibt nun
für jeden eigenständig begonnenen Gehversuch genau einen
erneuten Voll-Log-Kopiervorgang frei, auch bei wiederholtem
Fehlercode. Dauerhaft wiederholte, nicht interaktiv
gestartete Fehler bleiben dedupliziert; Selbsttest ergänzt.
Sprung >4, Rückschritt, gleich bleibende/steigende Entfernung,
falsche Ebene, fremdes Vordergrundfenster, altes Event bleiben
harte Stopps. `CLICK_INTENT` loggt nun den **gesamten
vorhergesagten Klick-Prefix**, damit tatsächliche API-Kacheln
im nächsten Log exakt verglichen werden können. Regressions-
tests verwenden vier im Live-Log tatsächlich beobachtete
Klickmuster sowie Fälle mit falschen Schritten und Korridorlimit.
Keine Weltkacheln werden aus Bildern gelesen. Zielerreichung
noch offen, erneuter Ingame-Test nötig.

## 1.8.28 – Neue RuneLite-Live-Daten bestätigen weiße Minimap-Mitte und mehrere Spielklicks

Der Nutzer lud am 28.10.2026 ein neues OSRS-Play-1.8.27-Log mit
232 Zeilen (20:36:52–20:37:40 Europe/Berlin) hoch. Darin zwei
automatisch gestartete Gehversuche nach der neuen Kalibrierung:

1. 20:37:13: `MINIMAP_ANCHOR_CALIBRATED` meldet
   `AUTO_FIXED_LAYOUT_WHITE_DOT center_x=645 center_y=111
   client=773x539 white_dot_result=VISIBLE_NEAR_FIXED_CENTER`.
   `ZOOM_RESET_INPUT_SENT input_events=5` folgt. Ein echter
   `CLICK_INTENT` von (3242,3188) nach (3242,3195) mit Bildschirm-
   Klick (645,83) erzeugt einen bestätigten API-Fortschritt
   (3244,3192) mit `observed_path_index=4`. 0,8 s später kommt
   (3243,3193): **ein** benachbarter Schritt näher zum Wegpunkt,
   der nicht exakt auf dem gespeicherten Prefix liegt. 1.8.27
   meldete `UNEXPECTED_API_TILE` und stoppte korrekt unter der
   damals geltenden strikten Regel; kein weiterer Klick.
2. 20:37:31: Neuer Test von Plugin-Tile (3242,3195) mit Auto-Anker
   (645,111), `CLICK_INTENT` (629,87), bestätigte RuneLite-
   Positionen (3241,3197) → (3239,3200). Das ist ein
   `WAYPOINT_CONFIRMED`, gefolgt von automatischem Klick 2 zum
   Wegpunkt (3234,3206), Bildschirmposition (625,87).
   Neue API-Tiles (3237,3202) → (3235,3204) bestätigen den
   weiteren realen Fortschritt, ohne Off-Path. Um 20:37:38
   erkennt die Sicherheitslogik `RUNELITE_FOREGROUND_LOST`.
   Diese Abschaltung bleibt unverändert: Ein fremdes
   Vordergrundfenster darf niemals weitere Eingaben erhalten.

**1.8.28-Änderung:** `RuneLiteAutoWalk.Observe` erlaubt nach mindestens
zwei bestätigten Prefix-Schritten eine **einzelne** frisch vom
Plugin gelieferte benachbarte Kachel, die weder auf einem früheren
Prefix liegt noch Ebene oder Wegpunkt zurücksetzt, aber näher zum
aktuellen Wegpunkt führt und höchstens eine Kachel von den nächsten
drei geplanten Pfadkacheln entfernt ist. Statt sie blind zu
akzeptieren, löst `WAYPOINT_CORRIDOR_REPLAN` die alte `Pending`-
Freigabe und verlangt vor dem nächsten Klick den vollständigen,
frisch gerechneten Kollisionspfad ab dem neuen bestätigten Tile.
Maximal zwei solche Ausnahmen **pro bewaffneter Testsitzung**;
bei einer dritten Abweichung wieder `OFF_PATH` samt rotem
Fehler- und Clipboard-Log. Ein rückwärts führender, weiter
entfernter oder nicht benachbarter Schritt bleibt verboten.

Neue synthetische Regressionen reproduzieren exakt den ersten
realen Schritt (3244,3192) → (3243,3193) und prüfen die
Ablehnung einer seitlichen Frühabweichung, eines nicht näherrückenden
Schritts und der dritten Korridorausnahme. Alle Weltkacheln stammen
unverändert ausschließlich aus dem Plugin-Hub-Exporter. Die
Minimap-Geometrie und der Zoom-Reset aus 1.8.27 bleiben unverändert.
Erneuter echter Live-Test mit RuneLite im Vordergrund erforderlich;
ein Lauf bis Lumbridge wurde noch nicht gezeigt.

## 1.8.27 – rote Linien richtig interpretiert, sichtbaren weißen Spielerpunkt verifizieren

Der Nutzer meldete nach dem 1.8.26-Release erneut: Der gelbe Kreis
sitzt **nicht** auf dem Charakterpunkt. Eingereicht wurden das reale
Live-Log vom 28.10.2026, 20:18–20:19 Europe/Berlin (224 Zeilen),
und ein Screenshot des Kalibrierungsdialogs mit dem neuen falschen
gelben Ring. Die bisherige Dokumentation hat den ersten annotierten
Screenshot **falsch abgelesen**. Vergrößerung beider Originalbilder
liefert folgende eindeutige Zuordnung:

- Die **roten Linien schneiden sich bei (640,110)** im ursprünglich
  768 Pixel breiten, vollständig sichtbaren RuneLite-Screenshot.
  Nicht (665,137) – das war ein Analysefehler.
- Im aktuellen Screenshot mit dem 773×534-WGC-Client liegt der
  **kleine weiße Spielerpunkt nahe (645,111)**. Der 1.8.26-
  Kalibrierungsring bei (665,110), im Log nach manueller Korrektur
  (666,114), liegt etwa **20 Pixel rechts** vom weißen Spielerpunkt.
  Die Titelzeile ist in der bereitgestellten Anzeige bereits
  enthalten; ein zweiter Titelleistenabzug war unbegründet.
- Um 20:19:36 wurden `MINIMAP_ANCHOR_CALIBRATED center_x=666
  center_y=114`, `ZOOM_RESET_INPUT_SENT` (5 Eingaben), danach
  `FOREGROUND_INPUT_INSERTED` für Klick x690/y90 protokolliert.
  0,9 Sekunden später meldete der verifizierte Exporter
  (3240,3184,0) statt des erwarteten Nordost-Wegpunkts
  (3244,3190,0): korrekter Safety-Stopp `UNEXPECTED_API_TILE`.
  Der erste Export war zu diesem Zeitpunkt ca. 47 Sekunden alt;
  die Nachbeobachtung bestätigt keine erfolgreiche Route.

Der korrekte Fix verwendet beim bekannten klassischen Layout
`playerCenter=(clientWidth-128,111)` – für 768 Pixel also (640,111),
für den aktuellen 773-Pixel-Client (645,111). Zusätzlich muss die
aktuelle Minimap-Aufnahme um diesen fest berechneten Punkt **mindestens
zwei helle Pixel eines kleinen weißen Spielerindikators** enthalten.
Nur diese im Bildschirm sichtbare geometrische Validierung darf den
automatischen gelben Ring anzeigen. Falls die Dot-Prüfung scheitert,
wird die Automatik nicht freigegeben; manuelle Auswahl im bekannten
Layout ist nur innerhalb ±3 Pixel zum erwarteten Punkt **und mit
sichtbarem weißen Dot-Nachweis** gestattet. Ein anderer Kreismarker,
eine Login-Oberfläche oder grüne Landschaft darf nicht als Beweis
dienen. Synthetische Tests prüfen das, das falsche x666 und das
historische x679. Die echten Weltkacheln bleiben Plugin-exklusiv.

Der separierte RuneLite-Minimap-Zoom-Reset per geprüftem Rechtsklick
und Escape aus 1.8.26 bleibt. Kein 3D-Zoom. Der Exporter übermittelt
den Zoomwert weiterhin nicht; API-Zoom-Skala bleibt unbestätigt.
Neuer User-Ingame-Test erforderlich, vor allem die Position des Rings.

Die unten stehende 1.8.26-Aussage (665,137 als rote Kreuzung) ist
historisch **falsch und widerlegt**.

## 1.8.26 – Nutzerkorrektur: Spielerpunkt bleibt mittig, Minimap-Zoom statt Kamera

Reales 1.8.25-Log vom 28.10.2026, 19:59–20:00 Uhr:
`MINIMAP_MARKER_CALIBRATED ... auto_detector=... center_x=679
center_y=138` hatte **nicht den Charakter**, sondern ein anderes
kreisförmiges Symbol auf der Minimap gewählt. Der Nutzer zeigte im
Dialog den gelben Kreis auf dem falschen Marker und stellte klar:
Der Spielerpunkt ist **stets die feste Mitte der Minimap**. Sein vorher
annotierter Screenshot zeigt die roten Linien genau bei (665,137)
inklusive Windows-Titelleiste, in Clientkoordinaten (665,110).
Die manuellen x644–645/y109–113 und der Autokandidat x679/y138
weichen beide deutlich ab. Diese Ring-Erkennung war ein Fehler.

Der neue `RuneLiteMinimapMarker` liefert daher keine Farb-/Ringsuche
mehr, sondern im bekannten klassischen RuneLite-Layout mit Client-
Größe ca. 768–773 × 528–534 **immer die feste Position (665,110)**.
Er akzeptiert nur einen eng begrenzten Bereich um diese Fenstergröße;
bei anderen Layouts ist eine manuelle, visuell überprüfte Kalibrierung
erforderlich. Im bekannten Layout muss selbst eine manuelle Wahl
innerhalb 7 Pixel um die feste Mitte liegen. Neue synthetische Tests
belegen Unabhängigkeit von einem runden Map-Icon bei 679/138 und
blockieren die bisherigen Fehlzentren. Fotos werden nur vorübergehend
im RAM gezeigt; Weltkacheln kommen ausschließlich vom Plugin.

Die vom Nutzer verlangte **Minimap-** statt 3D-Kamera-
Zoomsteuerung nutzt die von RuneLite dokumentierte Interaktion:
Bei aktiviertem RuneLite-Minimap-Plugin-Zoom setzt ein **Rechtsklick
auf der Minimap die Skala auf den Standard 4 px/Tile** zurück.
Quelle: https://runelite.net/blog/show/2022-10-05-1.9.0-Release/;
API-Spezifikation `Client.getMinimapZoom()` dokumentiert ebenfalls
den Standardwert 4:
https://github.com/runelite/runelite/blob/master/runelite-api/src/main/java/net/runelite/api/Client.java.
Die neue `ResetRuneLiteMinimapZoomAsync` setzt im bestätigten
RuneLite-Vordergrund nach Prüfung von Fenster, PID, Trefferfenster,
Mausknöpfen und Cursor einen einzelnen `SendInput`-Rechtsklick auf
einen sicheren Minimap-Punkt abseits des Spielermarkers, danach
`Escape` zur Schließung eines eventuellen Kontextmenüs. **Keine
3D-Kamera-Wheel-Ereignisse mehr.** Der GUI-Maßstab ist fest 4.
Weil der lokale OSRS Toolkit Exporter den tatsächlichen
`getMinimapZoom()`-Wert nicht übermittelt, ist nur die ausgeführte
Reset-Interaktion, nicht die erreichte numerische Skala beweisbar.
Fehler blockieren vor dem ersten Bewegungsklick und lösen das
vollständige Zwischenablagen-Log mit roter Warnung aus.

Erwartete neue Logs: `MINIMAP_ANCHOR_CALIBRATED` mit
`AUTO_FIXED_LAYOUT`, `RUNELITE_MINIMAP ZOOM_RESET_REQUEST`,
`RUNELITE_MINIMAP ZOOM_RESET_INPUT_SENT`, danach `ARMED` und bei
wirklicher RuneLite-API-Bewegung `PROGRESS_CONFIRMED`.
Ein Live-Test unter dem Nutzerprofil `auto` ist nach Release nötig.

Die folgende 1.8.25-Sektion ist historisch: ihre Ring-Deutung und
der 3D-Zoom-Plan sind durch den Live-Befund widerlegt.

## 1.8.25 – SendInput live bestätigt; Spielerpunkt und Kamera automatisch vorbereiten

Echtes Nutzerlog von 1.8.24 am 28.10.2026,
19:38:04–19:39:20 Europe/Berlin (322 Zeilen), plus
annotierter Screenshot aus RuneLite. Der seit 1.8.24
aktivierte echte Win32-SendInput funktioniert **live**:
Vier `FOREGROUND_INPUT_INSERTED`-Kommandos führten zu
mehreren neuen API-Kacheln auf dem geplanten Kollisionsweg.
Bei der zweiten Kalibrierung um 19:38:46 wurde von
3220/3198 bis 3226/3196 gegangen; für den Wegpunkt
3227/3196 folgte ein `WAYPOINT_CONFIRMED`, danach
startete sogar automatisch ein zweiter Klick.
Das Plugin und der Input-Transport arbeiten damit
erstmals **zusammen**. Das ist noch kein bestätigter
vollständiger Lauf bis Lumbridge.

Die zwei verbliebenen Stop-Ursachen sind unabhängig:

1. Im Fenster wurde `API_POSITION_UPDATED` auch für
   die erneute Abfrage derselben Datei mit `new_event=false`
   bis zum Ablauf der Dreisekundenschwelle gemeldet.
   `Observe` erhielt diese alte Zeitmarke erneut und
   wertete den gleichen bereits bestätigten Path-Index
   als `OFF_PATH` (19:38:24 und 19:39:17). 1.8.25 ruft
   `Observe` nur noch bei **tatsächlich neuer** API-
   Ereigniszeit auf. Wiederholte bestätigte Kacheln
   bleiben selbst bei neuer Zeitmarke ohne Fehlstopp;
   echte rückwärts gerichtete Schritte werden geblockt.
2. Nach einem bestätigten Path-Prefix bis 3230/3194
   meldete das echte Plugin 3234/3194, also nur **eine**
   Kachel neben dem angeklickten Waypoint 3233/3194.
   Die Verifikation blockte dies bislang streng als
   fremden Pfad. Jetzt erlaubt sie nur nach ausreichend
   zuvor verifizierten Path-Tiles eine einmalige
   `WAYPOINT_NEAR_CONFIRMED`-Endpunkttoleranz von genau
   einer Kachel, nur bei gleicher Ebene; anschließend
   ist eine **neue** Kollisionsroute erforderlich.
   Ein echtes frühes Off-Path bleibt ein Stopp.

Der Nutzer hat einen markierten Screenshot bereitgestellt
(nicht ins Repo übernommen). Die roten Linien kreuzen
den sichtbaren runden Spieler-Minimap-Marker etwa bei
Bildpunkt 666/137 inklusive Fenster-Titelleiste;
das entspricht etwa Client-x666/y111. Die bisher
manuell angenommenen Zentren x643–645 lagen ca. 20px
zu weit links. Ein lokaler reproduzierbarer Bildtest
ergab **einen** eindeutigen Treffer: 16 von 16 dunklen
Ringpunkten um einen hellen 15px-Marker, im begrenzten
oberen rechten Minimap-Feld. `RuneLiteMinimapMarker`
sucht diesen Ring ausschließlich in einem aktuellen
WGC-Clientbild im RAM. Mehrere gleichartige Marker,
fehlende Evidenz oder eine andere Fenstergeometrie
führen zum weiterhin verfügbaren manuellen Kalibrieren;
ein automatisch gesetzter gelber Kreis muss vor Start
vom Nutzer im Dialog **geprüft und bestätigt** werden.
Screenshots/Pixel/Accounts werden nicht gespeichert,
die reale Weltkachel kommt weiterhin nur aus dem Plugin.

Neu ist daneben ein vom Nutzer gewähltes 3D-Kamera-
Zoom-Preset `Far`, `Medium`, `Near`, `Unchanged`. Nach
der ausdrücklichen Mausfreigabe bringt der Client
RuneLite in den Vordergrund, richtet den Mauszeiger
auf den verifizierten Welt-Viewport und sendet eine
begrenzte Serie echter Windows-Mausradereignisse.
Zuerst wird Richtung äußerem Zoomlimit gefahren,
danach optional eine feste Anzahl Schritte nach innen.
Die Einstellung ist **relativ und abhängig von den
RuneLite-Camera-Plugin-Optionen**, denn der vorhandene
OSRS Toolkit Exporter enthält keinen `getScale()`-
Kamerawert. `ZOOM_INPUT_SENT` bestätigt deshalb nur
die Windows-Eingaben und nie einen genauen Kamera-
Zoomwert. Der **separate Minimap-Zoom** bleibt
im Test auf RuneLites API-Standard **4 Pixel/Tile**;
der Nutzer bestätigt weiterhin Nordausrichtung und
Standard-Minimap-Zoom. Die offiziellen RuneLite-
APIs besitzen `getMinimapZoom`, aber der installierte
Exporter übermittelt diese Information nicht. Bei
anderer Skalierung darf die Projektion nicht als
verifiziert gelten.

Windows-CI prüft synthetische Ring-Erkennung,
Ambiguitätsablehnung, Export-Repeat-Filter,
ein Tile Toleranz am Endpunkt und echte Off-Path-
Sperren. Erster Live-Test dieser Änderungen offen.

## 1.8.24 – Erster echter RuneLite-Mausklick statt PostMessage

Das Nutzerlog von OSRS Play 1.8.23 am 28.10.2026,
19:23:01–19:24:18 Europe/Berlin (268 Zeilen), belegt
erfolgreiche Updateinstallation, direktes Plugin-Tile
3214/3197/0, gültige A*-Routen nach Lumbridge und
**drei `CLICK_INTENT`/`WINDOW_MESSAGES_POSTED`**.
Klick 1 um 19:23:34 an 677/106 (Wegpunkt 3222/3198)
führte nach 9 s zu `POSITION_EVENT_TIMEOUT`;
Klick 2 um 19:23:56 an 676/108 auf denselben Wegpunkt
erhielt 5 s später ein neues API-Tile 3215/3197/0,
das **nicht** auf dem gesendeten Route-Prefix lag.
Es wurde korrekt als `UNEXPECTED_API_TILE` geblockt;
das ist kein Beweis, dass der vorherige Klick wirkte.
Ein dritter Klick um 19:24:15 an 677/111 für Wegpunkt
3223/3197 war beim manuellen Kopieren des Logs erst
3,3 Sekunden alt; daraus lässt sich kein Ausgang
ableiten. Beide vollständigen Fehlerlogs wurden
automatisch kopiert und die rote Warnung ausgelöst.

Der nachgewiesene Unterschied: Windows `PostMessage`
akzeptiert das Einreihen von Fensternachrichten,
belegt aber nicht die Annahme durch RuneLites Java/AWT-
Mausverarbeitung. Der bisherige Weg meldete diese
Nachrichten zutreffend nur als unbestätigt. **1.8.24
wechselt ausschließlich beim manuell freigegebenen
Autowalk-Test zu echtem Windows-`SendInput` im RuneLite-
Vordergrund.** Der Kalibrierungsdialog fordert neben
Nord-/Zoom-Bestätigung jetzt eine separate Einwilligung
zur sichtbaren Maussteuerung. Beim ersten Klick darf
die App RuneLite in den Vordergrund holen; jeder weitere
Klick verlangt, dass RuneLite bereits im Vordergrund
bleibt. Vor DOWN/UP werden die geprüfte RuneLite-HWND,
unveränderte Clientgröße, Fensterzuordnung des konkreten
Bildschirmpixels, virtuelle Multi-Monitor-Koordinaten,
physisch nicht gedrückte Maustasten und unverändert
aktiver Benutzerauftrag kontrolliert. Ein verweigerter
Fokus, verdecktes Ziel, falsche Cursorposition oder
anderes Programm stoppen ohne neuen Spielklick mit
vollständigem Clipboard-Log und rotem GUI-Fehler.

`FOREGROUND_INPUT_INSERTED` ist **immer noch keine
bestätigte Bewegung**. Erst `PROGRESS_CONFIRMED`/
`WAYPOINT_CONFIRMED` nach einem späteren RuneLite-API-
Tile beweist fortgesetzte Routenbewegung. Die alte
Erstklick-Ausnahme <=60s nur binnen 12s nach Kalibrierung,
alle späteren <=3s-Events, 9s Timeout, 40 Klicks/5 min,
Kollisionspfad und Transportverbot bleiben. Diese
Eingabevariante ist vor dem nächsten Nutzer-Live-Test
**nicht bestätigt**. Test: RuneLite aus dem Jagex Launcher
mit Profil `auto` eingeloggt, einmal Tile bewegen,
Lumbridge wählen, minimapkalibrieren und Einwilligung
ankreuzen. Danach RuneLite im Vordergrund lassen,
Maus nicht selbst bedienen. Auf `FOREGROUND_INPUT_INSERTED`
und nachfolgende bestätigte API-Bewegung achten. Bei
`FOREGROUND_CLICK_NOT_SENT` liefert `reason=` die genaue
fehlgeschlagene Sicherheitsvorbedingung.

## 1.8.23 – Automatik-Startblockade aus echtem 1.8.22-Lauf beheben

Echtes Nutzerlog 28.10.2026, 19:07:19–19:09:22 Uhr
Europe/Berlin, OSRS Play 1.8.22 mit automatischem
Updater `PREVIOUS_UPDATE` und `ALREADY_CURRENT`.
Am Start lag ein 1.538 Sekunden alter Export einer
früheren RuneLite-Sitzung vor. Deshalb war die
erste Nutzeranfrage um 19:07:30 korrekt gesperrt
(`START_REQUIRES_PLUGIN_POSITION`, Clipboard kopiert).
Um 19:07:34 folgte ein echtes API-Ereignis.
Weitere manuelle Schritte um 19:08:21 und 19:08:39
wurden sauber übernommen; Lumbridge A* war stets bereit.

Eine Minimap-Aufnahme/Kalibrierung startete um 19:07:36,
wurde um 19:08:51 bestätigt (`ARMED`, Zentrum
645/112, 4,0 px/Tile). Eine zweite Kalibrierung
von 19:09:03 bis 19:09:12 gelang ebenfalls.
**Es gab null `CLICK_INTENT` und null Inputversuche.**
Die Ursache ist exakt rekonstruierbar: Bei `ARMED`
war das letzte angenommene Positionsereignis 12 bzw.
33 Sekunden alt. Der `CanDispatch`-Filter erlaubte
aber selbst nach bewusst kalibrierter, aktueller
Minimap nur `event_age <= 3s`. Der unveränderte
Hub-Exporter sendet bei Stillstand keine Heartbeats.
Der Nutzer stoppte den ersten Lauf nach 12 Sekunden,
der zweite war nach zehn Sekunden noch aktiv, als
das Log kopiert wurde. Kein beweisbarer Klick-Transport-
Defekt, denn noch kein Klick wurde ausprobiert.

1.8.23 führt `CalibratedFirstClickAllowed` ein:
Nur **ein** anfänglicher Wegpunkt darf binnen zwölf
Sekunden nach dem bestätigten Kalibrierungsdialog
von einem höchstens 60 Sekunden alten, aber bereits
in **derselben laufenden Programmsitzung** akzeptierten
API-Ereignis gestartet werden. Ohne so ein
Sessionereignis gibt es keine Koordinate und keinen
Klick. Die bereits validierte HWND- und Bildgeometrie,
kurzer kollisionsgeprüfter Wegpunkt, Transportverbot,
Timeout, Stopp und Fehler-Clipboard bleiben. Nach
dem ersten Klick darf eine weitere Eingabe nur nach
einem neuen höchstens drei Sekunden alten API-Ereignis
ausgelöst werden. Regressionsprüfungen decken 33s-
Last-Known-Start, 61s-Ablehnung, abgelaufene 12s-
Kalibrierung sowie das fortbestehende Verbot weiterer
Altevent-Klicks ab. Der echte Klick/RuneLite-Java-
Inputtransport ist weiterhin ungetestet. Ein neuer
`CLICK_DEFERRED`-Eintrag mit Eventalter und Warte-
bedingungen macht künftige ausbleibende Eingaben
im Log ohne Rückschluss aus Eventlücken nachvollziehbar.

## 1.8.22 – Erster expliziter RuneLite-Bewegungstest

Der reale Test von 1.8.21 am 28.10.2026, 18:40:52–18:41:43
Europe/Berlin belegt `PREVIOUS_UPDATE`, `ALREADY_CURRENT`,
13 neue API-Positionen (davon zwölf Bewegungen), 14
`ROUTE_RECALCULATED` und 14 `PROGRESS`-Einträge.
Lumbridge wurde von 72 auf 59 Restschritte angenähert
und auf 68 Schritte wieder verlassen; zwei Meldungen
`ON_PREVIOUS_ROUTE`, zehn `OFF_PREVIOUS_ROUTE`,
zwei `BASELINE`. Eine diagonale Bewegung
`dx=1,dy=-1` wurde korrekt als benachbart erkannt.
Keine Positionsfehler und keine Screenshots/OCR für
Weltkoordinaten. Der Nutzer möchte nun zum ersten Mal
automatische Bewegung direkt im Spiel testen.

Der neue `RuneLiteAutoWalk` wird nur durch den GUI-Knopf
„Automatisch gehen (Test)“ aktiv. Vorher muss ein aktueller
RuneLite-WGC-Frame gezeigt und dessen Minimap-Spielerpunkt
durch Nutzerklick bestimmt werden; Norden oben, Zoom
ganz heraus und Pixel-pro-Tile-Wert werden **manuell**
bestätigt. Diese Projektion ist vor dem Live-Test nur
eine kalibrierbare Annahme. Der WorldTile kommt weiterhin
streng aus dem offiziellen Plugin Hub. Aus einer validierten
Kollisionsroute werden nur kurze, untransportierte, gleich-
ebene, benachbarte Wegpunkte in höchstens 34 Pixel Abstand
um den kalibrierten Minimap-Mittelpunkt gewählt. Eingaben
gehen als überprüfte HWND-Klicknachrichten an das eindeutig
identifizierte RuneLite-Fenster; RuneLite-Java-Akzeptanz
dieser Nachrichten ist **noch nicht live bestätigt**.

`CLICK_INTENT` ist kein Erfolg. Nach dem Versand wird
ein frischer API-Tile auf dem vorher gewählten Pfad in
aufsteigender Wegreihenfolge verlangt; erst nach
`WAYPOINT_CONFIRMED` darf der nächste Klick folgen.
`OFF_PATH`, mehr als neun Sekunden ohne API-Fortschritt,
ausgefallene/ungültige Exportdaten, veränderte HWND/
Clientgröße, Pause, Stop, Zielwechsel oder Routenfehler
stoppen zukünftige Klicks. 40 Klicks/fünf Minuten sind
die Obergrenzen. Alle echten Blockaden rufen
`ClientFailureLog.ReportFailure` auf und kopieren das
vollständige lokale Sitzungslog. Der letzte bereits
übermittelte Spielweg kann trotz STOPP noch auslaufen;
Nutzer kann im Spiel manuell gegensteuern.

Windows-CI prüft die GUI, reine Minimap-Projektion,
Kollisionsweg-Grenzen, alte Eventzeitstempel,
API-Wegbestätigung und Timeouts. Der erste echte
Run mit Profil `auto` ist ausdrücklich offen.

## 1.8.21 – Live-A*-Navigation bestätigt, diagonale Logklassifikation repariert

Das zweite reale 1.8.20-Protokoll vom 28.10.2026,
18:20:50–18:21:22 Uhr Europe/Berlin bestätigt
42 API-Proben, zehn neue Kachelereignisse,
zehn `RUNELITE_ROUTE ROUTE_RECALCULATED`-Einträge
**nach** Auswahl des Ziels Lumbridge. Die
kollisionsbasierte Routenlänge sank beim manuellen
Gehen von 92 auf 73 Schritte. 72 Regionen und
421 typisierte Transporte wurden ohne Map-Referenzbilder
geladen; keine neuen Positions-/Routingfehler,
keine OCR oder Grafikaufnahme zur Positionsfreigabe.
Die alleinige rote Meldung betrifft nach wie vor
die ausdrücklich nicht implementierte Spielautomatik.

Beim Prüfen fiel ein gezielter Logfehler auf:
`dx=1,dy=-1` zwischen benachbarten Diagonal-Tiles
wurde als `non_adjacent_observation=true`
markiert, weil die bisherige Formel Manhattan-Distanz
statt Chebyshev-Abstand benutzte. 1.8.21 repariert
das, ergänzt `chebyshev_tiles` und ersetzt
`capture_gap_ms` (kein Screenshot mehr im
RuneLite-Companion) durch korrektes `event_gap_ms`.
Negative Tests für echte Mehrfeld-Sprünge bleiben.
Der Nutzer-Live-Test von 1.8.21 steht noch aus.

Zusätzlich erhält der Companion einen rein lesenden
`RuneLiteRouteProgress`-Zustand pro ausgewähltem Ziel.
Die Fortschrittsanzeige benutzt ausschließlich aktuelle
API-Ereignisse und bereits aus gepinnten Kollisionen
berechnete A*-Routen; die vorherige Weg-Kachelmenge
wird vor jeder Neuberechnung mit der **jetzt**
gemeldeten Spielerposition verglichen. Ein Aufruf
meldet `BASELINE`, `ON_PREVIOUS_ROUTE`,
`OFF_PREVIOUS_ROUTE`, `ARRIVED` oder `LEFT_GOAL`,
`step_delta` misst die Änderung der **verbleibenden
Routenschritte** und ist nicht die Zahl tatsächlich
gelaufener Kacheln. Der Zielwechsel, die Pausierung
und unzuverlässige API-/Kartendaten setzen den
Fortschritt zurück. Die GUI zeigt die Route und die
neue fünfte Routenverlauf-Zeile.

Live-Test nach Auto-Update: RuneLite-Profil `auto`,
Ziel Lumbridge auswählen, erst einige Kacheln auf der
vorgeschlagenen Route gehen, dann absichtlich davon
abweichen und bei Bedarf zurückkehren. Die
`RUNELITE_ROUTE PROGRESS`-Einträge müssen
die vorherige Wegmenge und die Restschritte trennen.
Kein Spielinput, kein automatischer Bot-Lauf.

## 1.8.20 – RuneLite-Eventstream statt OCR und erste Live-Kollisionsroute

Die 1.8.19-Sitzung vom 28.10.2026, 17:56–17:58 Uhr
bestätigt Version/Updater, 44 direkte API-Positionen, aber
89 wegen Eventalter falsch als nicht aktuell behandelte
`position.json`-Lesungen, 91 Vierfach-OCR und
46 `GRID_INFO_NOT_READABLE`-Positionen. Es wurden nur
zwei Pixelabdruck-Referenzen angelernt; die neue Methode
löste den Kernfehler nicht. Ursache laut offiziellem
Plugin-Quellcode: `onGameTick` exportiert Positionen
nur bei einem Kachelwechsel (`pos.equals(lastWritten)`).

Ab 1.8.20 benutzt die **normale RuneLite-GUI** ausschließlich
`RuneLiteApiPositionSource`, auch `--position-probe` hat
keinen Screenshotzugriff mehr. Die alte OCR-Bibliothek kann
für isolierte Regressionen verbleiben, ist aber im Companion
kein Positions-Gate. Der Event-Tracker setzt sich bei
Fenster-/Titelwechsel, Pause, Profilverlust oder ungültigen
Zeitstempeln zurück. Beim Start wird ein altes Event aus
einer möglichen früheren Sitzung nicht als live behandelt,
sondern ein echter Kachelwechsel verlangt. Ein einmal
empfangenes Ereignis darf innerhalb der Dateisicherheitsgrenze (24 h) als
**letzter bekannter API-Stand** gezeigt werden, nie als
bestätigter Heartbeat. Bewegungslog enthält nur neue
API-Ereignisse, keine künstlichen wiederholten Frames.

An die Zielauswahl wird `WorldMapData.LoadRoutingOnly` +
`FindPath` angebunden: nur öffentliches, an konkrete
Commits gebundenes Kollisions-/Transportmaterial, keine
Kartenbilder. Die GUI berechnet im Hintergrund pro neuem
Kachelereignis einen Weg und zeigt die nächste
begehbare Kachel/Restschritte/Zielradius. Kein Spielinput.
Test: über den Jagex Launcher RuneLite Profil `auto`
öffnen, Toolkit Exporter aktiv lassen, OSRS Play starten,
ggf. ein Feld gehen für frisches Event, Ziel auswählen,
mehrere Schritte manuell gehen, Restlänge und
`RUNELITE_ROUTE ROUTE_RECALCULATED` prüfen.

**Noch extern offen:** Ein verlässlicher Heartbeat zur
Unterscheidung zwischen Stillstand/Plugin-Ausfall oder
Logout erfordert ein Upgrade des Drittanbieter-Plugins.
Ein Client-Update allein kann diese Information nicht
erzeugen. Der echte 1.8.20-Live-Test steht noch aus.

## 1.8.18 bestätigt – stationäre Positions-Pixel neu sichern (1.8.19)

Die echte Sitzung vom 28.10.2026, 17:33–17:34 Uhr Europe/Berlin
belegt automatisches Update auf 1.8.18, den erfolgreichen
v1.8.18-Manifestcheck, 40 bestätigte Positionen, 31 direkte
API-Auswertungen, sechs korrekt abgefangene vorzeitige
Grafikframes und lokale Summary-Scans mit acht vorhandenen,
null verworfenen, einer fehlenden JSON-Kategorie. Lumbridge
als Read-only-Ziel wurde ausgewählt und protokolliert.

Neue Blockade: Das Plugin schreibt `position.json` nur bei
Tilewechseln. `3176/3198/0` war in Proben 45/46/47
direkt über die API bestätigt, aber nach drei Sekunden
fehlen im selben Bild die unabhängig gelernten Glyphen
`6` und `7` (`CLIENT_EXPORT_PLUS_SCREEN_PIXELS full_matches=0`).
Für `3202/3193/0` gab es Proben 72/73 als direkte
API-Bestätigung; später enthält das OCR-X/Y `32Ø2` und
liefert nur IDs ohne eine X/Y-Zahlenzeile. Es folgten
21 verfehlte Proben und berechtigte Fehler-Clipboard-Kopien.

1.8.19: `RuneLiteGridFingerprint` arbeitet ausschließlich
auf aktuellen WGC-Bildern, rastert die 20 numerischen Zellen
des World-Location-Overlays und prüft echte Kommas und
Trennräume. Es speichert nur SHA-256-Identitäten **im RAM**.
Erst zwei zeitlich getrennte, vorher bereits zweifach
API-bestätigte Frames desselben Ortes autorisieren einen
referenzierten Rasterabdruck. Derselbe alte JSON-Ort darf
nur mit einer neuen *exakt identischen* aktuellen Raster-
Signatur und erneutem normalen Zweibildbeweis bestätigt
werden. Änderungen der X/Y-Ziffern (auch im selben Chunk),
Ebene, Kommas, HWND, Fenstergeometrie oder neuem API-Ort
sperren. Keine Rohpixel, Fingerprintwerte oder Accountdaten
im Log oder Datenträger. Synthetische Positiv- und
Negativtests werden hinzugefügt; Windows-/Live-Test ausstehend.

## Live-Bestätigung von 1.8.17 und nächster lesender Ausbau (1.8.18)

Der Benutzer hat am 28.10.2026 den offiziellen `OSRS Toolkit Exporter`
aus dem Plugin Hub in RuneLite installiert und **alle** Exportarten
aktiviert. Das echte Log 17:12:48–17:13:29 Uhr bestätigt
`version=1.8.17`, den automatischen Download/Neustart mit
`PREVIOUS_UPDATE` und `ALREADY_CURRENT`, 36 direkte
`DIRECT_CLIENT_API`-Lesungen, 30 bestätigte Positionen von 52
und zehn erkannte Bewegungen. Bei älteren Exportdateien wurden
fünf Positionen erneut mit `OCR_CORROBORATED` und Pixelvergleich
bestätigt. Vier `GRID_INFO_NOT_READABLE`-Proben traten nur unmittelbar
bei Bewegung auf. Der Export-Dateizeitstempel war teils neuer als
das zuletzt erhaltene WGC-Bild. Das ist eine normale asynchrone
Reihenfolge, kein Grund, die Position zu raten oder den OCR-Fallback
auf einem vorherigen Bild zu erzwingen.

1.8.18 behandelt diesen Fall als `WAITING_FOR_EXPORT_FRAME`, bis
das nächste unabhängige Bild **nach** dem JSON-Export entstanden
ist. Die Zweibildfreigabe bleibt zwingend. Der RuneLite-Companion
erhält eine rein lesende Navigationsempfehlung: ausgewähltes Ziel
aus `NavigationDestinations.All`, bestätigtes X/Y, Ebenenvergleich,
Chebyshev-Luftlinie und Zielradius. Keine tatsächliche Route,
keine Kollisionsevaluierung auf jedem Tile, kein Input an den
Spielclient. Zusätzlich liest ein formatbegrenzter, pfadgebundener,
lokaler Adapter die neun offiziellen Zusatzexporte alle zehn Sekunden.
Gezeigt werden ausschließlich Zähler/Gesamtlevel, keine
Gegenstand-IDs, Namen, Mengen, Preise, Bank-Rohdaten oder Accountnamen
in GUI-Logs/Clipboard. Alte Exporte werden als ältere Snapshots
kenntlich gemacht, nicht als Livezustand verwendet. Tests simulieren
fremde Profile, beschädigte Daten, falsche Zähler, historische
Bankstände und Ebenenkonflikte. .NET-10-Windows-CI steht noch aus.

## Client-API über bereits geprüften Plugin Hub gefunden (1.8.17)

Auf ausdrücklichen Wunsch des Benutzers wurde die Praxis anderer
OSRS-Bots vor einer weiteren Korrektur geprüft. Microbot und OSBot
verwenden clientinterne Positionsobjekte statt Screenshot-OCR;
Rund um Instanzen müssen Welt- und lokale Koordinaten sauber
unterschieden werden. Ein modifizierter Microbot/RuneLite-Client
kommt für den Benutzer nicht infrage; der offizielle Jagex Launcher
und RuneLite-Profil `auto` sind unveränderliche Voraussetzungen.

**Wichtiger konkreter Fund:** Der reguläre RuneLite Plugin Hub hat
einen bereits gelisteten `position-exporter`-Eintrag, aktuell ohne
`disabled=`-Marker. Repository: `Gorsokk/position-exporter`,
geprüfter Commit `8e2799abb23836ea84700a6bfbba00742295845b`.
`OSRS Toolkit Exporter` lauscht auf `GameTick`, nutzt
`client.getLocalPlayer().getWorldLocation()` und schreibt bei
Kacheländerung über RuneLites `Filepath`-API atomar eine lokale
`.runelite/plugin-data/position-exporter/<character>/position.json`.
Felder: `exported_at`, `player_name`, `world`, `plane`, `x`, `y`,
`region_id`, `region_x`, `region_y`. Laut Quellcode kein Netzwerk.
Das alternative geprüfte `HTTP Server`-Plugin bietet nur `/stats`,
`/inv`, `/equip`, keine Position; `statussocket` ist derzeit mit
`disabled=true` im offiziellen Plugin-Hub-Marker markiert.

OSRS Play 1.8.17 erkennt optional genau diesen Export ohne Änderung
des offiziellen Clients. Der komplette RuneLite-Fenstertitel muss
dem JSON-Charakternamen **exakt** entsprechen; mehrdeutige/alte,
beschädigte oder rechnerisch inkonsistente Dateien dürfen nicht
blind eine Position freigeben. Nur 3 Sekunden alte Positionen
werden direkt aus der API gelesen, stets mit weiterhin notwendiger
WGC-Zweibildbestätigung. Bei älteren Dateien (stationärer Spieler)
braucht der Bildleser drei zum Export passende aktuelle X/Y- und
Region-OCR-Stimmen, kein widersprechendes vollständiges ID-Feld,
sichtbare getrennte Kommas, echte Pixelziffern und Plane plus zwei
frische Frames. Nur in diesem nun extern eindeutig benannten Fall
darf ein bisher unbeschrifteter Einzelglyph benannt und nach zwei
bestätigten Frames in die Ephemeral-Historie übernommen werden.
Das reguläre OCR-Verfahren und seine strengeren Verbote bleiben
für nicht installierte/unverfügbare Plugins unangetastet.

**Einrichtung:** Benutzer muss in regulärem RuneLite `OSRS Toolkit
Exporter` aus dem Plugin Hub installieren und `Export position`
aktivieren; die anderen standardmäßig aktiven GE-/Inventar-/
Bank-/Charakterdatenexporte aus Privatsphäregründen ausschalten.
World Location Grid Info bleibt für alten Datei-/Ausfall-Fallback
sichtbar. Kein selbst geladenes oder ungeprüftes Plugin, kein
Entwicklungsclient, kein Speicherzugriff und keine Spielautomation.
Der 1.8.17-Live-Test sowie der Windows-CI-Lauf stehen aus.

## 1.8.16-Live-Bestätigung und verbleibender OCR-Ausfall

Echtes Benutzerprotokoll vom 28.10.2026, 16:27–16:28 Uhr Europe/Berlin
bestätigt das auto-installierte Release `1.8.16`, `PREVIOUS_UPDATE`,
`ALREADY_CURRENT` und `clipboard=COPIED`. Sieben Proben nutzen
`SINGLE_CHUNK_CROSSCHECK_VERIFIED` aktiv, acht ID-Pixelprüfungen
haben `PIXEL_GRID_VERIFIED`; 13 Positionen wurden unabhängig
zweifach bestätigt. Nach Probe 25 fehlten jedoch zeitweise alle
numerisch lesbaren Chunk-IDs; später bei circa 3155/3186/0
hatte `FIELD_QUORUM xy_votes=4 chunk_votes=2 region_votes=4`
und dennoch `unknown_singleton_rejects=6`, `full_matches=0`:
Ziffer 6 kommt in den aktuellen IDs 807310/12593 nicht vor.
Ab Probe 25 werden keine neuen Positionen freigegeben;
`GRID_INFO_NOT_READABLE` und `POSITION_NOT_CONFIRMED_TIMEOUT`
kopieren nachweislich die vollständige Logdatei. Neue Belege
machen die Forderung nach einer strukturierten Datenquelle statt
immer weiterer ungerechtfertigter OCR-Zeichenersetzung deutlich.

## 1.8.15 live: starke X/Y-Mehrheit sperrt den Ein-Chunk-Sonderweg (1.8.16)

Das Benutzerprotokoll vom 27.10.2026, 23:11–23:12 Uhr (Europe/Berlin)
bestätigt die automatisch installierte Version `1.8.15` mit
`PREVIOUS_UPDATE`, `ALREADY_CURRENT`, Push-Build `40592198060`
und `clipboard=COPIED`. Die Bewegungschronik bestätigte u. a.
3158/3190, 3158/3187, 3157/3197, 3158/3201, 3153/3193
und 3159/3199, jeweils nach der unabhängigen Zweibildprüfung.
Weder der Updatepfad noch der Kopierpfad müssen erneut geraten werden.

Die in 1.8.15 ergänzte `SINGLE_CHUNK_CROSSCHECK_VERIFIED`-Route
trat im echten Protokoll nicht auf: Bei Probe 35, 37 und 38 sieht
`nearest_2x` als einzige OCR-Variante die Chunk-ID `805263`.
`nearest_3x`, `bicubic_4x` und `contrast_4x` lesen das **gleiche**
numerische X/Y `3151/3196`; zwei bis vier volle X/Y-Lesungen
führten zum Log `ID_ONLY_QUORUM NOT_READY xy_readings=3/4
chunk_readings=1 single_chunk_assisted=false`. Vergleichbare
Ereignisse gab es bei Probe 7, 10, 11, 26, 28 und 44.

1.8.16 erweitert **nur die Ein-Chunk-Ausnahme**: Beliebig viele
vollständige X/Y-Zahlenpaare aus den vorhandenen vier OCR-Pässen
sind zulässig, falls alle **denselben** Wert haben, mindestens
eine vollständige Lesung nicht aus dem Chunk-Pass stammt und drei
Region-Stimmen übereinstimmen. Widersprechende X/Y-Werte oder
mehr als eine abweichende Chunk-ID sperren. Die Checksummen von
Chunk/Region/X/Y, der Vergleich zur einzigen echten 64-Kandidaten-
Pixelposition, echte Ziffernvorlagen samt sichtbaren Trennzeichen
und Ebene und die zweite frische Aufnahme bleiben Pflicht.
Neu im Log: `xy_distinct_values`, `cross_pass_xy_votes`,
`id_xy_votes`. Negative Tests simulieren widersprechende Werte,
fehlende OCR-Unabhängigkeit, zu wenige Regionsstimmen, falsche
Chunk-ID und fehlendes echtes Komma. Der Windows-CI-Test ist
noch ausstehend; keine Spielsteuerung.

Unabhängig davon gab es bei Proben 41/42 eine echte zweite Grenze:
`FIELD_QUORUM CHECKSUM_VERIFIED xy_votes=4 chunk_votes=2
region_votes=4`, aber `RUNELITE_PIXEL unknown_singleton_rejects=6
full_matches=0`. Ohne eindeutig benannte oder historisch zweifach
bestätigte Ziffernglyphe wird auch ein korrekt gelesenes X/Y
**nicht** freigegeben. Das erklärt den roten
`GRID_INFO_NOT_READABLE`-Fehler und die automatische Logkopie,
ist jedoch **kein** Grund, unbenannte Pixelziffern zu erraten.

## Chunk nur in einem OCR-Pass vorhanden; Positionsprüfung blockiert (1.8.15)

Das echte 1.8.14-Live-Protokoll vom 27.10.2026 um 22:54–22:55 Uhr
(Europe/Berlin) bestätigt `version=1.8.14`, `PREVIOUS_UPDATE`,
`ALREADY_CURRENT`, korrekte Windows-Push-Build-Identität
`40590145230` und `clipboard=COPIED` – auch nach erneutem Fehler.
Die neue Bewegungschronik zeigt `FIRST_CONFIRMED` in Probe 8
bei 3157/3197 und danach zwölf `CHANGED`-Ereignisse mit
`dx`, `dy`, `plane_delta`, `sample_gap`, `capture_gap_ms` und
`route_between_samples_unknown=true`. Die beobachteten
Zeit-/Wegdifferenzen widersprechen keinem zweifach bestätigten
Tile, und es werden keine unbekannten Zwischenpositionen erfunden.
**1.8.14-Motion-Tracing wurde live bestätigt.**

Neue OCR-Lücke in Probe 69–75 (Bewegung nach ungefähr 3163/3200):
`nearest_2x` liefert `Chunk ID 809360`, aber ein unvollständiges
Tile-Feld. `nearest_3x` erkennt `Tile3163» 3200» o`, aber nur
`Chunk ID 80360` oder beschädigte Region. Die beiden 4×-Varianten
lesen `Chunk ID 8Ø936ø` bzw. `8B36ø`, während `Region ID 12594`
in mindestens drei Varianten eindeutig erscheint. Resultat:
`FIELD_QUORUM NOT_READY chunk_readings=1`,
`ID_ONLY_QUORUM NOT_READY chunk_quorums=0`,
`attempted=false`; nach vier Sekunden rote Warnung und
automatische Logdateikopie. Die nötige Chunk-ID kann nicht aus
einer unvollständigen Zeile per `Ø`-Ersatz gewonnen werden.

1.8.15 erweitert die bestehende strikt pixelabhängige Ausweichroute
auf genau **eine** vollständige Chunk-ID, wenn gleichzeitig
**genau ein** vollständiges X/Y-Zahlenpaar aus einem anderen
OCR-Aufbereitungspass und **mindestens drei** gleiche Region-IDs
vorliegen. Chunk- und Region-Checksummen müssen zum X/Y-Paar
passen; ein weiteres konkurrierendes Chunk-Feld sperrt. Nur dann
werden bis zu 64 mögliche Pixelpositionen geprüft. Wie zuvor
erfordern diese echte eindeutig benannte Ziffern, zwei Kommas,
sichtbare Ebenenziffer, eine einzige vollständig passende Position,
keinen Widerspruch zum numerischen X/Y-Paar und die zweite
unabhängige Aufnahme. Der Logbeleg nennt
`stage=ID_ONLY_QUORUM result=SINGLE_CHUNK_CROSSCHECK_VERIFIED`
mit `chunk_votes=1`, `region_votes>=3`, `xy_readings=1` und
`single_chunk_assisted=true`. Synthetische Negativtests sperren
falschen Chunk-Checksum, gleiche Chunk/XY-Quelle, zu wenige
Region-Stimmen, konfliktreiche Chunkwerte und fehlende Kommapixel.
Ein echter 1.8.15-Live-Test steht noch aus; keine Spielaktionen.

## Positionswechsel live bestätigt; Bewegungstimeline hinzugefügt (1.8.14)

Das Live-Protokoll vom 27.10.2026, 22:40–22:41 Uhr Europe/Berlin
bestätigt `PREVIOUS_UPDATE`, `version=1.8.13`, Push-Build
`40588083146`, `ALREADY_CURRENT` und `clipboard=COPIED`.
Die neue Einzel-OCR-Route wurde genau in Probe **42** erfolgreich
benutzt: `FIELD_QUORUM NOT_READY xy_readings=1`, danach
`ID_ONLY_QUORUM IDS_VERIFIED chunk_votes=2 region_votes=3
xy_readings=1`, 64 Pixelkandidaten, vier vollständige Layoutmatches,
genau eine Position und `single_xy_matches_pixels=true`.
Der anschließend freigegebene Ort **3142/3202/0** wurde bereits
in Probe 41 unabhängig bestätigt. **1.8.13 funktioniert live.**
53 von 66 Feldquoren melden `CHECKSUM_VERIFIED`. Viele
Positionen wurden beim manuellen Gehen bestätigt, darunter
3148/3206, 3145/3208, 3141/3210, 3142/3209 bis 3142/3202,
3146/3198 und 3159/3204. Kurzzeitige Nichtlesbarkeit oder das
Warten auf eine zweite Aufnahme löste sich wieder; kein neuer
Positions-Timeout. Die einzige gemeldete Fehlfunktion bleibt die
unimplementierte Automation.

1.8.14 baut deshalb **keine** weiteren OCR-Vermutungen ein, sondern
die nächste sicherheitsrelevante Diagnose für spätere Bewegung:
`RuneLiteMotionTrace` akzeptiert nur bereits in zwei getrennten
Frames bestätigte `WorldTile`-Positionen mit monotoner Zeit und
Probennummer. Bei der ersten Position bzw. jeder Änderung protokolliert
`RUNELITE_MOTION OBSERVATION` vollständige Koordinaten,
Zeit-/Probenabstand, Richtung, Manhattan-Kacheldistanz, Ebene und
`non_adjacent_observation`. Ein größerer Abstand ist **nur eine
Beobachtungslücke**, kein Beweis für eine abgelaufene Wegstrecke,
eine Teleportation oder einen berechenbaren Navigationspfad.
Stabile Frames zählen ohne laute Duplikatereignisse; Gesundheitslogs
zeigen Zähler. Pause, Prozess-/Fenster-/Aufnahmefehler löschen die
Bewegungssitzung, nicht lesbare Einzelbilder erzeugen keine neue
Position. Zusätzlich unterscheidet die Rasterdiagnose jetzt das
nur bei Einzel-X/Y anwendbare `single_xy_matches_pixels` korrekt
von `not_applicable` und zählt bei der echten Einzelstimme
`ocr_agreements=1` statt irrtümlich null.
Synthetische Tests prüfen diese Details; **1.8.14 selbst ist noch
nicht live erprobt**, autonome Spielaktionen weiterhin deaktiviert.

## Live-Erfolg mit unlesbarem X und eine fehlende Einzelstimme (1.8.13)

Das echte 1.8.12-Live-Protokoll vom 27.10.2026, 22:24–22:25 Uhr
(Europe/Berlin) bestätigt `PREVIOUS_UPDATE` und `ALREADY_CURRENT`
bei Version 1.8.12, Push-Build `40585149835`. Die neue
`ID_ONLY_QUORUM`-Route wird mehrfach `IDS_VERIFIED`: In Probe 13
erreichen zwei Chunk- und vier Region-ID-Lesungen die Checksummen;
alle X/Y-OCR-Texte enthalten aber `Tile315Ø`. Die unabhängige
Pixelprüfung meldet bei 64 Kandidaten `full_matches=4`,
`distinct_positions=1`, `PIXEL_GRID_VERIFIED`. Probe 14 ist der
getrennte Zweibildbeleg für **3150/3193/0**; Proben 15–42
bestätigen diese Position weiter. Nach manueller Bewegung werden
auch **3157/3199**, **3153/3200** und **3148/3206** bestätigt.
**Der 1.8.12-Ausweichweg funktioniert nachweislich live.**

Neuer Block in Probe 12: `nearest_3x` liefert allein das vollständige
numerische Paar `3150/3193`, aber alle anderen Varianten beschädigen
die Null. Obwohl beide IDs mindestens zwei Stimmen haben, lehnt
`FIELD_QUORUM` mangels zweiter X/Y-Stimme ab und
`ID_ONLY_QUORUM` lehnt wegen `xy_readings=1` ab. Kein Pixeltest.
1.8.13 weitet ausschließlich diesen Sonderfall aus: Höchstens eine
vollständige numerische X/Y-Lesung darf zusammen mit beiden
ID-Quoren den 64-Zellen-Pixelweg eröffnen, wenn die einzelne
X/Y-Lesung schon mit den ID-Checksummen vereinbar ist. Der strenge
ID-Pixelpfad bleibt unverändert (jede Ziffer aus aktuellen/zweimal
bestätigten ID-Glyphen, zwei reale Kommas, Ebene, ein eindeutiger Ort).
Ein einzelner vollständiger OCR-Beleg mit anderem Chunk oder
Region-ID darf die Prüfung nicht umgehen. Außerdem wird das eine
OCR-Zahlenpaar gegen den tatsächlich gefundenen eindeutigen Pixelort
verglichen und bei Differenz gesperrt (`single_xy_matches_pixels=false`).
Die Zweibildbestätigung bleibt Pflicht. Synthetische Tests prüfen
Übereinstimmung, abweichendes X/Y im selben Chunk, abweichende IDs,
X/Y außerhalb des Chunks und zwei Zahlenpaare. **Ein echter
1.8.13-Live-Test fehlt**, automatische RuneLite-Aktionen fehlen auch.

## Letzter OCR-Block: beschädigte X/Y-Ziffer in allen Varianten (1.8.12)

Das echte 1.8.11-Live-Protokoll vom 27.10.2026 um 22:02
(Europe/Berlin) bestätigt `version=1.8.11`, Push-Build
`40582676507`, automatische Installation, aktuelle Manifest-
und Release-API-Version 1.8.11. `region_ignored_suffixes=3`
trat u. a. in Probe 51–53 auf, die beide ID-Prüfsummen,
`PIXEL_GRID_VERIFIED` (`full_matches=4`, `distinct_positions=1`)
und ab Probe 52 die echte Freigabe **3145/3196/Ebene 0**
erreichten. Weitere bestätigte Bewegungspositionen waren
3142/3201, 3142/3205, 3145/3208, 3146/3210, 3145/3199
und 3146/3195. **Die 1.8.11-Feldsuffixänderung funktioniert live.**

Bei anderem Bewegungsgang stehen hingegen mehrfach `Tile315Ø`
oder `Tile314Ø` in ALLEN vier Windows-OCR-Varianten: X ist also
nie vollständig numerisch. Selbst wenn beide Chunk- und Region-IDs
mehrfach konsistent gelesen werden, bleibt `FIELD_QUORUM NOT_READY`
mit `xy_readings=0`; die nachgelagerte Pixelprüfung wird gar nicht
erreicht. Blindes `Ø`→`0` bleibt verboten.

1.8.12 fügt einen eng begrenzten und separat protokollierten
`ID_ONLY_QUORUM`-Ausweichweg ein: Er ist nur bei **null** vollständigen
numerischen X/Y-OCR-Lesungen, mindestens zwei erkannten `Tile`-Labels,
einem jeweils eindeutigen und mindestens zweifach bestätigten
Chunk-/Region-ID-Wert sowie der mathematischen Konsistenz beider
IDs anwendbar. Dadurch sind nur noch **64 mögliche Felder** innerhalb
dieses Chunks denkbar. Die tatsächliche X/Y-Position wird dann aus
realen Pixelrasterformen der Chunk-/Region-ID desselben Bildes
und gegebenenfalls aus Glyphen von mindestens zwei früheren
vollständig bestätigten Aufnahmen bestimmt. Anders als bei der
OCR-begleiteten Rasterprüfung sind **keine unbekannten,
nur wiederholten Glyphen** erlaubt: Jede der acht X/Y-Ziffern muss
einen unabhängig bezeichneten Pixelbeleg besitzen. Dasselbe
Bild muss weiterhin echte Kommas und eine trainierte Ebenenziffer
zeigen; ausschließlich **eine** eindeutige Rasterposition darf
übrigbleiben, danach ist eine zweite frische Aufnahme zwingend.
Der sonstige OCR-Konfliktveto und alle früheren Sperren bleiben.

Offline-Fixtures erproben explizit `Tile315Ø` mit wahrer
Position 3150/3196, mutiertes Komma, fehlenden ID-Quorum,
vorhandenes numerisches Gegenpaar, unmögliche Chunk-/Region-Zuordnung
und ein für Pixel nicht belegbares unbekanntes Singleton.
Die Diagnose nennt `stage=ID_ONLY_QUORUM`, `candidate_tiles=64`
und `evidence_source=ID_ONLY_PIXEL_ENUMERATION`.
**Noch kein echter 1.8.12-Live-Test und keinerlei automatische
Spielaktion.**

## Neue OCR-Zeilenverschmelzung durch andere HUD-Texte (1.8.11)

Das neue 1.8.10-Live-Protokoll vom 27.10.2026, 21:45–21:46
(Europe/Berlin) zeigt den erfolgreich automatisch installierten
`build_channel=push`-Build `40580390362` mit Version 1.8.10.
Die Manifest- und Release-API melden 1.8.10/`ALREADY_CURRENT`.
**Keine** `RUNELITE_PIXEL`-Zeile ist mehr nach 480 Zeichen abgeschnitten:
Beispielsweise Probe 1 enthält nun `full_matches=0`,
`distinct_positions=0`, `elapsed_ms=41`, `result=NOT_RELEASED`.
Unabhängig bestätigte Positionen bei manueller Bewegung waren
3151/3198, 3153/3197, 3158/3202, 3155/3198 und 3144/3195,
jeweils Ebene 0. Einmalig sichtbare unbekannte Startglyphen und
unlesbare `315Ø` statt `3150` bleiben vorsichtig gesperrt.

**Neues konkretes Problem bei Probe 43/44:** Zusätzliches
RuneLite-Spiel-/Tooltip-OCR `Pot (GE: 12 gp)` liegt in derselben
OCR-Zeile wie die Grid-Info-Felder. Es entstanden u. a.
`Region ID 12593 12`, `Region ID 12593 Pot (GE: 12 gp)`
und später `Chunk ID 805263 12`. Die bestehenden
`^Region ID (\\d{5})$`-/`^Chunk ID (\\d{6})$`-Feldregexe
verwerfen solche Zeilen vollständig; obwohl das X/Y-Feld und
die unveränderte echte ID mehrfach stimmen, ergibt
`FIELD_QUORUM result=NOT_READY` mit `region_readings=0` oder
`chunk_readings=0`. Das ist ein Verlust von Prüfmaterial,
**kein Nachweis einer falschen Position**.

1.8.11 erlaubt deshalb **ausschließlich im Feldquorum**, nach genau
sechs Chunk- bzw. fünf Region-Ziffern einen durch Whitespace klar
abgesetzten fremden OCR-Suffix. Direkt nachfolgende zusätzliche Ziffern
(`1259312` statt `12593 12`) bleiben ungültig. Die beiden ID-Felder
müssen weiter je mindestens zwei passende OCR-Stimmen haben,
rechnerisch zu X/Y passen und gegen reale Glyphen, Kommas und
Ebene derselben Aufnahme sowie eine zweite unabhängige Aufnahme
bestätigt werden. Konkurrierende Quoren oder fehlende Pixelsicht
blockieren weiterhin. Die neue Diagnose protokolliert
`chunk_ignored_suffixes`/`region_ignored_suffixes`, und synthetische
Regressionen reproduzieren Tooltip-Suffixe, prüfsummengeprüften
Rasterbeweis, ungetrennte Extraziffern und widersprüchliche IDs.
**Noch kein echter 1.8.11-Live-Test.**

## Live-Bewegung bestätigt; Pixel-Diagnose nach 480 Zeichen gekappt (1.8.10)

Das am 27.10.2026 um 21:33 (Europe/Berlin) gelieferte echte
1.8.9-Protokoll bestätigt die **automatische Installation** anhand
`PREVIOUS_UPDATE`, `version=1.8.9`, `build_channel=push`,
Build `40578229326`; Manifest und GitHub-Release-API liefern beide
1.8.9 und `ALREADY_CURRENT`. Der reguläre RuneLite-Prozess mit Profil
`auto` liefert frische opake 773×534-Frames. Im Kaltstart bei
3139/3216/0 wurde die unverifizierte Einzelziffer `6` ohne bestätigte
historische Vorlagen weiterhin abgelehnt – korrekt fail-closed.

Nach manueller Bewegung wurden mehrere tatsächlich verschiedene Orte
aus zwei unabhängigen Aufnahmen freigegeben: 3143/3212/0 ab Probe 10,
3145/3206 ab 19, 3141/3213 ab 27, **3147/3208 ab 32** (zuvor
wegen einmalig sichtbarer `7` gesperrt), 3152/3204 ab 37,
3157/3202 ab 42, 3157/3198 ab 53, 3157/3192 ab 68 und
3148/3199 ab 74. In Pixelproben 17–20, 30–35 u. a. zeigt
`historical_glyph_matches=4` oder `8` und null historische
Widersprüche. Der sitzungsinterne Vorlagenspeicher erreichte 24
unterschiedliche bestätigte Frames mit 96 begrenzten Vorlagensätzen.
Die Log-Kopie wurde bei der vorgesehenen
`AUTOMATION_NOT_IMPLEMENTED`-Sperre automatisch angefordert und
später manuell erneut ausgelöst; `clipboard=COPIED` wurde bestätigt.
Die tatsächliche RuneLite-Spielautomatik bleibt ausstehend.

**Neu entdeckt:** In 58 vom Benutzer kopierten `RUNELITE_PIXEL`-Zeilen
steht `...[gekürzt]`. Ursache ist `Diagnostics.Add`:
`Sanitize(details)` verwendet das 480-Zeichen-Standardlimit,
BEVOR die vollständige Datei und deren Zwischenablagekopie erzeugt
werden. So fehlen gerade `full_matches`, `distinct_positions`,
`elapsed_ms` und `result` am Ende der detailreichen Pixelproben;
das pauschale Versprechen „komplette Logdatei“ war inhaltlich
damit eingeschränkt, obwohl technisch die ganze *Datei* kopiert wurde.
1.8.10 verwendet für Detailtexte bis zu 16.384 Zeichen und einen
expliziten Marker mit ausgelassener Zeichenzahl bei außergewöhnlich
langen Einträgen. Vertrauliche Muster werden vor dem Schreiben
weiter bereinigt; Quellen/Ereignisnamen behalten enge Grenzen.
Ein Windows-Selbsttest verlangt einen über 480 Zeichen langen
`RUNELITE_PIXEL`-Eintrag samt Endwerten im *tatsächlich kopierten*
Dateiinhalt und prüft die Großereignis-Grenze. **Live-Freigabe
1.8.10 noch nicht bestätigt.**

## Erstmals stabil bestätigte Bewegung und fehlende Einzelziffer (1.8.9)

Das reale 1.8.8-Live-Protokoll vom 27.10.2026, 21:12 (Europe/Berlin)
zeigt `FIELD_QUORUM result=CHECKSUM_VERIFIED` mit vier Stimmen für X/Y,
zwei für Chunk und drei für Region; ein abweichendes OCR-Feld wurde
verworfen. Die unabhängige Pixelprüfung ergab mehrfach
`PIXEL_GRID_VERIFIED`; ab Probe 2 bis 11 wurde die Position
**3144/3212/Ebene 0** wiederholt als `OVERLAY_CONFIRMED` anerkannt.
Bei Bewegung über 3142/3213 war noch eine Pixelprobe erfolgreich;
bei **3139/3216** scheiterten Probe 32–34 an einem einzelnen nicht
trainierten Pixelzeichen. Die Region `12594` und Chunk `803218`
enthalten keine Ziffer `6`, obwohl Y=3216 diese genau einmal enthält.
Das Ergebnis war korrekt `unknown_singleton_rejects=4`; eine
unbewiesene Einzelziffer bleibt gesperrt. Die GitHub-Release-API
erkannte trotz vom Raw-CDN noch als 1.8.7 ausgeliefertem Manifest
bereits die lokal aktuelle Version 1.8.8. `clipboard=COPIED` und
roter GUI-Hinweis blieben funktionsfähig.

1.8.9 fügt daher ausschließlich eine **sitzungsinterne Sammlung
bereits in Chunk-/Region-IDs nachgewiesener Rasterziffern** hinzu.
Ein Eintrag wird erst nach vollständiger Pixelverifikation UND
der bisherigen Zweibildbestätigung übernommen. Eine historisch
bekannte, jetzt nur einmal sichtbare Ziffer darf erst nach
**mindestens zwei verschiedenen bestätigten Aufnahmetimestamps**
verwendet werden. Mindestens fünf mit der gegenwärtigen ID
gemeinsame Ziffern müssen im Pixelraster passen; Konflikte gegen
gegenwärtige oder historische andere Ziffern blockieren. Die
Zwischenspeicherung lebt nur im Prozess, höchstens fünf Minuten,
maximal 96 Vorlagensätze und wird bei anderem Fenster, anderer
Framegröße, Capture-/Prozessfehler oder explizitem Reset gelöscht.
Screenshots, Koordinaten oder Kontoinformationen werden nicht
persistiert. Neue `RUNELITE_PIXEL`-Diagnosen protokollieren
`historical_glyph_matches`, `historical_contradictions`,
`trusted_history_frames` sowie `stage=GLYPH_HISTORY` nach
erfolgreicher Bestätigung. Die synthetische Regression deckt
den Bewegungswechsel 3144/3212 → 3139/3216 sowie Einzelbild-
und beschädigte-Glyphe-Sperren ab. **Echter 1.8.9-Live-Test offen;
kein Eingabe- oder Navigationscode freigeschaltet.**

## Komplementäre OCR-Feldfehler bei lebender Positionsverschiebung (1.8.8)

Das vom Benutzer gelieferte 1.8.7-Live-Log vom 27.10., 20:53–20:54
(Europe/Berlin), bestätigt den erfolgreich installierten 1.8.7-Push-Build.
Sowohl Start- als auch manuelle Updateprüfung ermittelten in der Raw-Datei
und beim offiziellen GitHub-Release-Endpunkt konsistent Version 1.8.7.
Die automatische Log-Zwischenablage wurde bei der gesperrten Positionslesung
ausgelöst und `clipboard=COPIED` im Gesundheitslog bestätigt.

Positionsaufnahme: Die OCR-Varianten identifizierten u. a. `Tile3145`/`3210`
und `Chunk 805265`, aber die Region laut Nearest-2× `12594`, laut
Nearest-3× `12534`. Hochskaliertes Bicubic las die korrekte Region
`12594`, aber Y als `321Ø` und Chunk als `8Ø5265`. Daher gab es in den
meisten Proben nur eine oder null vollständige `Observation`s. In Probe 17
lieferte die neuere, wiederholungsbasierte Pixelprüfung tatsächlich
`PIXEL_GRID_VERIFIED`, `unknown_tile_digits=4`,
`repeated_unknown_layouts=4`, `distinct_positions=1`, aber am nächsten
Frame verwarf die bisherige Zwei-Gesamt-OCR-Beobachtungs-Regel die
Position erneut. **Kein stabiler neuer Positionserfolg behauptet.**

1.8.8 ergänzt einen weiteren rein lesenden Vorfilter: Jede der vier
Bildaufbereitungen liefert ihre exakt erkannten numerischen Tile-X/Y-Paare,
Chunk-IDs und Region-IDs getrennt. X/Y müssen gemeinsam in wenigstens
zwei Pässen vorkommen, jede ID ebenfalls mindestens zweimal. Es darf pro
Feld **nur ein einziger Quorumwert** existieren. Die rechnerischen Chunk-
und Region-IDs müssen zu den beiden Tile-Koordinaten passen. Ein anderweitig
vollständig gültiger, mathematisch anderer OCR-Positionsbeleg widerspricht
dem Quorum und stoppt die Freigabe. Erst danach prüft der Bitmap-Leser
wie zuvor tatsächlich sichtbare zwei Kommas, wiederholte unbekannte
Tile-Glyphen, trainierte Ebenenziffer und die unveränderte Zwei-Frame-
Bestätigung. Kein `Ø`→`0`, `o`→`0`, `»`→`,` oder Verwerfen einer gültigen
Gegenposition. Jede Probe protokolliert getrennt `stage=FIELD_QUORUM`
mit Stimmen, Ausreißern, Checksumme bzw. Verweigerung sowie die bisherige
Rasterdiagnose mit `evidence_source=FIELD_QUORUM` oder `FULL_OBSERVATIONS`.
Synthetische Regressionen verwenden die im Live-Log vorkommende Kombination
aus jeweils unvollständigen, aber pro Feld zweifach bestätigten OCR-Werten;
konkurrierende Quoren und falsche Checksummen bleiben gesperrt.
**Noch keine 1.8.8-Live-Bestätigung, weiterhin keine RuneLite-Automatik.**

## Erste Bewegung, veränderte ID-Ziffern und weiterhin veraltetes Manifest (1.8.7)

Das echte 1.8.6-Log vom 27.10. um 20:28–20:29 (Europe/Berlin) lief mit
`build_channel=push` und korrekter Commit-/Buildidentität. WGC lieferte
fortlaufend frische, opake 773×534-Clientbilder. Die erste Position
war laut zwei identischen Nearest-OCR-Lesungen `Tile3144`/`3211` und
`Chunk ID 805265`/`Region ID 12594`. Die Pixelprüfung testete 41.760
Anker und fand echte Kommas sowie vier Layouts mit validen ID-Ziffern,
aber keinen vollständigen Tile-Treffer. Anders als beim vorherigen
Ort 3143/3210 enthalten die neuen ID-Ziffern **keine `3`**, obwohl
dieses Zeichen in beiden Koordinaten steht. Der frühere Algorithmus
verlangte selbst für zweimal pixelgleich sichtbare Tile-Ziffern eine
Vorlage aus den ID-Zeilen. Späterer Bewegungs-OCR-Text deutet u. a. auf
3147/3208, wo auch die nur einmal sichtbare Ziffer `7` in den IDs fehlt.
Bis zur Klärung wurden alle Kandidaten korrekt abgelehnt. Fehlerlog-
Kopie/roter Hinweis erfolgten; Gesundheitslog meldete `clipboard=COPIED`.

1.8.7 lässt deshalb einen begrenzten zusätzlichen Rasterbeweis zu:
Eine unbekannte X/Y-Ziffer muss in derselben Tile-Zeile **mindestens zweimal**
vorkommen, ihre beiden wirklichen Pixelmuster dürfen um höchstens zwei
Schwellenwertpixel abweichen und müssen von allen im selben Frame aus
Chunk-/Region-IDs trainierten Ziffern klar unterscheidbar sein. Die
beiden OCR-Varianten müssen weiterhin identische Zahlen/IDs liefern,
die zwei sichtbaren Kommas, eine echte trainierte Ebenenziffer, Chunk-/
Region-Checksummen und zwei frische Frames sind unverändert Pflicht.
Eine nur einmal sichtbare, untrainierte `7` bleibt absichtlich blockiert;
dafür gibt es die neuen Zähler `unknown_tile_digits`,
`unknown_singleton_rejects` und `repeated_unknown_layouts`.
Der neue Beweis ist schwächer als eine unabhängige Ziffernvorlage;
er wird nur im weiterhin **rein lesenden Companion** eingesetzt.

Das gleiche 1.8.6-Log zeigte trotz zufälliger Nonce und Cache-Headern
`remote_version=1.8.5`, unmittelbar nach Veröffentlichung von 1.8.6.
Das kann eine veraltete CDN-Antwort sein; die genaue Cacheinstanz ist
nicht belegbar. 1.8.7 prüft zusätzlich über den offiziellen GitHub-REST-
Endpunkt `releases/latest` ein stabiles Release und dessen veröffentlichten
`sha256:`-Assetdigest. Bei neueren vollständigen Metadaten wird nur die
streng validierte HTTPS-Assetadresse des korrekten Repos verwendet;
bei gleicher Version aber abweichenden Hashes wird gestoppt. Bei API-
Ausfall dient das alte validierte Raw-Manifest als Fallback. Windows-
Offline-Tests simulieren diese Kombinationen. **Beide Live-Fixes sind
bis zu einem neuen Benutzerlog noch nicht bestätigt.**

## Erstes erfolgreiches 1.8.5-WGC-Live-Protokoll und Update-Cache (1.8.6)

Das nächste vom Benutzer gelieferte Live-Protokoll (`2026-10-27`, 20:11
Europe/Berlin) meldet die echte aus GitHub Actions veröffentlichte
`build_channel=push`-Version 1.8.5. Schon Probe 1 hatte
`PIXEL_GRID_VERIFIED` (`roi=350x220`, `ocr_agreements=2`,
`comma_layouts=4`, `digit_templates=4`, `full_matches=4`,
`distinct_positions=1`), brauchte aber aus Sicherheitsgründen noch
ein weiteres unabhängiges frisches Frame. Ab Probe 2 meldete die
Original-EXE `OVERLAY_CONFIRMED` mit X=3143/Y=3210/Ebene=0. Bis Probe 11
blieb diese Bestätigung stabil; die Pixelprüfung benötigte nach der ersten
34-ms-Probe nur rund 11–12 ms. Die reguläre Windows-OCR las unverändert
`»` oder `o` statt exakter Kommas/Null; es wurde **keine** Ersatzregel
freigeschaltet. Ein automatisches Gameplay wird ausdrücklich nicht
behauptet: Das rote `AUTOMATION_NOT_IMPLEMENTED` und die automatische
Log-Zwischenablage sind der erwartete verbleibende Funktionsblock.

Die automatische Updateprüfung empfing rund eine Minute nach der
1.8.5-Veröffentlichung noch `remote_version=1.8.4` vom öffentlichen
`raw.githubusercontent.com`-Manifest, während GitHub auf `main` bereits
`version=1.8.5` zeigte. Als plausible Ursache bleibt CDN-Cache-Staleness,
nicht ein fehlender Update-Aufruf (die Logs dokumentieren ihn).
1.8.6 legt für jeden Manifestabruf eine neue zufällige, datenneutrale
URL-Kennung an und setzt `Cache-Control: no-cache, no-store, max-age=0`
sowie `Pragma: no-cache`. Die bestehende Release-URL und SHA-256-Prüfung
bleiben unverändert. Das neue Diagnosesignal `MANIFEST_BEHIND_LOCAL`
meldet eine alte Quelle ausdrücklich, ohne ein Downgrade zuzulassen.
Offline-Tests verlangen zwei unterschiedliche Manifest-URLs und beide
Cache-Header. Zusätzlich gleicht der Versionsvergleich die vierteilige
Windows-Assemblyversion (`1.8.6.0`) mit der dreiteiligen Manifestversion
(`1.8.6`) semantisch ab; gleiche Versionen dürfen nicht fälschlich als
`MANIFEST_BEHIND_LOCAL` gewertet werden. Eine bestätigte Verbesserung am
echten CDN steht aus.

## Erste echte 1.8.4-Rasterprobe und Positionssuchfenster-Fix (1.8.5)

Der Benutzer stellte ein vollständiges 1.8.4-Sitzungslog nach dem
Auto-Update bereit. Der Updater holte das Manifest in 143 ms und meldete
die lokale 1.8.4 als aktuell. Genau ein vertrauenswürdiges RuneLite-Fenster
und frische, opake Hintergrundbilder wurden erneut bestätigt. Die zwei
Nearest-OCR-Lesungen erkannten in allen Proben dieselben Chunk-/Region-IDs
und X/Y-Werte. Die neue Rasterprüfung wurde **tatsächlich** ausgeführt:
`ocr_agreements=2`, `anchors_tested=4536`, aber bei allen Proben
`comma_layouts=0`, `digit_templates=0`, `full_matches=0`.

Der ursprüngliche Benutzer-Screenshot war auf das Panel selbst zugeschnitten;
die Koordinaten seines ersten Glyphs im Vollbild müssen daher nicht x=7/y=13
sein. Die erste Version des Pixel-Lesers suchte nur im oberen linken
240×120-Bereich mit Ankerursprüngen x≤80/y≤55. Ein weiterer Screenshot
zeigte das komplette RuneLite-Fenster: Der darin enthaltene 131×55-
Panelbereich stimmt nach binärer Schwellenwertprüfung **in jedem Pixel**
mit dem früheren engen Ausschnitt überein, liegt aber bei +14/+53 Pixeln.
Der darin sichtbare erste Glyphenanker (x=21,y=66) lag jenseits der
alten Grenze y≤55. Auch alle Ziffernvorlagen, zwei sichtbare Kommas und
die Tile-Ebenenziffer 0 bestanden die unabhängige Rasterprüfung dieses
Vollbilds. Die privaten Bilder werden nicht ins Repository übernommen.
1.8.5 verwendet stattdessen
**den gesamten schon zuvor in Windows-OCR zugelassenen 350×220-Ausschnitt**
für die Pixelmaske und mögliche Textanker. Zusätzlich protokolliert sie
das exakte Suchfenster, den Anteil heller Pixel, die gestaffelten
Komma-/Trennzeichen-Prüfungen, die Ziffernvorlagen, vollständige Treffer
und die Laufzeit. Ein verschobenes synthetisches Panel jenseits der alten
Grenzen ergänzt die Regressionstests. Die harten Schranken für sichtbare
Kommas, unabhängige OCR-Aufbereitungen, Ziffernähnlichkeit, IDs und zwei
    frische Frames bleiben bestehen. **Der spätere 1.8.5-Live-Lauf bestätigte diese Lesung.**

## Gezielte Pixel-Ziffernprüfung aus einem echten Panel-Ausschnitt (1.8.4)

Das vom Benutzer bereitgestellte 131×55-Pixel-Bild zeigt den World-Location-
Text, dessen Kachel- und Ebenenziffern Windows-OCR trotz vier Durchläufen
als `»`, `o` oder `Ø` verfälschte. Eine lokale **analytische** Untersuchung
der gelieferten Bildpunkte (kein Bild oder Pixeltemplate ins Git-Repository
übernommen) ergab ein regelmäßiges 7-Pixel-Zeichenraster mit 11 Pixel hohen
Prüfzellen und 15 Pixel Zeilenabstand. Gleich benannte Ziffern aus der Tile-,
Chunk- und Region-Zeile sind auf diesem Bild pixelgleich oder weichen
um höchstens zwei Schwellenwert-Pixel ab. Beide Kommas sind pixelgleich.

`RuneLitePixelGridReader` erzeugt daraufhin ausschließlich zur Laufzeit
binäre Ziffernvorlagen aus Chunk-/Region-ID **derselben Aufnahme**. Diese
beiden Werte samt X/Y-Token müssen zuvor in mindestens zwei voneinander
unabhängigen Windows-OCR-Aufbereitungspässen übereinstimmen. Anschließend
prüft der neue Leser jedes Zeichen der Tile-Zeile mit Abstand und
Verwechslungsmarge gegenüber allen gelernten Ziffern, die beiden sichtbar
identischen Kommas, leere Spalten, die aufgelesene Ziffer der Ebene und
die mathematischen Chunk-/Region-IDs. Unbekannte Zeichen und andere Schrift-
oder Layoutgeometrie führen zu `NOT_RELEASED`. Außerdem darf kein gültiger
Windows-OCR-Kandidat zu einer anderen Position führen. Die frühere
Zweibild-Frischebedingung bleibt unverändert. Neue `RUNELITE_PIXEL`-Logeinträge
zeigen die Zahl geprüfter Kandidaten und die Ablehnungsstufe ohne Screenshots.

Der kleinformatige Benutzerausschnitt konnte durch dieselben strengen
Raster- und Vorlagenprüfungen *lokal analytisch* mit eindeutiger
Kachel-/Ebenenablesung geprüft werden; das ist **kein** realer Lauf der
Windows-EXE gegen die fortlaufenden WGC-Frames. Regressionen verwenden
synthetische 7×11-Muster, keine privaten Bilddaten. Vor der Freigabe
weiterer Funktionalität muss die neue 1.8.4-EXE mit `auto` im tatsächlichen
Lauf eine stabile Position aus unabhängigen aktuellen Aufnahmen nachweisen.

## Update-Fix aus echtem Sitzungslog (1.8.3)

Der Benutzer meldete, dass der alte Client nicht automatisch auf 1.8.2
aktualisiert wurde. Sein vollständiges 1.8.2-Log zeigte keine `UPDATER`-Events,
weil nur das alte `MainWindow` die `Updater.CheckAndUpdateAsync`-Funktion
aufrief; das reguläre `RuneLiteWindow` tat es nicht. Seit 1.8.3 überprüft
das Standardfenster sofort beim Anzeigen, alle 30 Minuten sowie manuell über
`Jetzt Updates prüfen` das verifizierte GitHub-Release. Check, Manifest,
Download, SHA-256, Helperstart und Fehler werden im vollständigen Sitzungslog
erfasst; Updatefehler zeigen die rote Fehlerkennung und kopieren das Log.
Nur die OSRS-Play-EXE wird neu gestartet; RuneLite selbst wird nicht beendet.
1.8.2 und ältere Companion-Versionen können diesen neuen Aufruf nicht
nachträglich erhalten: **Einmaliger manueller Download von 1.8.3 nötig.**

Dasselbe echte 1.8.2-Sitzungslog lieferte über 70 OCR-Proben: Der
RuneLite-Prozess und frische Hintergrundbilder waren verfügbar,
Chunk-/Region-Zeilen häufig lesbar, Tile-Koordinaten und Ebene hingegen
mit fehlinterpretierten Trennzeichen (`»`) und Null-Ziffern (`o`, `ø`).
Alle Kandidaten wurden korrekt als unbestätigt abgelehnt; es gibt
weiterhin keine sichere Ebene-0-Erkennung und keinen
RuneLite-Spielautomationspfad. Das Update-Problem ist davon unabhängig.

## Vollständige Sitzungslogs und automatische Fehlerkopie (1.8.2)

Der standardmäßige RuneLite-Companion zeigt jetzt ein scrollbares Diagnoselog,
eine rote Meldung `VERBESSERUNG MUSS EINGEBAUT WERDEN` und einen manuellen
Kopierknopf. Die lokale Datei wird bei jedem Ereignis erweitert. Im OCR-Log
stehen pro Bild alle angewendeten Verfahren und die tatsächlich gelesenen drei
Grid-Zeilen, mit Prüfstatus. Capture-Zustand, Frame-Zähler, Frische,
Fensteranzahl, Statuswechsel, Buildkennung, Ausnahmen und Sperrgründe folgen.

`AUTOMATION_NOT_IMPLEMENTED` ist beim normalen Start ein echter Funktionsblock
und löst eine einmalige automatische Kopie nach der ersten Probe aus.
Persistierende Positionsprobleme werden nach vier Sekunden und schwere Fehler
sofort gemeldet. Im Legacy-Modus lösen protokollierte Fehler und blockierte
Ausführungen ebenfalls die Kopie aus. Für denselben Grund wird nicht bei jedem
Timer-Tick die Zwischenablage erneut überschrieben; ein manueller Knopf kopiert
den dann aktuellsten Stand. Keine Zugangsdaten, Screenshots oder Prozessdaten.

[Technische Log-Dokumentation](LOGGING-1.8.2.md). Die Windows-CI prüft die
Kopier-/Redaktionslogik mit einer simulierten Zwischenablage. Ein neuer Live-
Test am Profil `auto` ist danach weiterhin notwendig.

## Weiterarbeit an der Overlay-OCR (1.8.1)

Die Positions-OCR prüft nun auch zusammengezogene `Chunk ID`-/`Region ID`-
Beschriftungen, skaliert bei Bedarf bicubisch auf 4× und probiert eine zweite,
kontrastverstärkte 4×-Ansicht (alles nur im RAM). Zwei reguläre 2×-/3×-Lesungen
bleiben der schnelle Standardpfad. Falls mehrere Verfahren gültige, aber
unterschiedliche Koordinaten erkennen, wird das Bild **abgelehnt**, selbst wenn
Chunk- und Region-ID bei beiden passen. Parser-Regressionen prüfen den Fall.

**Noch keine Live-Bestätigung auf `auto`:** Der bisherige `Tile3142» 3212» o`-
Befund ist dadurch nicht automatisch korrigiert. Es findet keine pauschale
`»`→`,`- oder `o`→`0`-Ersetzung statt; Eingaben bleiben deaktiviert. Nach
Windows-CI ist der nächste aussagekräftige Test `--runelite-position-probe`
auf der gestarteten regulären RuneLite-Sitzung. Erst dessen echte, frische
Ergebnisse entscheiden über weitere Schritte.

## Zuerst lesen

**Kein autonomer RuneLite-Bot ist fertig.** Der neue Standardstart ist ein
nur lesender Companion. Die alten Trainings-/Reise-/Bankroutinen sind noch
an das bisherige C++-Client-Backend gebunden und dürfen nicht einfach mit
RuneLite-Koordinaten gespeist werden. Dieses Backend ist ausdrücklich über
`--legacy-official` erreichbar; bei fehlender RuneLite-Position gibt es keinen
automatischen Rückfall.

Der Benutzer verlangt reguläres RuneLite **über den offiziellen Jagex
Launcher**. Ein separater Entwicklungsclient ist ausdrücklich verworfen.
Keine Authentifizierungsdateien, Zugangsdaten oder Prozessspeicher auslesen;
keine Launcher-Version umgehen. Der Benutzer übernimmt die Anmeldung selbst.

## Lokales Profil, tatsächlich über die UI eingerichtet

Das neue Profil **auto** wurde angelegt und aktiviert. Es begann mit RuneLites
normalen Standardplugins, nicht als Kopie des abgeschalteten Profils default.
Das alte Profil default wurde nicht überschrieben. Diese Benutzereinstellungen
liegen lokal in RuneLite, **nicht automatisch im Git-Checkout**. Keine
Authentifizierungs-/Profilrohdateien wurden ins Repo übernommen.

Über den offiziellen Hub installiert (Installation vom Benutzer bestätigt):

| Plugin | Angezeigter Stand | Einrichtung / Grenze |
|---|---|---|
| World Location | 1.6 | Aktiv. Tile Location aus; alle Gitterlinien aus; Grid Info an; Grid Info Type **Unique ID**; Instance Info Type **Copy**. |
| Shortest Path | 1.22.1 | Installiert/gestartet mit Standardkonfiguration; Transport-/Teleporteinstellungen und F2P-Wege noch nicht live geprüft. |
| Quest Helper | 4.18.0 | Installiert/gestartet; keine Quest gestartet. Shortest-Path-Integration noch nicht explizit eingeschaltet/geprüft. |
| Not Enough Runes | 1.10 | Installiert/gestartet mit Standardkonfiguration. |
| Inventory Setups | v1.26.0 | Installiert/gestartet; noch keine Tätigkeitszusammenstellung angelegt. |

Eingebaute Plugins:

- **Low Detail an**; Low detail an, Hide lower planes aus.
- **FPS Control an**; globale Begrenzung an / **30 FPS**, Hintergrundbegrenzung
  an / **15 FPS**. Beide Bildraten wurden im Spiel angezeigt.
- **Tile Indicators an**; Zielkachel an, tatsächliche Spielerkachel an,
  Hover-Kachel aus. Cyanfarbene eigene Kachel im Spiel sichtbar.
- **Menu Entry Swapper aus**, in der Liste ausdrücklich geprüft.
- Animation Smoothing war aus. Weitere eingebaute Standardplugins wurden nicht
  pauschal abgeschaltet oder einzeln verifiziert. Insbesondere XP Tracker,
  Idle Notifier, Clue Scroll und GPU sind vor weiterer Nutzung einzeln zu prüfen.
- RuneLite → Overlay settings → **Interface font = Consolas, Größe 12**,
  ohne Bold/Italic. Größere getestete Schrift (16/18) brach die Tile-Zeile im
  schmalen Panel um. Schriftänderung löst das OCR-Problem bisher nicht.

## Live-Befund am 27.10.2026

Reguläres `RuneLite.exe` wurde über den Jagex Launcher gestartet. Der Benutzer
meldete den Charakter selbst an. Im Spiel wurden im Grid-Info-Panel visuell
**X=3142, Y=3212, Ebene=0**, **Chunk ID=803217**, **Region ID=12594** gelesen.
Dies war ein Beobachtungsbefund, kein vom Bot erfolgreich übernommener Standort.

Der neue CLI-Test `--runelite-position-probe` nahm jeweils zwölf frische
Fensterbilder auf und gab ausschließlich die drei Plugin-Zeilen und den
Prüfstatus aus. Keine Spielaktionen, keine gespeicherten Screenshots.

- RuneScape-Schrift: unter anderem `Tile 311+2, 3212, o`.
- Consolas 12: wiederholt `Tile3142» 3212» o`, trotz richtiger Chunk-/Region-ID.
- Ergebnis: **GRID_INFO_NOT_READABLE**, keine bestätigte Position, Probe-Exit 2.
- Eine erste breite OCR-Region konnte HUD-Zahlen auf dieselbe Textzeile gruppieren.
  Der Ausschnitt ist deshalb auf links oben 350×220 begrenzt. Das allein löst
  die falsche Ziffern-/Interpunktionslesung nicht.

**Nicht** `o` durch `0` oder `»` durch Komma ersetzen, um einen grünen Test zu
erzwingen. Die Redundanz der Chunk-/Region-ID prüft nicht Ebene oder jede
einzelne Kachel innerhalb eines Chunks. Zwei Bilder mit demselben OCR-Fehler
sind kein unabhängiger Beweis für die Richtigkeit der Ziffern.

## Implementierung und Prüfungen

- `native/RuneLitePositionSource.cs`: normales installiertes RuneLite.exe als
  Quelle; WGC im RAM; Grid-Info-Zeilen statt Hover-Tooltip; exakte Zahlen,
  Chunk-/Region-Abgleich; zwei frische Bilder; ungültige/alte/fehlende Bilder
  verwerfen die Position. Kein Login-/Charakter-/Instanznachweis implementiert.
- `native/RuneLiteWindow.cs`: neuer Standardstart, nur lesend, Pause/Fortsetzen
  und Jagex-Launcher-Schaltfläche. Keine RuneLite-Eingaberoutine.
- `native/RuneLitePositionTests.cs`: reine Parser-/Frischetests plus Windows-
  OCR-Fixtures. Eine unzweideutige **Ebene-2**-Fixture muss exakt gelesen werden.
  Die bekannte Ebene-0-Fixture darf nur exakt richtig oder abgelehnt werden;
  Ablehnung wird ausdrücklich als **ZERO-PLANE LIMITATION** ausgegeben. Das ist
  eine dokumentierte fehlende Fähigkeit, keine erfolgreiche Live-Integration.
- `--runelite-position-probe`: lesender zwölfteiliger Live-Test, Exit 0 bei
  mindestens einer bestätigten Position, sonst 2. Keine Bereitschaftsprüfung
  für autonomes Training; noch keine Bewegungs-/Logout-Gesamtprüfung.
- Build: 0 Warnungen/Fehler. Reiner RuneLite-Selbsttest und Windows-OCR-Fixtures
  lokal erfolgreich; tatsächliche Positionsprobe fehlgeschlagen wie oben.
  Vollständige Regression und CI-Ergebnis im zugehörigen Merge/PR prüfen.

Windows/.NET-10-Befehle aus dem Repo:

```powershell
dotnet build native/OSRSLiveDiagnostics.csproj -c Release
dotnet run --project native/OSRSLiveDiagnostics.csproj -c Release --no-build -- --self-test
dotnet run --project native/OSRSLiveDiagnostics.csproj -c Release --no-build -- --ocr-self-test
dotnet run --project native/OSRSLiveDiagnostics.csproj -c Release --no-build -- --runelite-position-probe
dotnet run --project native/OSRSLiveDiagnostics.csproj -c Release --no-build -- --navigation-self-test
```

Auf diesem Rechner liegt das SDK zusätzlich als `../dotnet/dotnet.exe` relativ
zum Repo. Das Repository selbst liegt lokal in `work/osrslive` unter dem
Workspace vom 25.10.; Build-Ausgaben in `native/bin` sind ignoriert.

## Nächster sinnvoller Schritt

Zuerst die **Positionsquelle** abschließen, bevor weitere Bot-Aktionen portiert
werden. Prüfen, ob eine regulär verfügbare strukturierte Quelle besser geeignet
ist als erneut mehr OCR-Heuristik. Der reguläre Launcher lädt private Plugins
nicht einfach durch `--developer-mode`; dieses Experiment nicht wiederholen.
HTTP API wurde bereits geprüft: `/stats`, `/inv`, `/equip`, keine Position.
Alte Hinweise auf Morg HTTP Client belegen keine aktuelle Hub-Verfügbarkeit.

Wenn die Overlay-Quelle fortgesetzt wird: eine lesbare, stabile Panelgeometrie
herstellen und echte Ziffern einschließlich Ebene unabhängig verifizieren.
Danach Stillstand, manuelle Bewegung, Ebenenwechsel, Logout, Fensterwechsel,
Instanzen und widersprüchliche Daten testen. Anschließend Charakter/Welt/F2P-
Zustand sowie Projektions- und Eingabegeometrie an dieselbe RuneLite-Sitzung
binden. Erst danach einen einzelnen bounded Trainingsauftrag testen.

## Weitere Unterlagen

- [Plugin-Prüfung](RUNELITE-PLUGIN-PRUEFUNG-2026-10-27.md): 2.810 Hub-Einträge
  und 131 eingebaute Plugins, Metadaten-Screening mit näher geprüfter Auswahl.
  Keine Behauptung einer vollständigen manuellen Quellcodeprüfung aller Plugins.
- [Migration](RUNELITE-MIGRATION-2026-10-27.md), [offene Fehler](AKTUELLE-FEHLER.md).
- Suchbare HTML-/CSV-Kataloge in `docs/plugin-review/`; reproduzierbar durch
  `python tools/review_runelite_catalog.py`.
- Der verworfene private Java-/Entwicklungsprototyp liegt nur lokal außerhalb
  des Repos in `work/runelite-development-prototype-archived`. Er ist keine
  Laufzeitabhängigkeit und darf nicht als aktuelle Lösung ausgeliefert werden.

Windows-Bedienung über den verfügbaren Computer-Use-Skill; frische Fenster-
beobachtung vor Aktionen, bei Benutzereingaben neu erfassen. Den Skill vor
Fortsetzung lesen. Nie Authentifizierung selbst bedienen oder Screenshots/
Zugangsdaten zur Übergabe ins Repo schreiben.
