# Übergabe: RuneLite, Profil auto, OSRS Play 1.8.11

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
