# OSRS Play – Status der offiziellen Spielanbindung (v0.7.2)
Stand: 23. Oktober 2026

## Drei unterschiedliche Integrationsstufen
1. **Offizielle F2P-Server-Erreichbarkeit:** JA. OSRS Play v0.7.1 prüft mit TCP-Connect die Spieleport-Endpunkte; im Nutzerlog waren W399 und W553 mit je 6,3 ms am schnellsten. **Ohne** Gameplay-Pakete oder Login.
2. **Tatsächliche öffentliche OSRS-Kontodaten:** JA, in v0.7.2 ergänzt. Einzelne vom Nutzer ausgelöste HTTPS-Abfrage der Jagex HiScores; 24 Skill-Level, Total Level und Mining-XP ohne Credentials. Daten stammen von Jagex, können gecacht oder verzögert sein.
3. **Authentifizierte OSRS-Spielwelt:** NEIN. OSRS Play besitzt keine implementierte oder getestete Authentifizierung mit einem echten offiziellen OSRS-Spielserver, keinen zustandsbeobachtenden Game-Protokolladapter und keinen offiziellen Input-Kanal. Kein tatsächlicher Bot-Zyklus in OSRS.

## Architektur
`IAuthorizedOfficialGameAdapter` beschreibt die benötigte Schnittstelle: Authentifizierung, verifizierter Weltzustand und zugehörige Welt-ID.
`OfficialGameSessionGate.Readiness(null)` liefert ohne Adapter
`OFFICIAL_PROTOCOL_ADAPTER_MISSING`; unbestätigte Zugriffe dürfen
nicht als `OFFICIAL_SESSION_READY` ausgegeben werden.

Bevor eine offizielle Live-Integration entwickelt werden kann, benötigen wir:
- Dokumentierte, von Jagex für dieses Projekt bereitgestellte oder ausdrücklich freigegebene Login-Schnittstelle (insbesondere Jagex-Account-/Launcher-SSO).
- Welt-/Objekt-/Spieler-/Inventar-/XP-Nachrichten mit Revisions- und Formatversion.
- Erlaubte Steueraktionen einschließlich serverbestätigter Ergebnissemantik.
- Definierte Testaccounts, Weltzuordnung, zulässige Rate-Limits und Kontakte für technische Support-/Sanktionsausnahmen.

Die Angaben des Nutzers über eine individuelle Genehmigung werden nicht als unabhängig geprüfter technischer Nachweis für eine Schnittstelle interpretiert. **Eine Garantie für Bannfreiheit oder Anti-Cheat-Nichterkennung ist nicht möglich.**

## Quellen
- Offizielle Weltliste: https://oldschool.runescape.com/slu
- OSRS Public HiScores: https://secure.runescape.com/m=hiscore_oldschool/index_lite.ws
- Jagex Regeln: https://legal.jagex.com/docs/rules/macro-and-client-features-not-permitted
- Approved clients (offizieller Client, RuneLite, HDOS): https://oldschool.runescape.wiki/w/Game_client
- https://developer.runescape.com/rs/ dokumentiert derzeit die RuneScape-Client-Plugin-API; diese ist kein öffentliches OSRS-Standalone-Spielprotokoll.
