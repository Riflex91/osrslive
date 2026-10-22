# OSRS-Bot-Systeme: technische Vergleichsanalyse (22.10.2026)

> **Umfang und Belegniveau:** Auswertung öffentlich einsehbarer Quelltexte, API-Dokumentation und Produktbeschreibungen. Keine Anmeldung bei kommerziellen Bots, keine Inspektion proprietärer Binärdateien, kein Anschluss an Jagex-Server, keine empirischen Vergleichsbenchmarks. Deshalb **kein** Anspruch, die internen Implementierungen nicht offengelegter Clients „bis ins letzte Detail“ zu kennen. Unbekanntes ist explizit gekennzeichnet.

## 0. Kontext, Compliance und Stand 2026

- Offiziell zugelassen sind der Jagex-Client und die zugelassenen Drittanbieter RuneLite und HDOS. Ein eigener Bot-Client ist dadurch nicht legitimiert.
- Jagex untersagt u. a. unzulässige direkte Spielweltkommunikation, modifizierte Spielclients und Eingabeautomatisierung. Nutzung auf offiziellen Servern kann zur Kontosperre führen.
- Am 28.01.2026 wurde der Legacy-Java-Client als eigenständige Option eingestellt. **Java-Scripting-APIs** sind damit nicht generell funktionslos; aber ältere Architekturannahmen zu Java-Injection auf dem alten Spielclient sind obsolet.
- Marktverfügbarkeit ist im Oktober 2026 besonders volatil. Aussagen zu Produktstatus ohne Primärbestätigung sind als „laut Drittquelle“ markiert. Produktwerbung ist kein Funktionsnachweis.

Quellen: [Jagex Macro-Regeln](https://legal.jagex.com/docs/rules/macro-and-client-features-not-permitted), [Jagex Regelwerk](https://legal.jagex.com/docs/rules/rules-of-runescape), [OSRS Game Clients](https://oldschool.runescape.wiki/w/Game_client), [Legacy-Java-Abschaltung Jan. 2026](https://games-manuals.com/updates-patchnotes/old-school-runescape-1343370/holy-moleys--the-hooded-slayer-helm-are-here--january-21st-1822556746156364).

## 1. Klassifikation technischer Bot-Architekturen

| Klasse | Zustandsquelle | Aktionsschnittstelle | Vorteile | Wesentliche Schwächen |
|---|---|---|---|---|
| RuneLite-Fork mit eingebetteter API | Spielclientinterne Events, Widgets, Positions-/NPC-Informationen | API-/Scriptfunktionen | Semantisch reichhaltiger Zustandsraum, ausgereiftes Ökosystem | Hohe Kopplung an die Clientrevision, Regelverletzung bei Automatisierung |
| Externer Agent + lesendes Client-Plugin | Pro Tick erstellte Snapshots, Szenen- und UI-Hitboxen | Betriebssystem-Input separat | Klare Isolation, Testbarkeit, beobachtbare Entscheidungen | Benötigt weiterhin den zugelassenen Client, kein Headless-Spielclient |
| Legacy Java Injection/Reflection | Manipulierte/gespiegelte Laufzeitobjekte | Hooks/Events | Historisch tiefe API | Nach Abschaltung des Java-Spielclients kein zukunftsfähiger Primärpfad |
| Native-Client-Automatisierung | Native Client- oder visuelle Daten, öffentlich teilweise undokumentiert | Native Steuerung/OS-Input | Nicht vom Legacy Java Client abhängig | Größtenteils Closed Source, starkes Wartungs-/Kompatibilitätsrisiko |
| Computer Vision / Pixel-Automatisierung | Screenshots/Bildmerkmale | OS-Input | Keine direkte Spielprotokollimplementierung | UI-, Kamera-, Skalierungs- und Animationsanfälligkeit |
| Vollständig eigener Packet-/Headless-Client | Netzwerkprotokoll | Protokollaktionen | Theoretisch geringste Renderlast | Authentifizierung, Protokollpflege, Sicherheits-/Regelverstöße; 2026 keine verifizierte kompatible Referenz |
| Emulator-/Benchmark-Agent (RS-SDK) | Kontrollierter Emulator und offenes SDK | SDK über Gateway/Botclient | Reproduzierbare Tests, komplette Kontrolle und Lernumgebung | Keine Verbindung zum offiziellen OSRS möglich |

## 2. Auswertung einzelner Systeme

### 2.1 Microbot — Open-Source RuneLite-Fork

Quellen: https://github.com/chsami/Microbot ; https://github.com/chsami/Microbot/blob/main/AGENTS.md

**Belegte Architektur:** Modifizierter RuneLite-Fork mit dauerhaft aktivem Microbot-Plugin, Skript-Schnittstellen, gemeinsam genutzten Entity-Caches/Queryables und Client-Thread-Regeln. Der Projektbaum nennt `runelite-client/.../microbot`, `.../microbot/util`, `.../microbot/api`, `.../microbot/agentserver`; eine CLI und HTTP-Agent-Tools sind dokumentiert. Build via Gradle und eigene Unit-/Integrationstests.

**Qualitätsmerkmale:**
1. Event- und Cache-getriebene Entitätenabfrage statt ständigen Vollscans.
2. Client-Thread darf nicht blockiert werden; synchrone Warteaufrufe gelten als Fehlerquelle.
3. Bedingungsorientiertes Warten (`sleepUntil`) anstelle fester Verzögerungen.
4. Dokumentierte Entwicklungsregeln, Tests und Troubleshooting-Schnittstellen.
5. Client-Thread-Arbeiten und agentische Steuerung sind ausdrücklich getrennt.
6. Gestufte Testarten: reine Unit-Tests vs. Live-Integrationstests.

**Grenzen:** Ein RuneLite-Fork ist nicht unabhängig von RuneLite und nicht als selbstständiger offizieller Headless-Client belegt. Sicherheit, Performance und Robustheit sind nicht anhand reproduzierbarer Messungen unabhängig bestätigt.

**Transfer:** Nur unabhängiges Scheduler-/Query-Modell, konsistente Snapshot-API, Threading-Grenzen, maschinenlesbare Diagnose und Integrations-Tests übernehmen; keine Clienthooks kopieren.

### 2.2 OSRSB / OsrsBot — Script-API als Client-Erweiterung

Quelle: https://github.com/OSRSB/OsrsBot

**Belegt:** RuneLite-basiertes Automatisierungs-API; README erläutert explizit, dass der bestehende Client die laufende Pflege der Game-Client-Interaktion übernimmt, während OSRSBot Skriptfunktionen entwickelt. Vorcompilierte Skripte werden als JAR geladen; UI erlaubt Start/Pause/Stop.

**Stärke:** saubere Wiederverwendung vorhandener Clientinformationen, Script-SDK als separate Schicht, geringere Duplikation der Spiel-Interaktionslogik.

**Schwäche:** harte Abhängigkeit von RuneLite, eigener Skriptladeprozess erhöht Sicherheitsanforderungen, fehlende belastbare Aussagen zu aktuellem offiziellen Betrieb.

**Transfer:** Plugin-/Action-Interface und klare Zuständigkeitsgrenzen, aber für osrslive keine RuneLite-Laufzeitabhängigkeit.

### 2.3 mtc53/osrs-bot — lesender State-Link + externer Agent

Quelle: https://github.com/mtc53/osrs-bot

**Belegt:** RuneLite-Plugin erstellt einmal pro Game-Tick einen State-Snapshot; lokale HTTP-Endpoints `/ping`, `/state`, `/menu`, `/project`, `/widget`, `/watch` liefern Daten. Python-Prozess hält Zustandsmodell, Task-Runner, Banking-, Safety- und Skriptfunktionen getrennt. Szenenobjekte werden gefiltert, um Scans pro Tick einzuschränken. Das README bezeichnet die Plugin-Hälfte selbst noch als **nicht kompiliert**; daher ist dies **Design-Evidenz, kein Laufzeitnachweis**.

**Stärken:**
1. Snapshots statt uneinheitlicher einzelner Abfragen; explizites Snapshot-Alter.
2. Zuordnung von Weltkoordinaten zu sichtbaren Bounding-Boxes.
3. Vor Aktionen Verifikation des UI-Ziels; nach Aktionen Beobachtung des Ergebnisses.
4. Task-Prioritäten: Sicherheit > Interrupts > eigentliche Aufgabe > Leerlauf.
5. Reine Zustandsmodell-Tests ohne Spielclient, reproduzierbare Tests.
6. Anforderungsspezifische Datenabfrage (`watch`), niedrigeres Sampling-Aufkommen.
7. Expliziter Sicherheitshinweis und Verzicht auf automatische Bank-PIN-Eingabe.

**Grenzen:** Kein Headless-Client; Abhängigkeit von RuneLite und OS-Input, kein unabhängiger Erfolgsnachweis. Der im README beschriebene Ansatz darf nicht als Jagex-zugelassene Botting-Methode missverstanden werden.

**Transfer:** strikte Trennung von `StateSnapshot`, `Decision`, `ActionResult` und `Observation`; Snapshot-Timestamps und anlassgesteuerte Diagnose.

### 2.4 RS-SDK / LostCity — echter Emulator-Agent

Quelle: https://github.com/MaxBittker/rs-sdk

**Belegt:** TypeScript-SDK, LostCity-Serveremulator, Web-Botclient, WebSocket-Gateway und Headless-Optionen. Der SDK-Projekttext unterscheidet bewusst zwischen **Aktion dispatcht** und **Aktion vom Server bestätigt**. Er gibt explizit an, dass Bots dieses Projekts **nicht** auf offiziellen OSRS-Servern funktionieren. Standardmäßig sind Testwelt und Eigenschaften modifiziert (z.B. schnellere XP-Kurve und unbegrenzte Laufenergie).

**Stärken:**
1. Deterministische/steuerbare Testumgebung mit anpassbarem Tickrate-Parameter.
2. Laufzeit-API, Agentenbefehle und Zustandsauswertung.
3. Saubere Trennung `sdk ↔ gateway ↔ botclient ↔ emulator`.
4. Nachvollziehbarer Unterschied zwischen Eingabeversand und erfolgreichem Spielereignis.
5. API-Referenz wird in CI synchronisiert; belegt API-Drift vor Release.
6. Sicherheitsrisiko durch ungeprüfte Chat-Nachrichten für KI-Agenten wird dokumentiert.

**Grenzen:** Gameplay-Modifikationen erschweren 1:1-Übertragung der Erfolgskennzahlen auf offizielles OSRS. Headless-Betrieb im Emulator ist kein Beweis für direktes Headless-OSRS.

**Transfer:** Replay-Fixtures, Test-Emulatoren, State-/Effect-Acknowledgment, Contract-Tests und Fehlerklassifikation.

### 2.5 TRiBot — SDK-Modernisierung und Remote-Orchestrierung

Quelle: https://tribot.org/learn/dev/developer-overview

**Belegt laut Anbieter:** ältere statische Script-SDK und neue, nichtstatische Automation-SDK (2026, Echo-first); laut Dokumentation Integration der RuneLite-API, Plugin-Modell und Launcher-WebSocket-Automatisierung.

**Stärke:** Stabilitätserhalt der älteren API bei Einführung einer neuen Schnittstelle. Nichtstatische Abhängigkeiten unterstützen isolierte Tests und Mehrinstanzfähigkeit.

**Grenzen:** Die Quellimplementationen proprietärer Teile und die tatsächliche aktuelle Betriebsfähigkeit auf offiziellen Servern sind nicht öffentlich verifiziert. Drittanbieter listen Mitte 2026 Betriebseinschränkungen, die Anbieter-Webseite hat weiterhin Produktangaben.

**Transfer:** API-Versionierung, Dependency-Injection, klare Release-Kompatibilitätsmatrix; reine Monitoring-Orchestrierung ohne Multi-Account-Bot-Steuerung.

### 2.6 OSBot — breiter MethodProvider + Eventzustände

Quellen: https://osbot.org/api/org/osbot/rs07/script/API.html ; https://www.osbot.org/api/org/osbot/rs07/event/Event.html ; https://www.osbot.org/api/org/osbot/rs07/event/Event.EventStatus.html

**Belegt:** Script-/MethodProvider bietet Module u.a. `bank`, `combat`, `dialogues`, `inventory`, `npcs`, `objects`, `prayer`, `quests`, `walking`, `widgets`. Ereignismodell unterscheidet `QUEUED`, `WORKING`, `FINISHED`, `FAILED`.

**Stärken:** konsistente Domänen-API für komplexe Skripte; explizite Lifecycle-Zustände von Aktionen für Debugging; modularer Aufbau.

**Grenzen:** Öffentliche Javadocs belegen API-Kontrakte, nicht die tatsächliche Qualität jeder Implementierung. Laut unabhängigen Berichten wurde 2026 eine Schließungsphase angekündigt. Keine Aussage zu aktueller Funktionsfähigkeit ohne Test.

**Transfer:** typisierte Domain-Interfaces, Task-State-Diagnostik und Fehlerpfade.

### 2.7 DreamBot — Script-Ökosystem und Launcher

Quelle: https://dreambot.org/guides/

**Belegt:** Launcher prüft beim Start auf Updates und lädt sie gegebenenfalls automatisch; Script-Entwicklungsplattform, mehrere Clientinstanzen und separate Skript-Lifecycle-Konzepte. Die öffentliche Anleitung empfiehlt Temurin 11; technische Details neuer Clientprotokolle sind nicht öffentlich bestätigt.

**Stärke:** Der Launcher nimmt Nutzern manuelle Aktualisierung ab; eine stabile Oberfläche für Skripte reduziert Einstiegshürden.

**Grenzen:** Launcher-/JAR-Konzept erfüllt ausdrücklich nicht das gewünschte Single-EXE-Ziel; von mehreren Marktquellen werden 2026 Unterbrechungen gemeldet. Nicht als funktionierendes Headless-System nachgewiesen.

**Transfer:** sofortige automatische Releaseprüfung, integritätsgeprüfte Installation, Rollback, klare Statusanzeige.

### 2.8 PowBot — Mobile-API und native Desktop-Ausrichtung

Quellen: https://docs.powbot.org/ ; https://docs.powbot.org/Getting_Started/ ; https://powbot.org/ ; https://osrsmarkets.com/bots/powbot/

**Belegt/Anbieterangaben:** API-Bereiche für Bank, Combat, Equipment, Events, NPC, Widgets, Walking, Skills, World etc. Mobile-Variante verwendete Android/Kotlin/Java, Anbieter wirbt zusätzlich mit einem Rust-basierten Desktopprodukt am nativen OSRS-Client. Dynamische Widget-Suche statt fester UI-IDs wird empfohlen, weil UI-IDs wechseln können.

**Stärken:** domänenspezifische Kapselung und robuste Widget-Lokalisierung über Eigenschaften statt starrer IDs; diversifizierter Betrieb ohne Legacy-Java-Spielclient.

**Grenzen:** Native Desktop-Interna sind nicht öffentlich belegt, Herstellerangaben sind keine unabhängige Prüfung. Drittquelle meldet Einstellung der Verkäufe/Services im Juli 2026; Website-Status kann sich ändern.

**Transfer:** semantische, versionierte Entitäts-/UI-Schemata und Drift-Meldungen.

### 2.9 RuneMate, OSMB, weitere kommerzielle Systeme

Quellen: https://osrsmarkets.com/markets/scripts/clients/ ; https://limebot.org/news/osrs-bot-clients-status-october-2026

- **RuneMate:** breit bekanntes Skriptökosystem; öffentlich nicht genügend aktuelle Primärinformationen, um die Interna seriös zu vergleichen; Marktverzeichnis meldet Schließung im August 2026.
- **OSMB:** laut Anbieter-/Vergleichsseiten historisch visuelle/emulatorbasierte Mobile-Automatisierung; Architekturdetails und Status nicht unabhängig ausreichend verifiziert.
- **LimeBot, Botting Hub, Pluginscape, TwiLite, OnlyBot, PyLite etc.:** Statusseiten weisen auf einen fragmentierten Markt im Oktober 2026 hin. Ohne kontrollierte Quellcode- oder API-Einsicht wären Detailurteile spekulativ.

## 3. Querschnitt: Was technisch gute Bot-Systeme ausmacht

Die folgenden Schlussfolgerungen sind **Engineering-Synthese** aus offen dokumentierten Designs; weder garantieren sie gutes Gameplay noch belegen sie Compliance:

**A. Spielzustand als konsistenter Snapshot:** Ein Event-Sammelpunkt erzeugt Zustand samt monotonem Zeitstempel, `tickId`, Datenherkunft und Schema-Version. Dadurch lassen sich Entscheidungen im Nachhinein nachvollziehen. Einzelne unsynchronisierte API-Abfragen führen zu inkonsistenten Aussagen.

**B. Action → Beobachtung → Bestätigung:** `ActionRequested`, `ActionDispatched`, `StateChanged`, `ActionSucceeded`/`TimedOut` sind verschiedene Zustände. Die bloße Aussendung einer Aktion darf nie als Erfolg gewertet werden. Für offline Tests über einen Emulator nachbildbar.

**C. Domänenmodule:** Inventar, Bewegung, Bank, Interaktion, Quest-/Dialog-UI sind als kleine Schnittstellen mit klaren Vorbedingungen zu modellieren. Ein breites, untypisiertes „doAnything()“ wäre schwer zu testen.

**D. Priority Tasks vs. State Machines:** Vorrangige Sicherheit und unerwartete UI-Interrupts können prioritätsgetrieben behandelt werden; längere strukturierte Workflows erfordern meist explizite Phasen und Recovery-Übergänge. Kombinierte hierarchische Zustandsmodelle vermeiden Explosion der Übergänge.

**E. Asynchronität:** UI-Thread, Event-Leser, Scheduler, Diagnose und Update-Prozess müssen getrennt sein. Blockierende Wartezyklen dürfen keine Spiellogik-/UI-Queues einfrieren.

**F. Robuste Beobachtung:** UI-Elemente mittels Semantik/Widget-Eigenschaften statt hart kodierter Koordinaten; Datenfrische und fehlende Observability in Logs markieren statt blind zu handeln.

**G. Safety vor Fortschritt:** Stopp, Timeout, Wiederanlauf, Datenverlustgrenzen, definierte Invarianten. Keine Speicherung von Kontozugangsdaten oder Bank-PINs. Unbekannte Zustände sollen zunächst protokolliert und getestet werden.

**H. Reproduzierbarkeit:** Offline-Replay, Record/Replay-Fixtures, API-Contract-Tests, FakeClock, definierte Szenarien und Regressionstests. Nicht dieselben selbst erzeugten Beispielereignisse mit Live-Spielerfolg verwechseln.

**I. Observability:** getrennte Zähler für simulierte/echte Events, Queue-Lag, Erkennungs- und Ausführungszeiten, Ausnahmen, CPU/RAM, Render-Status und Versions-/Source-Schema. Der Copy-Log-Button sollte eine kurze, diagnostisch auswertbare Zusammenfassung erzeugen.

**J. Verteilungsmodell:** Single-EXE + Startup-Check + Hash/Authentizitätskontrolle + abgesicherter Relaunch + Rollback + ältere Builds reproduzierbar; GitHub-Release nur nach erfolgreichen CI-Tests.

## 4. Abgeleitete Architektur für osrslive (ohne offizielle Spielanbindung)

```
OSRS-Live-Diagnostics.exe (.NET, eigenständige GUI)
 ├─ Event Store  (Version, Timestamps, Severity, Ringbuffer)
 ├─ Snapshot Aggregator (Origin: SIMULATED/EXTERNAL; ohne echten Adapter)
 ├─ Simulation / Replay (Tick-/Entitätstests)
 ├─ Health (CPU, RAM, UI-Lag, GC, Fehler, Verluste)
 ├─ Renderer Preview (nur lokale Vorschau, optional)
 ├─ Copy/JSON/ZIP Diagnostics
 └─ Self Updater (GitHub EXE manifest, Hash, helper, rollback)

Gesondertes, NICHT implementiertes Forschungsthema:
  OfficialOSRSAdapter (keine autorisierte öffentliche Steuer-API bekannt)
```

**Bewusste Grenze:** Die Anwendung enthält kein OSRS-Anmeldeformular, keine Session-Token-Verarbeitung, keine Spielprotokoll-Implementierung, kein automatisiertes Gameplay und keine Anleitungen zur Umgehung von Anti-Bot-Systemen. Ein autonomer Client für offizielle Server wäre regelwidrig und ist kein Bestandteil des Releases.

## 5. Priorisierte Qualitätsroadmap

| Priorität | Testbares Lieferergebnis | Erfolgskriterium |
|---|---|---|
| P0 | Windows single-file EXE ohne Runtime-Voraussetzungen | Installation benötigt nur **eine** EXE-Datei; Start auf cleanem Windows x64 |
| P0 | Selbstupdate beim Start | Hash geprüft; nach Update startet neuere Version selbst; Offline-Fallback |
| P0 | Update-Rollback | Fehlschlag hinterlässt funktionsfähige alte EXE, Backup vorhanden |
| P0 | Sensible Daten redigieren | Selftest: Token/Passwort/E-Mail nicht exportiert |
| P1 | Monotone Ticks und Fehlerzähler | reproduzierbarer Simulator-Test und Log-Export |
| P1 | UI-Lag, CPU/RAM/Threadzahl | Messwerte in Live-UI und JSON; Grenzwerte eindeutig |
| P1 | Replay aus aufgezeichneten, bereinigten Fixtures | deterministische, szenariobasierte Integrationstests |
| P2 | Adapter-SDK nur als kontrollierte, lokale Offline-Eventquelle | Schema-Validierung, Authentizität, Backpressure, Timeout |
| P2 | Signierte Update-Metadaten / Publisher-Signatur | zusätzlich zum SHA-256 ein unabhängiger Vertrauensanker |
| BLOCKED | Offizieller Spielserver als selbst entwickelter Client | keine von Jagex genehmigte Schnittstelle nachgewiesen |

## 6. Verifizierungslücken

1. Keine Vergleichsmessung über denselben realen Spielzustand auf den genannten Bot-Systemen.
2. Keine Binäranalyse proprietärer Clients: private Hooks, Protokollformate, Account-Infrastruktur und Anti-Ban-Heuristiken sind unbekannt.
3. Kein Test von Jagex-Login, Spielweltverbindung oder grafischem Original-Client.
4. Kein belastbarer „bester Bot“: Leistungswerte von Verkäufern sind nicht reproduziert.
5. Windows-EXE-Selbstupdate muss nach GitHub-CI mit einem kontrollierten Release v0.3.1 auf einem Windows-Rechner validiert werden. Insbesondere Defender/SmartScreen und Datei-Sperren sind relevante Betriebssystemfaktoren.

## 7. Quellenindex

- https://legal.jagex.com/docs/rules/rules-of-runescape
- https://legal.jagex.com/docs/rules/macro-and-client-features-not-permitted
- https://oldschool.runescape.wiki/w/Game_client
- https://github.com/chsami/Microbot
- https://github.com/chsami/Microbot/blob/main/AGENTS.md
- https://github.com/OSRSB/OsrsBot
- https://github.com/mtc53/osrs-bot
- https://github.com/MaxBittker/rs-sdk
- https://tribot.org/learn/dev/developer-overview
- https://osbot.org/api/org/osbot/rs07/script/API.html
- https://www.osbot.org/api/org/osbot/rs07/event/Event.html
- https://dreambot.org/guides/
- https://docs.powbot.org/
- https://powbot.org/
- https://osrsmarkets.com/markets/scripts/clients/
- https://limebot.org/news/osrs-bot-clients-status-october-2026

**Stand:** 22.10.2026. Angaben ändern sich; verlinkte Quellen erneut prüfen, bevor aus der Analyse Produkt- oder Sicherheitsbehauptungen abgeleitet werden.
