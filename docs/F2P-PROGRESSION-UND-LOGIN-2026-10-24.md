# F2P-Analyse und Login-Korrekturen, Version 1.5.0

Stand: 24. Oktober 2026. Ausgewertet wurden die verlinkte Übersichtsseite, die einzelnen Trainingsseiten, Questübersichten, Handelsbeschränkungen und die Itemseiten der implementierten Rezeptkette. Die folgenden Regeln sind eine konservative Planungsgrundlage, keine Behauptung eines bereits funktionsfähigen Maxing-Agenten.

## Befund aus dem eingesandten Log

1. Der Launcher blieb zunächst bei `LAUNCHER_PLAY_NOT_UNAMBIGUOUS`. Der offizielle Client erschien erst später; im Log gab es keine versandte Eingabe. Der Launcher hat eine größere Mindestbreite als die Spielansicht. Einbetten kann seinen rechten Play-Button abschneiden und ihn für Accessibility als offscreen markieren. Er bleibt jetzt in seinem unveränderten Originalfenster; nur das Spiel wird eingebettet.
2. Danach blockierte `WORLD_LABEL_NOT_RECOGNIZED`. Die ursprüngliche Regel verlangte eine vollständige OCR-Zeile `World 397`. Am echten Client lieferte die vergrößerte Footer-Erkennung teilweise nur `397` und `Click switch`; das Wort „World“ ging verloren. Die neue Regel verlangt eine eindeutige dreistellige Weltzahl direkt oberhalb des erkannten Switch-Hinweises im unteren linken Loginbereich. Eine isolierte Zahl reicht nicht. Unterschiedliche gleichzeitig erkannte Welten blockieren weiterhin.
3. `CLIENT_CLIPPED` wurde bisher ohne Maße gemeldet. Neue Geometriediagnosen enthalten Bildgröße, tatsächliche Clientgröße und Ausschnitt. Native Minimiert-/Maximiert-Styles werden beim Einbetten entfernt, der Client in ein normales Layout versetzt und die tatsächliche Clientgröße bei weiteren Größenprüfungen berücksichtigt. Der Fehler darf bei unbekannter Geometrie weiterhin Eingaben stoppen; ein abgeschnittener Bildbereich wird nicht künstlich ergänzt.

Im lokalen Test öffnete die Anwendung die echte Weltenauswahl durch `OPEN_WORLD_SELECTION` als Fensternachricht. Der Wechsel des Spielbildschirms war anschließend sichtbar. Der Launcher-Start zuvor wurde über Computer Use ausgelöst. Das ist ein konkreter Nachweis für diese einzelne Spieleingabe, aber noch kein Nachweis eines vollständig automatischen Logins oder beliebiger Hintergrundeingaben bei verdecktem Client. Anschließend lag die schnellste Welt auf einer anderen Seite. Die Computer-Use-Prüfung wurde durch Escape beendet, bevor der gesamte Ablauf erneut überprüft werden konnte.

## Bildschirmerkennung und Seitennavigation

Das Weltraster bekommt einen zusätzlichen OCR-Durchlauf: Schon erkannte aufsteigende Spalten liefern Zeilenabstand und Spaltenabstand. Kleine Zahlenausschnitte werden vergrößert auf einer Kontaktfläche gelesen. Die Ergebnisse müssen erneut eine hinreichend lange aufsteigende Weltnummern-Spalte bilden. Spielerzahlen und ein erratenes Tabellenfeld sind kein Weltbeleg.

Für die native 1000×600-Ansicht können die Bildpfeile der Weltenauswahl erkannt werden. Nur ein bestätigtes Weltraster, ein passender Pfeilbereich und eine außerhalb der erkannten Weltspanne liegende Zielwelt erlauben Blättern. Fehlende OCR innerhalb der aktuellen Spanne löst kein blindes Blättern aus. Pro Fenster gelten zwölf Seitenaktionen als Obergrenze und drei unveränderte Aktionsversuche. Ohne frisches Bild, passende Fenstergeometrie oder bei Pause erfolgt keine Eingabe. **Die neue Seitennavigation und der komplette Login sind noch am echten Client zu bestätigen.**

## Welche Inhalte die Übersicht für die Planung erschließt

Die [F2P-Übersicht](https://oldschool.runescape.wiki/w/Free-to-play) trennt freie Skills, zugängliche Ausrüstung, Quests, Reisemittel, Nahrung, Aktivitäten und Kontobeschränkungen. Daraus folgt für die Architektur: Mitgliedschaft und Weltmodus begrenzen den Aktionsraum; Gegenstände allein beweisen keine benutzbare Aktivität. Die Übersicht ersetzt weder eine Laufroute noch die Erkennung von Inventar, Questfortschritt oder Spielobjekten. Auch erwähnte Mitglieder-Effekte sind kein Zugang zu Mitglieder-Aktivitäten.

Für alle Skills auf 99 bleibt Mitgliedschaft erforderlich. Ein F2P-Abschnitt endet deshalb mit einem Mitgliedschaftsbedarf, wenn die freien Skills abgeschlossen sind. Ein Bond wird nicht automatisch als bezahlt, erschwinglich oder eingelöst behandelt.

## Trainierbare Skills und Basisketten

`F2PProgression.Methods` enthält lückenlose, eindeutige Basismethoden für jeden trainierbaren Level von 1 bis 98 der 15 freien Skills. Die Methoden sind bewusst finanzierbare Ausgangspunkte, keine universell schnellste Strategie. Ein höheres freigeschaltetes Material ist nicht zwangsläufig günstiger oder sinnvoller.

| Skill | Implementierte Basisplanung | Abhängigkeiten und Quelle |
| --- | --- | --- |
| Attack, Strength, Defence | Hühner bei Lumbridge, mit zum Zielskill passendem Kampfstil | Benutzbare Waffe, Nahrung, Ziel- und Lebenspunkteprüfung; [Melee](https://oldschool.runescape.wiki/w/Free-to-play_melee_training). Höhere Monster benötigen zusätzliche Kampf- und Überlebensbelege. |
| Hitpoints | Begleitfortschritt durch Kampf | Kein erfundener separater HP-Klick; Kampfplanung muss die gewählte Angriffsart berücksichtigen. |
| Ranged | Einfacher Bogenkampf | Bogen und dazu passende Munition, Nahrung und Kampfstil; [Ranged](https://oldschool.runescape.wiki/w/Free-to-play_Ranged_training). Green d'hide und weitere Upgrades benötigen eigene Item-/Questprüfungen. |
| Magic | Wind Strike als zugängliche Basismethode | Luft- und Mind-Runen; ein Elementarstab kann die entsprechenden Runen ersetzen, benötigt aber einen gesondert geprüften Ausrüstungszustand. [Magic](https://oldschool.runescape.wiki/w/Free-to-play_Magic_training). Höhere Zauber werden nicht allein wegen des Levels automatisch teuer eingekauft. |
| Prayer | Knochen vergraben | Knochen im Bestand. Kampfbeute und Quest-XP können Verbrauchskosten reduzieren. [Prayer](https://oldschool.runescape.wiki/w/Free-to-play_Prayer_training). |
| Mining | Kupfer/Zinn bis 15, anschließend Eisen | Nutzbare Pickaxe; Eisen kann für Smithing aufbewahrt werden, sofern Bankkapazität und Transportweg bekannt sind. [Mining](https://oldschool.runescape.wiki/w/Free-to-play_Mining_training). |
| Smithing | Bronze schmelzen; Plattenpanzer ab 18/33/48/68/88 | Erze bzw. fünf passende Barren pro Plattenpanzer, Hammer, Schmelzofen oder Amboss. [Smithing](https://oldschool.runescape.wiki/w/Free-to-play_Smithing_training). Quest-XP kann den Einstieg ersetzen. |
| Woodcutting | Bäume bis 15, Eichen bis 30, danach Weiden | Nutzbare Axt; Holz kann Firemaking versorgen. [Woodcutting](https://oldschool.runescape.wiki/w/Free-to-play_Woodcutting_training). Maple-Zugang und gefährlichere Standorte werden nicht unterstellt. |
| Firemaking | Normale, Eichen-, Weiden-, dann Ahornscheite ab 1/15/30/45 | Zunderbüchse und Holz. Ahorn bleibt eine mögliche günstige Methode bis 99; Eibe ab 60 ist kein Pflichtkauf. [Firemaking](https://oldschool.runescape.wiki/w/Free-to-play_Firemaking_training). |
| Fishing | Netzfischen bis 20, danach Fliegenfischen | Kleines Netz bzw. Fliegenangel und Federn; Fang kann Cooking beliefern. [Fishing](https://oldschool.runescape.wiki/w/Free-to-play_Fishing_training). |
| Cooking | Shrimps bis 15, Forellen bis 25, danach Lachs | Roher Fisch und bestätigte Kochstelle; Feuer-/Range-Zugang und verbrannte Nahrung müssen ein Ausführer beobachten. [Cooking](https://oldschool.runescape.wiki/w/Free-to-play_Cooking_training). Schnellere Weinherstellung ist eine spätere kostenabhängige Alternative. |
| Crafting | Lederhandschuhe, Stiefel, Haube, Armschienen, Körper, Chaps, Hardleather body ab 1/7/9/11/14/18/28 | Leder, Nadel, Faden; Gerben ist ein eigener Beschaffungsschritt. Schmuck ist eine weitere Kette mit Formen, Barren, Edelsteinen und Ofen. [Crafting](https://oldschool.runescape.wiki/w/Free-to-play_Crafting_training). |
| Runecraft | Luft bis 9, Erde bis 20, danach Körperrunen | Passender Talisman oder Tiara und Rune/Pure Essence. Die Feuerrunen-Alternative ab 14 ist nicht zwingend schneller als Erde. [Runecraft](https://oldschool.runescape.wiki/w/Free-to-play_Runecraft_training). |

Für Werkzeuge werden Benutzung und Tragen getrennt: Bei Axt/Pickaxe liegen die Nutzungsstufen bei 1/6/11/21/31/41, die entsprechenden Attack-Anforderungen bei 1/5/10/20/30/40. Ein Werkzeug darf aus dem Inventar benutzt werden, wenn die Tragevoraussetzung fehlt. Eine vorhandene Rune-Pickaxe macht Mining unter 41 nicht ausführbar. Schwarze Äxte und Pickaxes erhalten keine erfundenen Smithing-Rezepte. Der Werkzeugvorschlag wird zusammen mit dem Aktivitätsplan geloggt; er löst allein keinen Einkauf aus.

## Quests als Fortschrittsabhängigkeiten

Die [Questliste](https://oldschool.runescape.wiki/w/Quests/Free-to-play) und der [regionale Questleitfaden](https://oldschool.runescape.wiki/w/Optimal_quest_guide/Free-to-play) wurden getrennt gelesen. Es gibt jetzt neun konkrete XP-Gelegenheiten im Katalog: Cook's Assistant, Doric's Quest, The Knight's Sword, The Restless Ghost, Sheep Shearer, Misthalin Mystery, Witch's Potion, Imp Catcher und Vampyre Slayer. Ein vollständiger, aktueller Queststatus ist Voraussetzung für die Aussage „noch nicht abgeschlossen“. Kampflastige Quests bleiben als gefährlich markiert.

Beispielketten:

- [Doric's Quest](https://oldschool.runescape.wiki/w/Doric%27s_Quest): sechs Clay, vier Copper ore, zwei Iron ore; 1.300 Mining-XP ermöglichen den frühen Sprung auf 10. Eisen selbst abzubauen erfordert 15 Mining. Ein normaler Account kann es stattdessen kaufen; ein Ironman benötigt eine andere echte Quelle.
- [The Knight's Sword](https://oldschool.runescape.wiki/w/The_Knight%27s_Sword): Mining 10, eine vollständige Redberry pie, zwei Iron bars und Pickaxe. 12.725 Smithing-XP können einen frischen Skill auf 29 bringen. Cooking 10 ist nur nötig, wenn die Pie selbst gekocht wird; es ist keine allgemeine Questvoraussetzung. Die Route führt an deutlich stärkeren Gegnern vorbei.
- [Below Ice Mountain](https://oldschool.runescape.wiki/w/Below_Ice_Mountain): 16 Questpunkte sind eine Zugangsvoraussetzung für Camdozaal. Der Katalog behauptet nicht, diese Quest schon ausführen zu können.

Dragon Slayer I und moderne Inhalte wie The Ides of Milk/Brutus wurden in der Analyse berücksichtigt, sind aber keine automatisch ausführbaren Questabläufe. Ein Boss aus einem Guide wird nicht ohne Angriffserkennung, Gesundheitskontrolle, Nahrung und bestätigte Freischaltung zum Standardziel.

## Beschaffung und Ausrüstung

`Acquire` trennt bekannte Bestände, fehlende Menge, Kauf und Herstellung. Ein Angebot ist an Produkt und fehlende Stückzahl gebunden. Bank/Inventar/Münzen müssen aktuell sein, Preisbelege höchstens 60 Sekunden alt und dürfen nicht aus der Zukunft stammen. Die Münzreserve bleibt erhalten. Ein GE-Kauf benötigt normalen Kontomodus, einen freien Angebotsplatz und einen verfügbaren Ausführer. Ein günstiger Guide-Preis beweist weder ein ausführbares Angebot noch Gewinn.

Herstellung benötigt ein Katalogrezept, die Skillvoraussetzung, den beobachteten Arbeitsplatz, alle Materialien der vollständigen Charge sowie Werkzeuge. Vorrätige Produkte werden bevorzugt. Unbekannte Stationen, unzureichende Mengen, fehlende Ausführer und alte Preise führen zu `WAIT`.

Verifizierte Beispielkette für eine [Amulet of power](https://oldschool.runescape.wiki/w/Amulet_of_power): Gold ore → Gold bar mit Smithing 40; Gold bar plus Diamond und Amulet mould → unstrung Diamond amulet mit Crafting 70; Ball of wool zum Auffädeln; [Lvl-4 Enchant](https://oldschool.runescape.wiki/w/Lvl-4_Enchant) mit Magic 57, einer Cosmic rune und zehn Earth runes. Der Kauf eines fertigen Amuletts kann wesentlich früher sinnvoll sein. [Diamond amulet](https://oldschool.runescape.wiki/w/Diamond_amulet).

Weitere gespeicherte Rezepte: Bronze-, Steel-, Mithril-, Adamantite-, Runite- und Gold-Barren sowie [Rune axe](https://oldschool.runescape.wiki/w/Rune_axe) aus einem Runite bar bei Smithing 86. Iron-Bar-Schmelzen wird nicht als garantiert verlustfreies Rezept hinterlegt: Ohne passenden Effekt kann es scheitern. Materialbeschaffung, Preise und Stationen werden noch nicht automatisch beobachtet.

## Wirtschaft, Reisen und Kapazitäten

Die [Kontobeschränkungen](https://oldschool.runescape.wiki/w/Trade_restrictions) betreffen unter anderem Verkäufe bestimmter Ressourcen. Der neue Test verlangt **mehr als** 20 gespielte Stunden, mindestens zehn Questpunkte und Gesamtlevel 100 gemeinsam; frühere Mitgliedschaft ist eine eigene Ausnahme. Das ist keine generelle Kaufsperre. Ironman-Konten werden gesondert behandelt; die Bond-Ausnahme erlaubt keine normalen GE-Itemkäufe. Die [Grand Exchange](https://oldschool.runescape.wiki/w/Grand_Exchange) benötigt ein Modell für drei freie Slots, belegte Angebote, Kaufgrenzen, Abschlussbestätigung, Gebühren und Liquidität. Diese Beobachter/Ausführer fehlen noch.

Die Reiseanalyse unterscheidet Gehwege, Home-/Minigame-Teleport, Zauber, Kanus und freigeschaltete Teleportgegenstände. Level, Runen, Ladungen und Cooldowns müssen jeweils beobachtet werden. Ein in einem Guide genannter PvP- oder Wilderness-Weg wird nicht automatisch zur Standardroute. Bank- und Inventarplätze, Nahrungsvorrat und Laufenergie müssen in die spätere Chargengröße eingehen. Sicherheitsoptionen für zusätzliche Bankplätze werden nicht automatisch verändert.

## Widersprüche und Datenqualität

- Die Übersicht nennt einen F2P-Maximalgesamtlevel, der nicht mit ihrer ebenfalls genannten Anzahl freier Skills plus neun Mitglieder-Startleveln aufgeht. Deshalb wird diese Zahl nicht als Maxing-Bedingung fest einprogrammiert. Maßgeblich ist die explizite Liste aller aktuell bekannten Skills mit Level 99.
- Der regionale Questleitfaden und die aktuelle Questliste unterscheiden sich beim Gesamt-QP-Ziel; neue Quests dürfen nicht aus einer alten Reihenfolge als erledigt gelten.
- Der Smithing-Guide nennt Rune-Platebodies in einem Abschnitt ab 98; die [Itemseite](https://oldschool.runescape.wiki/w/Rune_platebody) verlangt 99. Die Basismethode führt daher mit Adamant-Platebodies zu 99 und plant nicht fälschlich Rune-Platebodies ab 98 ein.
- Preis-, Gewinn- und XP/h-Tabellen sind Beispiele unter jeweiligen Annahmen. Es wurden keine solchen Marktpreise als Live-Daten übernommen. Normale und Ironman-Strategien werden nicht vermischt.

## Prüfstand und verbleibende Arbeit

Lokal bestehen Build, Selftests, Windows-OCR-Fixtures, Layouttest und Hintergrundtest an eigenen Fenstern. Die neuen Tests prüfen unter anderem sämtliche Levelbereiche, falsche Werkzeugtier-Nutzung, unvollständige Chargen, Kontomodus, Handelsgrenzen, Preisalter, Produktbindung und fehlende Ausführer. OCR-Fixtures enthalten den verlorenen „World“-Text und eine fehlende Weltziffer im ersten Erkennungsdurchlauf. Sie ersetzen keinen echten Account-Test.

Der aktive Zielplaner erzeugt aus wiederholt konsistenten Skilllesungen nun einen konkreten Aktivitäts- und Werkzeugvorschlag. Weil Inventar-, Bank- und Questbeobachter fehlen, lautet dessen Voraussetzung zunächst `READ_PREREQUISITES`. Es werden keine Bestände erfunden. Navigation im Spiel, Kampfausführung, Training, Bank, GE und Herstellung bleiben eigenständige fehlende Ausführer. Vollautonomes Maxing ist weiterhin nicht fertig.
