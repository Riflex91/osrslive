# Einstieg, Spielzustand und Reconnect – 1.7.2

## Warum der Charakter nach dem Login steht

Der bereitgestellte 1.7.1-Log enthält beide erfolgreichen Eingabeanforderungen für `PLAY_SELECTED_CHARACTER` und `ENTER_GAME`. Danach bleiben `stage=Unknown`, `chat_controls=False` und `WAITING_FOR_SKILLS` bestehen. Der Beobachter erreicht den Zweig zum Öffnen der Skills nicht. Der Koordinator bekommt deshalb keine bestätigten Charakter-/Skilldaten und beginnt keinen Trainingsauftrag. Die Ursache liegt vor der Aufgabenwahl; das Fehlen einer manuellen Aufgabe ist nicht der Grund.

Zusätzlich zur Text-OCR verwendet 1.7.2 einen Bildmarker für die feste native Ansicht 1000×600: automatisch erkannte Minimap mit zentraler Spielermarkierung, das rot/grün/blaue Balkensymbol des Skills-Tabs sowie Titel-/Statusbereiche mehrerer Chat-Schalter und den Report-Bereich. Der Marker ist als Bildbeleg typisiert, nicht als erfundener OCR-Text. Er erlaubt das Erkennen von `InGame` und das Öffnen des bildlich identifizierten Skills-Tabs. Er erfindet weder Charaktername noch Skillwerte. Alle 24 Werte, Gesamtstufen-Prüfsumme, Charakter-Prompt und zwei unabhängige Beobachtungen bleiben vor Spielaktionen erforderlich. Fehlende Voraussetzungen werden nun als eigener Startzustand angezeigt, etwa `IN_GAME_CHARACTER_PROMPT_NOT_READABLE` oder `IN_GAME_SKILLS_INCOMPLETE_…_OF_24`.

## Schnellerer Einstieg

Die Beobachtung wird alle 400 ms angeboten statt alle zwei Sekunden; eine laufende Aufnahme/OCR/Ausführung wird weiterhin nicht parallel erneut gestartet. Die pauschale Vier-Sekunden-Sperre nach jeder Eingabe entfällt. Ein neu erkannter anderer Bildschirm kann nach kurzer Eingaberuhe weiterverarbeitet werden. Zwei frische Bestätigungen benötigen mindestens 250 ms Abstand; Zwischenbeobachtungen setzen den ersten Kandidaten nicht mehr ständig zurück.

Ein unveränderter Launcher-/Play-Bildschirm erhält frühestens nach acht Sekunden einen weiteren gleichartigen Klick, die zweite Einstiegskarte nach vier Sekunden. Damit wird eine langsam reagierende Verbindung nicht mit Klicks überflutet. Welt- und Authentifizierungsprüfungen werden nicht übersprungen. Eine feste Login-Dauer wird nicht zugesagt; Clientstart, OCR, Serverantwort und Weltprüfung bestimmen die tatsächliche Zeit.

## Reconnect

Nach einem sichtbaren Logout oder Verbindungsverlust wird die aktive Charakter-/Skill-Bindung verworfen. Laufende Ausführer senden keine weiteren Aktionen auf Login-, Verbindungs- oder unbekannten Bildschirmen. Dauerhafte Handels-, Beschaffungs- und Kampfdateien werden nicht gelöscht.

Ein ausgefallenes Netzwerkinterface sperrt sofort. Während einer bekannten Spielsitzung prüft ein begrenzter TCP-Verbindungstest etwa alle zehn Sekunden den offiziellen Spielwelt-Port. Erst zwei aufeinanderfolgende Fehlschläge melden einen Ausfall; ein einzelner fehlgeschlagener Test wird nicht als sichere Trennung behandelt. Der Test authentifiziert nicht und liest keinen Spielprotokollzustand. Erreichbarkeit dieses Ports ist kein Nachweis einer gültigen Jagex-Anmeldung.

Der Client erhält zunächst acht Sekunden für seine eigene Wiederverbindung. Weitere Netzwerkversuche verwenden 2/4/8/16/30 Sekunden Wartezeit, maximal 30 Sekunden. Ein eindeutig sichtbarer Retry-/Reconnect-Schalter benötigt erneut zwei unabhängige Frames. Nach einem Klick bleiben weitere Versuche zehn Sekunden gesperrt. Zeigt der Client wieder den bekannten Login-/Welcome-Bildschirm, wird der normale, geprüfte Einstieg fortgesetzt. Bei zurückgekehrter Spielansicht müssen Charakter und Skills neu gelesen werden, bevor der Koordinator offene Vorgänge abgleicht oder eine neue Aufgabe auswählt.

F8/Pause bleibt ein ausdrücklicher Benutzerstopp und wird nicht durch Reconnect aufgehoben. Passwort-, Authenticator- oder Verifikationscode-Abfragen benötigen weiterhin den Benutzer. Ein unbekannter Fehlerdialog oder ein Client, der ohne sichtbaren Wiederverbindungsweg hängen bleibt, wird nicht durch blinde Klicks, erzwungenes Beenden oder eine angenommene Anmeldung übersprungen.

## Prüfung

Die Tests prüfen Offline-Backoff, Begrenzung der Heartbeat-Frequenz, zwei unabhängige Retry-Frames, Ablehnung wiederverwendeter Bilder, Logout-Rückkehr, Authentifizierungsgrenzen, Vorrang eines Verbindungsabbruchs vor einem eingefrorenen HUD, schnelle Bildschirmwechsel und das Verbot von Klickschleifen. Der Bild-HUD-Test prüft mehrere unabhängige Bereiche und bestätigt ausdrücklich, dass der Marker keine Charakter-/Skill-Bindung freigibt. Bestehende OCR-, Karten-, Layout- und Transporttests gelten weiter.

Ein Test der Zustandslogik ersetzt keinen absichtlich herbeigeführten echten Internetabbruch oder einen vollständig bestätigten Spielbetrieb. Die Netzwerkverbindung des Rechners wird zum Testen nicht abgeschaltet.

Im Liveversuch mit dem offiziellen Client wurden `InGame` und das automatische Öffnen des Skills-Tabs bestätigt. Anschließend zeigten die Diagnosezustände noch eine unlesbare Charakter-Prompt und fehlende Skillzahlen. Dafür enthält der abschließende Build einen isolierten Prompt-Ausschnitt sowie eine Schwarz-auf-Weiß-Lesung der gelben Basisstufen mit festem OCR-Kontext. Ein synthetischer OCR-Test liest alle 24 Stufen und deren Summe und schließt daneben gezeichnete erhöhte aktuelle Stufen aus. Der Desktop-Versuch wurde per Escape beendet; die abschließende Prompt-/Ziffernkorrektur und ein vollständiger Trainingsstart sind deshalb noch nicht live bestätigt.
