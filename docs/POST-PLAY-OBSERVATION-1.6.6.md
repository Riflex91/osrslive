# OSRS Play 1.6.6 – Beobachtung nach dem Play-Now-Klick

## Anlass und belegter Stand

Der vollständige Log vom 24.10.2026 für 1.6.5 zeigt:
- 18:28:31 `AWAITING_INDEPENDENT_PLAY_BUTTON_CONFIRMATION`
- 18:28:33 `PLAY_SELECTED_CHARACTER_REQUESTED` und ein gesendeter Fensternachrichten-Klick
- 18:28:39, 18:28:47 und 18:28:55 veränderte aufgenommene Spielbilder
- Kein belastbar erkannter Bildschirm danach; weiterhin `WAITING_FOR_RECOGNIZED_SCREEN` und `WAITING_FOR_SKILLS`

Damit ist der Play-Klick gesendet und das Bild hat sich danach verändert. Ob der offizielle Client den Klick verarbeitet, die Verbindung hergestellt oder den Charakter eingeloggt hat, ist noch nicht bestätigt. Es gab **kein** neues Screenshot des Bildschirms nach dem Klick; sein genauer Inhalt ist unbekannt.

## 1.6.6

- `PostPlayScreenReader` liest **nur nach** einem gesendeten Play-Now- bzw. Enter-Game-Klick eine begrenzte Region im mittleren Bereich der offiziellen 1000×600-Spielfläche erneut mit vergrößerter Windows-OCR.
- Der zusätzliche OCR-Durchlauf erkennt ausschließlich vollständig passende UI-Formulierungen `Welcome`, `CLICK HERE TO PLAY`, `Connecting to server`, `Please wait` oder `Loading`. Private Chatnachrichten, Charaktername, E-Mail, Passwörter und OCR-Rohdaten werden nicht ausgegeben. Seitenleisten und Chat liegen außerhalb der Fokussierung.
- `POST_PLAY_SCREEN_DIAGNOSTIC` dokumentiert nur den durch `GameScreenReader` ermittelten Zustand sowie boolesche Hinweise zu Welcome/Enter/Connecting/Chat und die Gesamtzahl erkannter Labels. Identische Meldungen werden zeitlich gedrosselt.
- Während `Connecting` beobachtet wird, meldet die Runtime `POST_PLAY_CONNECTING` und löst keine weitere Eingabe aus.
- Bei einem OCR-erkannten `CLICK HERE TO PLAY` und unabhängig erkanntem `Welcome` wird die seit 1.6.5 bereits bestätigte Weltprüfung beibehalten. Die nächste bestätigende Enter-Game-Eingabe verlangt zusätzlich zwei unabhängige Frames mit mindestens 500 ms Abstand und derselben Begrenzung.
- Unbekannte Bildschirme bleiben `POST_PLAY_WAITING_FOR_RECOGNIZABLE_SCREEN`. Die Runtime klickt nicht blind auf eine angenommene Bildschirmmitte und interpretiert Bildänderungen **nicht** als erfolgreichen Login.
- F8-Pause, vorhandener Hintergrundtransport, konservative Login-/Passwortsperren und die bestehenden Spielaktionsgrenzen bleiben bestehen.

## Testgrenzen

Die Windows-Selftests prüfen bekannte UI-Texte, ungeeignete Bildgrößen, doppelte Aktionshinweise und die Policy, nach der ein Enter-Game-Klick ohne Welcome und vorher bestätigte Welt nicht zulässig ist. Build und Test bestehen nicht für einen vollständigen Live-Login-Nachweis.

Beim nächsten Live-Test `last-session.log` und nach Möglichkeit einen Screenshot des **neuen Bildschirms nach Play Now** übermitteln, sofern keine sensiblen Daten sichtbar sind. Besonders aufschlussreich sind `POST_PLAY_SCREEN_DIAGNOSTIC`, `POST_PLAY_CONNECTING`, `AWAITING_INDEPENDENT_ENTER_GAME_CONFIRMATION`, `ENTER_GAME_REQUESTED` und `GAME_OBSERVER STATE`.

Automatische Updates werden beim Start oder bei einer untätigen offiziellen Sitzung über Release/Manifest verteilt; ein laufender Jagex- oder OSRS-Prozess wird dafür nicht absichtlich beendet.
