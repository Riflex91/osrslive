# OSRS Live Diagnostics 0.3.0 (Windows single EXE)

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
