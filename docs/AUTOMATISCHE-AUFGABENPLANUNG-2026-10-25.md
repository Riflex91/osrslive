# Automatische Aufgabenwahl und begrenzte Wiederholung

Die Aufgabenwahl in `GameplayCoordinator` nutzt einen sitzungsbezogenen
`ActivityScheduler`. Er wählt ausschließlich die eingebauten, zum bestätigten
Skillstand passenden Trainings- und aktivierten Kampfmethoden.

## Verhalten

- Tätigkeiten mit vorhandenem Werkzeug/Material bzw. Kampfbedarf erhalten
  Vorrang vor Aufgaben, die erst Beschaffung brauchen. Innerhalb dieser Gruppe
  entscheidet die niedrigere Skillstufe, dann die feste Skill-/Methodenreihenfolge.
  Beispiel: Eine vorhandene Bronzeaxt bei Woodcutting 15 erlaubt Eichenholz;
  Attack 1 ohne Nahrung verdrängt diese Tätigkeit nicht mehr.
- Das gewählte Ziel bleibt über bestätigte Reise- und Beschaffungsschritte
  bestehen. Nach einem abgeschlossenen Schritt wird neu priorisiert.
- Bestätigter leerer Bankbestand, fehlendes Kaufbudget, fehlender frischer
  Preis/GE-Platz, fehlende Trainingsmaterialien, ein fehlender begehbarer Weg
  oder ein nicht identifiziertes Zielobjekt stellen ausschließlich die betroffene
  Aufgabe zurück: zunächst 30 Sekunden, dann 60, 120, 240 und maximal 300 Sekunden.
  Eine abgeschlossene Aufgabe setzt ihre Fehlerzählung zurück; ein Gehschritt
  allein tut dies nicht. Zurückgestellte Aufgaben werden nach Ablauf wieder
  berücksichtigt. Sind alle zurückgestellt, erfolgen keine neuen Aufgabenaktionen.
- Ein noch nicht bestätigter Kaufauftrag behält seine ID und seine Zuordnung
  zur Aufgabe. Während eines Beschaffungs-Backoffs darf bereits versorgte Arbeit
  weiterlaufen; eine zweite Beschaffungskette wird dabei nicht angelegt.
  Alte `pending-supply.json` ohne `ActivityId` bleiben lesbar.
- `ACTIVITY_DEFERRED` nennt Aufgaben-ID, Grund und UTC-Wiederholungszeit im Log.

## Grenzen und Sperren

Offene Handels- und Kampfjournale werden weiterhin vor jeder neuen Aufgabe
abgeglichen. Ein zurückgestellter Kaufwunsch ist kein bestätigtes GE-Angebot.
Unklare Ergebnisse führen weiterhin zur bestehenden Prüfsperre. Gesundheits-,
Charakter-, OCR-, Fenster- und Eingabefehler lösen keinen Aufgabenwechsel aus.
Die Rückstellgründe sind eine ausdrückliche Positivliste; neue Fehlergründe
werden nicht automatisch als ungefährlich eingestuft.
Ein vollständig visuell bestätigter Journalabschluss hebt die entsprechende
Planungssperre auf; Teilfortschritt oder bloßer Zeitablauf reichen nicht aus.

Die Planungsdaten liegen im Arbeitsspeicher und werden mit der Sitzung bzw.
dem Charakterwechsel zurückgesetzt. Handels-/Kampfjournale und Kaufwünsche
bleiben auf dem Datenträger. Es werden keine neuen Spielaktionen, Quests oder
Zielgebiete eingeführt. Jeder Ausführer verlangt weiterhin seine vorhandenen
visuellen Prüfungen; die Planungsänderung ersetzt keinen Live-Test.

## Prüfung

`--self-test` enthält die deterministischen Scheduler-Regressionen: vorhandene
Materialien vor unversorgtem Kampf, Levelgrenzen und CombatEnabled, stabiles
Reiseziel, Ausweicharbeit, zeitliche Backoff-Grenzen und Obergrenze, Rücksetzung
nach Erfolg, kein Wechsel bei Sicherheitsfehlern, dauerhafte Sperre bei unklarem
Ergebnis sowie JSON-Kompatibilität bestehender Beschaffungsaufträge.
