# Offline-Agentkern: Umsetzung der vorhandenen OSRS-Bot-Analyse

Stand: 22. Oktober 2026 • v0.4.0

Die bestehende Recherche `OSRS-BOT-ARCHITEKTUR-ANALYSE-2026-10-22.md` liefert
die Designprinzipien. Wir übernehmen ausschließlich offen dokumentierte,
allgemeine Softwarearchitektur, **keine** Client-Injektion, Paketemulation,
Umgehung der Anti-Cheat-Erkennung oder automatisiertes Spiel auf Jagex-Servern.

| Analyseerkenntnis | Umsetzung | Nachweis |
|---|---|---|
| Konsistente Snapshots (Microbot/Agenten) | `AgentSnapshot`: Tick, Herkunft, Zeitpunkt, Health, Inventar | OfflineAgentTests |
| Action-State-Modell (OSBot) | `ActionPhase`: Blocked, Planned, Confirmed, Failed | `ACTION_PLANNED`, `ACTION_CONFIRMED` |
| Versand ≠ Erfolg (RS-SDK) | `OfflineAgent.Confirm` verlangt beobachteten Effekt | Negativtest ohne Zustandsänderung |
| Priorisierte Aufgaben (Agentensysteme) | Recovery vor Banking vor Collecting | Prioritätstests |
| Datenfrische und Versionierung | Tick muss monoton; Snapshot <=3s alt | Duplicate/Stale Tests |
| Isolierte testbare Ausführung | `Simulate` verweigert Replay/External | Replay-Ausführungstest |
| Diagnostik | Agententscheidungen im Copy-Log und Export | GUI-Simulation |
| Verteilbarkeit | Bestehende Single-EXE und GitHub Auto-Updater | Windows-CI und User-Test |

## Nutzbares Testverfahren

1. Die EXE starten, den automatischen Updatewechsel bestätigen.
2. Auf **Offline-Agent: AUS** klicken. Der Button aktiviert die Simulation automatisch
   und wechselt zu **Offline-Agent: AN**.
3. Mindestens 35 Ticks laufen lassen. Danach **Log kopieren**.
4. Im Log nach `ACTION_PLANNED`, `ACTION_CONFIRMED`, `Bank` und `Recover` suchen.
   Fehler sollten in `ACTION_FAILED` oder `PLAN_BLOCKED` erscheinen.
5. Den Renderer ausschalten: Die Simulation und der Agent laufen unabhängig weiter.

## Grenzen und nächste Engineering-Schritte

- v0.4.0 nutzt einen **synthetischen** Zustand; die erzeugten Ressourcen und
  Lebenspunkte sind kein OSRS-Gamestate.
- Der lokale Simulator bestätigt Effekte sofort. Künftige Tests benötigen
  verzögerte, fehlende und widersprüchliche Acknowledgments, Timeouts
  sowie deterministische Replay-Fixtures.
- Der Scheduler und die Entscheidungslogik sollten später einen bounded
  Queue-Adapter erhalten, der klar zwischen Beobachtung und Ausführung trennt.
- Status/Version eines legitimen externen Datenadapters müssen explizit und
  sicher protokolliert werden. Offizielle Game-Automatisierung ist keine
  akzeptierte Quelle für diese Releasevariante.
- Jagex verbietet automatisiertes Gameplay und unerlaubte Clients. Eine
  Methode, ein Spielkonto vor Anti-Cheat-Erkennung oder Bans zu schützen,
  ist weder implementiert noch nachweisbar.

Primärregeln: https://legal.jagex.com/docs/rules/rules-of-runescape
und https://legal.jagex.com/docs/rules/macro-and-client-features-not-permitted


## Revision v0.4.1
- Der doppelte Inventarzustand aus v0.4.0 wurde entfernt. Der Simulator erzeugt nur noch Inventarereignisse aus einem konsistenten Zustand.
- Aktionsergebnisse werden erst in einem **späteren Tick** bestätigt; gleiche-Tick-Erfolge sind unzulässig.
- Eine deterministische Fehler-Injektion verwirft jede 17. Aktion und erzwingt einen klar klassifizierten Timeout mit anschließender Wiederaufnahme.
- Reale Jagex-Verbindung bleibt technisch nicht implementiert. Kein technisches Versprechen einer undetektierbaren Ausführung oder ausbleibenden Kontosperre.


## Revision v0.4.2 – Pointer-Modell (ohne Betriebssystemeingaben)
- Die GUI enthält ein getrenntes **virtuelles** Mausbewegungslabor mit deterministischen, glatten Kurven.
- Pointerzustand und Bewegungsmetrik sind von der Offline-Agenten-Engine vollständig entkoppelt.
- Die Interaktion mit dem Windows-Systemcursor, anderen Programmen oder offiziellen OSRS-Servern ist ausdrücklich **nicht** implementiert.
- Reproduzierbarkeitstests prüfen Endpunkte, Monotonie, Kurvenform und begrenzte Parameter.
- Aussagen zur Nichterkennung oder Bannfreiheit können aus diesen Tests nicht abgeleitet werden.
