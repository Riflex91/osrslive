# OSRS Play v1.1.0 – erster Live-Bildbeobachter
Datum 23. Oktober 2026

## Umsetzung
- Separater `OfficialLiveRenderObserver` erfasst im Vordergrund
  sichtbare 112×72 Pixel ausschließlich vom **originalen osclient.exe**
  (kein Jagex Launcher, keine Browser-/MFA-Fenster).
- 96 Farbproben (12×8) und Helligkeitsvarianz werden ausschließlich
  im flüchtigen Arbeitsspeicher verglichen. Pixel werden nie geloggt
  oder gespeichert; nur Anzahl veränderter Zellen und allgemeiner Status.
- Minimierte/verdeckte/nicht verfügbare Fenster werden nicht erfasst.
- Zehn-Sekunden-Runtime-Status und Zustandswechsel vermerken
  `render_state`. Ein veränderliches Bild beweist
  *nicht* Login, Weltzugehörigkeit, Spiel- oder Botbereitschaft.
- Botstart wurde als automatischer Readiness-Test im vorhandenen
  Live-only-Startablauf verankert. Er wird nur mit geprüftem Login,
  echtem Spieler-/Weltstatus, F2P-Welt und genehmigtem Aktionsadapter
  freigegeben. **Der offizielle Adapter fehlt**: daher keine Aktionen.

## Testbedingungen
1. Vorhandenes Spiel wird nach Jagex Login sichtbar eingebettet
2. Status bewegt sich `NoGameWindow` → `VisibleStatic` oder
   `VisibleChanging`, wenn tatsächlich Bilder sichtbar sind
3. Bei verdecktem/minimiertem Fenster `NotForeground`/`Minimized`
4. Keine Screenshots oder Login-Daten in Logs
5. `LIVE_AGENT AUTO_START_EVALUATED` und `START_BLOCKED`
   beweisen die aktive sichere Sperre, **keinen Botstart**
6. CI-Selbsttest prüft Bildklassifikator und Auth-/F2P-/Action-Gates

## Fehlende technische Bausteine
- Charakteridentität, Sitzungs-/Weltenzustand, UI-Szenenerkennung
  oder genehmigte native Datenquelle mit gesichertem Herkunftsnachweis
- Unabhängig verifizierte tatsächliche F2P-Welt (TCP-Ping ist keine Weltenwahl)
- Von Jagex freigegebene Steuerung und deren serverbestätigte Effekte
- Autonomer Login nur bei einer vom Jagex Launcher gültig gespeicherten
  Sitzung; MFA und offizielle Login-Eingaben werden nicht umgangen

Referenz: https://legal.jagex.com/docs/rules/rules-of-runescape
