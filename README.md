# OSRS Play 0.8.1 – stabiles offizielles OSRS-Spielfenster

**Windows-Fenstereinbettung ohne Änderungen an Jagex-Software.**
Diese Version korrigiert den mit dem echten v0.8.0-Windows-Log
reproduzierten Fehler, bei dem die Oberfläche alle ~1,2 Sekunden zwischen
Jagex Launcher und offiziellem `osclient.exe` hin und her wechselte.

- Sobald die echte `osclient.exe` in OSRS Play eingebettet ist, bleibt
  **genau dieses Fenster** der aktive Host, solange sein Fenster-Handle
  existiert und weiterhin unter OSRS Play hängt. Es wird nicht mehr
  nach normalen Desktopfenstern zurückgesucht, nur weil ein eingebettetes
  Fenster nicht länger als unabhängiges Hauptfenster auftaucht.
- Übergang **Jagex Launcher → offizielles Spiel** bleibt möglich;
  Rücksprung zum Launcher nur, wenn kein originales Spiel mehr gehostet wird.
- Die Windows-Stile des eingebetteten Spiels entfernen vorübergehend
  die unabhängige `WS_EX_APPWINDOW`-Markierung, damit möglichst kein
  zweites Spielelement in der Taskleiste erscheint. Beim Freigeben oder
  Schließen von OSRS Play werden Original-Parent, Fensterstile,
  Taskleistenmarkierung und ursprüngliche Position wiederhergestellt.
- Keine Wiederholung unnötiger Win32-Resize-Aufrufe jedes Mal, wenn die
  UI aktualisiert wird. Neue Einbettung sowie echtes Größenänderungs-
  ereignis lösen weiterhin ein Resize aus.
- Der Original-Jagex Launcher und `osclient.exe` werden **nicht gepatcht**,
  verändert, injiziert oder durch selbstgebaute Spielprogramme ersetzt.
- **Start/Stop** und autonome Mining-/Gold-Aktionen gelten weiterhin
  ausschließlich für die separate lokale Sandbox. Es gibt noch keinen
  zertifizierten Game-State-/Aktionsadapter zum echten OSRS.

**Test auf Windows:** OSRS Play starten, "Anmeldung starten" drücken,
im offiziellen Jagex Launcher anmelden, "Play Now" wählen. Nach dem
Wechsel zum OSRS-Fenster 30 Sekunden beobachten. Im Log darf nun nach
`OFFICIAL_CLIENT GAME_WINDOW_READY` keine wiederholte Folge
`WINDOW_DETACHED kind=Game` / `WINDOW_ATTACHED kind=Launcher` auftreten.
Danach "Fenster freigeben" verwenden und prüfen, dass der originale
offizielle Client wieder als unabhängiges Windows-Fenster nutzbar ist.

**Einschränkung:** Cross-Process-Fensterhosting über `SetParent` kann
auf bestimmten Windows-DPI-/GPU-Konfigurationen scheitern; Jagex-Dialoge
zur Konto-/MFA-Bestätigung können weiterhin extern erscheinen. Ob die
Fensterintegration auf deinem konkreten PC fehlerfrei bleibt, kann erst
der neue Praxistest beweisen.

---
# OSRS Play 0.8.0 – Echter offizieller Client im OSRS-Play-Fenster

**Neu:** Der Reiter **„Offizielles OSRS (echtes Spiel)“** führt den
sichtbaren Jagex-Launcher-Login und anschließend das echte `osclient.exe`-
Spiel in **einem gemeinsamen OSRS-Play-Spielbereich** zusammen. Beide
Fenster stammen aus unveränderter, offiziell installierter Jagex-Software.

## Anmeldung und Spiel im OSRS-Play-Fenster
1. Den offiziellen **Jagex Launcher** und den **Official OSRS Client**
   installiert haben. Es wird **keine** RuneLite-/HDOS-Erweiterung gebraucht.
2. Auf **Anmeldung starten** klicken. OSRS Play öffnet den unveränderten,
   installierten Jagex Launcher und **bettet dessen Windows-Fenster in den
   Reiter `Offizielles OSRS` ein**, sobald es erscheint.
3. Im **eingebetteten originalen Launcher** den Jagex Account anmelden,
   Charakter und **Official Client** auswählen und `Play Now` wählen.
   **Nur Jagex selbst** sieht Zugangsdaten, MFA und Sitzungstokens.
4. OSRS Play erkennt das gestartete `osclient.exe`-Fenster und zeigt dieses
   automatisch **im selben Reiter** an; das zuvor eingebettete Launcher-
   Fenster wird wieder freigegeben und minimiert.
5. Im echten OSRS-Client können die normalen Mausklicks verwendet werden.
   **Fenster freigeben** stellt die ursprüngliche Windows-Einbettung wieder
   her, ohne das offizielle Spiel oder den Jagex Launcher zu beenden.
6. Falls die Erkennung fehlschlägt, **Login/Spiel verbinden** wählen;
   bei mehreren Fenstern den gewünschten Prozess ausdrücklich auswählen.

**Authentifizierungseinschränkung:** Der Jagex Launcher kann zur
Web-/MFA-Anmeldung einen gesonderten Browser, Systemdialog oder ein
Sicherheitsfenster verwenden. Diese Fenster werden aus Sicherheitsgründen
**nicht abgefangen oder künstlich in einen eingebetteten Browser umgeleitet**.
In diesem Fall kann ein Teil der Anmeldung außerhalb von OSRS Play
stattfinden. Eine vollständig einfenstrige Anmeldung kann daher noch
nicht garantiert werden.

### Technische und funktionale Grenzen
- Dies ist **Windows-Fenster-Einbettung (SetParent)**, keine Kopie des
  Jagex-Spielcodes und keine implementierte eigene OSRS-Clientprotokoll-Engine.
  Der offizielle, separat installierte Client muss vorhanden sein.
- Die Anmeldung findet im **originalen Jagex Launcher** statt.
  Eine eigene Login-Maske, ein Jagex-Account-SSO-Token-Abgriff oder
  eine Passwortspeicherung in OSRS Play sind **nicht implementiert**.
- Windows- bzw. GPU-/DPI-Besonderheiten des Jagex-Clients können
  Cross-Process-Fensterhosting verhindern. Ein gescheitertes Andocken
  beendet das Spiel nicht. Nach jeder Betriebssystem- oder Spielclient-
  Aktualisierung muss das Hosting erneut getestet werden.
- **Live-Agent noch nicht verfügbar:** Der Start/Stop-Button ist bei
  aktivem Reiter für das echte Spiel deaktiviert. Der bisherige Bot
  besitzt keine verifizierte Beobachtungs- und Aktionsschnittstelle
  für den offiziellen C++-Client. Er läuft weiterhin nur im separaten
  Reiter **„Lokale Testwelt“**. Das Programm behauptet weder echtes
  automatisches Mining noch erfolgreiche Anti-Cheat-Umgehung.
- Standardregeln von Jagex untersagen automatisierte Eingaben und
  nicht genehmigte Spielclientveränderungen. Eine vom Nutzer berichtete
  individuelle Sondergenehmigung ist nicht extern überprüft. Windows-
  Einbettung selbst ist keine Bestätigung einer Jagex-Freigabe.
- Teste zum ersten Mal **auf eigene Verantwortung** und möglichst erst
  mit einer ausdrücklich freigegebenen Testinstallation.

Quellen:
- https://oldschool.runescape.wiki/w/Jagex_Launcher
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setparent
- https://legal.jagex.com/docs/rules/rules-of-runescape

---

## v0.7.1 – Offizielle F2P-Welten ohne Login prüfen

Die Schaltfläche **F2P-Ping testen** ruft die offizielle
[OSRS-Weltliste](https://oldschool.runescape.com/slu) über HTTPS ab.
Es werden **ausschließlich kostenlose, unbeschränkte Standardwelten**
berücksichtigt: keine Mitgliederwelten, PvP-/Eventwelten und keine Welten
mit 500/750 Gesamtlevel-Anforderung. Die App prüft von diesem Windows-PC
die Erreichbarkeit von `oldschool<world-300>.runescape.com:43594`.
Für die besten acht Kandidaten wird die Verbindungsdauer mehrfach gemessen
und nach dem Median sortiert. Das Ergebnis erscheint im Fensterfuß und
im Log als `F2P_PING BEST_WORLD` mit Welt, Region und TCP-Latenz.

Die Messung stellt **nur TCP-Verbindungen** zum Spielport her. Sie sendet
keine Login-Daten und keine Spielbefehle. Eine erfolgreiche Messung ist
**keine** laufende Spielsitzung. Der integrierte Bot läuft weiter
ausschließlich im lokalen Sandbox-Spielserver. Wenn die offizielle Weltliste
nicht verifiziert werden kann oder keine Welt erreichbar ist, wird **keine
Welt ausgewählt**. Ohne offizielle Protokoll- und Authentifizierungsschnittstelle
ist das Spielen auf echten OSRS-Servern noch nicht funktionsfähig.

Hinweis: TCP-Verbindungsdauer misst ein Transport-Latenzsignal, keinen
verlässlichen In-Game-Tick oder garantierten ICMP-Ping. Weltlisten und
Latenzen können sich kurzfristig ändern; bei erneutem Start erneut prüfen.

---

# OSRS Play v0.7.1 – eigener Mini-Client mit F2P-Welt-Ping

OSRS Play ist eine eigenständige, selbstenthaltende Windows-EXE.
Keine RuneLite-/HDOS-Abhängigkeit und kein Plugin.

**Funktionsfähig in v0.7.0:** Eigene 2D-Spielwelt, eine Spielfigur,
Erzadern, Kollisionsprüfung, eigener TCP-Spielserver (nur 127.0.0.1),
Mining-XP, Erz-Inventar und Gold. Mit **Bot starten** navigiert der Agent
autonom zu Erzadern, baut Erz ab, läuft bei vollem Inventar zum blauen
Verkaufsplatz und verkauft das Erz. Der Server verarbeitet Aktionen und
liefert den maßgeblichen Zustand zurück. **Bot stoppen** sperrt neue
automatische Befehle serverseitig. Wenn der Bot gestoppt ist, kann die
Figur per Klick auf die Karte manuell bewegt werden. Die grafische Anzeige
lässt sich unabhängig von der Botverarbeitung deaktivieren.

**Nicht implementiert:** Verbindung, Login oder Spielaktionen auf echten
OSRS-/Jagex-Servern. Der enthaltene Server ist ein eigens entwickelter
lokaler Testsandbox-Server, **kein kompatibler OSRS-Server**. Gold und XP
existieren nur in dieser Sandbox. Kein echtes Konto wird verändert;
kein Jagex-Login oder Anti-Cheat-Bypass ist enthalten.

Der bisherige Langzeit-Strategietest, die Mining-/GE-Szenariotests,
Diagnose-Exporte und der automatische Updater bleiben verfügbar.

## Test
1. EXE starten; die Welt sollte angezeigt werden.
2. **Bot starten**; Spielfigur und Inventar beobachten.
3. Gold sollte nach einem vollständigen Mining- und Verkaufszyklus
   auf dem lokalen Spielserver ansteigen.
4. **Bot stoppen**; kein neuer automatischer Befehl darf ausgeführt werden.
5. Mit Linksklick auf eine freie Kachel manuell bewegen.
6. Grafik ausschalten und weiterlaufen lassen.
7. Log kopieren und die Einträge `MINI_WORLD ORE_MINED`,
   `MINI_WORLD ORE_SOLD` und `MINI_BOT STOPPED` prüfen.

Für offizielle Server fehlen eine ausdrückliche, technische Freigabe
sowie eine legitime Authentifizierungs- und Protokollspezifikation.
Die selbst entwickelte Sandbox-Schnittstelle ist als austauschbarer
Transport mit beobachtbaren, überprüften Spielaktionen aufgebaut.

--- 
# OSRS Play 0.6.0 – integrierter Bot mit Start/Stop

**OSRS Play** ist der neue Name des bisherigen OSRS Live Diagnostics-Clients. Neu sind eine zentrale Schaltfläche **Bot starten / Bot stoppen**, ein sichtbarer Botstatus und ein Lebenszyklus-Test für den integrierten Agenten.

- Start aktiviert den lokalen `OfflineAgent` einschließlich des Tick-Schedulers, verzögerter Aktionsbestätigungen und Diagnose-Logs.
- Stop beendet die Simulation und die Agentenplanung; erneutes Starten ist möglich.
- **Wichtig:** Der Bot führt noch **keine Aktionen in OSRS** aus. Login, Live-Spielzustand, realer Maus- und Tastaturinput fehlen weiterhin.
- Die bisherigen Mining-/Grand-Exchange-Szenarien, Strategie-Checkpoints und Logexporte bleiben integriert.

## EXE-Name und Update-Kompatibilität
- Neue Installation: `OSRS-Play.exe` – eine vollständige, selbststartende Windows-Datei.
- Bestehende `OSRS-Live-Diagnostics.exe`: Automatisches Update erfolgt über die *identische* Legacy-Release-Datei, da der bisherige Updater genau diesen Namen erwartet. Auf deiner Festplatte kann dadurch der alte Name erhalten bleiben, während das Programmfenster **OSRS Play** heißt.
- Zwei unabhängige Downloadnamen im GitHub-Release dienen ausschließlich der Rückwärtskompatibilität und haben denselben SHA-256-Hash; zum Betrieb brauchst du nur **eine** davon.
- Der manifestierte alte Assetname bleibt erhalten, damit alte Installationen nicht ausgesperrt werden.

## Test
1. Bereits installierte EXE erneut starten und den Auto-Updater abschließen lassen.
2. Im Fenster **OSRS Play** auf **Bot starten** klicken; `BOT STARTED` und Simulations-Ticks prüfen.
3. Nach einigen Ticks **Bot stoppen** klicken und `BOT STOPPED` kontrollieren.
4. Erneut starten, stoppen und **Log kopieren**.

**Diagnose-App, kein spielbarer OSRS-Client und kein Bot.** Eine Verbindung zu offiziellen Jagex-Servern ist nicht implementiert. Diese Version benötigt weder RuneLite noch Java.

## Einmalige Installation

1. GitHub Releases öffnen: https://github.com/Riflex91/osrslive/releases/latest
2. **Nur `OSRS-Live-Diagnostics.exe`** herunterladen, z.B. nach `C:\Users\<Nutzer>\Desktop\OSRS-Live-Diagnostics.exe`.
3. Doppelklicken. Die Windows-x64-Version enthält ihre .NET-Laufzeit selbst. Kein Installationsordner mit mehreren Dateien erforderlich.
4. Beim Start wird automatisch nach Updates gesucht. Verfügbare Updates werden heruntergeladen, SHA-256 geprüft, installiert und selbst gestartet. Bei fehlendem Internet startet die lokale Version weiter.
5. `Simulation starten`, Renderer schalten, `Log kopieren` und das Protokoll einfügen. Alternativ JSON-/ZIP-Export.

Windows-Sicherheitswarnungen für unsignierte EXEs können auftreten. Einen Hinweis nur nach Prüfung der Release-Quelle akzeptieren; perspektivisch Authenticode-Signatur ergänzen.

## Auto-Update-Ablauf

- Quelle: `https://raw.githubusercontent.com/Riflex91/osrslive/main/update-exe.properties`
- Version, genau erlaubte Release-URL und SHA-256 werden validiert.
- Nur eine **neuere** semantische Version wird verarbeitet.
- Download mit Größenbegrenzung nach `%LOCALAPPDATA%\OSRSLiveDiagnostics\updates\...`.
- SHA-256 des Downloads wird vor und beim Anwenden nochmals geprüft.
- Eine temporäre Kopie der laufenden EXE wartet auf Prozessende, ersetzt dann die installierte EXE, startet sie neu und belässt bei Erfolg `*.previous` als Rückfallebene.
- Bei Updatefehler: aktuelle EXE weiterverwenden, Diagnose unter `%LOCALAPPDATA%\OSRSLiveDiagnostics\startup.log`.
- Im **Installationspaket** wird eine einzelne EXE ausgeliefert; beim Update können temporäre Helper-/Sicherungsdateien entstehen.

**Sicherheitsgrenze:** Der SHA-256-Hash im selben GitHub-Repository schützt vor kaputten/unpassenden Downloads, nicht gegen Übernahme des Repositorys oder GitHub-Releasekanals. Signaturprüfung mit unabhängiger Vertrauenswurzel ist für spätere Releases vorgesehen. Die EXE ist derzeit nicht signiert.

## Entwickler

Windows SDK: .NET 10. Projekt: `native/OSRSLiveDiagnostics.csproj`.

```powershell
dotnet build native/OSRSLiveDiagnostics.csproj -c Release -r win-x64
dotnet run --project native/OSRSLiveDiagnostics.csproj -c Release -- --self-test
dotnet publish native/OSRSLiveDiagnostics.csproj -c Release -r win-x64 -o publish
```

CI kompiliert auf `windows-latest`, führt einen Selbsttest aus, verlangt genau eine EXE und erstellt anschließend ein GitHub Release. Erst nach dem Release wird `update-exe.properties` aktualisiert. Der alte Java-Updatekanal `update-manifest.properties` bleibt separat.

## Analyse anderer Bots

Siehe [technische Vergleichsanalyse](docs/OSRS-BOT-ARCHITEKTUR-ANALYSE-2026-10-22.md) mit Quellenbelegen, Architekturbeschreibungen, Qualitätsmerkmalen, Unsicherheiten und Roadmap.

## 0.3.1 Laufzeitdiagnose und Updates

- `PERF_SAMPLE` enthaelt nun normierte `cpu_pct` und `ui_timer_lag_ms`-Werte neben Working Set und Threadanzahl.
- Die GitHub-Pruefung schreibt das Ergebnis in den mit **Log kopieren** abrufbaren Diagnosepuffer. `CHECK_FINISHED` alleine bedeutet weiterhin nicht, dass ein Update installiert wurde.
- Nach einer erfolgreichen Installation meldet die neu gestartete EXE `UPDATER PREVIOUS_UPDATE`.
- Eine langsame Windows-UI wird bei einer Timer-Verspaetung ueber 1000 ms als `UI_TIMER_DELAY` erfasst (Timer-Messung, keine GPU- oder Rendering-Latenz).
- GitHub Pull Requests bauen und testen nun ohne ein Release vorzeitig zu veroeffentlichen.
- Kontrollierter Update-Test: 0.3.0 lokal starten, nach Manifest-Upgrade auf 0.3.1 muss die 0.3.1-Version neu starten und `PREVIOUS_UPDATE` melden. Bei Fehlschlag: `%LOCALAPPDATA%\\OSRSLiveDiagnostics\\startup.log` pruefen.

Diese Anwendung ist weiterhin ausschliesslich ein Diagnose-/Simulationsclient, kein Jagex-Client.

## v0.4.0 – Offline-Agentenkern aus der Bot-Vergleichsanalyse

- **Offline-Agent: AN/AUS** startet eine isolierte autonome Demo **im Simulator**.
- `AgentSnapshot` enthält eine eindeutige Tick-ID, Herkunft, Zeitstempel, Health und Inventarfüllung.
- `OfflineAgent.Plan` ist ein deterministischer, priorisierter Planner: Gesundheit → Bank → Sammeln.
- `ActionPhase` unterscheidet `Blocked`, `Planned`, `Confirmed` und `Failed`.
- `OfflineAgent.Confirm` meldet Erfolg **nur nach beobachteter Zustandsänderung**.
- Alte, fehlerhafte, nicht monotone und externe Snapshots werden blockiert.
- Ausführungen sind technisch auf rein lokale Simulation begrenzt; Replay erlaubt nur Planung.
- **Log kopieren** erfasst `ACTION_PLANNED`, `ACTION_CONFIRMED` und mögliche Fehler.
- Selbsttests prüfen Prioritäten, Banking, Regeneration, State-Effect-Confirmation, Datenfrische und harte Quellgrenzen.
- Kein OSRS-Login, keine Verbindung zu Jagex, kein Input-Sender und keine Erkennungsumgehung.

Siehe [Offline-Agent-Architektur](docs/OFFLINE-AGENT-ROADMAP.md) und die [Bot-Vergleichsanalyse](docs/OSRS-BOT-ARCHITEKTUR-ANALYSE-2026-10-22.md).

## v0.4.1 – Weltzustandskonsistenz und verzögerte Bestätigungen

- Der Simulator führt **einen** Inventarzähler und einen Health-Zustand. Die Agentenentscheidung liest diese Werte, und `INVENTORY_CHANGED` meldet exakt diesen Zustand.
- `ACTION_PLANNED` bestätigt keinen Erfolg: der simulierte Effekt tritt frühestens im nächsten Game-Tick ein. `ACTION_CONFIRMED` enthält den geplanten und den bestätigten Tick.
- Jede 17. simulierte Aktion erhält absichtlich **keine Wirkung**. Nach drei Ticks erscheint `ACTION_TIMED_OUT ... synthetic_fault=true`; danach kann die Planung fortgesetzt werden.
- Gleichzeitige, stale oder inkonsistente Ereignisse werden blockiert. Im `--self-test` wird das Verbot einer Bestätigung im selben Tick geprüft.
- **Keine Spielanbindung oder Anti-Cheat-Umgehung.** Für jegliche von Jagex genehmigte Integration sind schriftlich begrenzter Funktionsumfang, benannte Testkonten, Clientfreigabe, Abgleich mit Jagex und eine zugehörige Account-Allowlist erforderlich. Keine Methode kann eine Sperre allein technisch ausschließen.

Technische Prüfung: `--self-test`, Live-Simulationslog und Windows-CI.

## v0.4.2 – Mining → GE: reproduzierbare Offline-Szenarien

Die Schaltfläche **Mining → GE testen** führt fünf isolierte Testwelten mit Erzabbau, virtuellem Inventar, BFS-Navigation, GE-Verkauf, blockierten Wegen und abgewiesenen Angeboten aus. Kein echter Spielclient und keine Jagex-Verbindung. CLI: `--scenario-tests`. Siehe [Testdokumentation](docs/MINING-GE-SIMULATION-2026-10-22.md).


## v0.5.0 – Autonome Langfristziele: alle Skills + GP (Strategie)
- Ein Strategiesystem für **24 Skills inklusive Sailing** (2376 maximales Gesamtlevel) und unbeschränktes Langfristziel Goldvermögen.
- `GoalPlanner` wägt unter `Balanced`, `MaxSkills` und `MaxGold` geschätzte XP/h und Netto-GP/h ab; nach Erreichen aller 99er-Level wird GP zum einzigen Optimierungsziel.
- Quest-/Freischaltungsbedingungen, Member-Status, Anlaufkosten, Liquiditätsreserve und veraltete Preisangaben werden als harte Gates behandelt.
- Im Offline-Agent-Log stehen beispielhafte Ranglisten mit `synthetic_fixture=true`, **keine** echten XP-, Markt- oder Kontowerte.
- Der Planner ist eine Entscheidungskomponente, **noch kein OSRS-Live-Client oder selbstspielender Account**.
- Siehe [Strategie und Botvergleich](docs/OSRS-AUTONOMY-AND-WEALTH-PLAN-2026-10-23.md).


## v0.5.1 – Persistente Offline-Strategiekampagne

- Neuer Button **Strategie-Langzeittest**: führt jeweils 120 synthetische Strategie-Schritte aus, speichert eine Prüfungsversion mit Skillständen, fiktiven Münzen, Anzahl der Schritte und zuletzt gewählter Aktivität.
- Beim nächsten Klick wird der geprüfte Checkpoint wiederhergestellt und deterministisch fortgesetzt.
- Speicherung unter `%LOCALAPPDATA%\\OSRSLiveDiagnostics\\offline-campaign-synthetic.json` per temporärer Datei und Dateiaustausch. Fehlerhafte Dateien werden nicht überschrieben.
- `CAMPAIGN CREATED/RESUMED/CHECKPOINT_SAVED` erscheinen beim Logexport, mit klar gekennzeichneten fiktiven Zahlen.
- Selbsttests prüfen Progression, 24-Skill-Abdeckung, Geldreserve, deterministische Fortsetzung, beschädigte Dateien und Verhalten nach sämtlichen 99er-Leveln.
- **Kein reales OSRS-Gameplay**: Der Langzeittest verändert weder Accounts noch Bank/GE und enthält keine Zugangsdaten.


## v0.6.1 – Korrekte Tickstatistik über mehrere Bot-Starts
- Sitzungsübergreifende Wartezeiten werden nicht als Tick-Intervall bewertet.
- Start/Stop und `GAME_TICK` besitzen die Kennung `run_id=N`.
- Eine offene Aktion wird beim Anhalten als `ACTION_CANCELLED` dokumentiert.
- Verspätete Tick-Callbacks aus früheren Sitzungen können nach Stop/Restart keine Spielzustände mehr verändern.
- Der Diagnosetest reproduziert die im Nutzerlog beobachteten 7,465 Sekunden Pause und überprüft ihre korrekte Behandlung.
- [Auswertung und Testplan](docs/OSRS-PLAY-SESSION-METRICS-2026-10-23.md).


## v0.7.2 – Echte öffentliche OSRS-Daten und sichere Trennung von Spielsitzungen
- **Echte OSRS-Skills:** OSRS Play kann über die Schaltfläche `Echte OSRS-Skills` nach Eingabe eines öffentlichen Anzeigenamens die tatsächlichen öffentlich verfügbaren Skillwerte über den Jagex-HiScores-Endpunkt `secure.runescape.com/m=hiscore_oldschool/index_lite.ws` lesen. Die 24 Skills einschließlich Sailing und das Gesamtlevel werden geprüft. **Keine** Passwörter, Zugangsdaten oder Anmeldetokens eingeben.
- **Datenschutz:** Der angegebene Charaktername wird nicht im exportierten Log ausgegeben. Jede Abfrage wird einzeln durch den Nutzer ausgelöst, nicht im Hintergrund wiederholt. Statistiken können auf der HiScores-Seite verzögert erscheinen; sie sind nicht identisch mit einem aktuellen authentifizierten Inventar oder einer Spielsitzung.
- **Offizielle Live-Session-Grenze:** Das neue `IAuthorizedOfficialGameAdapter`-Interface beschreibt die künftig benötigte authentifizierte Spielverbindung und einen verifizierten Weltzustand. Derzeit ist kein entsprechender Jagex-freigegebener Adapter vorhanden. Die `OfficialGameSessionGate`-Prüfung weist diesen Zustand ausdrücklich als `OFFICIAL_PROTOCOL_ADAPTER_MISSING` aus; ein Ping oder ein öffentlicher Skillabruf darf niemals als erfolgreiches Login gewertet werden.
- **Lokale Engine:** Der 600-ms-Client-Takt nutzt eine monotone Uhr statt einer festen Zusatzpause. Aktionen werden im Log als `ACTION_PLANNED` / `ACTION_CONFIRMED` / `ACTION_NOT_CONFIRMED` / `ACTION_CANCELLED` protokolliert. Eine Bestätigung erfordert auf dem lokalen Testserver eine tatsächliche passende Änderung von Position, Erz/XP oder GP.
- Regressionstests prüfen diese Zustands- und Herkunftsgrenzen. **Kein echtes OSRS-Mining oder offizieller Spiel-Login implementiert.**
