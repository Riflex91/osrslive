# OSRS Play 1.6.7 – Zweiten Spieleinstieg erkennen

## Live-Befund (24.10.2026, 18:41)

Version 1.6.6 hat den Play-Now-Button um 18:41:28 selbstständig angeklickt. Danach wechselte der offizielle Spielclient sichtbar zur grafischen Willkommensseite mit:

- Pergamentbanner „Welcome to Old School RuneScape“ oben
- Drei nebeneinanderliegenden Informationskacheln in der Bildmitte
- Dunkelroter mittlerer Schaltfläche **CLICK HERE TO PLAY**
- Seitlicher Iconleiste rechts

Trotzdem meldete `POST_PLAY_SCREEN_DIAGNOSTIC | stage=Unknown welcome_marker=False enter_marker=False ...`. Ursache: Die vergrößerte Nach-Play-OCR untersucht erst ab Spiel-Y=100, während das Willkommensbanner bei Y≈12–58 liegt. Die stilisierte Beschriftung der roten Schaltfläche wurde ebenfalls nicht als exakter OCR-Text erkannt. Zusätzlich priorisierte die Policy die erste Play-Now-Willkommensseite vor der zweiten Enter-Game-Phase; damit konnte sie bei erkannten „Welcome to Old School RuneScape“-Worten irrtümlich eine neue Weltfußzeile verlangen.

## Implementierung

`NativeWelcomePanelReader` erkennt die originale 1000×600-Willkommensseite ausschließlich anhand **gemeinsam** vorhandener Bildschirmmerkmale:

- Helles Pergamentbanner oben, kalibrierte neutrale Farbverteilung
- Dunkelrote zentrale Karte bei Spiel-X≈375–585, Y≈299–375
- Linke und rechte Kachel ohne ein ebenso dominierendes Rotfeld
- Weiße Beschriftung in der mittleren Karte

Andere Ansichten (falsche Auflösung, leere Ansicht, Passwort-/Authenticator-Bildschirm, Weltenliste oder rote Karte ohne weiße Beschriftung) erhalten **keine** Klickfreigabe. Die festen Bildbereiche sind Suchbereiche, keine aus einer Kontoposition abgeleiteten Welt- oder Charakterdaten. Der Klickpunkt liegt innerhalb der nachgewiesenen mittleren Schaltfläche.

Die Policy behandelt einen bildgeprüften `EnterButton` **vor** der ursprünglichen Play-Now-Willkommensseite, verlangt weiterhin die zuvor am echten Client erkannte ausgewählte Welt und lässt kein zweites Enter ohne diese Beobachtung zu. `VisualStartup` verlangt wie in 1.6.6 zwei frische, voneinander unabhängige Beobachtungen derselben Schaltfläche im Abstand von mindestens 500 ms. Entsprechende Zustände sind `AWAITING_INDEPENDENT_ENTER_GAME_CONFIRMATION` und `ENTER_GAME_REQUESTED`.

`POST_PLAY_SCREEN_DIAGNOSTIC` enthält zusätzlich `native_enter_card=true/false` und meldet nur boole'sche UI-Marker – keine Kontonamen, Screenshotdaten, Chatnachrichten oder OCR-Rohtexte.

## Tests und Grenzen

Die Kalibrierung wurde anhand der vom Anwender bereitgestellten Originalaufnahme vorgenommen, ohne das Bild oder darin vorkommende Sitzungsinformationen ins Repository zu kopieren. In dieser Aufnahme lagen die überprüften Pergament- und Rot-Anteile bei etwa 94 % bzw. 87 % der entsprechenden Suchbereiche, die roten Anteile der Seitenkacheln unter 2 % und mindestens 117 weiße Stichprobenpixel in der zentralen Beschriftung. Die konservativen Programmschwellen liegen darunter und müssen kombiniert erfüllt sein.

Windows-Selftests prüfen einen synthetischen 1000×600-Willkommensbildschirm sowie leere Bilder, falsche Abmessungen, Weltlisten, Authentifizierungsseiten und fehlende Schaltflächenbeschriftung. Policy-Tests prüfen die Priorität des zweiten Einstiegs und die weiterhin notwendige zuvor bestätigte Welt.

Eine erfolgreiche CI beweist die interne Erkennung; sie bestätigt noch **nicht**, dass der Windows-Message-Klick vom offiziellen `osclient.exe` angenommen oder der Charakter in der Welt geladen wurde. Dafür wird ein weiteres echtes Sitzungslog nach 1.6.7 benötigt.

## Nächster Live-Test

OSRS Play 1.6.6 beim nächsten Start auf 1.6.7 aktualisieren lassen, Launcher und Spiel automatisch starten und nach dem ersten Play-Klick beobachten. Im Log besonders `POST_PLAY_SCREEN_DIAGNOSTIC` mit `native_enter_card=true`, `AWAITING_INDEPENDENT_ENTER_GAME_CONFIRMATION`, `ENTER_GAME_REQUESTED` und `GAME_OBSERVER STATE` prüfen. Optional einen Screenshot nach dem **zweiten** Klick senden; bitte nur ohne private Chat- und Kontodaten.
