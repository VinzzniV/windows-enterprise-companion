# Frontend-/UI-/UX-Audit – Windows Enterprise Companion

## 1. Executive Summary

Windows Enterprise Companion besitzt eine solide visuelle Grundlage und einen fachlich überzeugenden Geräte-Workspace. Das dunkle Design ist konsistent, Status werden meist als Farbe plus Text dargestellt, administrative Aktionen sind überwiegend nachvollziehbar, und die Trennung nach Datenquellen – AD, Kaspersky, opsi, Nessus und lokale WEC-Scans – ist gelungen.

Für einen breiten Enterprise-Einsatz ist der aktuelle Stand dennoch nicht bereit. Das größte Risiko ist nicht die Optik, sondern das Vertrauen in die angezeigten Daten:

- Im Test wurden Inventar und Security eines Clients erfolgreich neu erfasst. Der bereits gemountete Report-Tab zeigte anschließend weiterhin beide Quellen als `MISSING`.
- Das Dashboard zeigte zeitweise keine Schwachstellen, während die Vulnerability-Ansicht später 166 kritische Assets und 1.262 kritische Instanzen auswies.
- Clients und IT Lifecycle benötigen bei einem Erstaufruf lange, ohne Fortschritt, Restzeit oder Abbruchmöglichkeit.
- Tabellen mit 821 Geräten, 1.733 Findings oder 10.216 Paketständen besitzen keine gemeinsame Paging- oder Virtualisierungsstrategie.

Die Anwendung wirkt damit wie eine gute interne Admin-Konsole in einem fortgeschrittenen Alpha-/frühen Beta-Stadium: fachlich substanziell, aber bei Datenkonsistenz, Skalierung, Navigation und Fehlerbehandlung noch nicht belastbar genug für tägliche Entscheidungen in einer größeren IT-Organisation.

**Gesamtbewertung: 5/10.**

Auditbasis:

- Reale Desktop-App mit WebView2 bei 1600 × 1000 Pixeln.
- Responsive Prüfung bei 1024 × 700 und 800 × 600 Pixeln.
- Sichere, lesende Tests für AD, Nessus, lokalen Netzwerkscan, Inventar, Security, Diagnose, Eventlogs und Drucker.
- Review der React-/TypeScript-Architektur und des Designsystems.
- 33 Frontend-Testdateien mit 168 Tests: alle bestanden.
- Repository unverändert; keine Implementierung vorgenommen.

---

## 2. Scorecard

| Bereich | Bewertung | Kurzbegründung |
|---|---:|---|
| Visual Design | 7/10 | Kohärentes Dark Theme, gute Typografie, klare Statusfarben; teilweise zu viele große Cards. |
| UX | 5/10 | Gute Einzelansichten, aber lange Wartezustände, tote Enden und fehlende Aktualisierung zwischen Modulen. |
| Informationsarchitektur | 5/10 | Hauptbereiche auffindbar, aber Clients und IT Lifecycle überschneiden sich; globale und clientbezogene Funktionen sind nicht sauber getrennt. |
| Navigation | 5/10 | Stabile Sidebar und Client-Tabs; wenig kontextuelle Drill-downs und keine responsive Navigation. |
| Konsistenz | 5/10 | Gute gemeinsame Komponenten, aber Sprachmix, unterschiedliche Tab-Systeme und Design-Token-Drift. |
| Informationsdichte | 5/10 | Einige kompakte Tabellen sind gut; Dashboard, AD-KPIs und Diagnose verschwenden dagegen viel Höhe. |
| Tabellen-/Daten-UX | 4/10 | Sortierung und Filter vorhanden, aber kein Paging, keine Virtualisierung und problematische Langtexte. |
| Feedback/States | 4/10 | Spinner, Empty- und Error-States existieren; Fortschritt, Abbruch, Teilerfolg und konkrete Hilfe fehlen häufig. |
| Accessibility | 6/10 | Gute Fokusdarstellung, Labels und überwiegend semantische Elemente; Kontrast- und Tabellenprobleme bleiben. |
| Frontend Maintainability | 4/10 | Gute Typisierung und Tests, aber Komponenten mit 1.391–1.725 Zeilen sowie zu viel Featurelogik in einzelnen Dateien. |
| Enterprise Readiness | 4/10 | Quellenintegration ist stark, aber Datenfrische, Skalierung und Fehlertransparenz verhindern derzeit verlässlichen Regelbetrieb. |

---

## 3. Top-10-Probleme

### 1. Veraltete Daten bleiben nach erfolgreichen Scans sichtbar

Im Client-Workspace bleiben alle Tabs absichtlich gemountet. Der Report lädt seine Übersicht einmal beim Mounten und reagiert nicht auf später abgeschlossene Inventar- oder Security-Scans. Im Test:

- Inventar erfolgreich erfasst: 19.08.2026, 12:38:45.
- Security erfolgreich erfasst: 19.08.2026, 12:39:16.
- Report anschließend weiterhin: Hardware `MISSING`, Security `MISSING`.

Das ist ein P0-Vertrauensproblem: Der Benutzer kann nicht unterscheiden, ob Daten fehlen oder lediglich nicht neu geladen wurden.

### 2. Dashboard kann Risiken unterschätzen

Während Daten importiert oder veraltet sind, erscheinen Werte ohne klaren Freshness-Kontext. Ein Wert `0` ist dadurch visuell nicht von „noch nicht geladen“, „veraltet“ oder „Quelle nicht verfügbar“ zu unterscheiden.

### 3. Keine skalierbare Tabellenarchitektur

Die gemeinsame `DataTable` rendert und sortiert den vollständigen übergebenen Array. Es fehlen:

- Paging,
- Virtualisierung,
- serverseitige Sortierung und Filterung,
- Gesamtzahl-/Seitenmodell,
- konfigurierbare Spalten,
- persistierte Views.

Das betrifft unter anderem 821 Clients, 822 Lifecycle-Geräte, 1.733 Vulnerability-Findings und 10.216 opsi-Paketstände.

### 4. Lange Abfragen wirken eingefroren

Der Environment-Load hat technisch einen Timeout von 180 Sekunden. Die UI zeigt bis dahin aber nur `Loading environment inventory` beziehungsweise `Refreshing…`. Es gibt weder:

- konkrete Phase,
- verstrichene Zeit,
- Quellfortschritt,
- Abbruch,
- Hinweis auf erwartete Dauer.

### 5. KPIs sind keine Arbeitsoberfläche

IT Lifecycle meldete 775 Problemgeräte, 143 Nessus-Critical-Systeme und 330 veraltete Geräte. Die KPI-Karten sind nicht anklickbar. Der Administrator muss dieselbe Bedingung erneut im Filter suchen.

### 6. Report- und Dashboard-Hinweise führen nicht zum Ziel

Der Report fordert zum Öffnen der „Inventory section“ oder „Security section“ auf, bietet aber keine direkten Links. In der globalen Report-Ansicht sind diese Bereiche gar keine eigenständigen Navigationseinträge.

### 7. Große Findings sind kaum scanbar

Vulnerability-Findings zeigen komplette CVE-Listen in der Tabellenzeile. Einzelne Zeilen beanspruchen mehr als eine halbe Bildschirmhöhe. Die Detailansicht beschränkt Instanzen still auf 50, ohne diesen Ausschnitt klar zu kennzeichnen.

### 8. Fehler sind technisch, redundant oder nicht handlungsleitend

Beispiele:

- Lokaler Drucker-Scan: `WMI_UNAVAILABLE: The WMI query failed. Die Anfrage ist ungültig.`
- Print-Server-Fehler erscheinen sowohl in der Serverzeile als auch als große Fehlerkarte.
- Error Log zeigt WQL, JSON-Metadaten, Stacktraces und Quellpfade direkt in der Tabelle.

### 9. Responsive Shell fehlt

Bei 800 Pixel Breite bleibt die Sidebar 224 Pixel breit. Hauptinhalt und Navigation besitzen getrennte Scrollflächen; Navigationseinträge können unter den dauerhaft eingeblendeten Profilinformationen verschwinden. Tabellen und Client-Tabs werden eng, ohne alternative Darstellung.

### 10. Frontend-Komponenten sind zu groß und zu eng gekoppelt

Besonders auffällig:

- Patch Management: 1.725 Zeilen.
- Print Management: 1.391 Zeilen.
- Hardware Inventory: 556 Zeilen.
- Security: 512 Zeilen.
- Settings: 509 Zeilen.

Das erschwert isolierte Tests, Refactoring und konsistente UX-Änderungen.

---

## 4. Screen-by-Screen Review

| Screen | Stärken | Schwächen | Auswirkung |
|---|---|---|---|
| Dashboard | Gute Modulübersicht; Patch-Statusbalken vermittelt Verteilung schnell. | Große Karten, wenige Zeitstempel, keine zentrale Action Queue, potenziell veraltete Nullwerte, kaum Drill-down. | Kann Sicherheit suggerieren, obwohl Quelldaten fehlen oder synchronisieren. |
| Clients | Gute Suche und Quellenmatrix; Client-Workspace ist fachlich stark. | Alle Zeilen im DOM; Statusfläche farblich überladen; Online-Fehler werden teilweise still geschluckt. | Funktioniert bei Hunderten Geräten, skaliert aber schlecht weiter. |
| Client Overview | Sehr gute AD-/Kaspersky-/opsi-/Nessus-Zusammenführung; direkter Link zu Vulnerabilities. | Lange DNs und Portlisten; Quellenalter nicht einheitlich hervorgehoben. | Gute Diagnosebasis, aber bei langen technischen Werten schwer scannbar. |
| Client Inventory | Klarer Empty State; Scan schnell; „Freshly captured“ mit Timestamp. | Kleine Tabellen benötigen bereits bei Desktopbreite horizontales Scrollen. | Nutzbar, aber Layout nicht robust gegenüber langen Hardwarewerten. |
| Client Security | Gute Coverage-Kommunikation und Raw-Evidence-Disclosures. | Info-Beobachtungen werden als „Findings“ gezählt; Scan zeigt nur generischen Spinner. | Fachlich stark, Begrifflichkeit kann unnötig alarmieren. |
| Client Diagnostics | Gute Kategorien und gespeicherter letzter Lauf. | Alle erfolgreichen Checks werden als große Cards angezeigt. | Erfolgsfälle dominieren die Seite; Abweichungen wären wichtiger. |
| Client Event Logs | Gute Presets, kompakte Tabelle und Live-Hinweis. | Vollständige Meldungen nur über abgeschnittenen Text/Tooltip; kein Detailpanel. | Schnelles Scannen gut, genaue Untersuchung eingeschränkt. |
| Client Printers | Klarer Scope und primäre Aktion. | Lokaler Scan scheiterte mit gemischtsprachigem WMI-Fehler ohne Diagnoseweg. | Kernworkflow funktioniert im geprüften Standardfall nicht. |
| Client Compare | Verständliche A/B-Struktur und klare Read-only-Erklärung. | Zwei native Selects mit potenziell über 800 Clients; keine Suche oder Datenverfügbarkeitsanzeige. | Auswahl ist bei realem Bestand unnötig langsam. |
| Active Directory | Gute KPIs, klare Connection-Konfiguration und progressive Disclosure. | Sehr große KPI-Cards; Hygiene-Details als rohe Distinguished Names. | Ergebnisse vorhanden, aber schlecht priorisierbar und kaum weiterverarbeitbar. |
| IT Lifecycle | Starke Quellenübersicht und zentrale Health-Matrix. | Dupliziert Clients; 775 Problemgeräte ohne klickbare KPI; alle 822 Zeilen im DOM. | Hoher Informationswert, aber geringe operative Handlungsfähigkeit. |
| Vulnerabilities | Gute Assets-/Findings-/Scans-Trennung; Scans-Tabelle kompakt. | Tabs ohne ARIA-Tabsemantik; leeres Trenddiagramm; massive CVE-Zeilen; keine sichtbare Pagination. | Gerade der wichtigste Risikobereich ist der schwerste zu scannen. |
| Patch Management | Umfassender Funktionsumfang, gute Statuscodes, brauchbares Detailpanel. | Deutsch in sonst englischer App; redundante KPIs; sehr breite Tabellen; 10.216 Paketstände ohne erkennbare Datenbegrenzung. | Hohe funktionale Reife, aber hohe kognitive und technische Last. |
| Print Management | Gutes Konzept aus gespeicherten Servern, Scan und konsolidierter Ansicht. | Doppelte Empty States und Fehlermeldungen; sehr technische Authentifizierungsfehler. | Fehlerfälle fühlen sich unfertig an. |
| Network Scan | Einfacher Formularfluss; Ergebnis kompakt und verständlich. | Sprache inkonsistent; Ladebutton textlich abgeschnitten; kein Fortschritt oder Abbruch. | Für kleine Scans brauchbar, für größere Netze unzureichendes Feedback. |
| Report Export | Vorbildliche Angaben zu Alter, Erfassung und Provenienz. | Wiederholt fehlende Daten mehrfach; keine Links zu benötigten Scans; Cross-Tab-Stale-Bug. | Bericht kann unmittelbar nach erfolgreicher Erfassung fälschlich blockiert bleiben. |
| Settings | Effektive Konfiguration und Credential-Status sind transparent; abschnittsweises Speichern. | Sehr lange Einzelseite; keine Inhaltsnavigation; Löschen von Credentials nicht als Gefahr gestaltet. | Fehleranfällig und schwer zu überblicken. |
| Error Log | Filter und Hinweis auf physische Logquelle vorhanden. | Rohdaten statt Admin-Zusammenfassung; lange Zeilen und horizontaler Scroll; „Clear“ semantisch missverständlich. | Für Entwickler nützlich, für Administratoren zu technisch. |

---

## 5. Informationsarchitektur

### Aktuelle Probleme

- `Clients` und `IT Lifecycle` zeigen im Kern dieselbe Gerätelandschaft mit ähnlichen Statusspalten.
- Security ist fachlich vorhanden, aber nur als Client-Tab und indirekt über Dashboard-Karten erreichbar.
- `Report export` existiert global und clientbezogen, ohne klaren Unterschied im Navigationsmodell.
- `Netzwerkscan` ist deutsch, die übrige Hauptnavigation überwiegend englisch.
- Patch-, Print- und Netzwerkoperationen sind nicht als gemeinsame Operations-Domäne gruppiert.
- Settings und Error Log sind korrekt als Verwaltung gruppiert, aber die Settings-Seite benötigt eine zweite Navigationsebene.

### Empfohlene Zielstruktur

| Hauptbereich | Unterbereiche |
|---|---|
| Overview | Fleet Dashboard, Action Queue, Data Freshness |
| Devices | Device List, Saved Views, Compare, Device Workspace |
| Identity | Active Directory Overview, Hygiene Findings |
| Risk & Compliance | Vulnerabilities, Security Posture, Coverage |
| Operations | Patch Management, Print Management, Network Scan |
| Reports | Fleet Reports, Device Reports, Export History |
| Administration | Connections & Credentials, Thresholds, Profiles, Error Log |

`IT Lifecycle` sollte nicht als parallele Geräteliste bestehen bleiben. Die KPI-Logik gehört in eine Fleet-Posture-Übersicht; die Gerätetabelle sollte als gespeicherte View innerhalb von `Devices` weiterleben.

---

## 6. Workflow-Analyse

| Workflow | Aktueller Pfad | Bewertung | Empfehlung |
|---|---|---|---|
| Client prüfen | Clients → Suche → Zeile → Overview | Nach initialem Environment-Load gut; etwa 3 Interaktionen plus Suche. | Globale Gerätesuche im Topbar und Deep Links aus allen KPIs. |
| Gerät diagnostizieren | Client → Diagnostics → Run | Klar und sicher; Ergebnis bleibt gespeichert. | Nur Abweichungen öffnen, erfolgreiche Kategorien standardmäßig einklappen. |
| AD-Zustand prüfen | Active Directory → Analyze/Hygiene → Details | 2–3 Klicks, aber lange Wartezeit und rohe Ergebnisdaten. | Fortschrittsphasen und filterbare Identitätstabellen. |
| Security prüfen | Dashboard/Clients → Client → Security → Scan | Verständlich, aber Dashboard-„Security“ führt nicht direkt zum betroffenen Client. | Security-KPIs mit vorgefilterter Geräteansicht und Coverage-Status. |
| Software-/Patchstatus prüfen | Dashboard → Patch → Paket → Detail/Clients | Guter fachlicher Drill-down, aber Tabellenmasse und Spaltenbreite bremsen. | Server-Paging, gespeicherte Filter und klarer Paket-/Client-Modus. |
| Auffällige Systeme finden | IT Lifecycle → Filter → Client | KPI zeigt Problemmenge, ist aber nicht klickbar. | Jede KPI als gespeicherter Filter mit URL. |
| Vom Überblick zum Problem | Dashboard → Modul → Filter → Objekt | Häufig 4 oder mehr Interaktionen; Kontext geht teilweise verloren. | Action Queue mit priorisierten, direkt adressierbaren Problemen. |
| Clients vergleichen | Clients → Compare → zwei Selects → Compare | Bei 821 Geräten zu schwer bedienbar. | Suchbare Comboboxen, zuletzt verwendete Clients, Datenverfügbarkeit pro Option. |
| Bericht erzeugen | Client → Report | Readiness transparent, aber Daten können nach Scan veraltet bleiben. | Query-Invalidierung nach Scan und direkte „Jetzt erfassen“-Links. |

---

## 7. UI-/Design-System-Audit

### Positiv

- Inter und JetBrains Mono sind passend eingesetzt.
- Primärfarbe, Fokusdarstellung und Statusfarben erzeugen eine erkennbare Produktoberfläche.
- Badges kombinieren Punkt und Text; Status ist daher nicht ausschließlich farbbasiert.
- Der Client-Workspace besitzt korrektes `tablist`-/`tab`-/`tabpanel`-Verhalten und Pfeiltastensteuerung.
- Raw Evidence ist überwiegend progressiv ausgeblendet.

### Statussystem

Aktuell werden Health, technische Ausführung und Datenverfügbarkeit teilweise in derselben Farblogik vermischt. Empfohlen wird ein dreidimensionales Statusmodell:

| Dimension | Zulässige Status |
|---|---|
| Health | Healthy, Warning, Critical |
| Execution | Idle, Running, Succeeded, Partial, Failed |
| Availability | Available, Missing, Unknown, Not configured, Not applicable |
| Lifecycle | Current, Update available, Pending, Disabled |

Farben:

- Grün ausschließlich für gesund oder erfolgreich.
- Gelb für Aufmerksamkeit, Pending und Teilerfolg.
- Rot für kritisches Risiko oder fehlgeschlagene Ausführung.
- Blau für laufend und rein informativ.
- Grau für unbekannt, nicht konfiguriert, deaktiviert und nicht anwendbar.

`Missing`, `N/A`, `Unknown`, `Disabled` und `Stale` müssen fachlich getrennt bleiben.

### Kontrast

`text-slate-500` erreicht auf `slate-950` ungefähr 4,24:1, auf `slate-900` nur etwa 3,75:1. Bei den häufig verwendeten 12-Pixel-Hilfstexten liegt das unter dem WCAG-AA-Ziel von 4,5:1.

Empfehlung: normaler Hilfstext mindestens `slate-400`; `slate-500` nur für große oder rein dekorative Texte.

### Tabellen

Die gemeinsame Tabelle benötigt eine zweite Generation mit:

- serverseitigem Paging und Sortierung,
- optionaler Virtualisierung,
- sticky Header und optional sticky Primary Column,
- Zeilenauswahl getrennt von Navigation,
- klaren Links/Buttons in Zellen statt `role="button"` auf `<tr>`,
- kompakten Details statt unbeschränkter Langtexte,
- Anzeige „1–100 von 10.216“,
- Spaltenauswahl und gespeicherten Views.

### Cards und Dichte

Cards sollten Struktur schaffen, nicht jede Information umrahmen. Besonders Dashboard, Diagnostics und AD verwenden zu viele gleichgewichtete Container. Kritische Abweichungen sollten visuell dominieren; gesunde Normalzustände dürfen kompakter werden.

---

## 8. Frontend-Code-Audit

### Gute Grundlagen

- React 19, TypeScript und generierte Bridge-Typen.
- Gemeinsame UI-Komponenten und semantische Farbtokens.
- Fehlergrenze pro Routingbereich.
- Wiederverwendung von Inventory-, Security- und Diagnostics-Views im Client-Workspace.
- Gute Unit-/Component-Testbasis: 168 Tests bestanden.
- Zeitlimits für Bridge-Aufrufe sind zentral konfiguriert.

### Kritische technische Befunde

1. **Cross-Tab-Stale-State**

   Alle Client-Sektionen bleiben gemountet. Der Report lädt nur bei Änderung von `host` und wird nach erfolgreichen Scans nicht invalidiert: [ClientDetailPage.tsx](</C:/Users/vinzent.niederwieser/windows-enterprise-companion/frontend/src/features/clients/ClientDetailPage.tsx:172>), [ReportingSection.tsx](</C:/Users/vinzent.niederwieser/windows-enterprise-companion/frontend/src/features/reporting/ReportingSection.tsx:58>).

2. **Nicht skalierbare DataTable**

   Die Props kennen nur `rows`, nicht `page`, `total` oder `loading`. Anschließend wird das gesamte Array kopiert, sortiert und gerendert: [DataTable.tsx](</C:/Users/vinzent.niederwieser/windows-enterprise-companion/frontend/src/shared/ui/DataTable.tsx:18>).

3. **Feste Desktop-Shell**

   Die Sidebar verwendet dauerhaft `w-56 shrink-0`; responsive Varianten fehlen: [App.tsx](</C:/Users/vinzent.niederwieser/windows-enterprise-companion/frontend/src/app/App.tsx:228>).

4. **Vulnerability-Fan-out**

   Overview, Assets, Findings, Scans und Trend werden parallel geladen, unabhängig vom aktiven Tab. Bei laufender Synchronisierung wird alle zwei Sekunden erneut geladen: [VulnerabilitiesPage.tsx](</C:/Users/vinzent.niederwieser/windows-enterprise-companion/frontend/src/features/vulnerabilities/VulnerabilitiesPage.tsx:46>).

5. **Stilles Abschneiden**

   Finding-Details rendern nur `instances.slice(0, 50)`, ohne einen klaren Hinweis „erste 50 von X“: [VulnerabilitiesPage.tsx](</C:/Users/vinzent.niederwieser/windows-enterprise-companion/frontend/src/features/vulnerabilities/VulnerabilitiesPage.tsx:93>).

6. **Lange Environment-Abfrage ohne progressive UI**

   Der gemeinsame EnvironmentContext bündelt die Abfrage sinnvoll, kennt in der UI aber nur `loading/error/result`: [EnvironmentContext.tsx](</C:/Users/vinzent.niederwieser/windows-enterprise-companion/frontend/src/shared/environment/EnvironmentContext.tsx:96>). Der technische Timeout liegt bei 180 Sekunden: [actionTimeouts.ts](</C:/Users/vinzent.niederwieser/windows-enterprise-companion/frontend/src/shared/bridge/actionTimeouts.ts:1>).

7. **Designsystem wird nicht vollständig eingehalten**

   Die Dokumentation verbietet rohe `emerald`-/`amber`-/`red`-Utilities: [README.md](</C:/Users/vinzent.niederwieser/windows-enterprise-companion/frontend/src/shared/ui/README.md:13>). Settings verwendet dennoch `emerald` und `amber`: [SettingsPage.tsx](</C:/Users/vinzent.niederwieser/windows-enterprise-companion/frontend/src/features/verwaltung/SettingsPage.tsx:273>).

8. **Megakomponenten**

   Patch Management und Print Management kombinieren Datenbeschaffung, Caching, Workflowstatus, Formulare, Tabellen und Detailpanels in jeweils einer Datei. Diese sollten nach Featurezustand und Viewmodell zerlegt werden.

### Testlücken

Trotz bestandener Tests fehlen besonders:

- Client-Scan → Report-Readiness aktualisiert sich.
- Dashboard während Sync/Stale/Source unavailable.
- Performance- und DOM-Budgettests für große Tabellen.
- Responsive Navigation bei 800–1.024 Pixeln.
- Tastatur-/Screenreader-Tests für DataTable und Vulnerability-Tabs.
- Integrationstest für den lokalen Drucker-Scan.
- Kontrast- und automatisierte Accessibility-Prüfungen.

---

## 9. Priorisierte Findings P0–P3

| ID | Priorität | Finding | Empfohlene Maßnahme | Aufwand |
|---|---|---|---|---:|
| F-01 | P0 | Report bleibt nach erfolgreichen Scans veraltet. | Gemeinsamen Query-/Cache-Layer einführen; nach Scan Inventory-, Security-, Overview-, Reporting- und Dashboard-Queries invalidieren. | M |
| F-02 | P0 | Dashboard-Nullwerte können fehlende oder veraltete Daten als gesund darstellen. | Jeder KPI erhält `value`, `state`, `capturedAt`, `source`, `coverage`; während Sync kein fachlicher Nullwert. | M |
| F-03 | P1 | Gemeinsame DataTable rendert vollständige Datenbestände. | Paging-/Sort-/Filter-Contract entwickeln; bei großen Listen serverseitig arbeiten, optional virtualisieren. | XL |
| F-04 | P1 | Environment-Load kann bis zu 180 Sekunden ohne Fortschritt laufen. | Phasen- und Quellenfortschritt, Zeitangabe, Cancel/Retry und partielle Ergebnisse. | L |
| F-05 | P1 | IT-Lifecycle-KPIs sind nicht klickbar. | KPI-Karten als Deep Links in vorgefilterte Device Views. | S |
| F-06 | P1 | Lokaler Client-Drucker-Scan scheitert mit ungültiger WMI-Abfrage. | Query korrigieren; lokalen Integrations-/Smoke-Test hinzufügen; Fehler auf konkrete Ursache abbilden. | M |
| F-07 | P1 | Vulnerability-Findings und CVEs überlasten Tabellenzeilen. | Einzeilige Zusammenfassung, CVE-Anzahl, Disclosure/Drawer; Pagination. | M |
| F-08 | P1 | Sidebar und Topbar sind nicht responsive. | Collapsible Sidebar, kompakter Header und Single-Scroll-Container unter definierten Breakpoints. | L |
| F-09 | P1 | Fehlertexte sind technisch und teilweise doppelt. | Fehlercodes zentral auf Admin-Nachricht, Ursache, nächste Aktion und technische Details abbilden. | M |
| F-10 | P1 | Globaler Report führt nicht zu fehlenden Scanbereichen. | Direkte Deep Links beziehungsweise „Inventar erfassen“/„Security prüfen“-Aktionen. | S |
| F-11 | P2 | Clients und IT Lifecycle duplizieren dieselbe Geräteliste. | IT Lifecycle in Fleet Posture und gespeicherte Device Views überführen. | L |
| F-12 | P2 | Patch-Clienttab ist bei 10.216 Paketständen nicht operativ filterbar. | Client-/Paket-/Statusfilter, Pagination, gespeicherte Views. | L |
| F-13 | P2 | Settings ist eine lange unstrukturierte Seite. | Lokale Inhaltsnavigation, Abschnitts-URLs, Dirty-State, Validierungszusammenfassung. | M |
| F-14 | P2 | Credential-Löschaktionen sehen wie normale Sekundäraktionen aus. | Danger-Variante und explizite Bestätigung bei sicherheitsrelevanten Löschungen. | S |
| F-15 | P2 | AD-Hygiene zeigt rohe DNs statt benutzbarer Entitäten. | Tabellen mit CN, Typ, Pfad, Status, Suche, Kopieren und Drill-down. | M |
| F-16 | P2 | Vergleich verwendet zwei Selects mit Hunderten Clients. | Suchbare Comboboxen, Scan-Verfügbarkeit und zuletzt verwendete Geräte. | S |
| F-17 | P2 | Status- und Sprachsystem ist inkonsistent. | Produktweite Terminologie und eine einzige UI-Sprache; technische Originalwerte nur in Details. | M |
| F-18 | P2 | Megakomponenten erschweren Änderungen. | Patch und Print in Data Hooks, Viewmodels, Tabellen, Detailpanel und Workflows zerlegen. | L |
| F-19 | P3 | Trend-Card bleibt trotz unzureichender Daten groß und leer. | Kompakter Empty State mit Datum, ab wann ein Trend möglich ist. | XS |
| F-20 | P3 | Low-contrast-Hilfstexte. | `slate-500` bei kleinen Texten durch kontrastreicheren Token ersetzen. | XS |
| F-21 | P3 | Error-Log-„Clear“ ist missverständlich. | „Vorherige Einträge ausblenden“ plus sichtbare Erklärung. | XS |
| F-22 | P3 | Print-Empty-State wiederholt dieselbe Information. | Server- und Printer-Empty-State zu einem geführten Einstieg zusammenführen. | XS |

---

## 10. Quick Wins

1. Zeitstempel und Quellenstatus auf jede Dashboard-Karte setzen.
2. Leeres Trenddiagramm durch einen kompakten „Noch nicht genügend Daten“-State ersetzen.
3. CVE-Listen auf eine Zeile plus „+ N weitere“ begrenzen.
4. IT-Lifecycle-KPIs mit den bereits vorhandenen Filterwerten verlinken.
5. Report-Hinweise als Links zu Inventory und Security ausgeben.
6. Error Log zunächst als einzeilige Zusammenfassung anzeigen; Rohdaten in Disclosure verschieben.
7. Settings mit einer sticky Inhaltsnavigation versehen.
8. Credential-Löschbuttons als Danger-Aktionen gestalten.
9. Print Management auf einen einzigen initialen Empty State reduzieren.
10. Nach zehn Sekunden Ladezeit einen sichtbaren Hinweis mit verstrichener Zeit und Retry/Cancel anbieten.
11. Bei Finding-Details deutlich „50 von X Instanzen angezeigt“ ausgeben.
12. Kleine `text-slate-500`-Texte auf `slate-400` oder einen geprüften Semantic Token anheben.

---

## 11. Zielvision

Die Zielanwendung sollte als operative „Fleet Workbench“ funktionieren, nicht als Sammlung einzelner Modul-Dashboards.

```text
Globaler Header
├─ Gerätesuche / Command Palette
├─ Datenfrische und laufende Jobs
└─ Benutzer, Profil, Privilegien

Overview
├─ Action Queue – priorisiert nach Risiko und Alter
├─ Quellenstatus – AD, Kaspersky, opsi, Nessus, WEC
├─ Klickbare Fleet-KPIs
└─ Letzte Änderungen / fehlgeschlagene Jobs

Devices
├─ Gespeicherte Views
├─ Performante, serverseitige Tabelle
└─ Device Workspace
   ├─ Overview
   ├─ Inventory
   ├─ Security
   ├─ Diagnostics
   ├─ Events
   ├─ Printers
   └─ Reports

Risk & Operations
├─ Vulnerabilities
├─ Patch
├─ Print
└─ Network
```

Zentrale UX-Prinzipien:

- Jeder KPI führt zu den Datensätzen hinter der Zahl.
- Jeder Wert zeigt Alter, Quelle und Coverage.
- Nach jeder Mutation oder Erfassung werden alle abhängigen Ansichten konsistent aktualisiert.
- Gesunde Normalfälle bleiben kompakt; Ausnahmen erhalten Raum.
- Große Datenmengen werden nicht vollständig in den Browser geladen.
- Technische Details sind verfügbar, aber nicht die primäre Fehlermeldung.
- Sprache, Status und Aktionen sind produktweit vereinheitlicht.

---

## 12. Empfohlene Umsetzungsreihenfolge

### Phase 1 – Vertrauensbasis

1. Query-/Cache-Invalidierung zwischen Scans, Reports und Dashboard.
2. Einheitliches Freshness-/Coverage-Modell.
3. Dashboard-Zustände für Loading, Sync, Stale, Missing und Error.
4. Integrationstests für Scan → Report → Dashboard.

### Phase 2 – Skalierungsfundament

1. Neue DataTable-Schnittstelle.
2. Serverseitige Pagination, Sortierung und Filterung.
3. Vulnerabilities, Clients, IT Lifecycle und Patch zuerst migrieren.
4. DOM-, Renderzeit- und Speichernutzungsbudgets definieren.

### Phase 3 – Informationsarchitektur und Shell

1. Clients und IT Lifecycle zu Devices/Fleet Posture konsolidieren.
2. Risk, Operations, Reports und Administration gruppieren.
3. Responsive/collapsible Sidebar.
4. Globale Gerätesuche und URL-basierte Filter.

### Phase 4 – Operative Workflows

1. Action Queue und klickbare KPIs.
2. Vulnerability-Finding-Drawer.
3. Report-Deep-Links.
4. Searchable Client Compare.
5. Print- und Error-State-Überarbeitung.

### Phase 5 – Konsistenz und Wartbarkeit

1. Status- und Sprachsystem vereinheitlichen.
2. Settings strukturieren.
3. Patch-/Print-Megakomponenten zerlegen.
4. Designsystem-Verstöße entfernen.

### Phase 6 – Accessibility und Enterprise Hardening

1. WCAG-AA-Kontrastprüfung.
2. Semantische Tabelleninteraktionen.
3. Automatisierte Accessibility- und Keyboard-Tests.
4. Langläufer mit Fortschritt, Abbruch und Teilergebnissen.
5. Performance- und realistische Großdaten-Regressionstests.
