# OSRS Play 1.2.0 – visueller Start und Diagnose

Windows-Oberfläche für den unveränderten Jagex Launcher und `osclient.exe`.
Diese Version ist **noch kein selbstständig spielender Maxing-Agent**.

## Bedienung

- **Log ausblenden / einblenden:** gibt Platz an die Spielansicht zurück; Protokollierung läuft weiter.
- **Log kopieren** und **Strg+Umschalt+C:** derselbe bereinigte Bericht mit allen bis zu 10.000 gepufferten Ereignissen, auch bei verborgenem Log. Die Vorschau zeigt nur 420 Ereignisse; Pufferüberläufe stehen im Bericht.
- **Automatik pausieren / fortsetzen**, **F8:** Eingabeautomatik anhalten. F8 wird auch während der Mausbewegung geprüft.
- Bei belegter Zwischenablage folgen begrenzte Wiederholungen und gegebenenfalls „Erneut kopieren“.

Logdatei: `%LOCALAPPDATA%\OSRSPlay\live-logs\last-session.log`.
Passwörter, übliche Token-Felder und E-Mail-Adressen werden im Ereignislog redigiert.
Keine Screenshot- oder OCR-Text-Exporte.

## Automatischer Einstieg

1. Im offiziellen Jagex Launcher anmelden, den gewünschten Charakter und **Offizieller Client** auswählen. Die Anwendung übernimmt diese bestehende Auswahl.
2. OSRS Play übernimmt ein vorhandenes offizielles Fenster oder öffnet den Launcher. Windows Accessibility identifiziert einen eindeutigen „Spielen“-/„Play“-Button; die Bedienung erfolgt per Maus.
3. Die vorhandene F2P-Prüfung vergleicht erreichbare uneingeschränkte freie Welten aus Jagex' Weltliste anhand mehrerer TCP-Proben.
4. Windows OCR liest das sichtbare Spielfenster im Vordergrund. Auf einem erkannten Welcome-Bildschirm öffnet der Ablauf bei abweichender Welt die Weltenauswahl. Die gemessene Zielwelt wird nur angeklickt, wenn ihre Nummer eindeutig sichtbar ist.
5. Bei passendem sichtbarem Weltlabel bedient der Ablauf „Play Now“ und einen erkannten nachfolgenden Welcome-/„CLICK HERE TO PLAY“-Bildschirm. Jeder Klick benötigt eine neue Beobachtung; Dispatch gilt nicht als Erfolg.

**Grenzen:** nicht sichtbare Weltseiten werden noch nicht automatisch durchgeblättert. Mehrdeutige OCR-Treffer, unbekannte Bildschirme, fehlende OCR-Sprachen und Anmeldung/MFA benötigen einen manuellen Schritt. Es werden keine Zugangsdaten eingegeben. Die Oberfläche muss im Vordergrund bleiben. Nutzerinteraktion hat Vorrang; entgegenlaufende Mausbewegung während der Ausführung pausiert. Pro unverändertem Aktionstyp gibt es maximal drei Eingabeversuche. Keine Zusage zur Nichterkennung.

Sichtbare Login-Elemente sind kein serverseitiger Authentifizierungsnachweis. Die vorhandene Farbproben-Beobachtung bleibt erhalten und wird nicht als Skill-, Inventar- oder Bankdaten ausgegeben.

## Langfristiges Ziel und Beschaffung

`AccountGoalPlanner` enthält separat testbare Regeln:

- Verfügbare Skills bis 99 trainieren; erst bei **allen 24** bekannten Skills auf 99 auf Vermögen/Ausrüstung umschalten.
- Mitglieder-Skills auf F2P nicht als erledigt behandeln.
- Bestätigten Eigenbestand vor Beschaffung verwenden.
- Kaufen und Herstellen anhand übergebener aktueller Gesamtkosten, erfüllter Voraussetzungen und Ausführbarkeit vergleichen; übergebene Münzreserve erhalten.
- Bei unbekanntem Bestand/Kontostand oder veralteten Preisen warten.

**Diese Regeln sind noch nicht an einen spielenden Executor angeschlossen.** Aktive Charakter-/Skilldaten, Inventar-/Bankerkennung, Navigation, Aktivitäten je Skill, Rezept-/Upgrade-Katalog, Preisdaten und Handels-/Herstellungsroutinen mit Ergebnisbestätigung fehlen. Die Anwendung kauft, fertigt und trainiert deshalb noch nichts automatisch. Öffentliche Hiscores ersetzen die fehlenden Sitzungsdaten nicht.

## Entwicklung und Prüfung

Windows x64 mit .NET 10 SDK und Windows OCR-Sprachunterstützung:

```powershell
dotnet build native/OSRSLiveDiagnostics.csproj -c Release
dotnet run --project native/OSRSLiveDiagnostics.csproj -c Release --no-build -- --self-test
dotnet run --project native/OSRSLiveDiagnostics.csproj -c Release --no-build -- --ocr-self-test
dotnet run --project native/OSRSLiveDiagnostics.csproj -c Release --no-build -- --ui-smoke-test
dotnet publish native/OSRSLiveDiagnostics.csproj -c Release -r win-x64 -o publish
```

`--ui-smoke-test` startet weder Updates noch Jagex noch Eingaben und schreibt `ui-smoke-test.log`. `--ocr-self-test` prüft lokale OCR auf einem generierten Bild ohne Bildschirmaufnahme oder Eingaben.

[Recherche, Umsetzung und Verifizierungslücken](docs/OSRS-VISUAL-STARTUP-2026-10-24.md).
