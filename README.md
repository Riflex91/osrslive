# OSRS Play v1.0.0 – automatisches Live-Fenster ohne GUI-Schaltflächen

OSRS Play zeigt nur noch die Originalfenster von Jagex Launcher und `osclient.exe`.
Eigene OSRS-Play-Schaltflächen und weitere Bedienelemente wurden entfernt; die originalen Eingabefelder und Spielbuttons des Jagex-Clients bleiben unverändert.

## Automatischer Start

1. OSRS Play prüft den verifizierten GitHub-Auto-Updater.
2. Vorhandenes offizielles OSRS- oder Launcher-Fenster wird automatisch übernommen. Sonst wird der originale Jagex Launcher gestartet.
3. Ein sichtbares Jagex-Launcher-Fenster wird in OSRS Play eingebettet. Die vorhandene Jagex-Anmeldung kann benutzt werden. Passworteingabe und MFA bleiben ausschließlich bei Jagex. Ohne gültige bestehende Sitzung ist ein **automatisches Einloggen ohne Benutzereingriff nicht möglich**.
4. Sobald die offizielle `osclient.exe` startet, wird deren Fenster im selben OSRS-Play-Bereich eingebettet.
5. Die offizielle F2P-Weltliste wird automatisch abgerufen. Die App misst die TCP-Verbindungsdauer vom eigenen Windows-PC aus und zeigt die schnellste Standard-F2P-Welt im Status und im Log an.

## Noch nicht möglich

- OSRS Play kann die gemessene F2P-Welt **noch nicht selbstständig im eingebetteten Originalspiel auswählen**; eine verifizierte Schnittstelle zur Weltenwahl fehlt. `applied_in_game=false` kennzeichnet das im Log.
- Der Live-Bot wird **nicht gestartet**, weil weder ein verifizierter Weltzustand noch ein geeigneter Aktionsadapter vorhanden sind. `LIVE_AGENT START_BLOCKED` ist ein bewusster Sicherheitszustand, **kein erfolgreicher Botstart**.
- Das Hosten des Originalfensters ist keine native Implementierung des OSRS-Spielprotokolls. Jagex Launcher und offizieller Client erledigen weiterhin Login und Netzwerkverkehr.
- Jagex untersagt automatisiertes Spielen laut seinen allgemeinen Spielregeln. Die Installation oder Fenstereinbettung allein begründet keine Freigabe für Live-Botaktionen.

## Keine GUI-Buttons

Es gibt keine OSRS-Play-Schaltflächen, Textsuche oder Log-Filter mehr. Es bleiben der offizielle Spielbereich, der aktuelle Status und ein lesbares Live-Log.
Für Support lässt sich das aktuelle Diagnoseprotokoll mit **Strg+Umschalt+C** in die Zwischenablage kopieren; außerdem wird es alle 10 Sekunden und beim Beenden nach `%LOCALAPPDATA%\OSRSPlay\live-logs\last-session.log` geschrieben.
**Hinweis:** Das Log enthält den technischen Ablauf des OSRS-Play-Fensterhosts, aber keine Konto-Passwörter, Tokens oder echten Aktionen. Das eingebettete Spiel selbst kann weiterhin Buttons und Anmeldefelder anzeigen.

## Windows-Abnahme

Nach automatischem Start `LAUNCHER_START_REQUESTED` oder `GAME_WINDOW_READY` kontrollieren. Der beste Server erscheint als `F2P_PING BEST_WORLD`, mit `applied_in_game=false`. Der Status bleibt `Bot: NICHT GESTARTET` bis ein verifizierter Live-Adapter vorhanden ist.

Quelle: https://legal.jagex.com/docs/rules/rules-of-runescape
