# OSRS Play 1.7.0 – Kampf und automatische Kartennavigation

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

**Aufgabe …** pausiert die Automatik und öffnet Einzelaufträge für Training, Herstellung, Kaufen/Verkaufen, Bank, Kampf und Reisen. Nach dem Einreihen **Automatik fortsetzen** wählen. Ohne Einzelauftrag wählt die Automatik selbst eine unterstützte Aktivität und deren Reiseziel. Die benötigten Referenzkarten werden automatisch heruntergeladen und lokal zwischengespeichert. Der Kontotyp ist eine Einstellung, keine automatische Ironman-Erkennung; die anfängliche Münzreserve beträgt 500.

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
