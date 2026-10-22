# Mining → Grand Exchange: Offline-Szenariotests (v0.4.2)

**Keine offizielle OSRS-Anbindung.** Sämtliche Daten sind synthetisch.

## Simulationsmodell
- Virtuelle Mine (0,0) und virtuelle Grand Exchange (12,0), BFS-Vier-Nachbar-Wegsuche.
- Drei Erzadern mit je zwei Einheiten und simuliertem Respawn.
- 28 Inventarslots, nicht stapelbares Erz, Spitzhacke als ausgerüstet angenommen.
- Fiktiver Testpreis **45 virtuelle Münzen/Erz**; ausdrücklich kein OSRS-Marktpreis.
- Ein Verkauf ändert Inventar und Geld erst zwei Simulationsticks nach der Angebotsabgabe.
- Simulierte Ticks sind logisch, ohne echte 600-ms-Wartezeit.

## Testmatrix

| Name | Störung | Erwartetes Ergebnis |
|---|---|---|
| baseline | keine | 2 Zyklen, 56 Erz verkauft, 2520 virtuelle Münzen |
| rock-depletion | langer Respawn | Warten und Weiterabbauen, 1 Verkauf |
| route-detour | blockiertes Wegfeld | BFS-Umweg, 1 Verkauf |
| ge-rejection | Erstangebot abgewiesen | Erneutes Angebot ohne Inventarverlust |
| no-route | gesperrter Kartenquerschnitt | Nach 8 Versuchen sicherer Abbruch mit 28 Erz |

Invarianten bei jedem Tick: `mined == sold + inventory`, `coins == sold * 45`,
`0 <= inventory <= 28`, maximal ein Feld Bewegung pro Tick.
GE-Bestätigungen frühestens zwei Ticks nach Abgabe, deterministischer Replay-Test.

## Testausführung

`dotnet run --project native/OSRSLiveDiagnostics.csproj -c Release -- --scenario-tests`

Die CI führt denselben Befehl aus. In der Windows-EXE auf **Mining → GE testen**
klicken, anschließend **Log kopieren**. Erwartet wird `SUITE_PASS` mit fünf
`SCENARIO_RESULT`-Zeilen. Im Blockadetest ist `ROUTE_ABORT` beabsichtigt.

Nicht enthalten: echte OSRS-Karte, echte GE-Preise, reale Spielfunktionen,
Maus-/Tastaturaktionen oder Verhalten zur Umgehung von Anti-Cheat.
