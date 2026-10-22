from pathlib import Path
import sys

root = Path(sys.argv[1] if len(sys.argv) > 1 else ".")
def edit(name, changes):
    p = root / name
    s = p.read_text(encoding="utf-8")
    for before, after in changes:
        found = s.count(before)
        if found != 1:
            raise RuntimeError(f"{name}: expected one occurrence, found {found}: {before[:50]}")
        s = s.replace(before, after)
    p.write_text(s, encoding="utf-8")

edit("src/main/java/de/osrs/diag/DiagnosticsCore.java", [
    ('VERSION = "0.2.1"', 'VERSION = "0.2.3"'),
    ('record Entry(long id, Instant at, Level level, String source, String event, String detail)',
     'record Entry(long id, Instant at, long monotonicNanos, Level level, String source, String event, String detail)'),
    ('private long nextId = 1;', 'private long nextId = 1;\n    private long listenerFailures = 0;'),
    ('new Entry(nextId++, Instant.now(), level, sanitize(source, 32), sanitize(event, 55), sanitize(detail, 480))',
     'new Entry(nextId++, Instant.now(), System.nanoTime(), level, sanitize(source, 32), sanitize(event, 55), sanitize(detail, 480))'),
    ('try { listener.accept(entry); } catch (RuntimeException ignored) { /* never break diagnostics */ }',
     '''try { listener.accept(entry); } catch (RuntimeException failure) {
                synchronized (this) {
                    listenerFailures++;
                    Entry diagnostic = new Entry(nextId++, Instant.now(), System.nanoTime(), Level.ERROR,
                            "CORE", "LISTENER_FAILED", sanitize(failure.getClass().getSimpleName(), 80));
                    if (entries.size() == LIMIT) { entries.remove(0); dropped++; }
                    entries.add(diagnostic); errorCount++;
                }
            }'''),
    ('entries.clear(); dropped = 0; errorCount = warningCount = tickCount = liveCount = 0;',
     'entries.clear(); dropped = 0; listenerFailures = 0; errorCount = warningCount = tickCount = liveCount = 0;'),
    ('tickCount, liveCount, started, mode, rendering.get()',
     'tickCount, liveCount, listenerFailures, started, mode, rendering.get()'),
    ('long ticks, long liveEvents,\n                        Instant started',
     'long ticks, long liveEvents, long listenerFailures,\n                        Instant started'),
    ('append(" | Externe Events: ").append(s.liveEvents()).append("\\n")',
     'append(" | Externe Events: ").append(s.liveEvents())\n            .append(" | Listener-Fehler: ").append(s.listenerFailures()).append("\\n")'),
    ('out.append("--- AUTOMATISCHE AUSWERTUNG ---\\n");',
     '''long usedMemMiB = (Runtime.getRuntime().totalMemory() - Runtime.getRuntime().freeMemory()) / 1048576;
        out.append("JVM-Speicher: ").append(usedMemMiB).append(" MiB (Heap belegt)\\n");
        out.append("--- AUTOMATISCHE AUSWERTUNG ---\\n");'''),
    ('append(", \\"externalEvents\\": ").append(s.liveEvents()).append("},\\n");',
     'append(", \\"externalEvents\\": ").append(s.liveEvents())\n            .append(", \\"listenerFailures\\": ").append(s.listenerFailures())\n            .append(", \\"heapUsedBytes\\": ").append(Runtime.getRuntime().totalMemory() - Runtime.getRuntime().freeMemory()).append("},\\n");'),
])
edit("src/main/java/de/osrs/diag/DiagnosticAnalyzer.java", [
    ('long gap = java.time.Duration.between(previous.at(), event.at()).toMillis();',
     'long gap = java.util.concurrent.TimeUnit.NANOSECONDS.toMillis(event.monotonicNanos() - previous.monotonicNanos());'),
    ('if (stats.errors() > 0) findings.add("Fehlereinträge erkannt:',
     'if (stats.listenerFailures() > 0) findings.add("Ereignisempfänger-Ausnahmen erkannt: " + stats.listenerFailures() + "; CORE/LISTENER_FAILED prüfen.");\n        if (stats.errors() > 0) findings.add("Fehlereinträge erkannt:'),
    ('Tick-Abstände: Ø ', 'Monoton gemessene Tick-Abstände: Ø ')
])
edit("src/main/java/de/osrs/diag/DiagnosticsApp.java", [
    ('   •   Fehler " + s.errors() + "   •   Pufferverlust " + s.dropped()',
     '   •   Fehler " + s.errors() + "   •   Listener " + s.listenerFailures() +\n                      "   •   Heap " + ((Runtime.getRuntime().totalMemory()-Runtime.getRuntime().freeMemory())/1048576) + " MiB" +\n                      "   •   Pufferverlust " + s.dropped()'),
])
edit("src/main/java/de/osrs/diag/UpdateChecker.java", [
    ('if (!version.matches("[0-9]+\\\\.[0-9]+\\\\.[0-9]+") || !validJarUrl(url) || !sha.matches("(?i)[0-9a-f]{64}"))',
     'if (!version.matches("[0-9]+\\\\.[0-9]+\\\\.[0-9]+") || !validJarUrl(url) || !sha.matches("(?i)[0-9a-f]{64}") ||\n                !url.equals("https://github.com/Riflex91/osrslive/releases/download/v" + version + "/OSRS-Diagnostics-Client.jar"))'),
])
edit("src/main/java/de/osrs/diag/SelfTest.java", [
    ('require(core.clipboardReport().contains("TEST"),"clipboard includes unfiltered log");',
     '''require(core.clipboardReport().contains("TEST"),"clipboard includes unfiltered log");
            core.addListener(entry -> { if (entry.event().equals("TRIGGER")) throw new IllegalStateException("synthetic failure"); });
            core.add(DiagnosticsCore.Level.INFO, "TEST", "TRIGGER", "listener fails");
            require(core.stats().listenerFailures() == 1 && core.stats().errors() == 1, "listener errors are recorded without recursion");
            require(core.completePlainLog().contains("LISTENER_FAILED"), "listener exceptions visible in export");'''),
    ('require(metrics.jsonReport().contains("averageTickGapMs"), "JSON includes timing metrics");',
     '''require(metrics.jsonReport().contains("averageTickGapMs"), "JSON includes timing metrics");
            require(metrics.jsonReport().contains("listenerFailures") && metrics.jsonReport().contains("heapUsedBytes"), "JSON includes runtime metrics");'''),
])
edit("VERSION.txt", [("0.2.1", "0.2.3")])
print("v0.2.3 patched")
