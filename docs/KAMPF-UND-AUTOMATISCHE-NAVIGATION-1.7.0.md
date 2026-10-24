# Kampf und automatische Navigation – 1.7.0

Die Version baut auf dem GitHub-Stand 1.6.7 auf. Die neueren Weltlisten- und Login-Korrekturen bleiben enthalten. Automatische Navigation benötigt keine aufgezeichneten Benutzerwege und keinen Dialog zum Kalibrieren von Wegpunkten.

## Ablauf

Nach der wiederholt bestätigten Charakter-/Skill-Erkennung entscheidet der Koordinator über eine unterstützte Aktivität. Vor einer ortsabhängigen Aufgabe wird deren Reiseziel bestimmt. Für Bank und GE sowie mehrere Trainingsplätze sind öffentliche Weltkoordinaten hinterlegt. Wege zwischen diesen Zielen werden zur Laufzeit aus der tatsächlich erkannten Position berechnet; es sind keine fest abgespielten Klickfolgen.

1. Die kreisförmige Minimap und die weiße Spielermarkierung werden automatisch im 1000×600-Spielbild gesucht. Die HP-Zahl benötigt zusätzlich den roten Herzbereich und eine separate vergrößerte OCR-Lesung.
2. SIFT-Merkmale der Minimap werden mit öffentlichen Referenzkarten verglichen. RANSAC bestimmt eine Transformation einschließlich Drehung und Skalierung. Mindestens acht räumlich verteilte übereinstimmende Merkmale, passende Bildkanten und eine eindeutige Kartenebene sind erforderlich.
3. Zwei zeitlich unabhängige, weitgehend unveränderte Positionslesungen bestätigen den Ausgangspunkt. A* sucht auf öffentlichen Kollisionsdaten einen begehbaren Weg. Diagonalen dürfen keine Mauerecken schneiden.
4. Der nächste kurze Wegabschnitt wird über die ermittelte Transformation in einen Klick innerhalb der Minimap übersetzt. Nach dem Klick muss die neue Position erneut beobachtet werden. Ein transportierter Mausklick zählt nicht als Ankunft.
5. Nach jedem Abschnitt plant der nächste Durchlauf aus der neuen Position weiter. Bedingungslose Treppenverbindungen aus den öffentlichen Daten können die Ebene wechseln. Bei ausbleibender Bewegung werden sichtbar identifizierte Türen/Tore begrenzt untersucht. Wiederholtes Scheitern oder verlorene Positionssicherheit beendet weitere Laufklicks.

Erste Kartendownloads erfolgen automatisch; weitere Starts benutzen den lokalen Cache unter `%LOCALAPPDATA%\OSRSPlay\maps`. Es werden öffentliche Kartendateien geladen, keine Charakterdaten oder Spielbilder hochgeladen. Die Referenzbilder bleiben auf dem Rechner. Eine aktive Internetverbindung ist für den ersten Download und weiterhin für aktuelle GE-Preise nötig.

## Reichweite

Der Router unterstützt zunächst das zentrale F2P-Festland zwischen x=2944…3391 und y=3072…3519 auf den Ebenen 0…3. Die nördliche Grenze schließt die Wilderness aus. Eingebaute Ziele umfassen Lumbridge, Draynor, Varrock, Falador, Al Kharid, GE, Hühner, Bäume/Eichen/Weiden, Minen, Fischplätze, Herd, Schmelzofen und Amboss. Bei einem Bankauftrag wird ein erreichbares Bankziel anhand des berechneten Wegs ausgewählt.

Unbekannte Positionen, geschlossene/verdeckte Minimap, nicht sicher lesbare Lebenspunkte und Positionen außerhalb dieser Abdeckung führen zu einem erklärten Stopp. Die Kartenerkennung ist kein universeller Positionssensor für sämtliche Dungeons, Instanzen oder Mitgliedergebiete. Bei den Runecraft-Zielen ist zunächst der äußere Altareingang hinterlegt; Talisman-/Tiara-Zugang und der Wechsel zur inneren Altarkarte sind noch kein vollständiger Reiseablauf.

## Kampf

Unterstützt werden `ATTACK_CHICKENS`, `STRENGTH_CHICKENS`, `DEFENCE_CHICKENS`, `RANGED_CHICKENS` und `WIND_STRIKE`. Ausschließlich der exakte sichtbare Kontextmenüeintrag für **Chicken (level-1)** wird als Angriffsziel akzeptiert. Andere Monster oder Spieler erfüllen diese Bedingung nicht.

Nahkampf verwendet eine Bronze-/Eisenscimitar und einen über den XP-Tooltip zugeordneten Kampfstil. Fernkampf verwendet Shortbow und Bronze arrows; Magie bindet Wind Strike über den exakten Cast-Menüeintrag an das Ziel. Ausrüsten verlangt den passenden Wield-Eintrag und anschließend den entsprechenden Remove-Eintrag in der Ausrüstung. Bereits getragene benötigte Waffen/Munition werden vor einem Neukauf untersucht.

Der Start verlangt mindestens 90 % der bekannten Basis-Lebenspunkte; unterstützte Nahrung kann vorher gegessen werden. Im laufenden Kampf werden HP auch während der Bild-/Eingabeschritte geprüft. Unter 70 % wird Nahrung gegessen und eine neue HP-Lesung verlangt. Fehlt Nahrung oder bleibt die Heilung aus, wird ein Rückzug zum eingebauten sicheren Reiseziel begonnen. Ein Rückzug bleibt über mehrere Wegabschnitte gespeichert.

Ein Kampfauftrag bestätigt einen beobachteten Anstieg der XP des gewählten Skills. Er behauptet weder einen Kill noch Besitz an Bodenbeute. Hitpoints wird begleitend trainiert, nicht über eine erfundene eigene Angriffsoption. Bei Level 99 wird für diesen Zielskill kein neuer Trainingsauftrag begonnen. Weitere Monster, Beuteaufnahme, allgemeine Ausrüstungs-Upgrades und vollständige Gefahrenabwehr sind nicht Bestandteil dieses ersten Kampfausführers.

`pending-combat.json` bindet Vorbereitung, Angriff und Rückzug an einen Charakter-Hash und die ursprüngliche Skill-XP-Lesung. Der Angriff wird vor dem einzigen Angriffsklick dauerhaft markiert. Nach Pause/Neustart wird ein offener Kampf überwacht und abgeglichen, ohne den Angriff blind zu wiederholen. Eine unterbrochene Ausrüstungsvorbereitung wird zunächst aufgelöst. Ein unbekanntes Ergebnis bleibt als solches sichtbar.

## Versorgung und Bedienung

Ohne Einzelauftrag berücksichtigt die automatische Auswahl die unterstützten Kampfskills neben den bisherigen Trainingsausführern. Fehlende Nahrung, Waffe, Pfeile oder Runen führen zunächst zur Bank. Erst eine ausdrücklich leere Banksuche erlaubt auf einem normalen Konto einen Kaufversuch am GE. Münzreserve und die vorhandenen Angebots-/Preisprüfungen gelten weiter. Der Beschaffungsauftrag bleibt während der Reise von der Bank zum GE in `pending-supply.json` erhalten. Ein Ironman-Konto bekommt keinen GE-Kauf als Ersatz für eine nicht vorhandene Herstellungsmethode.

**Aufgabe …** bietet zusätzlich Kampf und Reisen zu den eingebauten Orten. Die Auswahl eines Einzelauftrags ist optional; sie ist keine Voraussetzung für die automatische Aufgabenwahl. F8 pausiert weiterhin Eingaben. Log aus-/einblenden und Log kopieren bleiben unverändert. Die Spielansicht bleibt fest 1000×600; der Logbereich dockt darunter an. Minimiert ist die Bildaufnahme weiterhin pausiert.

## Prüfungen und Beweisgrenzen

- `--self-test`: Kollisionsbitformat, A*, Umgehen gesperrter Diagonalen, Wilderness-Grenze, Treppen, Koordinatentransformation, HP-/XP-Prüfungen sowie persistente Kampfphasen und Schutz gegen erneute Angriffe.
- `--navigation-self-test`: Download/Lesen der echten öffentlichen Daten; Wege Lumbridge → GE, Draynor → Al Kharid, Varrock → Falador und oberes Lumbridge-Stockwerk → Draynor. Referenzbilder mit Drehungen von 0°, 37° und −60° sowie unterschiedlichen Maßstäben, automatisches Erkennen der Minimap-Geometrie, Ablehnung schwarzer oder mehrdeutiger Karten und eines bewegungslosen Charakters.
- `--ocr-self-test`, `--layout-self-test`, `--background-self-test`: bestehende OCR-, Fenster- und Transportprüfungen. Der Hintergrundtest benutzt eigene Testfenster und kann bei gleichzeitiger Benutzereingabe den Fokus-/Mausvergleich ausdrücklich als nicht aussagekräftig melden.

Diese Prüfungen ersetzen keinen erfolgreichen Ende-zu-Ende-Kampf und keine erfolgreiche Reise auf dem offiziellen Client. Die Bildzuordnung zwischen gerenderten Referenzkarten und der tatsächlichen nativen Minimap, die HP-Erkennung sowie Ausrüstungs-/Kampfstil-Menüs müssen dort noch bestätigt werden. Die Anwendung wird nicht als fertig nachgewiesener vollautonomer Langzeit-Maxing-Agent ausgegeben.

Beim Desktopversuch am 24.10.2026 ließ sich der ausgewählte Charakter über die beiden nativen Einstiegsschaltflächen manuell in die echte Spielansicht übernehmen. Die eingebettete Beobachtungsansicht und Minimap-Erkennung funktionierten; der automatische Beobachter bestätigte weder HP noch Weltposition. Es wurden keine Reise- oder Kampfaktionen des neuen Ausführers live bestätigt. Der Computer-Use-Helfer wurde wiederholt per Escape gestoppt.

Bei dieser Untersuchung wurde zusätzlich ein Fehler im Bildtransport gefunden: Windows-Graphics-Capture-Oberflächen mit `BitmapAlphaMode.Ignore` wurden bislang unmittelbar als ARGB übernommen. Nicht definierte Alpha-Bytes können dabei gültige RGB-Bildinhalte beim PNG-/OCR-Vorverarbeiten transparent machen. 1.7.0 setzt beim Übernehmen des komponierten Fensterbilds Alpha ausdrücklich auf 255; die Farbwerte bleiben erhalten. Ein Regressionstest prüft die anschließende GDI-Komposition. Die praktische Wirkung dieser letzten Korrektur ist am echten Spiel noch nicht erneut bestätigt. Der Diagnosebericht enthält nun Alpha-Stichprobenzähler sowie im Beobachtungsmodus `minimap_identified`, `hp`, `position_identified` und Merkmalsanzahl. OCR-Volltext und Spielbilder werden nicht in den Log exportiert.

Der native Inventarausschnitt im eingebetteten 1000×600-Bild zeigte außerdem, dass der bisherige Startwert der Inventarspalten um 32 Pixel nach rechts versetzt war. Die Slotrechtecke beginnen jetzt bei x=760 statt 792. Auch diese anhand der sichtbaren Anordnung korrigierte Geometrie benötigt noch einen bestätigten Inventar-/Kontextmenülauf im Spiel.

## Quellen und feste Datenstände

- [Explv: Kartendaten](https://github.com/Explv/osrs_map_tiles), Commit `1937ccfa4007345ef62f7694e773509deac8a1d7` vom 08.10.2026. [Koordinatenkonventionen des Kartenbetrachters](https://github.com/Explv/Explv.github.io/blob/master/js/model/Position.js). Zoom 8 entspricht vier Bildpunkten je Spielfeld; Kartenbilder enthalten Jagex-Grafiken und werden zur Laufzeit vom öffentlichen Anbieter geladen.
- [Skretzo: Shortest Path](https://github.com/Skretzo/shortest-path), Commit `537092df2be681b7f47b488c1adc7be40c8d7b59`; Kollisionsdaten vom 22.10.2026. Verwendet werden das dokumentierte Vier-Bit-Kantenformat und bedingungslose Tür-/Treppenverbindungen. SHA-256 der Kollisions-ZIP: `28A3B72D8E7167876C45B9EB557C5EFC56896641D0C2EAC66835E3442EC653DE`. Lizenzhinweis unter `native/Licenses`.
- [OpenCvSharp](https://github.com/shimat/opencvsharp), Paket `OpenCvSharp4.Windows.Slim` in Version `4.13.0.20260627`; [OpenCV](https://github.com/opencv/opencv). Lizenztexte sind in der EXE eingebettet und mit `--third-party-notices` auslesbar. Die Slim-Variante enthält die benötigten Bild-/Merkmalsmodule ohne Video-/FFmpeg-Paket.
- [OSRS Wiki: Minimap](https://oldschool.runescape.wiki/w/Minimap), [Combat Options](https://oldschool.runescape.wiki/w/Combat_Options), [Chicken](https://oldschool.runescape.wiki/w/Chicken), [Food](https://oldschool.runescape.wiki/w/Food). Die Wiki beschreibt die Spielmechanik; sie liefert keine automatische serverseitige Bestätigung der beobachteten Sitzung.
