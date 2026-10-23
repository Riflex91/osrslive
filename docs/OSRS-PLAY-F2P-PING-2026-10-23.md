# OSRS Play v0.7.1: offizieller F2P-Welt-Ping
Stand: 23. Oktober 2026

## Zweck
Welcher offizielle F2P-Standardserver hat **vom eigenen Windows-Rechner**
die niedrigste Latenz? Die alte lokale Sandbox wird nicht als offizielle
OSRS-Verbindung ausgegeben.

## Schnittstelle
- Offizielle Liste: https://oldschool.runescape.com/slu (HTTPS).
- Weltname `Old School N` (1-basierte Anzeige), offizieller Weltindex
  `N+300`, TCP-Hostname `oldschoolN.runescape.com`.
- Port 43594 (bekanntes RuneScape/OSRS-Spielportformat).
- Der Latenztest führt einen TCP-Connect aus und schließt den Socket sofort;
  **keine** Gamepakete, Accountnamen, Passwörter oder Jagex-Tokens.
- **Fail-closed:** Welt nur, wenn Tabelle geladen, Mitgliedschaftstyp exakt
  `Free`, Aktivität leer oder `-`, Weltindex plausibel und Verbindung erfolgreich.
- Es werden keine PvP-, Members-, Skill-Total- oder Event-Welten mitgetestet.
- Bis zu 8 parallele Initialproben, danach drei Zusatzproben für die
  acht schnellsten F2P-Kandidaten; Ranking per Median für die Finalisten.
- Bei Offline-/DNS-/Firewall-/HTML-Fehlern gibt es keinen behaupteten Gewinner.

## Tests
`--self-test` prüft HTML-Klassenunabhängigkeit, F2P-Grenzen,
ungeeignete Events, Mitglieder- und Totallevel-Welten, Hostname,
Median sowie Ablehnung unerkannter Inhalte. Kein CI-Netzzugriff erforderlich.

## Nicht implementiert
Der offizielle Jagex-Login, der offizielle Welt-/Spielprotokoll-Client,
Live-Spielmechaniken und die echte Mining-/Handelsautomation sind noch
nicht vorhanden. Das Ergebnis ist lediglich eine **empfohlene F2P-Welt
für einen späteren, ausdrücklich genehmigten Integrationstest**.

## Quellen
- https://oldschool.runescape.com/slu
- https://github.com/runelite/runelite/blob/master/runelite-client/src/main/java/net/runelite/client/plugins/worldhopper/ping/Ping.java
