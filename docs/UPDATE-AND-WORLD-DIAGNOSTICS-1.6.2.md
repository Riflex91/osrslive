# OSRS Play 1.6.2 – Weltlisten-Diagnose und automatische Updates

Stand: 24.10.2026. Grundlage: Live-Log von 1.6.1 mit erfolgreichem `LAUNCHER_RESTORE_REQUESTED`, Start des offiziellen `osclient.exe`, `OPEN_WORLD_SELECTION_REQUESTED` und anschließendem `TARGET_WORLD_NOT_UNAMBIGUOUSLY_VISIBLE`.

## Weltwahl

Der Launcher-Einstieg funktioniert in diesem Log. Die Weltliste öffnet sich, doch der Zielwelteintrag ist nicht eindeutig erkannt. Aus dem Log allein lässt sich nicht feststellen, ob keine verlässlichen Weltzellen gelesen werden, die Zielwelt auf einer anderen Seite liegt oder die Navigation-Pfeile nicht erkannt werden.

- `WorldGridOcr` wirft eine bereits nach aufsteigenden Spalten validierte Groberkennung nicht mehr weg, wenn der zusätzliche Kontaktbogen nur einige Zahlen liefert. Neue Zellen werden nur nach kombinierter Validierung der Spalte aufgenommen; widersprüchliche oder isolierte Nummern autorisieren keine Eingabe.
- `WorldPageReader` kann einen sichtbaren Seitenpfeil schon bei einer geprüften aufsteigenden Spalte mit sechs Nummern betrachten, statt pauschal 20 Zellen zu verlangen. Die vorhandenen bildgestützten Pixelkriterien und die Grenzen für 12 Seitenaktionen sowie unveränderte Versuche bleiben bestehen.
- `WORLD_GRID_DIAGNOSTIC` protokolliert ausschließlich die Anzahl verifizierter Zellen, die Zahl grober dreistelliger Nummern, beobachtete Weltspanne, Zielwelt, Anzahl Zieltreffer, Seitenpfeil-Erkennungen und Entscheidungszustand. Keine Chatnachrichten, Bilder, Roh-OCR oder Zugangsdaten werden protokolliert.
- Fehlt weiterhin eine eindeutige Auswahl, bleibt die Eingabe gesperrt. Welt 399 wird nicht blind angeklickt. Automatischer Login oder echte Trainingsausführung sind weiterhin nicht Ende-zu-Ende bestätigt.

## Update-Lebenszyklus

- **Beim Start:** Die Anwendung prüft das über HTTPS geladene Release-Manifest auf `main`. Nur eine neuere Version mit streng eingeschränktem GitHub-Release-Link wird heruntergeladen. Die heruntergeladene EXE wird mit dem im Manifest veröffentlichten SHA-256 geprüft. Ein Hilfsprozess ersetzt sie nach Beenden der alten Instanz und startet sie neu; bei Installationsfehlern wird ein Rollback versucht.
- **Laufende Anwendung:** Alle 30 Minuten wird eine erneute Prüfung angestoßen, aber nur, wenn kein offizieller Spiel- oder Launcher-Prozess/Fenster aktiv ist und keine visuelle Aktion läuft. Während Login oder Gameplay wird die Aktualisierung zurückgestellt. Dadurch werden Spielaktionen oder Transaktionen nicht absichtlich durch einen Update-Neustart unterbrochen. Bei dauerhaft laufendem Spiel wird spätestens beim nächsten Anwendungsstart erneut geprüft.
- **Nach Merge:** Windows-CI baut und testet den Stand von `main`, veröffentlicht nach Erfolg ein GitHub-Release mit einer einzelnen Windows-EXE und aktualisiert erst danach `update-exe.properties` mit Version, festem Release-Link und Prüfsumme.
- **Vertrauensgrenze:** SHA-256 bestätigt Übereinstimmung mit dem Repository-Manifest, ist keine eigenständige kryptografische Herausgebersignatur. Die Aktualisierung ist vom erreichbaren GitHub-Host und der Release-Veröffentlichung abhängig.

Die bisherige öffentliche Release-/Manifest-Version auf `main` war bei der Entwicklung noch 1.1.0. Der Merge und ein erfolgreicher Veröffentlichungsworkflow sind daher notwendig, bevor 1.6.2 als automatisches Update ausgeliefert wird.

## Tests

CI prüft Build, Selftests, Windows-OCR-Fixtures, UI-Layout, Hintergrund-Fenstertransport und den Single-EXE-Publish. Neue Regressionstests decken unvollständige fokussierte Welt-OCR, verworfene widersprüchliche Zellen und einzelne unverbundene Nummern ab. Die reale Weltlisten-Navigation bleibt am offiziellen Client anhand eines weiteren Logs zu bestätigen.
