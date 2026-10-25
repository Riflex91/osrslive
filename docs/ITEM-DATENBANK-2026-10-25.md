# Item-Datenbank und Wiedererkennung

Die Anwendung legt nach einer vollständig bestätigten Inventarprüfung eine
lokale SQLite-Datenbank unter `%LOCALAPPDATA%\OSRSPlay\agent\items.sqlite` an.
Sie enthält beobachtete Itemnamen, Inventar-Kontextaktionen und SHA-256-
Signaturen der vollständigen 32×32-Pixel-Slots. Zugangsdaten, Charakternamen,
Spielbilder und Menü-Klickkoordinaten werden dort nicht gespeichert.

## Verhalten

`items` enthält eindeutige Namen sowie erste/letzte Beobachtungszeit;
`menu_actions` die tatsächlich gelesenen Aktionen mit Kontext `inventory`;
`visual_observations` ordnet Bildsignaturen den beobachteten Namen, einem
Darstellungsprofil und einer zufälligen Sitzungskennung zu. Widersprüchliche
Namen für dieselben Pixel werden als mehrdeutig markiert.

`reference_items` enthält IDs und normalisierte Referenznamen aus der öffentlichen
OSRS-Wiki-Preis-Mapping-API. Der Katalog wird höchstens einmal pro Programmstart
geprüft und nach sieben Tagen aktualisiert. Netzwerkprobleme lassen gespeicherte
Referenzen und die eingebauten Methodennamen verfügbar. OCR-Schreibweisen, die
keinem Referenznamen entsprechen, werden als unbestätigte Beobachtungen gespeichert
und nicht für Wiedererkennung freigegeben. Es gibt keine automatische unscharfe
Korrektur wie „bi9“ → „big“. Eine später tatsächlich gelesene gültige Schreibweise
kann eine unbestätigte Identität ersetzen; widersprüchliche gültige Namen bleiben
gesperrt. Schema 1 wird um den Referenzkatalog auf Schema 2 erweitert.

Referenzquelle: https://prices.runescape.wiki/api/v1/osrs/mapping

Die schnelle Wiedererkennung verwendet nur bestätigte Beobachtungen der
aktuellen Sitzung, höchstens fünf Minuten nach ihrer direkten Menüprüfung.
Sie überlebt neue Aufgabenzyklen und `GameInteractions`-Objekte. Charakter-,
Client- und Sitzungswechsel setzen diese Freigabe zurück; die Datenbank bleibt
erhalten. Gespeicherte Signaturen aus früheren Sitzungen sind keine aktuelle
Inventarbeobachtung. Eine spätere gezielte Startvalidierung kann darauf aufbauen.

Jeder Durchlauf öffnet und bestätigt weiterhin den Inventar-Tab und fordert
drei stabile Frames. Unveränderte Itembilder benötigen kein erneutes Kontextmenü;
unbekannte, geänderte, abgelaufene oder mehrdeutige Bilder werden neu gelesen.
Jeder einzelne Pixel fließt in die SHA-256-Signatur ein. Mengen kommen immer
aus dem aktuellen Frame; ein früher bekannter Münzbestand wird nie übernommen.
Vor dem vollständigen Inventarergebnis werden alle während des Scans gesehenen
vollständigen Slot-Signaturen erneut verglichen.

Einzelne Item-/Menüeinträge werden erst gelernt, wenn das jeweilige Menü
nachweislich geschlossen und das vollständige Itembild unverändert ist.
Ein Fehler an einem späteren Slot verwirft diese bereits bestätigte Identität
nicht; er verhindert weiterhin ein vollständiges Inventarergebnis. Ein
unbestätigtes Menü oder verändertes Itembild wird nicht gelernt.
Die Datenbank führt keine Aktionen aus: Eine gespeicherte
Aktion erlaubt keinen Klick auf ein ungeprüftes Menü. Unbekannte Mengen und
mehrdeutige Anzeigen behalten die bestehenden Sperren.

## Diagnose und Grenzen

`ITEM_DATABASE / INVENTORY_READ` nennt `cached_items`, `scanned_items`,
`empty_slots` und `duration_ms`. Diese Zähler erlauben einen direkten Vergleich
zwischen erstem und wiederholtem Durchlauf. Ein Datenbankfehler wird sichtbar
gemeldet; nur unmittelbar bestätigte Sitzungsdaten können dann im Speicher
weiterverwendet werden. Ein unbekanntes zukünftiges Datenbankschema wird nicht
überschrieben. SQLite-WAL und eine Transaktion je bestätigtem Item schützen
die zusammengehörigen Katalogeinträge.

Die Datenbank verbessert die Inventarerkennung. Sie ersetzt keine Karten-
lokalisierung, Reisebestätigung, HP- oder Erfolgskontrolle. Die beobachtete
Positionssperre `AUTOMATIC_WORLD_POSITION_NOT_CONFIDENT` kann weiterhin eine
selbstständige Aufgabe verhindern. Es wird keine menschliche Unauffälligkeit
oder Umgehung einer Erkennung behauptet. Die vorhandenen seriellen Eingaben
warten weiterhin auf neue visuelle Beobachtungen.

## Prüfung

`--self-test` prüft SQLite-Persistenz, deduplizierte Menüaktionen, Konflikte,
Lebensdauer/Sitzungsgrenzen, aktuelle Mengen, beschädigte Dateien, zukünftige
Schemata und vollständige Pixel-Hashes. Ein Integrationstest verwendet zwei
getrennte Aufgabenzyklus-Objekte: Ein unverändertes Item öffnet beim zweiten
Inventarlesen kein Kontextmenü; geänderte Pixel erzwingen die Menüprüfung,
auch wenn die frühere Stichprobensignatur gleich bleibt.

SQLite wird über Microsoft.Data.Sqlite 10.0.14 mit SQLitePCLRaw 2.1.13 eingebunden.
Der Publish-Lauf muss weiterhin genau eine selbstständig lauffähige EXE liefern.
