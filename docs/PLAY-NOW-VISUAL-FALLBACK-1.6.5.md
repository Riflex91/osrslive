# OSRS Play 1.6.5 – Play Now-Erkennung

## Reproduzierter Fehler

Beim Live-Test vom 24.10.2026 wird die nach Ping empfohlene **W553** tatsächlich im originalen Client angezeigt. Die Aufnahme zeigt in der Mitte den Button „Play Now“, aber die allgemeine Windows-OCR meldet `PLAY_BUTTON_NOT_RECOGNIZED`. Hier geht es nicht mehr um die Weltenliste: Es fehlt die robuste Erkennung der stilisierten weißen Schaltflächenbeschriftung.

## Implementierung

- `WelcomePlayButtonReader` wird nur für eine offizielle 1000×600-Aufnahme mit gelesenem „Welcome to Old School RuneScape“-Titel aufgerufen. Auf Passwort-, Authenticator- oder Weltlisten-Seiten bleibt der neue Zweig deaktiviert.
- Es werden die **vier Kanten** eines geschlossenen dunklen Rechtecks in der Bildschirmmitte geprüft. Eine dunkle Fläche oder ein beliebiger Text allein genügt nicht.
- Im obersten Teil des Rechtecks müssen genügend helle, annähernd neutrale Pixel der weißen Beschriftung vorkommen; im unteren Teil genügend gelbe Pixel der getrennten Charakterzeile. Diese wird **nicht gelesen, gespeichert oder protokolliert**.
- Bei ausreichend Bildbelegen wird die rechteckige, innerhalb des Buttons liegende Klickregion als speziell validierter `PlayButton` markiert. Die Entscheidung bleibt durch den unabhängigen OCR-Nachweis des sichtbaren Welt-Footers und die Übereinstimmung mit der empfohlenen Welt geschützt.
- Die `VisualStartup`-Runtime verlangt **zwei unabhängige Screenshots** derselben Klickregion mit mindestens 500 ms Abstand. Der Zwischenzustand `AWAITING_INDEPENDENT_PLAY_BUTTON_CONFIRMATION` wird im Log ausgegeben. Erst anschließend wird das vorhandene begrenzte Windows-Message-Click-Verfahren benutzt. F8 pausiert weiterhin, alte Frames dürfen keinen Klick auslösen.
- Die bisherige gewöhnliche `Play Now`-OCR bleibt als alternative Erkennungsmethode erhalten. Der Fokus liegt auf dem beobachteten Layout; bei anderen Client-Skins oder ungeprüften Abmessungen blockiert die Bild-Fallback-Logik.

## Verifikation

Die im Chat hochgeladene 1.6.4-Aufnahme bestätigt die vier dunklen Button-Kanten (bei der lokalen Bildprüfung jeweils 100 % der Stützpunkte), 527 sehr helle Pixel im weißen Beschriftungsbereich und 291 gelbe Pixel darunter. Diese Zahlen dienen nur der lokalen Design-Validierung und werden nicht in der Anwendung protokolliert.

Der Windows-Selftest prüft die erkennbare synthetische Button-Geometrie samt Textbereichen und das sichere Ablehnen von schwarzen/leeren oder ungeeigneten Ansichten. Ein erfolgreicher CI-Build belegt **nicht**, dass `osclient.exe` den danach gesendeten Klick annimmt. Das muss mit dem nächsten Live-Log (`AWAITING_INDEPENDENT_PLAY_BUTTON_CONFIRMATION`, `PLAY_SELECTED_CHARACTER_REQUESTED`, nachfolgender Spielbildschirm) geprüft werden.

## Update

PR-Builds sind Testartefakte. Nach einem erfolgreichen Merge nach `main` publiziert GitHub Actions automatisch Release-EXE und SHA-256-Manifest, sodass 1.6.4 beim nächsten Start auf 1.6.5 wechseln kann, ohne eine aktive Spielsitzung für Updates zu unterbrechen.
