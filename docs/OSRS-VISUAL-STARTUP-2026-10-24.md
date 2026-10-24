# Visueller Start, Diagnose und Zielplanung – 24.10.2026

## Umfang

Der Nutzer gibt eine ausdrückliche Jagex-Erlaubnis zur Steuerung des offiziellen Clients über Maus/Tastatur an. Gewünscht sind automatischer Spieleinstieg, beste F2P-Welt, Entwicklung aller Skills, danach Vermögen/Gear und selbstständige Beschaffung durch Kaufen/Herstellen. Die Erlaubnis bezeichnet den Nutzungsumfang, liefert jedoch keine technische API.

Diese Umsetzung nutzt sichtbare UI-Zustände und Windows-Mauseingaben. Keine Behauptung über Unerkennbarkeit, empirische Ban-Raten oder Betriebsfähigkeit über Monate/Jahre.

## Öffentlich dokumentierte Referenzen

Keine vollständige Marktinventur, Binäranalyse oder Einsicht in proprietäre Interna. Dokumentationsverfügbarkeit belegt nicht die aktuelle Lauffähigkeit auf einer bestimmten Spielrevision. Die Analyse vom 22.10.2026 bleibt historischer Kontext; ihre Aussagen wurden nicht pauschal neu bestätigt.

| System | Neu gesichteter öffentlicher Beleg | Übertragbare Erkenntnis |
|---|---|---|
| DreamBot | `AbstractScript.onLoop()` wird wiederholt ausgeführt; Rückgabewert ist Warteintervall | Scheduler vom UI-Thread trennen |
| OSBot | `MethodProvider` bündelt Client, Entitäten, Inventar, Maus/Tastatur und Event-Ausführung | Zustandsquelle und Aktionsausführung als eigene Module |
| Tribot | `ScriptContext` stellt Clientdaten, Eingabe, Inventar, Events, Wartebedingungen und Logger bereit | Aktionen anschließend über Zustandsänderungen bestätigen |
| Microbot | Öffentliches Repository beschreibt modifizierten RuneLite-Client | Dessen interne APIs sind keine Schnittstelle zum hier eingebetteten nativen Client |
| OSRSB/OsrsBot | Öffentliches Repository beschreibt RuneLite-Erweiterungs-API | Erweiterungs-API und Betriebssystem-Eingabe unterscheiden |
| RuneMate | Öffentlicher API-Issue zu `delayUntil`/`delayWhile` | Bedingungen effizient prüfen; kein Nachweis vollständiger interner Logik |
| PowBot | Dokumentationsabruf fehlgeschlagen | Keine neuen technischen Aussagen bestätigt |

Primärquellen, in dieser Sitzung abgerufen:

- https://dreambot.org/guides/scripting/creating-your-first-script/
- https://osbot.org/api/org/osbot/rs07/script/MethodProvider.html
- https://tribot.org/learn/dev/developer-overview
- https://tribot.org/learn/dev/intellij-project-setup
- https://github.com/chsami/Microbot
- https://github.com/OSRSB/OsrsBot
- https://gitlab.com/runemate/community/runemate-game-api/-/issues/154
- https://oldschool.runescape.com/slu
- https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr.ocrengine.recognizeasync

## Umsetzung

`VisualStartup` verbindet Fensterübernahme mit einem asynchronen Beobachtungs-/Entscheidungs-/Eingabezyklus. Erforderlich sind das echte angehängte Fenster, Vordergrund, eindeutige Erkennung, höchstens zwei Sekunden alter Bildbefund und unveränderte Fensterposition/-größe vor dem Klick. Das Spiel wird nicht auf Basis der bisherigen Farbproben bedient.

Ein Launcher-Button wird über Windows Accessibility lokalisiert, anschließend per Maus betätigt. Vorhandene Charakter-/Clientauswahl wird übernommen. Spiel-UI wird lokal mit Windows OCR gelesen. Bekannte Welcome- und Weltenauswahl-Zustände sind begrenzt; unbekannte Bildschirme führen nicht zu geratenen Klicks. Bilder und OCR-Inhalte werden nicht gespeichert oder exportiert.

Mausbewegungen erfolgen in kurzen Schritten. F8, Fokusverlust, Abbruch und entgegenlaufende Benutzerbewegung unterbrechen. Das ist kontrollierte Eingabe, kein belegtes menschliches Erkennungsprofil. Input-Dispatch wird nicht als erfolgreicher Login ausgegeben. Nach drei unveränderten Aktionstypen wird nicht weitergeklickt. Nicht sichtbare Weltseiten benötigen derzeit einen manuellen Schritt.

Hide/Show- und Copy-Log-Buttons sind sichtbar. Copy und Strg+Umschalt+C erzeugen denselben bereinigten Bericht mit allen bis zu 10.000 Pufferereignissen; die Vorschau zeigt nur 420. Ausblenden leert weder Puffer noch Dateiprotokoll. Zwischenablagefehler erlauben Wiederholung.

`AccountGoalPlanner` modelliert Maxing vor Vermögen/Gear, F2P-Beschränkungen und Eigenbestand/Kaufen/Herstellen mit aktuellen Gesamtkosten und Reserve. Er ist eine getestete Policy, kein spielender Agent und noch nicht an einen Runtime-Executor angeschlossen.

## Verifizierung

- Lokaler Release-Build mit .NET 10.0.402 ohne Warnungen/Fehler.
- Bestehende Selftests und neue Start-/Ziel-/Beschaffungstests erfolgreich.
- Windows OCR auf generiertem Welcome-Bild mit Play-/World-Text bis zur Policy-Entscheidung getestet. Englische OCR war nicht vorhanden; vorhandener Profilsprachen-Fallback funktionierte.
- Neue GUI ohne Spiel-/Update-/Eingabestart geöffnet: ausgeblendetes Log, Flächengewinn und Copy bei verborgenem Log mit „Log kopiert“ visuell bestätigt.
- Vorhandenen Jagex Launcher gelesen und sichtbaren Spielen-Button betätigt; anschließend erschien `osclient.exe` mit Welcome/Play Now und Weltlabel.
- Live-Test mehrfach wegen Benutzerinteraktion angehalten. Kein vollständiger Ende-zu-Ende-Nachweis von automatischer Weltenauswahl, Login, aktiver Charakteridentität oder Gameplay.

## Noch benötigte Implementierung

1. OCR gegen echte Clientbilder in verschiedenen Größen/Sprachen/Skalierungen validieren; synthetisches Bild ersetzt diesen Test nicht.
2. Nicht sichtbare Weltseiten, mehrdeutige Einträge und Auswahlbestätigung auf echten Bildschirmen abdecken.
3. Aktive Charakteridentität, Welt, Skills, Inventar und Bank belastbar erkennen. Öffentliche Hiscores sind keine verifizierten aktiven Sitzungsdaten.
4. Aktivitäten je Skill, Navigation, Voraussetzungen, Werkzeug, Verbrauchsmaterial und Bankwege implementieren und Ergebnisse bestätigen.
5. Beschaffung an Rezepte, Upgrade-Anforderungen, frische Preise, Gesamtmengenkosten und bestätigte Transaktionen anschließen. Die aktuelle Policy führt keine Käufe oder Herstellung aus.
6. Wiederanlauf, Fortschrittspersistenz und Langzeittests mit echten ausführbaren Aktivitäten validieren. Keine Garantie eines vollständig maximierten Accounts.
