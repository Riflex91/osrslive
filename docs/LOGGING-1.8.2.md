# OSRS Play 1.8.2 – vollständige Fehlerdiagnose

## Verhalten im laufenden Client

- Jeder Start erzeugt eine eigene **append-only**-Textdatei unter
  `%LOCALAPPDATA%\OSRSPlay\live-logs\`. Dateinamen enthalten den Backendtyp,
  Startzeit und Prozess-ID: `runelite-*.log`, `legacy-*.log`, bei einem Fehler
  vor dem GUI-Start `fatal-*.log`. Bisherige Sitzungen werden nicht automatisch
  überschrieben oder gelöscht.
- RuneLite zeigt ein scrollbares Log mit den letzten 150 Ereignissen; die Datei
  enthält **alle** Ereignisse der Sitzung. Das ältere Backend zeigt weiter
  seine letzten 420 Ereignisse an, schreibt jetzt aber ebenfalls fortlaufend.
- Bei einer neuen Fehler-/Blockursache schreibt das Programm zuerst den
  Fehler mit `IMPROVEMENT_REQUIRED` in die Datei und kopiert **den gesamten
  aktuellen Dateiinhalt** in die Windows-Zwischenablage. Die GUI zeigt rot
  `VERBESSERUNG MUSS EINGEBAUT WERDEN` und den Auslöser an.
- Derselbe dauerhaft bestehende Fehler wird weiter protokolliert, überschreibt
  die Zwischenablage aber nur einmal pro Sitzung. Jeder neue Fehlergrund löst
  eine neue Kopie aus. `Gesamte Logdatei kopieren` bzw. `Log kopieren` erneuert
  den Stand jederzeit manuell. Schlägt die Zwischenablage fehl, bleibt der
  Fehler sichtbar und der Knopf bietet einen neuen Versuch an.
- Absichtliches Pausieren ist kein Fehler. Das normale Warten auf ein zweites
  unabhängiges Bild ist ebenfalls kein Fehler, wird bei mehr als acht Sekunden
  ohne Fortschritt aber als Blockade gemeldet. Wiederholt unlesbare/mangelnde
  Aufnahmen werden nach vier Sekunden, interne Fehler und widersprüchliche
  Clients sofort gemeldet. Im Legacy-Modus wird ein geplanter, aber länger als
  30 Sekunden in einem Waiting-Zustand nicht ausgeführter Auftrag gesondert
  protokolliert (nicht während einer Nutzerpause oder im Beobachtungsmodus).
  Selbst wenn der RuneLite-OCR-Status zwischen mehreren Warteständen wechselt,
  entsteht nach zehn Sekunden ohne bestätigte Position eine zusätzliche
  `POSITION_NOT_CONFIRMED_TIMEOUT`-Diagnose.
- Der RuneLite-Companion ist noch **nur lesend**. Die fehlende automatische
  Spielausführung wird einmalig als `AUTOMATION_NOT_IMPLEMENTED` protokolliert
  und kopiert; die rote Meldung ist daher derzeit selbst bei erfolgreich
  bestätigten Positionsbildern korrekt.

## Welche Daten enthalten sind

| Kategorie | Inhalt |
|---|---|
| Start | Version, Build-Commit/-Run/-Kanal, Modus, lokale Zeitstempel |
| RuneLite-Fenster | Gefundene Prozesse, akzeptierte/abgelehnte Fenster, Clientgröße, Capture-Zustand |
| Bildaufnahme | Frame-Anzahl, Frame-Kopien, Alpha-/Crop-/Geometriestatus, Capture-Fehler, Alter in ms |
| OCR | Probe und Verfahren (`nearest_2x`, `nearest_3x`, `bicubic_4x`, `contrast_4x`), gelesene Grid-Zeilen, gültige/ungültige Parser-Entscheidung |
| Position | Chunk-/Region-Prüfung, Anzahl der gültigen OCR-Durchläufe, Zweibildbestätigung, eindeutige Koordinaten bei bestätigter Position |
| Ablauf | Statuswechsel, Pause/Fortsetzen, Launcher-Anforderung, abgelehnte oder veraltete Ergebnisse, 10-Sekunden-Gesundheitsdaten |
| Fehler | Auslöser, Sperrgrund, Exception-Typ, HResult, bereinigte Nachricht, Methoden-/IL-Stack und Zustand der Log-Kopie |
| Auto-Update (ab 1.8.3) | Prüfung beim GUI-Start, alle 30 Minuten und per Schaltfläche; Manifest-Version, Assetname, Downloadgröße, Dauer, SHA-256-Prüfergebnis, Helperstart, Ergebnis oder bereinigter Fehler. Bei Updatefehler automatische Log-Kopie mit roter Meldung. |
| Altes Backend | Zusätzlich bestehende Render-, Login-, Welt-, Inventar-, Navigations-, Kampf- und Aktivitätsereignisse sowie blockierte Executor-Zustände |

Die Datei speichert **keine Bildschirmbilder**, Prozessspeicher, Jagex-
Anmeldedaten oder RuneLite-Profilrohdateien. Ereignistexte werden vor dem
Schreiben bereinigt (bekannte Token-/Passwort-/Cookie-Muster und E-Mail-
Adressen). Die drei Grid-Zeilen sowie bestätigte X/Y/Ebene können enthalten
sein, weil die konkreten OCR-Zeichen für die Fehlersuche entscheidend sind.
Das Log ist daher ein **privates Diagnoseartefakt** und sollte vor öffentlicher
Weitergabe trotzdem kontrolliert werden. Es wird weder automatisch versendet
noch ins Git-Repository hochgeladen.

## Tests und offene Grenze

`--self-test` prüft Append-Datei, tatsächlichen kopierten Dateiinhalt,
manuelle Nachkopie, Duplikatunterdrückung und die simulierte belegte
Zwischenablage. Die bestehenden `--ocr-self-test`-, `--navigation-self-test`-
und Windows-Build-Prüfungen bleiben erforderlich.

Das Logging schafft Belege für einen gezielten Fix, behebt aber nicht von sich
aus die bekannte unlesbare Ebene-0-World-Location-OCR. Ein echter Lauf des
neuen Clients mit dem regulären RuneLite-Profil `auto` bleibt notwendig.

**Update-Hinweis 1.8.3:** Der 1.8.2-RuneLite-Companion rief die vorhandene
Updatefunktion gar nicht auf. Deshalb muss 1.8.3 einmalig manuell installiert
werden; danach laufen die automatischen Prüfungen auch im RuneLite-Modus.
Bei einer neuen verifizierten Version startet nur OSRS Play neu, nicht RuneLite.
