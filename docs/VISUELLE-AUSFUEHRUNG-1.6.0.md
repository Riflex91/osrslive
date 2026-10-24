# Visuelle Trainings-, Bank-, Handels- und Herstellungsroutinen

Version 1.6.0, 24. Oktober 2026.

Diese Version enthält erstmals an den echten Fenstertransport angeschlossene Ausführer. Sie sind nicht nur Zielvorschläge: Sie erzeugen Mausbewegungen, Links-/Rechtsklicks und eng begrenzte Texteingaben, lesen danach neue Spielbilder und prüfen Ergebnisse. **Die vollständigen Abläufe sind am offiziellen Client noch nicht Ende-zu-Ende bestätigt.** Die bestehenden Login-/Capture-Probleme können den Einstieg weiterhin verhindern. Die unverifizierte native Inventargeometrie und die tatsächlich angezeigten Menüformulierungen können einen Ablauf blockieren.

## Start und Bedienung

1. Alte OSRS-Play-Instanzen schließen und 1.6.0 starten. Charakter und offiziellen Client im Jagex Launcher auswählen; Zugangsdaten/MFA bleiben dort.
2. Nach dem Login muss die Anwendung zuerst dieselben vollständigen Skillwerte und denselben Charakter in unabhängigen Beobachtungen bestätigen. Ohne diese Daten starten keine Spielroutinen.
3. Ohne Einzelauftrag untersucht die Automatik das Inventar und wählt eine versorgte, unterstützte Trainingsmethode. Sie sucht Ziele ausschließlich im sichtbaren Spielbereich. Fehlt eine nötige Station, hält sie mit einer konkreten Begründung an.
4. Mit **Aufgabe …** lässt sich ein einzelner Trainingsschritt, eine Herstellung, ein GE-Kauf/-Verkauf oder eine Bankaktion einreihen. Das Öffnen pausiert die Automatik. Nach dem Einreihen im Hauptfenster **Automatik fortsetzen** wählen.
5. **F8** pausiert laufende Eingaben. **Log kopieren** liefert Zustandswechsel, ausgeführte Eingabearten und Prüfergebnisse. **Auftrag verwerfen** entfernt den Einzelauftrag; ein bestehendes GE-Angebot bleibt erhalten.

Der Kontotyp im Aufgabendialog muss zum Charakter passen. Ohne gespeicherte Einstellungen wird ein normales Konto angenommen; das ist keine automatische Ironman-Erkennung. Die anfängliche Münzreserve beträgt 500 und lässt sich im Dialog ändern. Ein Einzelauftrag überschreibt keinen bereits eingereihten Auftrag.

## Implementierte Abläufe

| Bereich | Implementierte Schritte | Ergebnisprüfung |
| --- | --- | --- |
| Woodcutting | Benutzbare Axt im Inventar prüfen; Baum/Eiche/Weide über Hover und exaktes Kontextmenü finden; Chop down auswählen | Neu gelesene Logs im Inventar |
| Mining | Pickaxe und Level prüfen; sichtbare Rocks per Mine-Menü bedienen | Tatsächliche neue Erzarten zählen; generische Rocks werden nicht als sicher erkannte Eisenader ausgegeben |
| Fishing | Kleines Netz oder Fliegenangel/Federn prüfen; Net bzw. Lure auf Fishing spot | Shrimps/Anchovies bzw. Trout/Salmon als reale Fangvarianten |
| Cooking | Rohen Fisch auf bestätigte Range verwenden; eindeutiges Produkt und Cook-1 auswählen | Passender Verbrauch und fertiges Produkt; kein Erfolg nur aufgrund des Klicks |
| Firemaking | Tinderbox über Use mit dem gewählten Holz verbinden | Genau ein gewählter Scheit verschwindet; die Routine behauptet daraus keinen konkret gelesenen XP-Wert |
| Lederverarbeitung | Needle, Leder und Thread prüfen; Use auf Leder; Make-1 des exakten Produkts | Lederverbrauch und Gegenstandszuwachs |
| Smithing | Smelt Furnace bzw. passenden Bar auf Anvil verwenden; Produkt auswählen | Barren-/Erzverbrauch und Fertigprodukt, bei Platebodies fünf Barren pro Stück |
| Prayer | Bury auf den genau identifizierten Knochenstapel/-slot | Genau ein Knochen verbraucht; kein erfundener Levelanstieg |
| Runecraft | Rune/Pure Essence am sichtbaren, bestätigten Craft-rune Altar verarbeiten | Essence-Verbrauch und tatsächlich entstandene Runen einschließlich Mehrfachrunen |
| Rezept-Herstellung | Katalogrezept, Level, alle Zutaten/Werkzeuge und Station prüfen; Einzelherstellung oder Use zweier Zutaten | Sämtliche Zutaten- und Produktdifferenzen |
| Lvl-4 Enchant | Magic-Tab und Spell über sichtbare Beschriftung finden; Cast-Menü an Diamond amulet binden | Amulet-of-power-Zuwachs und vollständiger vorgesehener Runenverbrauch |
| Bank | Bank booth erkennen; Produkte mit Deposit-All ablegen; gesuchtes Item über Banksuche finden; Withdraw-X und Mengenprompt bedienen | Tatsächlicher Abgang bzw. exakt angeforderter Zugang im Inventar |
| Grand Exchange | Leeren Buy-/Sell-Control finden; Item suchen/Offer auswählen; Menge und Preis setzen; Rechnung lesen; genau einmal Confirm; Angebotsslot und Collect prüfen | Item- und Münzdifferenzen; Teilfüllungen zählen nicht als vollständiger Auftrag |

Generische Altar- und Felsbezeichnungen unterscheiden weder Altarart noch Erztyp. Auch ihre Examine-Texte sind teilweise identisch. Deshalb bestätigen diese Routinen nur das **tatsächlich entstandene** Material. Sie teleportieren nicht zu einem anderen Altar und geben keine unbelegte Materialidentität aus. Die Runecraft-Routine setzt voraus, dass der Charakter bereits einen passenden sichtbaren Altar erreichen kann.

Die automatischen Trainingswege decken neun freie Nicht-Kampf-Skills ab. Die sechs Kampfskills besitzen noch keinen Kampfausführer. Verzaubern ist eine gezielte Herstellung, kein fertiger Magic-Kampftrainer. Bei 99 wird kein weiterer Trainingsauftrag für denselben Skill begonnen.

## Beobachten und Ausführen

`ExecutionVision` erzeugt Signaturen der 28 Inventarslots. Mengen werden in einem vergrößerten Ausschnitt gelesen. Abgekürzte K-/M-Mengen werden nicht wissentlich in exakte Bestände umgedeutet. `GameInteractions` öffnet Kontextmenüs und liest den Itemnamen aus der Examine-Zeile. Ein leerer Slot muss ein dazu passendes Cancel-Menü ergeben. Nicht lesbare oder mehrdeutige Slots bleiben unbekannt.

Gecachte Namen gelten nur bei unveränderter Bildsignatur. Nachdem das ganze Inventar gelesen wurde, müssen die Signaturen weiterhin passen; gelesene Stapelmengen werden ebenfalls gegen die abschließende Beobachtung geprüft. Ein wechselndes Inventar gilt nicht als stabiler Bestand.

Objekte werden durch reines Hover im sichtbaren Szenenbereich gesucht. Erst ein lesbarer Aktionshinweis und das passende Kontextmenü erlauben einen Klick. Das ist eine begrenzte visuelle Suche, keine Navigation über die Spielkarte. Eine vorhandene Pickaxe/Axt muss für den gelesenen Skill benutzbar sein; ein zu hohes Tier genügt nicht.

`WindowVisualGameDriver` verlangt nach jeder Eingabe ein unabhängiges neueres Bild desselben Fensters mit unveränderter Geometrie. Der Dispatcher prüft weiterhin Hostzugehörigkeit, Pause und Bildalter. Texte sind nur in erkannten Mengen-, Preis-, GE-Such- oder Banksuchprompts zulässig. Eingaben gehen als Fensternachrichten an das Spiel, ohne den globalen Mauszeiger zu bewegen. Das beweist noch nicht, dass der offizielle Client alle Nachrichten annimmt.

Eine bestätigte Skillbeobachtung bindet die Ausführung höchstens zwei Minuten an den Charakter. Danach müssen Skills erneut gelesen werden. Ein erkannter Charakterwechsel oder Login-/Weltenbildschirm stoppt die laufende Routine. Eine exklusive lokale Ausführungssperre verhindert, dass zwei Instanzen dieser neuen Routinen gleichzeitig steuern.

## Automatische Versorgung und Herstellung

Die Automatik bevorzugt bereits versorgte, zugängliche Trainingsmethoden. Bei vollem Inventar kann sie erkannte Produkte einzeln einlagern, ohne Werkzeuge pauschal abzugeben. Fehlt Material oder ein Werkzeug, kann sie eine begrenzte Bankentnahme versuchen. Nur eine explizit leere, ausgeführte Banksuche ist als fehlender Bankbestand vorgesehen. Eine erfolglos abgesuchte sichtbare Bankseite gilt nicht als Beweis für null Bestand.

Nach einer bestätigten leeren Banksuche kann ein normaler Account einen einzelnen nötigen Gegenstand über die GE beschaffen. Dazu müssen GE und Bank in der erkennbaren Umgebung bedienbar sein, genügend exakt gelesene Münzen im Inventar liegen und ein aktueller Preis vorliegen. Ein kompletter Navigations- und Beschaffungsgraph über alle Orte und Zutaten ist damit **noch nicht** umgesetzt. Eine fehlende Bank oder GE wird nicht durch zufälliges Laufen ersetzt.

Einzelne Rezeptaufträge unterstützen die gespeicherten Metallbarren, Rune axe, die Diamond-Amulett-Kette und Lvl-4 Enchant. Es wird jeweils eine verifizierbare Rezeptstufe hergestellt; fehlende Vorstufen werden nicht als vorhanden angenommen. Ob Kaufen oder Herstellen günstiger ist, kann der bestehende F2P-Planer anhand geprüfter Optionen entscheiden. Die neue Laufzeit hat aber noch keinen vollständig automatisch beobachteten Vergleich sämtlicher Einkaufs-, Wege- und Herstellungskosten.

## GE-Preise und Transaktionen

`WikiPriceService` verwendet die öffentliche [Wiki-Preis-API](https://oldschool.runescape.wiki/w/RuneScape:Real-time_Prices). Mapping-Daten liefern Namen, IDs, F2P-Eignung und Buy-Limits; der einzelne aktuelle High-/Low-Wert liefert Preis und Transaktionszeit. Fehlende oder mehr als 60 Sekunden alte Preise führen zu keinem neuen Auftrag. Der HTTP-Client besitzt einen beschreibenden User-Agent.

Ein Kauf setzt einen exakt gelesenen Inventar-Münzbestand voraus. Bankmünzen werden nicht als freie zusätzliche Mittel unterstellt. Die Münzreserve bleibt bei der Vorprüfung erhalten. Ein Batch ist auf 1.000 Einheiten begrenzt und muss zusätzlich das itembezogene Buy-Limit einhalten. Der tatsächliche freie Angebots-Control wird vor dem Einstellen beobachtet; drei freie Slots werden nicht einfach angenommen.

Vor Confirm müssen Artikel, Seite Buy/Sell, Menge, Stückpreis und Gesamtpreis zur Bestellung passen. Verkäufe eingeschränkter F2P-Ressourcen bleiben bei unbekannter Spielzeit/Questpunktlage gesperrt. Die [Bank-](https://oldschool.runescape.wiki/w/Bank) und [GE-Mechanik](https://oldschool.runescape.wiki/w/Grand_Exchange) sind getrennte Zustandsabläufe.

Unmittelbar vor Confirm wird die Absicht dauerhaft gespeichert: Auftrags-ID, Parameter, Bestand vor der Transaktion, Charakter-Hash und Angebotsposition. Ein Neustart erstellt deshalb kein zweites Angebot. Der Agent versucht zuerst, das bestehende Angebot wiederzuerkennen und einzusammeln. Der Klarname wird hierfür nicht gespeichert.

Collect verlangt die zu Artikel, Menge und Preis passenden Angebotsdetails. Ein vollständiger Kauf benötigt den passenden Gegenstandszugang und eine positive Münzausgabe innerhalb der Bestellung und Reserve. Ein Verkauf benötigt den passenden Itemabgang und mindestens den konservativ berechneten Nettoerlös nach der Gebührenrundung. Teilfüllung, fehlender Beleg oder abweichender Bestand lassen die Absicht bestehen. Automatisches Hochsetzen des Preises, blindes Wiedereinstellen und automatische Wiederholung von Confirm sind nicht implementiert.

## Dateien und Diagnose

Unter `%LOCALAPPDATA%\OSRSPlay\agent` liegen:

- `settings.json`: Kontotyp, Münzreserve und Aktivierung.
- `job.json`: optionaler Einzelauftrag. Die GUI erzeugt ihn; nach bestätigter Ausführung wird er entfernt.
- `pending-trade.json`: ein noch nicht vollständig bestätigtes GE-Angebot. Nicht zum Erzwingen eines neuen Kaufs löschen.
- `execution.lock`: die Sperrdatei für eine einzelne aktive Ausführung.

Relevante Logquellen sind `GAMEPLAY`, `VISUAL_INPUT` und `VISUAL_STARTUP`. `COMPLETED` bedeutet den im Grund genannten beobachteten Effekt. `BLOCKED` nennt eine fehlende Voraussetzung. `UnknownOutcome` behauptet keinen Erfolg und stoppt neue Trainingsaktionen bis zur Prüfung; ein vorhandenes GE-Angebot wird gesondert abgeglichen. Das Verwerfen eines GUI-Auftrags hebt keine GE-Transaktion auf.

## Tests und Grenzen

Die Tests prüfen Kontextmenügrenzen, fehlende Ausführungsbelege, Werkzeuglevel, Mischfang, Erz-/Runenertrag, Rezeptmengen, Inventarbestände, Preisalter, Reserven, Handelsbeschränkungen, Rechnungsbindung, Gebührenrundung, Teilfüllungen und Wiederanlauf des Angebotsjournals. OCR-Fixtures prüfen Menütext und Mengen einschließlich der Ablehnung von K-Abkürzungen. Der Integrationstest mit eigenen verdeckten Fenstern prüft Links-/Rechtsklick und Textübertragung sowie eine beobachtete Bildänderung. Parallel bewegte physische Maus/Fokus-Eingaben werden beim Vergleich ausdrücklich als nicht auswertbar gemeldet.

Diese Tests bestätigen Programmlogik und den Transport zu eigenen Fenstern. Sie bestätigen keine erfolgreiche Trainingseinheit, Herstellung oder GE-Transaktion auf dem offiziellen Client. Dort sind Inventarlayout, Tooltip-/Menütexte, Mengenfelder, Produktionsauswahl und Angebotsdetails noch zu kalibrieren und vollständig zu prüfen. Automatischer Kampf, Reisen zwischen Orten, Quest-Ausführung, zuverlässiges Gesundheits-/Gefahrenmanagement und kompletter Langzeit-Maxing-Betrieb bleiben offen. Minimiert ist die Bildaufnahme weiterhin pausiert.
