# OSRS Play 1.4.0 – feste Spielansicht und Hintergrundtransport

Windows-Oberfläche für den unveränderten offiziellen Jagex Launcher und `osclient.exe`.
Die Anwendung ist **noch kein vollständig selbstständig spielender Maxing-Agent**.

## Fenster und Log

Die Spielansicht bleibt fest **1000 × 600 Pixel** groß. Das Hauptfenster lässt sich verschieben und minimieren, aber nicht frei skalieren oder maximieren. **Log ausblenden / einblenden** klappt unterhalb des Spiels einen 180-Pixel-Bereich zu oder auf; nur die Gesamthöhe des Fensters ändert sich. Das Spiel bleibt dabei unverändert groß und vollständig sichtbar.

**Log kopieren** und **Strg+Umschalt+C** kopieren denselben bereinigten Bericht mit bis zu 10.000 gepufferten Ereignissen, auch bei verborgenem Log. Die Vorschau zeigt die letzten 420 Ereignisse. Überläufe werden genannt. Bei belegter Zwischenablage gibt es begrenzte Wiederholungen und eine sichtbare Wiederholungsmöglichkeit.

**Automatik pausieren**, **F8**: Eingaben anhalten. F8 wird als globaler Hotkey registriert; wenn die Registrierung bereits durch eine andere Instanz belegt ist, erscheint eine Warnung und der Pause-Button bleibt verfügbar.

## Hintergrundbetrieb: umgesetzt und noch zu prüfen

Windows Graphics Capture liest die Compositor-Oberfläche des eigenen Hostfensters und schneidet daraus das offizielle Spielfenster aus. Eine davorliegende fremde App wird nicht als Spielbild gelesen. Kein `CopyFromScreen`, kein notwendiger Vordergrund und keine Speicherung von Screenshots.

Eingaben werden als Mausnachrichten an das ermittelte Fenster bzw. dessen Render-Child geschickt. Der Transport verwendet weder `SendInput` noch `SetCursorPos` noch `SetForegroundWindow`. Ein Versand gilt nicht als Erfolg; erforderlich ist eine passende neue Beobachtung. Die feste Geometrie wird vor dem Versand geprüft, F8/Pause/Abbruch werden berücksichtigt, und ein gedrückter Knopf wird auch bei Abbruch losgelassen.

**Der Hintergrundtransport wurde an einem verdeckten Testfenster einschließlich beobachteter Bildänderung geprüft. Die tatsächliche Annahme der Nachrichten durch `osclient.exe` ist noch nicht bestätigt.** Eine Anwendung kann Fensternachrichten ignorieren oder eigene Fokusregeln anwenden. Es gibt keinen stillen Rückfall auf den globalen Mauszeiger.

Hintergrund bedeutet hier: hinter anderen Anwendungen oder verdeckt. **Minimiert pausiert die Bildaufnahme**, weil Windows dort keine frischen Renderframes garantiert. Veraltete Frames geben keine Aktionen frei. Statische Oberflächen erhalten bei Bedarf eine neue Capture-Sitzung. Capture-Aufräumarbeiten blockieren nicht den UI-Thread.

## Einstieg und Spielzustand

Im Jagex Launcher den gewünschten Charakter und **Offizieller Client** wählen und gegebenenfalls anmelden/MFA abschließen. Vorhandene Auswahl wird übernommen; die App gibt keine Zugangsdaten ein.

Der Startablauf erkennt den Launcher-Play-Button über Accessibility und bekannte Spielbildschirme mit lokaler Windows OCR. Die F2P-Prüfung lädt die offizielle Weltliste und vergleicht erreichbare uneingeschränkte freie Welten anhand mehrerer TCP-Proben. Ein erkannter passender Welteintrag wird gewählt und danach erneut beobachtet. Nicht sichtbare Weltseiten werden noch nicht automatisch durchgeblättert. Das native mehrspaltige Raster wird über aufsteigende Weltnummern-Spalten erkannt, nicht allein über die Position der Überschrift „World“.

In der Spielansicht erkennt der Leser Chat-Bedienleiste und Charakter-Prompt. Die Skilltafel wird über „Total level“ lokalisiert, und die winzigen Basislevel werden in vergrößerten Ausschnitten gelesen. Es müssen alle 24 Werte zur Gesamtstufe passen und in zwei zeitlich getrennten Beobachtungen übereinstimmen. Wiederverwendung eines Frames reicht nicht. Fremde Charakterdaten, unvollständige oder veraltete Lesungen geben keinen Zielvorschlag frei. Sichtbare OCR ist kein serverseitiger Authentifizierungsnachweis.

Falls die Skilltafel geschlossen ist, kann die Automatik bei der festen Standardgeometrie einen Hover auf den Skill-Tab versuchen und erst nach erkanntem „Skills“-Tooltip klicken. Dieser Ablauf benötigt noch eine Bestätigung am echten Client.

## Ziele, Fortschritt und Beschaffung

Der Zielplaner schlägt den nächsten zugänglichen F2P-Skill unter 99 vor; erst bei allen 24 bekannten Skills auf 99 wechselt er zu Vermögen/Ausrüstung. Mitglieder-Skills werden auf F2P nicht als abgeschlossen behandelt. Bestätigte visuelle Levelstände werden unter `%LOCALAPPDATA%\OSRSPlay\progress` gespeichert. Der Dateischlüssel ist ein Hash des Charakternamens; der Klarname wird dort nicht gespeichert. Ein gespeicherter Stand ersetzt keine frische Sitzungsbeobachtung.

Die Beschaffungspolicy bevorzugt vorhandene Gegenstände, vergleicht ausführbares Kaufen/Herstellen anhand übergebener frischer Gesamtkosten und erhält eine Münzreserve. **Navigation, Skill-Aktivitäten, Inventar-/Bankerkennung, Preis-/Rezept-/Upgrade-Katalog und tatsächliche Bank-/Handels-/Herstellungsroutinen fehlen.** Die Anwendung trainiert, kauft und fertigt deshalb noch nicht selbstständig.

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
