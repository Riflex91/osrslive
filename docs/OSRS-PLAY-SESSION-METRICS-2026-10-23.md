# OSRS Play v0.6.1 – Sessionbezogene Tick- und Stop-Diagnose

## Befund aus echtem Windows-Log vom 23.10.2026
- 70 `GAME_TICK`-Einträge bei zwei Offline-Bot-Sitzungen (41 + 29).
- Die alte Statistik zählte 69 Intervalle mit Ø 699,72 ms / Max 7465,11 ms.
- Die 7465 ms sind die **Pause zwischen Sitzungen**, keine Zeitverzögerung während einer laufenden Sitzung.
- Weitere Beobachtung: Beim Stoppen gab es noch ungeklärte, geplante Aktionen, die ohne Status verloren gingen.
- Warnungen 6 = 3 Simulator-Beispielwarnungen + 3 beabsichtigte synthetische Timeout-Tests.

## v0.6.1 – Änderungen
1. `TickStats` setzt die vorherige Tick-Referenz bei
   `SIMULATOR START` und `SIMULATOR STOP` zurück. Die
   Pausenzeit wird nicht mehr in Durchschnitt und Maximum eingerechnet.
   Die Gesamtzahl der Game-Ticks bleibt vollständig erhalten.
2. Simulator-Start, Ende und Ticks tragen eine explizite
   `session=N`-Markierung.
3. Ein offener `AgentPlan` wird bei Stop oder Disable als
   `OFFLINE_AGENT ACTION_CANCELLED` gemeldet und nicht als Erfolg bestätigt.
4. Session-Generation schützt den Timer gegen verspätete
   Callback-Ausführungen einer früheren Sitzung.
5. Regressionstests: zwei Sitzungen mit Pause, Tickstatistik,
   keine verspäteten Ticks/Bestätigungen nach Stop, Wiederanlauf und Session-IDs.

## Was damit *nicht* getestet wird
Keine RuneLite-/Jagex-Verbindung und keine echte Interaktion im Spiel. Die
Aktionen `Collect`, `Recover` und `Bank` bleiben simuliert.
