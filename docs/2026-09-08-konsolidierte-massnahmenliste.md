# Konsolidierte Maßnahmenliste: Windows Enterprise Companion

**Stand:** 8. September 2026 · **Grundlage:** Projektcommit `17ae57aab97d0e22e0cacfcbdc05fa94c013ce23`  
**Status:** Entscheidungsvorlage; nichts implementiert.

Diese Liste verbindet die unveränderten **Blind-Findings F01–F14** mit den **neuen Codebefunden T01–T09** aus dem technischen Folgeaudit. Die Priorisierung hier ersetzt nicht die ursprüngliche Auditbewertung.

**P1:** falsche Entscheidungsgrundlage, falsches Ziel, Datenverlustrisiko oder erhebliche Bedienblockade. **P2:** relevante Verständlichkeits-, Zustands- oder Robustheitslücke. **P3:** Optimierung nach Validierung. Kein bestätigtes P0. Aufwand **S/M/L** bezeichnet relative Größe, keine verbindliche Zeitschätzung.

## 1. Priorisierter Arbeitsbestand

| Rang / ID | Priorität | Maßnahme und Ergebnis | Herkunft | Schwerpunkt / Aufwand | Abhängigkeit |
|---|---|---|---|---|---|
| 1 · M01 | P1 | Bekannte Befunde von fehlender Abdeckung trennen; keine grüne Entwarnung aus Nullwerten | F01, T02 | Fachlogik + Frontend / M | Gemeinsamer Coverage-Vertrag |
| 2 · M02 | P1 | Refresh durch alle Cacheebenen führen; Zeitpunkt und alter Zustand sichtbar | F02, T06 | Backend + React-State / M–L | Mit M01 abstimmen |
| 3 · M03 | P1 | Geräteidentität und lokale Zielerkennung gegen Kollisionen absichern | T01, F09 | Core-Verträge + Korrelationslogik / L | Vor breiter Bestandsvereinheitlichung |
| 4 · M04 | P1 | Inventarsnapshot atomar und eindeutig je Host speichern | T07 | Persistenz / M | Historische Dublettenstrategie; M03 beachten |
| 5 · M05 | P1 | Gespeicherte Lesefehler von fehlenden Daten unterscheiden | T03 | Clientabschnitte + Repository-Vertrag / S–M | Kann parallel vorbereitet werden |
| 6 · M06 | P1 | Vergleich mit Unknown/Partial, erklärbaren Unterschieden und Erfassungszeiten | T04, F06, F07 | Vergleichslogik + Frontend / M | Datenstatus aus M05 berücksichtigen |
| 7 · M07 | P1 | Tabellenkeyboard und Kindcontrols korrekt behandeln | T05 | Gemeinsame UI / S–M | Vor weiterer Wiederverwendung der Zeileninteraktion |
| 8 · M08 | P1 | Details sichtbar öffnen und vollständige Meldungen zugänglich machen | F03, F05, T06-B | Gemeinsames Detailmuster / M | M07; bei Logs M13-Kürzung berücksichtigen |
| 9 · M09 | P1 | Erste Clienttreffer im kleinen Fenster sichtbar priorisieren | F04 | Clients + Tabellenlayout / M | Mit M08 abstimmen |
| 10 · M10 | P2 | Kennzahlen mit korrekter Einheit und Grundgesamtheit versehen | F09, F01 | Vertragsnamen + Texte / S–M | Keine Bestandsmigration als Voraussetzung |
| 11 · M11 | P2 | Suchzustand, Posture-Filter und Scrollposition zuverlässig erhalten | F13 | Routing + State / M | Gemeinsame Navigation-/Filterregel |
| 12 · M12 | P2 | Legacy-Bereinigung aus dem Inventar-Leseweg entfernen | T08, F10 | Repository + Wartung / S–M | Mit M04-Persistenzänderung koordinieren |
| 13 · M13 | P2 | Fehlerfilter vor Begrenzung anwenden; Logabdeckung/Kürzung kenntlich machen | T09, F05, F14 | Host-Logabfrage + UI / M | Mit M08 gemeinsamer Detailvertrag |
| 14 · M14 | P2 | Ziel, Lese-/Schreibwirkung und lokale Speicherung vor Aktionen erklären | F10 | Client-/Network-/Print-Aktionen / M | Tatsächliche Verträge M04/M12 beachten |
| 15 · M15 | P2 | Trend lesbar und fachlich nachvollziehbar machen | F12 | Vulnerabilities + Dashboard / M | Kohorten-/Urteilsregel bestätigen |
| 16 · M16 | P2/P3 | Integrationen, lokale Perspektive und Informationsdichte neu ordnen | F08, F11, F14 | Navigation + Inhalte / M | Nach P1-Verträgen; Verdichtung durch Nutzertest |

Die Rangfolge bewertet Risiko und Reichweite. Kleine sichtbare Korrekturen aus M08/M10 dürfen vor einer längeren Identitätsmigration ausgeliefert werden, sofern sie keine falschen Vollständigkeitsversprechen einführen. Die folgenden Abnahmekriterien verhindern eine rein kosmetische Abarbeitung.

## 2. Konkrete Arbeitspakete und Abnahme

### M01 — Abdeckung und bekannte Befunde

**Änderung:** Fachlichen Zustand und Erkenntnisstand getrennt transportieren. Kennzahlen nennen bekannte Anzahl, Quellen-/Gerätebasis und Einschränkung. Nessus-Critical/High aus vorhandenen Daten bei Partial weiterhin anzeigen, mit Datum und eingeschränkter Abdeckung. Missing/Orphan bleibt bei unvollständigem Abwesenheitsnachweis unterdrückt.

**Dateien:** `HygieneAssessmentPolicy`, `ItHygieneService`, `ActionCenterService`, Clients-/ActionCenter-KPIs, semantische Statusbausteine; genaue Fundstellen im Folgeaudit F01/T02.

**Abnahme:**

- Available + tatsächlich leer ergibt eine belegte Null; Unavailable ohne Daten ergibt keine grüne Entwarnung.
- Partial + bekannter kritischer Befund bleibt in Clients und Action Center erkennbar.
- Ein Gerät kann gleichzeitig „Problem bekannt“ und „Abdeckung unvollständig“ sein.
- Gefilterte Arbeitspositionen, eindeutige Geräte und Befundinstanzen bleiben unterschiedliche Einheiten.
- Automatisierter Test führt echte Backend-Policy bis zur UI-Vertragsinterpretation; kein ausschließlich passend gebauter UI-Mock.

**Risiko:** Zu breite Unterdrückung positiver Evidenz bzw. neue falsche Missing-Befunde. Beide Richtungen ausdrücklich testen.

### M02 — Konsistenter Refresh und Zustand

**Änderung:** Expliziten Refresh bis zum Backend weitergeben; Snapshotidentität/-revision und Datenstand festlegen. Erfolgreiche Quellenaktualisierung macht alte abhängige Views erkennbar oder aktualisiert sie konsistent. Invalidate beendet Loading, alte Antworten überschreiben keine neue Generation. Action-Center-Auswahl aus aktuellen Daten ableiten.

**Dateien:** `EnvironmentContext`, `ItHygieneHandlers`, `ItHygieneSnapshotCache`, Management-Overview und `ActionCenterPage`.

**Abnahme:**

- Ablauf Nessus-Ausfall → erfolgreiche Synchronisierung → Refresh → Wechsel zwischen Clients/Overview/Action Center zeigt denselben Snapshot oder ausdrücklich unterschiedliche Datenstände.
- `EnvironmentProvider.refresh()` erzwingt nachweislich den vorgesehenen Backend-Refresh.
- KSC-/opsi-/Nessus-Kontextänderung kann nicht unbemerkt alte Ergebnisse als neu geprüft zurückgeben.
- Invalidate während Pending hinterlässt keinen permanenten Spinner; verspätete Antwort beendet keinen neueren Request.
- Bei gleicher Befund-ID und neuen Details zeigt die geöffnete Auswahl die neuen Werte.
- Navigation allein startet weiterhin keinen unkontrollierten Gerätescan.

**Risiko:** Anfrageflut und Cacheverlust. Revision/Invalidierung vor pauschalem TTL-/Polling-Ausbau definieren.

### M03 — Eindeutige Identität und Zielauflösung

**Änderung:** Vollständige Host-/IP-Identität und explizite, belastbare Aliase verwenden. Kurzname bleibt Anzeige oder Alias, kein universeller Primärschlüssel. Lokalen Computer nicht allein aufgrund gleichen Kurznamens auswählen.

**Dateien:** `clients.ts`, `ScanTarget`, `ItHygieneService`, `ClientWorkspacePaging`, `ClientOverviewService` und davon abhängige Provider.

**Abnahme:** Zwei IPs mit gleichem ersten Oktett bleiben getrennt. `PC-01.domain-a` und `PC-01.domain-b` bleiben getrennt oder werden als mehrdeutig angezeigt. Ein fremder gleichnamiger Host wird nie zu Null-/Local-Target. Ein belegter Kurzname/FQDN-Alias derselben Maschine bleibt benutzbar. Gespeicherte Daten und Deep-Links erhalten eine dokumentierte Übergangsregel.

**Risiko:** Höchstes Migrations-/Regressionsrisiko der Liste. Vor Änderung Bestandsprüfung ohne Mutation; keine automatische Vereinigung mehrdeutiger Datensätze. Die bestehende Kurznamens-Testannahme gezielt ersetzen.

### M04 — Atomarer Snapshotersatz

**Änderung:** Ersetzen innerhalb einer atomaren Operation/Transaktion; Host-Eindeutigkeit nach kontrollierter Bereinigung absichern. Reihenfolge konkurrierender Capture-Ergebnisse definieren.

**Dateien:** `EfHardwareSnapshotRepository`, `HardwareSnapshotRecordConfiguration`, gegebenenfalls Migration.

**Abnahme:** Ein Fehler nach Beginn der Ersetzung erhält den alten Snapshot. Zwei konkurrierende Saves in getrennten DbContexts erzeugen höchstens einen gültigen aktuellen Eintrag je Identität. Abbruch führt zu keinem Zustand „alter gelöscht, neuer fehlt“. Real-SQLite-Test mit Fehlereinschleusung und Nebenläufigkeit besteht.

**Risiko:** Unique-Index auf unbekannten Altbeständen. Migration darf historische Belege nicht unkontrolliert löschen; M03-Zielschlüssel berücksichtigen.

### M05 — Missing, unbekannt und Fehler auseinanderhalten

**Änderung:** Inventory, Security und Health erhalten explizite Lade-/Missing-/Failure-Zustände. Nur ein nachgewiesenes Nichtvorhandensein führt zum Empty State. Beschädigte gespeicherte Daten nicht still als „nie erfasst“ deklarieren.

**Dateien:** `InventorySection`, `SecuritySection`, `HealthSection`, gegebenenfalls Inventar-Lesevertrag. Compare liefert bereits ein brauchbares Muster.

**Abnahme:** Null/NotFound, Timeout, Bridgefehler, Datenbankfehler und kaputter Payload führen jeweils zur richtigen Aussage. „Erneut laden“ bleibt gespeicherter Read und löst keinen Scan aus. Vorhandene alte Daten bleiben bei fehlgeschlagenem Refresh mit klarer Kennzeichnung sichtbar. Tests verwenden dieselben Fehlerfälle in allen Clientabschnitten.

**Risiko:** Legacy-Daten nicht unnötig unlesbar machen; kompatiblen fehlenden Teilbereich von beschädigtem Gesamtsnapshot unterscheiden.

### M06 — Vergleich vertrauenswürdig machen

**Änderung:** Zuerst Softwareabdeckung prüfen, dann vergleichen. Identität/Rohwert, Normalisierung, Toleranz und Anzeige getrennt behandeln. Zeitstempel und Coverage pro Datenkategorie dauerhaft im Ergebnis zeigen.

**Dateien:** `compare.ts`, `ComparePage`; wiederverwendbare Datums-/Statusdarstellung.

**Abnahme:** Fehlgeschlagene Softwareerfassung auf B ergibt niemals „nur auf A“. Gleicher Produktname mit anderer Version wird entweder als Versionsunterschied gezeigt oder der Vergleich ausdrücklich als reiner Namensvergleich bezeichnet. GPU-Whitespace und geänderte Reihenfolge liefern erklärbare Ergebnisse. Unterschiedliche RAM-/Disk-Rohwerte folgen einer dokumentierten Toleranz. Inventar und Security zeigen ihre jeweiligen Erfassungszeiten. Der Null-Software-Test wird fachlich korrigiert.

**Offen:** Die Rohwerte des historischen GPU-Paars sind nicht belegt. Vor einer Aussage über dessen exakte Ursache gesondert prüfen; das blockiert die reproduzierbare Normalisierungsverbesserung nicht.

### M07 — Gemeinsame Tabelle per Tastatur bedienen

**Änderung:** Zeilenaktion nur für echte Zeilenaktivierung; Kindcontrols behalten ihre Eingaben. Native Tabellenbeziehungen, Fokus und Auswahl semantisch stimmig halten.

**Dateien:** `DataTable` und Aufrufer mit Auswahl, Links oder Buttons.

**Abnahme:** Space auf Clientcheckbox ändert genau die Auswahl und navigiert nicht. Enter/Space auf Zeilenaktivierung funktioniert weiterhin. Buttons/Links in Zeilen lösen nur ihre eigene Aktion aus. Tabreihenfolge und Fokus sind sichtbar. Tests prüfen ausdrücklich das Ausbleiben des Zeilenhandlers bei Kindcontrols; anschließend Prüfung mit Tastatur im echten WebView.

**Risiko:** Gemeinsame Komponente betrifft viele Tabellen. Alle interaktiven Aufrufer inventarisieren, keine ausschließlich seitenlokale Event-Sonderlösung hinzufügen.

### M08 — Details im sichtbaren Arbeitskontext

**Änderung:** Cleanup, Error log und Action-Center-Kontext nutzen ein konsistentes sichtbares Detailmuster. Client-Eventlogs erhalten einen ausdrücklichen Volltextaufruf mit Umbruch/Kopiermöglichkeit. Vorhandene Error-log-Details weiterverwenden.

**Abnahme:** Bei 25 bzw. 500 Zeilen öffnet die erste Auswahl ihre Details sichtbar. Fokus wechselt sinnvoll und kehrt beim Schließen zurück. Kleine Fenster ermöglichen dieselbe Aufgabe ohne Suche am Seitenende. Lange Logtexte bleiben als Text sicher darstellbar; vorhandene Kürzung wird angezeigt. Host, Zeitpunkt und Quelle bleiben am Detail sichtbar.

**Risiko:** Versteckte Sitzungsentscheidungen bzw. falsche Kandidatenzuordnung. Beim Gerätewechsel lokale Review-Daten korrekt behandeln. Layouttest im echten Browser/WebView erforderlich; `getByRole` allein genügt nicht.

### M09 — Arbeitsinhalt vor Verwaltungsrahmen

**Änderung:** Clientkennzahlen verdichten, Batch-Workbench an Auswahl/Bedarf koppeln, Spalten nach Bedeutung priorisieren. Semantische Statuswörter nicht beliebig bis zu Einzelbuchstaben umbrechen.

**Abnahme:** In einer Ansicht entsprechend dem kleinen Blind-Audit-Fenster um 1026 × 671 sind Suche und erste Treffer ohne vorheriges Scrollen erreichbar. Bei höherem Zoom bleiben Auswahl und Hauptaktionen bedienbar. Kein isoliertes „e“ aus „candidate“ in regulären Statusdarstellungen. Laufender Batchfortschritt bleibt sichtbar, auch bei eingeklappten Optionen. Sticky-/Horizontal-Scroll im tatsächlichen App-Container prüfen.

**Risiko:** Metadaten nicht ersatzlos verlieren; Abdeckung und Datenalter erhalten. Keine allgemeine Compact-Regel ohne Prüfung auf anderen Tabellen.

### M10 — Einheiten und Bestände erklären

**Änderung:** „Outdated clients“ kurzfristig korrekt als veraltete Produktinstallationen benennen; optional zusätzlich eindeutige betroffene Geräte berechnen. Dashboard, Clients und Compare nennen ihre Auswahlbasis. Abweichender Compare-AD-Kontext wird explizit oder vereinheitlicht.

**Abnahme:** Fixture mit einem Gerät und zwei veralteten Produkten ergibt zwei Installationen und gegebenenfalls ein betroffenes Gerät. Titel/Tooltip erklären, welche Quellen, Filter und ausgeschlossenen Geräte eingehen. 41 gespeicherte Inventarhosts werden nicht als gesamte Flotte bezeichnet. Zähler bleiben nachvollziehbar, auch wenn Quellen fehlen.

**Risiko:** Keine vorhandene Summe still auf Distinct umstellen; Vertragsname, UI und Tests gemeinsam ändern. Bestandsvereinheitlichung braucht M03, reine Benennung nicht.

### M11 — Rücknavigation und Filterzustand

**Änderung:** Suchtext, Posture, Quellenfilter, Gruppe, Sortierung und Seite konsistent in URL/View-State modellieren. Tatsächlichen inneren Scrollcontainer wiederherstellen. Apply-/Sofortfilterregel samt Reset definieren.

**Abnahme:** Clients → Filter/Seite → Detail → Tabwechsel → All clients erhält den vollständigen Zustand. Hauptnavigation und Browser-Zurück verhalten sich gemäß dokumentierter Erwartung. Direktlinks funktionieren. Reset ist ausdrücklich möglich. Wiederherstellung springt nicht bei jeder neuen Antwort erneut. Sofortfilter erzeugen keine unnötige Anfragekaskade.

**Risiko:** Quell-/Domainwechsel dürfen fremden Such-/Auswahlkontext nicht unbemerkt übernehmen. Keine allgemeine Autoquery-Regel für teure externe Reads.

### M12 — Lesen ohne Bereinigungsnebenwirkung

**Änderung:** Ungültige Legacy-Hosts im Listenread ausfiltern; Löschung in einen getrennten, nachvollziehbaren Migrations-/Wartungsschritt verschieben.

**Abnahme:** Listenaufruf verändert keine Inventarzeile. Ungültige Hosts erscheinen weiterhin nicht als Ziel. Test ersetzt die bisherige Erwartung, dass nach dem Lesen Datensätze gelöscht wurden. Eventuelle Bereinigung hat eigene Tests und Grenzen.

**Risiko:** Lokale Altbestände können weiter Platz belegen; das ist getrennt vom lesenden UI-Vertrag zu behandeln.

### M13 — Logfilter und begrenzte Abdeckung

**Änderung:** Level-/Zeitfilter vor dem Limit anwenden. Antwort nennt ausgewertetes Fenster, Anzahl/Kürzung und Detailbegrenzung. Optional Meldungen nach stabiler Signatur gruppieren; Einzelbelege bleiben erreichbar. Leseaufwand begrenzen, nicht nur Ergebniszahl.

**Abnahme:** Ein relevanter Fehler hinter 500 Warnungen bleibt unter „Errors“ auffindbar oder die Grenze wird ausdrücklich erklärt. Mehr als 40 Fortsetzungszeilen erzeugen einen Kürzungshinweis. Dateilimit/Rotation führen nicht zur Behauptung vollständiger Historie. Gruppierung erhält Host/Quelle/Zeitraum. „Hide previous entries“ bleibt Ausblendmarker, keine Löschung.

**Risiko:** Sehr große Dateien und unparsebare Zeitstempel. Parser-, Filter- und Grenzfalltests gemeinsam ausführen.

### M14 — Aktionswirkungen konsistent erklären

**Änderung:** Vor relevanten Aktionen knapp Ziel, Konto/Kontext, aktiven Lesezugriff, lokale Speicherung und externe Änderung benennen. Save client als gespeichertes Ziel erklären. Netzwerkscan zeigt Bereich und Portprüfumfang; Save-/Delete-Fehler im Clientdetail sichtbar behandeln.

**Abnahme:** Nutzer kann vor Klick unterscheiden: Eventquery live/ungespeichert, Inventarscan live/lokal gespeichert, Ziel speichern lokal, operative Printaktion mit externer Wirkung. Kein Passwort wird über SaveTarget persistiert. Fehlgeschlagenes Speichern meldet keinen Erfolg und erzeugt keinen unbehandelten Promise-Fehler. Doppelklick startet keine unklare Mehrfachaktion.

**Risiko:** Zusätzliche Warn-/Bestätigungsdialoge nicht pauschal einführen. Wirkungserklärung nach konkreter Aktion; ein Modul kann lesende und schreibende Funktionen enthalten.

### M15 — Trend mit Werten und Kohorte

**Änderung:** Datumsachse, Skala, Einheit und zugängliche Wertedarstellung ergänzen. X-Position aus Datum ableiten. Common-Asset-Urteil und Tagesgesamtwerte auseinanderhalten oder nach bestätigter Fachentscheidung konsistent ausrichten.

**Abnahme:** Unregelmäßige Tage zeigen echte Abstände. Urteil erklärt Start-/Enddatum, gemeinsame Assets und die erste ausschlaggebende Severity-Differenz. Critical-Anstieg trotz fallendem High wird nachvollziehbar. Hinzugekommene/entfernte Assets sind erkennbar. Ohne Farbe/Hover können Werte gelesen werden; Ein-Punkt-/Leerdatenzustände bleiben korrekt.

**Risiko:** Bestehende Severity-Policy nicht still ändern. Dashboard-Trendhinweis mitprüfen.

### M16 — Navigation und Informationsdichte

**Änderung:** Integrationen mit Kaspersky/opsi/Nessus benennen; lokale Security-/Berichteinstiege direkt als lokal kennzeichnen. Wiederholungen im Client Overview verdichten. Technische Vertragsbegriffe aus normalen Aufgabenflüssen entfernen.

**Abnahme:** Kaspersky wird über den Herstellernamen gefunden, ohne Kenntnis von „IT Lifecycle“. Schon der Berichteinstieg nennt den lokalen Scope. Dirty-Marker, Anker, Credentials und Neustarthinweise bleiben erhalten. Quellenalter/-abdeckung bleiben trotz Verdichtung sichtbar. Ein kurzer Benutzertest bestätigt, dass Detailbelege weiterhin gefunden und Beziehungsbeobachtungen nicht als Eigentum verstanden werden.

**Risiko:** Keine interne Modul-/Konfigurationsmigration für reine UI-Namen. Ein Flottenbericht ist eine zusätzliche Fachfunktion, kein automatischer Bestandteil dieses Pakets. Die größere Map-/Dashboard-Neugestaltung hat P3, klare falsche Namen/Scopes P2.

## 3. Sinnvolle Umsetzungswellen

1. **Verlässliche Aussage und unmittelbare Bedienung:** M01/M02/M05 sowie begrenzte M07/M08-Korrekturen. Kleine M10-Beschriftungen und F07-Zeitstempel können früh ergänzt werden. Vor UI-Entwarnungen echte Datenverträge festlegen.
2. **Identität, Persistenz und Vergleich:** M03/M04/M06/M12; Datenmigrationen separat reviewen. Gegenbeispiele und Fehlerpfade vor Änderung als Regressionstests festhalten.
3. **Arbeitsfluss und Diagnostik:** M09/M11/M13/M14/M15; tatsächliche kleine Fenster, Zoom, Tastatur und viele Zeilen prüfen.
4. **Informationsarchitektur und Verdichtung:** M16 nach gezieltem Nutzertest. Keine große Navigationsarbeit als Voraussetzung für Risikokorrekturen.

Welle 2 enthält P1-Themen und sollte früh vorbereitet werden; die Aufteilung bezeichnet Abhängigkeiten, keine Erlaubnis zu unbegrenztem Aufschub. Konkrete Releasezuschnitte hängen von der Migrationsprüfung ab.

## 4. Noch zu entscheidende Fachfragen

| Frage | Empfohlene Richtung | Bedeutung |
|---|---|---|
| Was ist ein eindeutiges Gerät? | Vollständige Identität mit belegten Aliasbeziehungen; Ambiguität sichtbar | Voraussetzung M03, keine automatische Kurznamensvereinigung |
| Was bedeutet „0 Critical“? | Null bekannte Arbeitspositionen nur zusammen mit Abdeckung/Grundgesamtheit | M01; keine Vollständigkeit suggerieren |
| Vergleich von Software? | Bestand und Version getrennt; Unknown blockiert Abwesenheitsaussage | M06 |
| Filterzustand über Hauptnavigation? | Letzten Arbeitszustand erhalten, ausdrückliches Reset anbieten | M11; mit Direktlinks vereinbar machen |
| Was zeigt der Trend? | Vorhandene Common-Asset-Policy zunächst verständlich erklären | M15; größere Policyänderung getrennt entscheiden |
| Braucht der globale Bericht einen Flottenscope? | Vorläufig vorhandenen lokalen Einstieg korrekt benennen | Neue Flottenberichtsfunktion separat beauftragen |

Diese Fragen sind Teil der späteren Umsetzungsvorbereitung. Für den vorliegenden Analyseauftrag ist keine zusätzliche Freigabe erforderlich.

## 5. Vollständigkeit der Zuordnung

| Blind-Finding | Maßnahme(n) | Blind-Finding | Maßnahme(n) |
|---|---|---|---|
| F01 | M01, M10 | F08 | M16 |
| F02 | M02 | F09 | M03, M10 |
| F03 | M08 | F10 | M12, M14 |
| F04 | M09 | F11 | M16 |
| F05 | M08, M13 | F12 | M15 |
| F06 | M06 | F13 | M11 |
| F07 | M06 | F14 | M13, M16 |

Neue Codebefunde: T01 → M03; T02 → M01; T03 → M05; T04 → M06; T05 → M07; T06 → M02/M08; T07 → M04; T08 → M12; T09 → M13.

**Verifikationsbasis:** 467 Frontendtests und 162 gezielt ausgewählte Backend-/Persistenz-/Parsertests bestanden; zusätzliche synthetische Gegenbeispiele bestätigen mehrere bislang ungetestete Fehler. Das ist eine Ausgangsbasis, kein Nachweis fehlerfreier UX. Die Abnahmekriterien oben sind erst bei späterer Umsetzung auszuführen.
