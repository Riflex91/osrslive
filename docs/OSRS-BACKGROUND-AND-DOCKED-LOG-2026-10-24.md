# Feste Spielansicht und Hintergrundtransport – 24.10.2026, Version 1.4.0

## Referenz RuneMate

Die offizielle RuneMate-Community zeigt einen Hinweis auf die Einstellung zum 7. August 2026. Die aktuelle Hauptseite beschreibt RuneMate Vault. Der frühere Developer-Pfad lieferte 404; der öffentlich verlinkte API-Pfad leitete auf eine GitLab-Anmeldung um. Ein aktuell lauffähiger RuneMate-Client wurde nicht installiert oder ausprobiert. Veraltete Produktwerbung, insbesondere Aussagen zur Unerkennbarkeit, wird nicht als technischer Funktionsnachweis übernommen.

Quellen, am 24.10.2026 gelesen:

- https://www.runemate.com/
- https://www.runemate.com/community/threads/world-hopping-in-rs3.11114/
- https://www.runemate.com/developer
- https://runemate.gitlab.io/community/runemate-game-api/

Die Umsetzung folgt dem gewünschten Bedienprinzip: Spiel beobachten, Agentenstatus lesen, Log darunter öffnen. Sie enthält keinen Nachbau proprietärer RuneMate-Interna.

## Fenstergeometrie

`DockedWindowLayout` definiert einen festen Spielbereich von 1000 × 600 Pixeln, feste Kopf-/Status-/Toolbar-Zeilen und einen optionalen 180-Pixel-Logbereich direkt unter dem Spiel. `MainWindow` verwendet einen festen Rahmen ohne Maximieren. Aus-/Einblenden ändert die Gesamthöhe, nicht die Spielgeometrie. Die eingebettete Fenstergröße wird um native Rahmenränder korrigiert; ursprüngliche Stile und Außenmaße werden beim Trennen wiederhergestellt.

Der Layouttest prüft echte WinForms-Control-Abmessungen in beiden Zuständen. Die Prüfung läuft ohne Spielanbindung und ohne Eingaben an andere Apps.

## Hintergrundaufnahme

`BackgroundWindowCapture` verwendet Windows Graphics Capture mit einem freilaufenden D3D11-Frame-Pool. Aufgenommen wird die Compositor-Oberfläche des eigenen Hostfensters; ausgewertet wird der daraus ausgeschnittene Clientbereich des offiziellen Spiels. Andere überlagernde Anwendungen werden nicht über eine Desktop-Bildkopie mitgelesen. Der Launcher wird nicht durch den OCR-Spielpfad verarbeitet. Bilder und OCR-Volltexte werden nicht gespeichert oder exportiert.

Der Frame-Zeitstempel stammt aus Windows' SystemRelativeTime. Zu alte Frames werden abgelehnt. Ein kleiner Vorlauf des Compositor-Zeitstempels gegenüber QPC wird begrenzt berücksichtigt. CPU-Bildkopien sind begrenzt; bei statischen Oberflächen wird bei Bedarf eine neue Sitzung erzeugt, damit ein verworfener letzter Änderungsframe nicht dauerhaft den alten Zustand hinterlässt. Fensterwechsel und neue Geometrie starten eine neue Capture-Generation. Alte Callbacks können deren Ergebnisse nicht überschreiben.

Native Capture-Close-Aufrufe laufen abseits des UI-Threads, weil das Aufräumen auf Compositor-Arbeit warten kann. Das verhindert ein Hängen des Fensters beim Schließen. Minimierung gibt keine Aktionen frei; ohne frische Frames verfallen Zielvorschläge.

Microsoft-Primärquellen:

- https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.direct3d11captureframepool.createfreethreaded
- https://github.com/microsoft/Windows.UI.Composition-Win32-Samples/issues/138
- https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr.ocrengine.recognizeasync

## Eingaben

`BackgroundWindowInput` adressiert Mausbewegung, Down und Up direkt an das ermittelte Fenster/Render-Child. Koordinaten werden auf das Child umgerechnet, Prozesszugehörigkeit und Grenzen geprüft. Die Eingabeschicht verwendet keinen globalen Mauszeiger und fordert keinen Vordergrund an. F8, Pause und Abbruch bleiben wirksam; ein Down wird auch bei Abbruch durch Up beendet.

Ein erfolgreicher PostMessage-Aufruf bestätigt nur die Aufnahme in die Windows-Nachrichtenwarteschlange. Erst eine folgende passende Beobachtung kann eine Spielaktion bestätigen. Ein Client kann diese Nachrichten ignorieren oder selbst Fokusregeln anwenden. **Die Annahme durch den offiziellen `osclient.exe` ist noch nicht abschließend bestätigt.** Es wird bei Fehlschlag nicht auf SendInput oder Vordergrund-Umschalten zurückgefallen.

Der Integrationstest verwendet zwei eigene überlagerte Fenster: Eine blaue Zielfläche muss trotz roter Abdeckung blau aufgenommen werden; ein an ihr Child adressierter Klick muss genau einmal ankommen und eine danach aufgenommene grüne Fläche ergeben. Ohne parallele Nutzereingaben werden Cursor und Vordergrund auf Unverändertheit geprüft. Bei erkennbarer gleichzeitiger physischer Eingabe wird dieser Vergleich ausdrücklich als nicht auswertbar gemeldet; daraus folgt kein behaupteter Fokusnachweis. Der Test greift nicht auf ein Spielkonto zu.

## Spielzustand und Zielplaner

Der neue Leser erkennt Spiel-HUD und Charakter-Prompt, lokalisiert die Skilltafel an „Total level“ und liest die 24 Basislevel in vergrößerten Zahlen-Ausschnitten. Die Summe muss stimmen. Zwei unabhängige zeitlich getrennte Frames müssen dieselben Werte liefern. Ein gecachter Frame, unvollständige Daten, Charakterwechsel oder veraltete Lesungen reichen nicht für den Zielplaner.

Ein bestätigter visueller Stand erzeugt nun tatsächlich einen F2P-Zielvorschlag und kann einen lokalen Level-Checkpoint speichern. Charakter-Schlüssel werden gehasht, der Klarname wird nicht in den Checkpoint geschrieben. Das ist eine pseudonyme lokale Ablage, keine serverseitige Identitätsbestätigung. Gespeicherte Werte werden beim Neustart nicht als aktuelle Sitzungsdaten ausgegeben.

Auf dem echten Client wurden in der vorangegangenen Untersuchung Welt 397 gewählt, Play Now und der nachfolgende Welcome-Button bedient sowie die Skilltafel geöffnet. Diese Schritte erfolgten über Computer Use; sie belegen **nicht** die automatische Hintergrundausführung dieser neuen Eingabeschicht. Die lokale GUI-Prüfung am echten eingebetteten Client wurde durch Escape beendet; der neue Hintergrundweg wurde dort noch nicht vollständig verifiziert.

## Prüfstand und Grenzen

- Lokaler Build ohne Warnungen/Fehler.
- Selftests: bestehende Regressionen, mehrspaltiges Weltraster, Skill-Gesamtsumme, unabhängige Frames, Verfall, Charakterwechsel, lokale Checkpoints, Beschaffungspolicy.
- Windows-OCR-Fixture erfolgreich.
- WinForms-Layouttest erfolgreich: festes 1000×600-Spiel, 180-Pixel-Log darunter, keine Spielverkleinerung beim Umschalten.
- Hintergrundtransport-Test mit verdecktem eigenem Fenster und anschließender Bildbestätigung erfolgreich. Maus-/Fokusvergleich bei lokalen Durchläufen wegen paralleler Benutzerinteraktion teilweise nicht auswertbar.

Noch offen: echter Hintergrund-Ende-zu-Ende-Test am offiziellen Client; OCR der vollständigen echten Skilltafel unter der neuen festen Geometrie; nicht sichtbare Weltseiten; Navigation, Skill-Aktivitäten, Inventar-/Bankerkennung, Handel, Rezepte und Ausrüstungs-Upgrades. Die bisherigen Ziel-/Beschaffungsregeln ersetzen diese fehlenden Executor-Routinen nicht. Vollständiger autonomer Maxing-Betrieb sowie Betrieb bei minimiertem Client werden nicht als fertig ausgegeben.
