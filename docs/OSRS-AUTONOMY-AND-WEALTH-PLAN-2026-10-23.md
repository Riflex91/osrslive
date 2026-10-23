# OSRS Live: Autonomie, Skill-Maxing und langfristige GP-Optimierung
Stand 23. Oktober 2026. Ergebnis einer erneuten Untersuchung der öffentlich verfügbaren Bot-Architekturen.

## Zielformulierung
**24 Skills jeweils Level 99, Total Level 2376** (einschließlich Sailing).
Danach und parallel möglichst hoher GP-Bestand; "maximal GP" ist keine endliche
erreichbare Obergrenze. Optimize stets *Zeit, Fortschritt und Nettogewinn*
unter Account-, Bank-, Quest-, Sicherheits- und Liquiditätsbedingungen.
Quellen: https://oldschool.runescape.wiki/w/Total_level und
https://oldschool.runescape.wiki/w/Skill_mastery.

## Überprüfte Systeme und abgeleitete Eigenschaften
| System | Verifizierbare Erkenntnis | Architekturentscheidung für osrslive |
|---|---|---|
| Microbot | RuneLite-Fork, Script-Executors, per Tick aktualisierte Queryable Entity Caches, priorisierte Blocking Events. | Snapshot-Cache, Event-Interrupts getrennt von der Strategie, keine blockierende UI. |
| OSRSB/OsrsBot | Große domänenbasierte Script-API auf RuneLite, separates Script-Lifecycle. | Bank/GE/Quest/Training in eigenständige Action-Module auslagern, Strategie darüberlegen. |
| mtc53/osrs-bot | Getrennter Zustand/Taskrunner; liest clientbasierte Positions-, Menü- und Widgetinfos, kontrolliert Klickziel vor Klick. README: Plugin nicht kompiliert. | Unveränderliche Snapshots, prüfbare Preconditions und postconditions; keine Live-Funktionsfähigkeit behaupten. |
| RS-SDK / LostCity | Gateway und automatisierter SDK-Client im **Emulator**; explizit: Aktion dispatcht ist noch kein bestätigter Effekt. Langfristige Agenten-Benchmarks. | Strategie -> Unterauftrag -> beobachtete Wirkung -> Retry/Recovery; deterministische Replays. |
| RuneMate | Eigene Produktwebsite: permanent geschlossen am 07.08.2026. | Kein Integrationspfad. |
| OSBot / DreamBot / TRiBot / PowBot | Historische APIs und Script-/Launcher-Muster, aktuelle Closed-Source-Betriebsfähigkeit nicht zuverlässig verifiziert. | Modularität und Lifecycle übernehmen, keine proprietären Client-Interna behaupten. |

Quellen:
- https://github.com/chsami/Microbot/blob/main/docs/ARCHITECTURE.md
- https://github.com/OSRSB/OsrsBot
- https://github.com/mtc53/osrs-bot
- https://github.com/MaxBittker/rs-sdk
- https://www.runemate.com/

## Zielarchitektur mit überprüfbaren Schnittstellen

1. **Account snapshot:** Level/XP aller 24 Skills, Quest-/Gebiets-Unlocks,
   Member-Status, Bank, Gold, Inventar, Ausrüstung, Position, Schadens-/Risiko-
   Status, Beobachtungszeit, Datenquelle. Fehlende Daten = keine Live-Aktion.
2. **Ziel- und Methodenplaner:** priorisiert XP-Fortschritt und
   GP-Gewinn nach Zielprofil, Liquidität, Kaufvoraussetzungen,
   Questbedingungen, Preisaktualität und geschätztem Aufwand.
3. **Task-Layer:** konkrete Module für Mining, Navigation, Banking,
   Grand Exchange, Questing, Kampf, Ausrüstung, Erholung.
4. **Action-Orchestrator:** führt keine Aktionen auf Basis veralteter Daten aus,
   protokolliert Absicht, Ausführung, beobachteten Effekt und Fehler/Timeout.
5. **Preis-/Raten-Quellen:** Live-Preise via öffentliche
   https://oldschool.runescape.wiki/w/RuneScape:Real-time_Prices
   mit korrektem User-Agent und Cache; XP/h und Netto-GP/h getrennt aus
   gemessenen Läufen; keine ungestützten Gewinnversprechen.
6. **Replay & CI:** alle Datenabhängigkeiten fälschbar für Offline-Tests;
   deterministische Szenarien für Zeitouts, Bank-/GE-Probleme, fehlende
   Preisupdates, unzureichende Goldreserven und unerwartete Quests.
7. **Genehmigter Live-Adapter:** derzeit kein Client, keine Eingabeschnittstelle,
   kein Login, keine Sessionintegration. Die behauptete Jagex-Sondergenehmigung
   ist nicht unabhängig bestätigt. Eine Sperrvermeidungsgarantie existiert nicht.

## Implementiert in 0.5.0
- Pure `GoalPlanner.Rank` mit 24-Skill-Snapshot und `GoalPreference`.
- Strategische Gewichte `Balanced`, `MaxSkills`, `MaxGold`;
  wenn alle 24 Skills 99 sind, nur noch GP.
- Marktpreisalter (maximal 15 Minuten), Geld-Reserve, Quest-Freischaltungen,
  Member-Gates, Level-Voraussetzungen.
- Rangfolge mit Scores und separaten Gründen für nicht zulässige Angebote
  (`Rejected`-Zähler).
- Unit-Tests in `--self-test`; automatisch generiertes
  `GOAL_PLANNER`-Demo-Log beim Aktivieren des Offline-Agenten.

**Achtung:** Die Beispiel-Levels, Preise, Goldstände und XP/h-Raten sind
absichtlich fiktive Fixtures. Kein realer OSRS-Account wird gelesen,
bewegt, gelevelt oder gehandelt.

## Entwicklungsreihenfolge / messbare Meilensteine
- P0: echten, zugelassenen Account-/Spielzustand lesen; Quellvalidierung
  und beobachtbare Verbindungsdiagnose, ohne Zugangsdaten in Logs.
- P1: Bereitstellung eines Aktivitätskatalogs für alle 24 Skills,
  Quests und Kosten. XP-/GP-Raten belegt statt erfunden.
- P1: Abhängigkeiten/Unlocks als Graph mit Pfadoptimierung implementieren.
- P1: Navigation, Handel, Banking, Training mit Status/Timeout/Recovery.
- P1: Long-run-Replay mit persistenten Checkpoints.
- P2: adaptive Raten, dynamische GE-Kosten, Budgetschutz und robustes Replanning.
- P2: nachweisbare Account-Zielerreichung anhand tatsächlicher Spielzustände.
- BLOCKED: Freigabe einer externen Jagex-Eingabeschnittstelle,
  fehlender Zugriff auf offizielle Spielwelt in der vorhandenen EXE.

## Regelwerk und Zuverlässigkeitsgrenzen
Die allgemeinen Regeln untersagen automatische Eingaben und unzulässige
Clients; eine vom Nutzer berichtete Einzelgenehmigung wird nicht
als allgemeine Ausnahme für andere Konten behandelt.
https://legal.jagex.com/docs/rules/macro-and-client-features-not-permitted
Keine Anti-Cheat-Unsichtbarkeits- oder Bannfreiheitsgarantie.
