# OSRS Play 1.6.1 – Launcher-Start, Wiederherstellung und kontrolliertes Schließen

Stand: 24.10.2026. Umsetzung auf Basis der Diagnose von 1.6.0 (15:49–15:50 Uhr). Die Änderungen betreffen die offizielle Launcher- und Fenstersteuerung. Vollständiges Login, aktive Charakteridentität und Gameplay wurden nicht am echten Client erneut validiert.

## Reproduzierbarer Ausgangsbefund

- Der bereits laufende Launcher wurde erkannt, der Play-Button war anfänglich nicht eindeutig erreichbar (`LAUNCHER_PLAY_NOT_UNAMBIGUOUS`).
- Ein späterer Launcher-Klick wurde um 15:49:44 versendet; der offizielle Client war um 15:49:46 eingebettet. Der Klick allein ist kein Login-Beweis.
- Danach wurden `WORLD_LABEL_NOT_RECOGNIZED` und `PLAY_BUTTON_NOT_RECOGNIZED` ausgegeben. Welt- und Play-OCR benötigen gesonderte Live-Kalibrierung.
- Die vorherige Version minimierte den Launcher sofort beim Anhängen des Spielclients, auch ohne nachgewiesenen Spieleinstieg.

## Implementierung

1. **Laufendes Spiel bevorzugen.** Ein bereits eingebettetes offizielles Spielfenster hat Vorrang; der Launcher wird in diesem Zustand nicht erneut gestartet.
2. **Vorhandenes Launcher-Fenster verwenden.** Nur Fenster mit geprüfter Jagex-Launcher-Prozesszugehörigkeit werden erfasst. Ein minimiertes Fenster wird über `ShowWindowAsync(SW_RESTORE)` wiederhergestellt, bevor die Windows-Accessibility-Erkennung nach „Spielen/Play“ sucht. Wiederholte Wiederherstellungsversuche sind begrenzt.
3. **Launcher bei Bedarf öffnen.** Ist weder Spiel- noch Launcher-Fenster vorhanden, werden auch laufende Launcher-Prozesse berücksichtigt, die ohne sichtbares Fenster im Tray liegen. Das offizielle lokal installierte Executable wird gegebenenfalls gestartet bzw. erneut geöffnet. Zwischen Startversuchen liegen mindestens 25 Sekunden; es werden keine Zugangsdaten verarbeitet.
4. **Launcher vor Login nicht minimieren.** Der Wechsel zum Spielclient minimiert den Launcher nicht mehr automatisch.
5. **Erst nach stabiler visueller Sitzung regulär schließen.** Voraussetzung sind eingebettetes Spiel, erkannter Charakter und alle 24 Skill-Level mit übereinstimmender Gesamtsumme und mindestens zwei voneinander unabhängigen Beobachtungen (vorhandener `GameObservationTracker`). Die Bestätigung darf höchstens drei Sekunden alt sein. Sie wird vor langen Inventar-/Routinenläufen ausgewertet.
6. **Kein erzwungener Kill.** Das zuvor tatsächlich zugeordnete Launcher-Fenster erhält einmalig `WM_CLOSE`. Wird das Fenster entfernt, lautet der Log-Eintrag `LAUNCHER_WINDOW_CLOSED_CONFIRMED`. Bei ausbleibender Bestätigung wird nach zehn Sekunden gewarnt. Die Anwendung terminiert weder den Launcher-Prozess gewaltsam noch `osclient.exe`; Tray-Prozess oder Launcher-Spezialverhalten können das Schließen verhindern.

## Neue Diagnoseereignisse

- `LAUNCHER_AVAILABILITY_CHECK`: kein nutzbares Spiel-/Launcher-Fenster, vorhandener Launcher-Prozess ja/nein
- `LAUNCHER_START_REQUESTED` oder `LAUNCHER_REOPEN_REQUESTED`: offizielle Executable gestartet bzw. erneut aufgerufen
- `LAUNCHER_RESTORE_REQUESTED` oder `LAUNCHER_RESTORE_FAILED`: minimiertes Launcher-Fenster wiederherstellen
- `LAUNCHER_CLOSE_AFTER_VISUAL_LOGIN`: Ergebnis des einmaligen regulären Schließauftrags
- `LAUNCHER_WINDOW_CLOSED_CONFIRMED` oder `LAUNCHER_CLOSE_NOT_CONFIRMED`: Wirkung durch Fensterzustand überprüft

`server_auth_verified=false` bleibt ausdrücklich korrekt: Die visuelle Sitzung ersetzt keinen serverseitigen Login-Beleg.

## Prüfung und nächste Live-Tests

Die Windows-CI führt Build, Selftests, OCR-Fixtures, Layout- und Hintergrundtransporttests aus und veröffentlicht bei Pull Requests ein gesondertes EXE-Testartefakt. Die neu ergänzten Policy-Selftests verweigern den Launcher-Abschluss ohne Spiel, ohne Charakter, ohne 24 Skills, bei falscher Gesamtsumme oder bei veralteter Beobachtung. Der echte Jagex Launcher kann in der CI nicht sicher als Testinstanz betrieben werden.

**Live-Testfolge:** 1. kein Launcher geöffnet; 2. Launcher minimiert; 3. Spielclient noch nicht eingeloggt (Launcher muss offen bleiben); 4. Charakter und 24 Skills visuell bestätigt (Launcher-Close anfordern); 5. prüfen, ob Spiel unabhängig weiterläuft. Zur separaten Diagnose der weiterhin vorhandenen `WORLD_LABEL_NOT_RECOGNIZED`- und `PLAY_BUTTON_NOT_RECOGNIZED`-Hürden das Session-Log senden. Keine automatische GE-Transaktion zum Test auslösen.
