# OSRS Play v1.1.0 – echtes Spielbild beobachten (Live-only)

## Neue Live-Beobachtung

Ab v1.1.0 beobachtet OSRS Play **alle zwei Sekunden ausschließlich
das tatsächlich sichtbare, eingebettete offizielle `osclient.exe`-Fenster**.
Es werden 96 Farbproben aus einem kleinen mittleren Bildausschnitt analysiert,
um sichtbare Fensterzustände zu unterscheiden:

- `NoGameWindow`: kein originales OSRS-Spielfenster eingebettet
- `NotForeground` / `Minimized`: Spiel verdeckt oder Fenster minimiert;
  **keine Bildaufnahme**
- `TooSmall` / `CaptureUnavailable`: keine zuverlässige Aufnahme möglich
- `UniformPixels`: weitgehend gleichfarbiger Ausschnitt, z. B. Ladebildschirm
- `VisibleStatic`: ein echtes, sichtbares Spielbild mit wenig Veränderung
- `VisibleChanging`: im Spielausschnitt sichtbare Bildänderungen

Der Bericht erzeugt `OFFICIAL_OBSERVER RENDER_STATE_CHANGED` und
`RUNTIME HEALTH render_state=...`. Die App speichert **keine Screenshots,
keine extrahierten Chattexte und keine Pixelinhalte**. Der Launcher wird
ausdrücklich nicht aufgenommen, sodass Login- und MFA-Dialoge nicht durch
den Beobachter verarbeitet werden.

**Grenze:** Sichtbares oder veränderliches Spielbild ist **kein Beweis
für einen tatsächlich angemeldeten Charakter**, eine bestätigte OSRS-Welt,
Gold-/Inventarwerte oder einen funktionierenden Aktionskanal. Das Log und
die Statusanzeige behalten `Login: NICHT VERIFIZIERT`.

## Autostart und Agent

Die bisherige automatische Übernahme des Jagex Launchers bzw. offiziellen
OSRS-Clients bleibt. Mit einer bereits gültigen Jagex-Sitzung kann der
Launcher ohne erneute Passworteingabe gestartet werden. Wenn Jagex
eine Anmeldung, Charakterauswahl oder MFA verlangt, ist dieser offizielle
Schritt erforderlich; OSRS Play umgeht ihn nicht.

Der Agentstart wird nach Erscheinen des offiziellen Spielclientfensters
automatisch **geprüft**, jedoch noch nicht freigegeben:
`LIVE_AGENT AUTO_START_EVALUATED gate=OFFICIAL_ADAPTER_MISSING`.
Ein künftiger Adapter müsste unabhängig Authentifizierung, Charakter,
tatsächliche freie Welt und eine von Jagex zugelassene Aktionsschnittstelle
nachweisen. Bis dahin bleibt `LIVE_AGENT START_BLOCKED` und es
werden **keine automatisierten Aktionen** an den offiziellen Server gesendet.

Automatisch ermittelter F2P-Ping bleibt eine **Empfehlung**, keine
automatische Weltenwahl im Spiel. Die Oberfläche hat keine eigenen Buttons;
die Spielcontrols gehören vollständig Jagex.

## Test

EXE neu starten. Nach der Updateprüfung startet OSRS Play den offiziellen
Jagex Launcher, übernimmt das Spiel nach `Play Now` und zeichnet alle
zwei Sekunden das Bildstatus-Ergebnis auf. Betrachte den Bereich mindestens
15 Sekunden ohne andere Fenster darüber zu legen. Anschließend Logdatei
`%LOCALAPPDATA%\OSRSPlay\live-logs\last-session.log`
prüfen oder **Strg+Umschalt+C** drücken.

Die Windows-CI prüft Klassifikation von farbigen, leeren und geänderten
Bildproben sowie die Verweigerung des Botstarts ohne gültige Schnittstelle.
Die tatsächliche Bildaufnahme der GPU-gerenderten Original-`osclient.exe`
muss auf deinem Rechner verifiziert werden.
