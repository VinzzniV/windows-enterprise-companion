# Technischer Folgeaudit: Windows Enterprise Companion

**Datum:** 8. September 2026 · **Status:** Analyse abgeschlossen, keine Umsetzung  
**Projektstand:** `17ae57aab97d0e22e0cacfcbdc05fa94c013ce23`  
**Projekt:** `C:\Users\vinzent.niederwieser\windows-enterprise-companion`

## 1. Ergebnis und Abgrenzung

Die wesentlichen Probleme des Blind-Audits sind technisch nachvollziehbar. Die höchste Priorität haben verlässliche Daten- und Statusaussagen: Quellenabdeckung wird zwar transportiert, aber nicht konsequent in Kennzahlen und Bewertungen berücksichtigt; Aktualisierungen erreichen nicht alle Zwischenspeicher. Hinzu kommen Risiken bei Geräteidentität, Fehlerzuständen und der Speicherung von Inventarsnapshots.

Einige Beobachtungen brauchen eine differenzierte technische Einordnung:

- **F05:** Im Anwendungsfehlerprotokoll existiert eine Detailansicht. Sie liegt unter der vollständigen Tabelle und wird nicht in den sichtbaren Bereich geführt. Bei Client-Ereignissen existiert dagegen nur ein nativer `title`-Hinweis, kein ausdrücklicher Volltextaufruf.
- **F06:** Der GPU-Vergleich berücksichtigt ausschließlich die Namen. Eine unterschiedliche Treiberversion erklärt die Markierung daher nicht. Unsichtbare bzw. zusammenfallende Leerzeichen sind als Ursache reproduzierbar; die damaligen Rohwerte wurden nicht erhoben und bleiben ungeklärt.
- **F09:** Unterschiedliche Gerätebestände sind überwiegend beabsichtigt. „Outdated clients“ bezeichnet jedoch tatsächlich eine Summe veralteter Produktinstallationen und ist damit falsch benannt.
- **F11:** Der globale Berichteinstieg verwendet absichtlich den lokalen Computer. Es fehlt vor allem die rechtzeitige Kennzeichnung dieser Perspektive.

Der Blind-Audit wurde **weder geändert noch umgedeutet**. Unter „Blind-Beobachtung“ stehen nachfolgend gekennzeichnete Zusammenfassungen, keine nachträglichen Ergänzungen seines Wortlauts. Technische Befunde erhalten eigene Kennungen **T01–T09**. Prioritäten hier sind Empfehlungen dieses Folgeaudits; die ursprünglichen Prioritäten bleiben erhalten.

**Unveränderte Referenz:** [2026-09-08-blind-ux-audit.md:1](../docs/2026-09-08-blind-ux-audit.md)  
**SHA-256:** `B7DCDEFE81BF994A2BB73F29EC944C9EE1C53A2A0816BFE976B7D7F8C298B925`

Die Untersuchung verfolgt die Findings durch Frontend, Bridge, Fachlogik, Datenverträge, Persistenz und relevante Tests. Sie ist keine Behauptung, jede Projektzeile oder jede externe Integration vollständig geprüft zu haben. Die historische Anwendungssitzung wurde nicht erneut durchgespielt; produktive Datenbanken, Konten und externe Systeme wurden nicht verändert oder für neue Scans verwendet. Neue Reproduktionen verwenden synthetische Daten bzw. isolierte Tests. Nicht separat verifiziert wurde, ob die Binärdatei der damaligen Sitzung exakt diesem Commit entspricht; technische Kausalaussagen beziehen sich auf den hier untersuchten Projektstand.

## 2. Architektur und vorhandene Lösungsbausteine

### Datenfluss

Die Anwendung ist ein modularer .NET-10-Monolith mit Windows-Host und eingebettetem WebView2. React/TypeScript kommuniziert über eine typisierte Nachrichten-Bridge mit Modul-/Aktionsnamen und Korrelationskennung. Im regulären Betrieb ist dafür kein HTTP-Backend erforderlich. Der Host löst Handler je Anfrage in einem eigenen DI-Scope auf und führt Fehler, Zeitlimits und Abbruch zusammen. SQLite/EF Core speichert lokale Ergebnisse; konkrete Core-Leseverträge verbinden Module, ohne direkte Modulabhängigkeiten einzuführen.

Die technische Trennung ist grundsätzlich brauchbar. Beispiele sind [ActionDispatcher.cs:25](../src/Wec.Host/Bridge/ActionDispatcher.cs), [WebViewBridge.cs:35](../src/Wec.Host/Bridge/WebViewBridge.cs) und [ClientOverviewService.cs:95](../src/Modules/Wec.Modules.EmployeeLifecycle/Application/ClientOverviewService.cs). Generierte API-Typen sichern Formen und Enum-Werte, aber keine fachliche Bedeutung von „0“, „Client“, „aktuell“ oder „vollständig“.

| Ebene | Funktion | Relevante Grenze |
|---|---|---|
| Lokale Persistenz | Inventar, Security-/Health-Ergebnisse, Ziele, Nessus-Daten | Ein Lesevertrag braucht mehr als „Wert oder null“, wenn Daten fehlen, beschädigt oder nicht lesbar sind. |
| `ItHygieneSnapshotCache` | Gemeinsamer Management-Snapshot, serverseitige Filter/Seiten | Singleton ohne Zeitablauf; Schlüssel enthält AD-/KSC-Anfrage, keine Nessus-/opsi-Datenrevision. |
| `EnvironmentProvider` | Eigene React-Kopie des vollständigen Managementergebnisses | Wird durch andere Seiten nicht automatisch aktualisiert; `refresh()` erzwingt keinen Backend-Refresh. |
| Seitenzustand | Action Center, Clients, Cleanup, Vulnerabilities | Teilweise korrekt gegen verspätete Antworten geschützt, aber jeweils eigener Lebenszyklus. |
| `viewCache` | Lokaler Browserzustand über Besuche/Neustarts | Allgemeiner JSON-Cast ohne einheitliche Versionierung; kein Ersatz für fachliche Frische oder Abdeckung. Einzelne Aufrufer validieren selbst. |

Quellen: [ItHygieneSnapshotCache.cs:8](../src/Modules/Wec.Modules.EmployeeLifecycle/Application/ItHygieneSnapshotCache.cs), [EnvironmentContext.tsx:72](../frontend/src/shared/environment/EnvironmentContext.tsx), [viewCache.ts:1](../frontend/src/shared/viewCache.ts). Gespeicherte Ansichten sind gemäß ADR 0013 beabsichtigt. Daraus folgt keine Erlaubnis, einen alten Zustand als gegenwärtige Prüfung auszugeben.

### Was bereits vorhanden ist und erhalten bleiben sollte

- **Statusdimensionen:** `SemanticStatusBadge` trennt Verfügbarkeit, Aktualität, Ausführung, Gesundheit und Lifecycle. Das Problem ist keine fehlende Badge-Bibliothek, sondern die nicht durchgängige Anwendung auf Daten und Kennzahlen. [SemanticStatusBadge.tsx:1](../frontend/src/shared/ui/SemanticStatusBadge.tsx)
- **Datenalter und Abdeckung:** Client Overview besitzt Quellenmetadaten; Security enthält Prüfergebnisse und Coverage. Fehlend und unvollständig werden dort teilweise bereits richtig behandelt. [OverviewSection.tsx:1](../frontend/src/features/clients/sections/OverviewSection.tsx), [SecuritySection.tsx:95](../frontend/src/features/clients/sections/SecuritySection.tsx)
- **Fehlerdarstellung:** `presentError`, `ErrorState` und eingeklappte technische Details sind wiederverwendbar. Mehrere problematische Aufrufer umgehen sie bei gespeicherten Lesezugriffen. [errorPresentation.ts:1](../frontend/src/shared/bridge/errorPresentation.ts), [States.tsx:1](../frontend/src/shared/ui/States.tsx)
- **Abbruch und verspätete Antworten:** Bridge-Abbruch, Operationskennungen und Generation-/Request-Zähler existieren. Clients, Action Center und die gespeicherte Overview demonstrieren geeignete Muster.
- **Detailführung:** Vulnerabilities besitzt einen eigenen Befunddetailbereich. Cleanup und Error log müssen das Interaktionsmuster nicht neu erfinden. [VulnerabilityFindingDetailsPanel.tsx:1](../frontend/src/features/vulnerabilities/VulnerabilityFindingDetailsPanel.tsx)
- **Bewusst lesende Fachabläufe:** Cleanup trifft nur Sitzungsentscheidungen; Action Center erzeugt berechnete Arbeitsvorschläge. Fehlende Quellen werden in der Hygienelogik bei Abwesenheitsregeln bereits berücksichtigt. Diese Schutzwirkung muss bei Verbesserungen erhalten bleiben.

## 3. Einordnung der Blind-Findings

### F01 — Nullwerte trotz fehlender Quelle

**Blind-Beobachtung · P1:** „Critical 0“ bei fehlendem Nessus; erst aufgeklappte Abdeckung erklärt die Lücke. Nach Refresh 120 kritische Action-Center-Einträge. Clients zeigt grüne Nessus-Nullwerte.

**Codeursache und Klassifikation:** Bestätigtes UX-/Semantikproblem mit Backend-Anteil. `ActionCenterService.Summarize` zählt erzeugte, gefilterte Einträge. Fehlende Quellen erzeugen keine automatisch hinzukommenden kritischen Einträge. Auch `UnknownCoverage` zählt lediglich vorhandene Einträge mit eingeschränkter Abdeckung, nicht ausgefallene Quellen. Die UI legt die Quelleninformation in ein geschlossenes Disclosure. Clients färbt Nessus-Kennzahlen allein nach dem Zahlenwert. Die Hygienebewertung unterdrückt bei nicht vollständigem Nessus außerdem sämtliche Nessus-Regeln; der zusätzliche Verlust positiver Befunde ist T02.

**Betroffene Stellen:** [ActionCenterService.cs:197](../src/Modules/Wec.Modules.ActionCenter/Application/ActionCenterService.cs), [ActionCenterPage.tsx:264](../frontend/src/features/actioncenter/ActionCenterPage.tsx), [ClientsPage.tsx:379](../frontend/src/features/clients/ClientsPage.tsx), [HygieneAssessmentPolicy.cs:100](../src/Modules/Wec.Modules.EmployeeLifecycle/Application/HygieneAssessmentPolicy.cs).

**Vorhandene Infrastruktur / Reichweite:** Quellenstatus, `AssessedAtUtc`, Coverage und semantische Badges sind vorhanden. Derselbe Grundfehler betrifft „Missing Kaspersky“, „Missing opsi“, Outdated-/Stale-Zähler und den exklusiven Status „Incomplete“: Ein Gerät mit Problem und unvollständiger Abdeckung muss beide Eigenschaften ausdrücken können. Eine Action-Center-Zahl bezeichnet Arbeitspositionen, keine eindeutigen Geräte oder Nessus-Instanzen.

**Änderungsrisiko:** Einfach überall `null` einzusetzen würde bekannte positive Befunde verbergen und Verträge brechen. Empfohlen ist „bekannte Anzahl + bewertete Grundgesamtheit + Abdeckung“; fehlende Abwesenheitsnachweise dürfen weiterhin keine Missing-/Orphan-Regeln auslösen.

**Tests:** Service-Tests prüfen Zählung und vollständigen Providerfehler, UI-Tests das Vorhandensein von Abdeckung. Es fehlt eine Ende-zu-Ende-Vertragsprüfung „Quelle ausgefallen, bekannte Anzahl null/0/positiv, keine grüne Entwarnung“. Bestehende Tests der unterdrückten Abwesenheitsregeln sind fachlich richtig und bleiben erhalten.

### F02 — Widersprüchliche Quellenzustände zwischen Ansichten

**Blind-Beobachtung · P1:** Vulnerabilities und aktualisiertes Action Center melden Nessus erfolgreich/verfügbar; Client Overview bleibt auf „Failed“, ohne direkt sichtbaren Prüfzeitpunkt.

**Codeursache und Klassifikation:** Bestätigter State-/Refresh-Bug plus UX-Lücke. Overview liest Managementdaten aus `EnvironmentProvider`. Das Action Center aktualisiert separat den Backend-Snapshot mit `Force`. Der Provider behält seine vorherige Kopie. Sein `refresh()` umgeht nur den eigenen Cache: Er ruft `getHygiene` auf, dessen Handler ausdrücklich `force: false` verwendet. Der Backend-Cache besitzt weder TTL noch eine Nessus-Datenrevision. Nur Kaspersky `Unavailable` verhindert das Zwischenspeichern; Nessus-Fehler können darin verbleiben. Dies erklärt die beobachtete Abfolge technisch, ohne den damaligen Cacheinhalt als nachträglich gemessen auszugeben.

**Betroffene Stellen:** [EnvironmentContext.tsx:85](../frontend/src/shared/environment/EnvironmentContext.tsx), [EnvironmentContext.tsx:112](../frontend/src/shared/environment/EnvironmentContext.tsx), [ItHygieneHandlers.cs:23](../src/Modules/Wec.Modules.EmployeeLifecycle/Handlers/ItHygieneHandlers.cs), [ItHygieneSnapshotCache.cs:23](../src/Modules/Wec.Modules.EmployeeLifecycle/Application/ItHygieneSnapshotCache.cs), [OverviewSection.tsx:297](../frontend/src/features/clients/sections/OverviewSection.tsx).

**Vorhandene Infrastruktur / Reichweite:** Andere Handler unterstützen bereits explizites `Force`. Die Overview kennt `assessedAtUtc`, zeigt es jedoch nicht direkt im Management-Statusband. Settings ruft teilweise `environment.invalidate()` auf; das löscht nicht den Backend-Cache. Betroffen sind Clients, Cleanup, Action Center und Managementkontext nach Quellen-/Credential-Änderungen sowie Nessus-Synchronisierung. Einstellungen mit ausdrücklich erforderlichem Neustart sind davon getrennt zu behandeln.

**Änderungsrisiko:** Unkontrolliertes Neuladen bei jedem Öffnen würde Netzwerkzugriffe vervielfachen. Benötigt wird eine konsistente Snapshot-/Revisionsstrategie mit klarer Nutzeraktion und Kennzeichnung alter Daten, kein pauschales Abschaffen von Caches. Abgebrochene und parallel laufende Anforderungen müssen berücksichtigt werden, siehe T06.

**Tests:** `ItHygienePagingTests` prüft Wiederverwendung, andere Verbindung, Force und Wiederholung nach Kaspersky-Ausfall. `EnvironmentContext.test.tsx` prüft gemeinsame Loads und Credential-Trennung. Es fehlt der Ablauf „Nessus ausgefallen → Sync erfolgreich → Force in Ansicht A → Ansicht B“ sowie Refresh vom Provider bis zum echten Handler. Die bisherigen Mock-Grenzen übersehen die fehlende Force-Weitergabe.

### F03 — Review öffnet außerhalb des Blickfelds

**Blind-Beobachtung · P1:** Cleanup-Belege erscheinen unter 25 Zeilen ohne sichtbaren Wechsel.

**Codeursache und Klassifikation:** Bestätigter Interaktions-/Accessibility-Bug. Die Auswahl setzt den Host in den Suchparametern und lädt `selectedAssessment`. Die Review-Section folgt nach der Tabelle. Es gibt an dieser Stelle weder Fokusübergabe noch Scrollführung oder Overlay.

**Betroffene Stellen:** [DeviceCleanupPage.tsx:84](../frontend/src/features/devicecleanup/DeviceCleanupPage.tsx), [DeviceCleanupPage.tsx:327](../frontend/src/features/devicecleanup/DeviceCleanupPage.tsx), [DeviceCleanupPage.tsx:344](../frontend/src/features/devicecleanup/DeviceCleanupPage.tsx).

**Vorhandene Infrastruktur / Reichweite:** Ein Deep-Link per Host und eine benannte Review-Überschrift existieren. Wiederverwendbar ist das sichtbare Detailmuster aus Vulnerabilities. Error log hat denselben Platzierungsfehler; der Kontextbereich des Action Centers liegt ebenfalls nach der Tabelle.

**Änderungsrisiko:** Fokus muss beim Schließen zum Auslöser zurückkehren. Beim Wechsel des Kandidaten dürfen Sitzungsentscheidung, Begründung und geprüfte Quellen nicht auf das falsche Gerät übertragen werden. Nicht automatisch eine Lösch- oder Remediation-Funktion ergänzen.

**Tests:** Cleanup testet vorhandene Überschrift und lesende Grenzen mit sehr kleiner Fixture, nicht Sichtbarkeit bei 25 Zeilen. Die Tests sichern nicht ausdrücklich falsche Positionierung ab; ihre DOM-Prüfung ist dafür ungeeignet. Ergänzen: echter Layouttest bei kleiner Fensterhöhe, Tastaturauswahl, Fokus und Rückkehr.

### F04 — Clientliste wird durch den Rahmen verdrängt

**Blind-Beobachtung · P1:** Kennzahlen, Filter und Bulk-Workbench verbrauchen den ersten Ausschnitt; extreme Statusumbrüche.

**Codeursache und Klassifikation:** Bestätigtes Layout-/UX-Problem. Viele KPI-Karten, die Filter und die ständig vorhandene Batch-Workbench stehen vor den Ergebnissen. Zelltexte verwenden gemeinsam `overflow-wrap:anywhere`; dichte mehrteilige Statusinhalte konkurrieren um schmale Spalten. Das App-Gerüst bietet responsiven Navigation-Drawer und einen eigenen Scrollcontainer, priorisiert aber nicht die Arbeitsinhalte der einzelnen Seite.

**Betroffene Stellen:** [ClientsPage.tsx:361](../frontend/src/features/clients/ClientsPage.tsx), [DataTable.tsx:178](../frontend/src/shared/ui/DataTable.tsx), [clientStatus.tsx:1](../frontend/src/features/clients/clientStatus.tsx), [App.tsx:312](../frontend/src/app/App.tsx).

**Vorhandene Infrastruktur / Reichweite:** Auswahlzustand, Gruppen, Paging und CSS-Breakpoints existieren. Dichte Tabellen betreffen auch Action Center, Cleanup und Logansichten; die gemeinsame Wrap-Regel vergrößert deren Zeilen ebenfalls. Ein vorhandenes `stickyHeader`-Flag ersetzt keine Prüfung im tatsächlichen Scrollcontainer.

**Änderungsrisiko:** Batch-Auswahl und laufender Fortschritt dürfen beim Einklappen nicht verschwinden. „Weniger Text“ darf Abdeckung oder Zielidentität nicht verbergen. Spaltenbreiten müssen nach Inhalt statt globaler Gleichbehandlung festgelegt werden.

**Tests:** `ClientsPage.test.tsx` nennt die Liste kompakt, prüft aber DOM-Inhalt. Es gibt hier keine geometrische Garantie für sichtbare erste Treffer, Zoom, Wortumbrüche oder verschachteltes Scrollen. Abnahme an der im Blind-Audit beobachteten kleinen Ansicht und bei vergrößerter Schrift durchführen.

### F05 — Vollständige Logmeldungen nicht auffindbar

**Blind-Beobachtung · P2:** Zeilenklick brachte im getesteten Weg keinen sichtbaren Volltext.

**Codeursache und Klassifikation:** Zwei verschiedene Ursachen. **Error log:** Detailfunktion vorhanden, aber nach der gesamten Tabelle; Entdeckbarkeits-/Fokusproblem, keine technisch fehlende Detailfunktion. **Event logs:** Der volle Text steht ausschließlich im `title` eines gekürzten Spans. Es gibt keinen Zeilen- oder Detailhandler. Ein nativer Hoverhinweis ist kein verlässlich bedienbarer Volltextbereich. Dies korrigiert keine Blind-Beobachtung: Der ursprüngliche Bericht schließt unentdeckte Wege ausdrücklich nicht aus.

**Betroffene Stellen:** [ErrorLogPage.tsx:155](../frontend/src/features/verwaltung/ErrorLogPage.tsx), [ErrorLogPage.tsx:166](../frontend/src/features/verwaltung/ErrorLogPage.tsx), [EventLogSection.tsx:47](../frontend/src/features/clients/sections/EventLogSection.tsx).

**Vorhandene Infrastruktur / Reichweite:** Error log besitzt bereits Zeit, Quelle, Zusammenfassung und technische Details. Ein gemeinsames Detailmuster kann beide Ansichten und F03 bedienen. Zusätzlich ist die zugrunde liegende Logauswahl begrenzt, siehe T09; „vollständig“ darf nicht über abgeschnittene Backend-Daten hinwegtäuschen.

**Änderungsrisiko:** Lange Nachrichten/Stacktraces müssen performant, kopierbar und als Text dargestellt bleiben. Keine HTML-Ausführung aus Logdaten. Kontext und Fokus dürfen beim Aktualisieren nicht auf eine andere Zeile wechseln.

**Tests:** `ErrorLogPage.test.tsx` prüft ausdrücklich „complete raw evidence“, findet aber nur den DOM-Bereich nach einem Klick auf eine Ein-Zeilen-Fixture. Der Test beweist die vorhandene Funktion, nicht ihre Sichtbarkeit. Ergänzen: viele Zeilen, Tastatur, langer mehrzeiliger Text und erkennbare Backend-Kürzung.

### F06 — GPU-Differenz trotz gleicher sichtbarer Bezeichnung

**Blind-Beobachtung · P2:** Beide GPU-Zellen wirken identisch, „differs“ daneben.

**Codeursache und Klassifikation:** Reproduzierbarer Normalisierungs-/Erklärbarkeitsfehler; die genaue historische Rohwertabweichung ist offen. `compareInventory` verbindet GPU-Namen zu einem String und prüft strikte Stringgleichheit. Unterschiedliche Anzahl von Leerzeichen ergibt „differs“, obwohl HTML sie zusammenfallen lässt. Auch abweichende Reihenfolge mehrerer GPUs ergibt einen Unterschied. Nicht dargestellte Treiber- oder Geräteeigenschaften sind keine Vergleichsgrundlage.

**Betroffene Stelle:** [compare.ts:36](../frontend/src/features/clients/compare.ts), insbesondere GPU-Zeile ab 57; Ergebnisdarstellung [ComparePage.tsx:262](../frontend/src/features/clients/ComparePage.tsx).

**Vorhandene Infrastruktur / Reichweite:** `setDiff` normalisiert bereits Trim/Schreibweise, ist jedoch kein fertiger Hardwarevergleich. Dasselbe Stringmuster betrifft CPU und OS. Memory/Storage vergleichen bereits gerundete Anzeigewerte: Zwei unterschiedliche Bytewerte können deshalb als gleich gelten (synthetisch bestätigt, T04).

**Änderungsrisiko:** Zu aggressive Normalisierung kann echte Unterschiede verbergen. Zuerst definieren, ob Gerätebestand, Reihenfolge oder bestimmte Eigenschaften verglichen werden; Rohwertvergleich und Anzeigeformatierung trennen. Keine unbekannten historischen Rohwerte erfinden.

**Tests:** Der vorhandene Test deckt normale OS-/CPU-/RAM-Unterschiede ab, keine GPU-Leerzeichen oder Reihenfolge. Ein isolierter Probeaufruf des Originalcodes mit einem bzw. zwei Leerzeichen bestätigt `same: false`. Für die damaligen Geräte bleibt eine gezielte, gesonderte Rohdatenprüfung nötig.

### F07 — Vergleich verliert Zeitbezug

**Blind-Beobachtung · P2:** Erfassungsdaten aus der Auswahl fehlen im Ergebnis; Juli und September werden nebeneinander gezeigt.

**Codeursache und Klassifikation:** Bestätigter Darstellungsfehler. `loadSide` behält komplette Inventar- und Security-Ergebnisse einschließlich Zeitstempeln. Tabellenköpfe enthalten jedoch nur den Host. Es fehlt keine Backend-Fähigkeit.

**Betroffene Stellen:** [ComparePage.tsx:206](../frontend/src/features/clients/ComparePage.tsx), [ComparePage.tsx:262](../frontend/src/features/clients/ComparePage.tsx).

**Vorhandene Infrastruktur / Reichweite:** Datums-/Altersdarstellung aus Inventory und Quellenmetadaten aus Overview können wiederverwendet werden. Hardware und Software beruhen auf dem Inventarsnapshot; Security besitzt einen eigenen Erfassungszeitpunkt und eigene Abdeckung. Eine einzige Zeitangabe je Gerät wäre daher zu ungenau.

**Änderungsrisiko:** Zeitpunkt der Anzeige nicht als Zeitpunkt der Erfassung verwenden. Eine Zeitdifferenz macht den Vergleich nicht automatisch ungültig, muss aber sichtbar sein. Unbekannte oder zukünftige Zeitstempel ebenfalls erklären.

**Tests:** Vergleichstests decken Datenverfügbarkeit, Lesen ohne Scan und fehlgeschlagene Reads ab. Es fehlen assertions für Zeitstempel je Datenkategorie sowie für stark auseinanderliegende oder fehlende Erfassungszeiten.

### F08 — Kaspersky hinter historischen Bereichsnamen

**Blind-Beobachtung · P2:** Suche nach Kaspersky führt über „Environment Health / IT Lifecycle“.

**Codeursache und Klassifikation:** Bestätigtes Informationsarchitekturproblem, keine fehlerhafte Kaspersky-Konfiguration. Frontend-Navigation, Überschrift, Hilfetexte und Speichern-Button verwenden historische Fachbereichsnamen, weil Integration und Hygienepolicy in einem Einstellungsabschnitt liegen.

**Betroffene Stellen:** [SettingsSectionNavigation.tsx:1](../frontend/src/features/verwaltung/SettingsSectionNavigation.tsx), [SettingsPage.tsx:404](../frontend/src/features/verwaltung/SettingsPage.tsx), [settingsValidation.ts:42](../frontend/src/features/verwaltung/settingsValidation.ts).

**Vorhandene Infrastruktur / Reichweite:** Abschnittsnavigation, Dirty-Marker, Validierungsübersicht und Credential-Status sind bereits vorhanden. Betroffen sind alle sichtbaren Bezeichnungen und zugehörige Fehler-/Bestätigungstexte; nicht nur die Überschrift.

**Änderungsrisiko:** Eine bessere UI-Struktur verlangt keine Umbenennung von Modulnamen, Bridge-Aktionen, Konfigurationsschlüsseln oder gespeicherten Credentials. Bei Umgruppierung Ankerlinks, ungespeicherte Eingaben und Neustart-Hinweise erhalten.

**Tests:** Settings-Tests erwarten die historischen Namen ausdrücklich. Das sichert keine falsche Backend-Funktion ab, würde aber die überholte Beschriftung festschreiben. Bei einer gewollten Änderung Suchbegriffe, Dirty-Zustand und Fehlernavigation als Benutzeraufgabe neu testen.

### F09 — Uneinheitliche Grundgesamtheiten und falsche Zähleinheit

**Blind-Beobachtung · P2:** 41 Hosts, 686 Clients, 508 Vergleichstreffer sowie 475 Clients / 1608 Outdated clients.

**Codeursache und Klassifikation:** Gemischt: beabsichtigte Bestände mit unzureichender Beschriftung; zusätzlich ein bestätigter Einheiten-/Naming-Bug im Patchbereich. Die historischen Einzelzahlen wurden nicht neu aus der Produktivdatenbank berechnet.

| Ansicht | Tatsächliche Rechen-/Auswahlbasis |
|---|---|
| Dashboard „Clients“ | Hosts mit gespeichertem WEC-Inventar; kein Gesamtbestand der Managementsysteme. |
| Clients | Deduplizierter Hygienebestand aus AD/KSC/opsi, um Inventarhosts und gespeicherte Clientziele ergänzt; Nessus reichert zugeordnete Geräte an. |
| Compare | Eigene Zusammenführung aus AD-Suche **ohne deaktivierte AD-Geräte**, Inventar, gespeicherten Clientzielen und Security-Hosts; keine KSC-/opsi-Gesamtmenge. Die AD-Abfrage übernimmt zudem nicht die Verbindungsparameter des Environment-Requests. |
| Patch „Clients“ | Clients des opsi-Kontexts, gegebenenfalls Depotfilter. |
| Patch „Outdated clients“ | Summe der `OutdatedClientCount` aller Produktzeilen. Ein Client mit drei veralteten Produkten zählt dreimal. |

**Betroffene Stellen:** [dashboard.ts:87](../frontend/src/features/dashboard/dashboard.ts), [ClientWorkspacePaging.cs:1](../src/Modules/Wec.Modules.EmployeeLifecycle/Application/ClientWorkspacePaging.cs), [ItHygieneService.cs:324](../src/Modules/Wec.Modules.EmployeeLifecycle/Application/ItHygieneService.cs), [ComparePage.tsx:139](../frontend/src/features/clients/ComparePage.tsx), [PatchDashboardService.cs:214](../src/Modules/Wec.Modules.PatchManagement/Application/PatchDashboardService.cs), [PatchProductOverviewWorkspace.tsx:30](../frontend/src/features/patchmanagement/PatchProductOverviewWorkspace.tsx).

**Vorhandene Infrastruktur / Reichweite:** Ergebnisverträge enthalten Quelle, Filter und Snapshot-/Treffergrößen. Produkt-Client-Zuordnungen ermöglichen zusätzlich eine echte Distinct-Gerätezahl. Die Geräteidentität selbst besitzt ein weiteres Problem, T01.

**Änderungsrisiko:** Die fünf Mengen nicht künstlich gleichsetzen. Kurzfristig die Patchsumme korrekt als Installationen benennen; eine zusätzliche Zahl eindeutiger betroffener Geräte benötigt einen ausdrücklich definierten Vertrag. Eine gemeinsame Compare-Auswahl muss Quellen-/Credential-Kontext und bewusst ausgeschlossene Geräte beachten.

**Tests:** Paging-/Merge-Tests prüfen definierte Bestände, Patch-Tests einzelne Produktzustände. Es fehlt der Fall „ein Gerät, zwei veraltete Produkte“ mit beiden Einheiten im Gesamtresultat. Bestehende Ein-Produkt-Fixtures können den Fehler nicht entdecken.

### F10 — Unterschiedlich erklärte Aktionswirkungen

**Blind-Beobachtung · P2:** Save client, Re-run scan und Network Scan sind vor Ausführung weniger klar als Event logs.

**Codeursache und Klassifikation:** Überwiegend bestätigte UX-Lücke bei beabsichtigten Funktionen. `Save client` speichert lokal Zielname, Host, Rolle und optional Benutzername; der Save-Target-Aufruf überträgt kein Passwort. Inventarscans lesen den Zielcomputer und ersetzen den lokalen Inventarsnapshot. Security liest Prüfzustände und speichert ein Ergebnis. Network Scan ruft den konfigurierten Scanner mit Ziel/Portauswahl auf und liest optional DHCP-Reservierungen; „lesend“ bedeutet hier aktiven Netzwerkverkehr. Der UI-Default ist ein konkretes `/24` mit aktivierter Portprüfung.

**Betroffene Stellen:** [ClientDetailPage.tsx:124](../frontend/src/features/clients/ClientDetailPage.tsx), [TargetContext.tsx:75](../frontend/src/shared/targets/TargetContext.tsx), [HardwareInfoService.cs:49](../src/Modules/Wec.Modules.Inventory/Application/HardwareInfoService.cs), [SecuritySection.tsx:91](../frontend/src/features/clients/sections/SecuritySection.tsx), [NetworkScanPage.tsx:42](../frontend/src/features/networkscan/NetworkScanPage.tsx), [NetworkScanService.cs:33](../src/Modules/Wec.Modules.NetworkScan/Application/NetworkScanService.cs).

**Vorhandene Infrastruktur / Reichweite:** Gute Vorbilder sind Event logs und Cleanup. Print Management enthält getrennte lesende und operative Funktionen; ein pauschales Read-only-Etikett für einen ganzen Bereich wäre ungeeignet. Gemeinsame Bestätigungs- und Fehlerbausteine sind vorhanden.

**Änderungsrisiko:** Lokales Speichern, Remote-Abfragen und externe Änderung sauber trennen; nicht jede lesende Aktion mit einem zusätzlichen Bestätigungsdialog belasten. Kein tatsächliches Remediation-Verhalten aus unklarer Beschriftung ableiten. Bei Inventar ist die lokale Ersatzspeicherung durch T07 zusätzlich relevant.

**Tests:** Aufruf-/Payloadtests bestätigen viele Funktionen. Es fehlt eine konsistente Prüfung von Ziel, Konto, Reichweite und Speicherwirkung vor Ausführung. Im Clientdetail werden Save-/Delete-Promises ohne lokale Fehlerbehandlung verworfen; der Kontext fängt sie nicht ab. Dafür Fehlerrückmeldung und Mehrfachklickschutz ergänzen.

### F11 — Lokale und globale Perspektive wechseln

**Blind-Beobachtung · P2:** Flottenkarten neben Security eines Clients; globaler Report zeigt „This machine“.

**Codeursache und Klassifikation:** Fachlich beabsichtigte lokale Funktionen mit UX-/Navigationsproblem. Das Dashboard ruft `security/getLatestScan` ohne Target auf; der Handler übersetzt dies in den lokalen Computer. `ReportingPage` reicht `host={null}` an dieselbe Berichtskomponente, die im Clientkontext ein festes Ziel akzeptiert. Der Seitenhinweis „This machine“ existiert, erscheint aber erst nach dem Einstieg.

**Betroffene Stellen:** [DashboardPage.tsx:143](../frontend/src/features/dashboard/DashboardPage.tsx), [GetLatestSecurityScanHandler.cs:23](../src/Modules/Wec.Modules.Security/Handlers/GetLatestSecurityScanHandler.cs), [ReportingPage.tsx:5](../frontend/src/features/reporting/ReportingPage.tsx), [ReportingSection.tsx:1](../frontend/src/features/reporting/ReportingSection.tsx).

**Vorhandene Infrastruktur / Reichweite:** Zielgebundene Berichtskomponente und Kartenmetadaten sind vorhanden. Dashboard, globale Navigation und Berichtseinstieg sollten denselben Scope ausdrücken.

**Änderungsrisiko:** Einen Flottenbericht nicht beiläufig als vermeintliche Korrektur einführen: Aggregation, Berechtigungsumfang, Abdeckung und Größenlimits wären neue Fachanforderungen. Geringeres Risiko hat eine klare Benennung der bestehenden lokalen Funktion.

**Tests:** Lokale Standardwerte sind nicht falsch. Ergänzen: Geltungsbereich vor Einstieg sichtbar; lokaler und expliziter Clientbericht verwenden jeweils das richtige Ziel. Ein Benutzertest sollte prüfen, ob daraus weiterhin ein Gesamtbericht erwartet wird.

### F12 — Trend ohne Skala und erklärte Bewertungsgrundlage

**Blind-Beobachtung · P2:** Vier Linien, keine Achsenbeschriftung; „Worse“ unerklärt.

**Codeursache und Klassifikation:** Bestätigtes Visualisierungs-/Accessibility-Problem. Das SVG zeichnet Linien und eine Baseline; Datenlabels, Skala, Tooltips oder eine Wertetabelle fehlen. Die X-Position wird nach Index gleichmäßig verteilt, nicht nach tatsächlichem Datumsabstand. Fehlende Tage können deshalb als gleichmäßige Zeitreihe erscheinen.

Die Backend-Bewertung ist konkret definiert: erstes gegen letztes vorhandenes Datum, nur Assets an **beiden** Endpunkten, Vergleich der Summen lexikografisch nach Critical → High → Medium → Low. Schon eine Zunahme bei Critical bestimmt „Worse“, auch bei gleichzeitig fallenden niedrigeren Stufen. Die gezeichneten Tagespunkte enthalten dagegen **alle** Assets des Tages. Kurve und Urteil haben damit unterschiedliche Grundgesamtheiten.

**Betroffene Stellen:** [VulnerabilitiesPage.tsx:56](../frontend/src/features/vulnerabilities/VulnerabilitiesPage.tsx), [VulnerabilitiesPage.tsx:111](../frontend/src/features/vulnerabilities/VulnerabilitiesPage.tsx), [VulnerabilityQueryService.cs:58](../src/Modules/Wec.Modules.VulnerabilityManagement/Application/VulnerabilityQueryService.cs).

**Vorhandene Infrastruktur / Reichweite:** Datumswerte, Severity-Summen und Common/New/Removed-Assetzahlen sind vorhanden. Auch das Dashboard nutzt den Trend. Eine zugängliche Wertetabelle kann denselben Vertrag verwenden.

**Änderungsrisiko:** „Worse“ nicht stillschweigend auf Gesamtzahl umstellen. Erst die bestehende fachliche Policy sichtbar erklären; dann gegebenenfalls eine Produktentscheidung zur gemeinsamen Kohorte treffen. Lücken und UTC-Tage korrekt darstellen.

**Tests:** Backend-Trendlogik und UI-Leerzustände sind teilweise getestet; es fehlt eine gemeinsame Prüfung von Urteil und gezeichneter Kohorte, unregelmäßigen Tagen sowie einer ohne Farbe/Zeigegerät lesbaren Datendarstellung.

### F13 — Filter und Rückwege sind inkonsistent

**Blind-Beobachtung · P2:** Clients filtert sofort, andere Seiten nach Apply; Rückkehr über Hauptnavigation verliert Suche.

**Codeursache und Klassifikation:** Unterschiedliche Auslösepolitik ist eine UX-Entscheidung; Zustandsverlust und Scrollwiederherstellung enthalten konkrete Bugs. Clients stößt bei Texteingaben serverseitige Bridge-Anfragen an, keinen rein lokalen Filter. Such-/Seitenzustand wird beim Detailaufruf in `location.state` geschrieben. Ein frischer Hauptnavigationslink überträgt ihn nicht. Der Posture-Filter lebt separat in der URL und fehlt im gespeicherten View-State; „All clients“ navigiert zu `/clients` ohne diesen Queryparameter. Zusätzlich verwendet die Wiederherstellung `window.scrollY/scrollTo`, während die App in einem inneren Element scrollt.

**Betroffene Stellen:** [ClientsPage.tsx:142](../frontend/src/features/clients/ClientsPage.tsx), [ClientsPage.tsx:209](../frontend/src/features/clients/ClientsPage.tsx), [ClientsPage.tsx:337](../frontend/src/features/clients/ClientsPage.tsx), [ClientDetailPage.tsx:124](../frontend/src/features/clients/ClientDetailPage.tsx), [App.tsx:325](../frontend/src/app/App.tsx).

**Vorhandene Infrastruktur / Reichweite:** URL-Filter, View-State, Abbruch und serverseitiges Paging sind vorhanden. Action Center und Cleanup nutzen Entwurf/Anwenden bewusst, um Abfragen pro Tastendruck zu vermeiden. Die Navigation-/Scrollentscheidung ist für weitere Listen-Detail-Wege wiederverwendbar.

**Änderungsrisiko:** Historie, Direktlinks und explizites „Zurücksetzen“ unterscheiden. Filter nicht zwischen unterschiedlichen Quellkontexten übernehmen. Gleiches UX-Muster darf keine ungebremsten Remote-Abfragen auf allen Seiten erzeugen.

**Tests:** Tests prüfen direkte Posture-URLs und Rückkehr mit Verbindungsnachweisen, nicht die gesamte Kombination aus Suche, Posture, Seite, Tabwechsel, Hauptnavigation und innerem Scrollcontainer. Diese als navigierten Ablauf testen.

### F14 — Wiederholung und Fachsprache

**Blind-Beobachtung · P3:** Viele Stored-/Source-Hinweise, große Beziehungsdarstellung und wiederholte technische Logs erhöhen Leselast.

**Codeursache und Klassifikation:** Nachvollziehbares UX-/Content-Problem; optimale Verdichtung bleibt eine Benutzertestfrage. Source Ledger, Management Summary, Detail-Disclosure und Einzelkarten tragen teilweise dieselbe Erklärung. Fehlerprotokoll rendert Einzelereignisse ohne Gruppierung. Historische Modul-/Architekturbegriffe sind in sichtbare Texte gelangt.

**Betroffene Stellen:** [OverviewSection.tsx:297](../frontend/src/features/clients/sections/OverviewSection.tsx), [OverviewSection.tsx:401](../frontend/src/features/clients/sections/OverviewSection.tsx), [ErrorLogPage.tsx:155](../frontend/src/features/verwaltung/ErrorLogPage.tsx), [SettingsPage.tsx:404](../frontend/src/features/verwaltung/SettingsPage.tsx).

**Vorhandene Infrastruktur / Reichweite:** Disclosure, Zusammenfassungen und technische Fehlerdetails existieren bereits. Wiederholte Meldungen können mit Anzahl und erstem/letztem Auftreten zusammengefasst werden, der Rohtext muss zugänglich bleiben. Map/List sollte eine aufgabenbezogene Wahl sein.

**Änderungsrisiko:** „Gespeichert“, Abdeckung und Zeitbezug nicht nur wegen Wiederholung ersatzlos streichen. Gruppieren darf verschiedene Hosts, Ursachen oder Zeiträume nicht fälschlich zusammenziehen. Quellenbelege und Beziehungen sind keine Eigentums-/Zuweisungsbehauptungen.

**Tests:** Texttests können versehentlich alte Formulierungen festhalten; sie beweisen keine gute Verständlichkeit. Funktionale Grenzen erhalten, Inhaltsdichte mit repräsentativen Administratoraufgaben validieren. Kein pauschaler Navigationsumbau vor den P1-Korrekturen.

## 4. Zusätzliche technische Befunde

Diese Punkte stammen **aus der Codeanalyse**, nicht aus dem Blind-Audit. „Reproduziert“ bezeichnet nachfolgend synthetische Aufrufe des unveränderten Originalcodes, keine Wiederholung auf produktiven Geräten.

### T01 · P1 — Kurzname als universelle Geräteidentität

**Nachweis:** Frontend `clientKey` schneidet alles nach dem ersten Punkt ab. Backend-Korrelation und Client-Workspace verwenden denselben Ansatz, während `ScanTarget.CacheKey` den vollständigen Host bewahrt. `isLocalClient` nutzt ebenfalls den verkürzten Schlüssel. Reproduziert: `10.1.2.3` und `10.9.8.7` ergeben beide `10`; `PC-01.other.example` wird bei lokalem Maschinennamen `PC-01` zu einem **lokalen Null-Target**.

**Wirkung:** Fremde Geräte können verschmelzen, gespeicherte Nachweise falsch zugeordnet werden und Clientaktionen auf den lokalen Computer zeigen. Dies ist kein Nachweis, dass im Blind-Audit tatsächlich ein falsches Gerät verarbeitet wurde. Betroffen sind Clients, Compare, Managementkorrelation, Overview und darauf aufbauende Cleanup-/Action-Center-Verweise.

**Dateien / vorhandene Ansätze:** [clients.ts:44](../frontend/src/features/clients/clients.ts), [clients.ts:162](../frontend/src/features/clients/clients.ts), [ItHygieneService.cs:381](../src/Modules/Wec.Modules.EmployeeLifecycle/Application/ItHygieneService.cs), [ClientWorkspacePaging.cs:302](../src/Modules/Wec.Modules.EmployeeLifecycle/Application/ClientWorkspacePaging.cs), [ScanTarget.cs:17](../src/Wec.Core/Targets/ScanTarget.cs). Host, FQDN und Quellenidentitäten existieren; sie benötigen eine gemeinsame Regel mit expliziten Aliasbeziehungen und Ambiguität statt pauschaler Verkürzung.

**Risiko einer Korrektur:** Hoch bei historischen Zuordnungen und Schlüsseln. Keine blinde Datenmigration oder automatisches Zusammenführen. Bestehende Kurzname/FQDN-Aliasfälle erhalten, IPv4 und gleichnamige Geräte verschiedener Domains getrennt testen. Die bisherigen Normalisierungstests sichern den Kurznamensansatz ausdrücklich ab; deren Annahme muss fachlich eingeschränkt werden.

### T02 · P1 — Positive Nessus-Befunde verschwinden bei Teilabdeckung

**Nachweis:** `NessusComputerInventoryProvider` liefert bei laufender Synchronisierung oder veraltetem Cache weiterhin Assetdaten, markiert die Quelle aber `Partial`. `HygieneAssessmentPolicy` legt nicht nur Missing-Regeln, sondern auch Critical-/High-Regeln vollständig hinter `Availability == Available`.

**Wirkung:** Bereits bekannte kritische Befunde werden während einer Teilaktualisierung nicht als Hygiene-/Action-Center-Befund erzeugt. Clientzellen können zugleich die vorhandenen Nessus-Daten anzeigen. Zusätzlich zählen Summary-Werte Findings, nicht die Rohzahlen. Das verstärkt F01/F02 und kann Entscheidungen verzerren.

**Dateien / Infrastruktur:** [NessusSyncService.cs:140](../src/Modules/Wec.Modules.VulnerabilityManagement/Application/NessusSyncService.cs), [HygieneAssessmentPolicy.cs:100](../src/Modules/Wec.Modules.EmployeeLifecycle/Application/HygieneAssessmentPolicy.cs), [ItHygieneService.cs:396](../src/Modules/Wec.Modules.EmployeeLifecycle/Application/ItHygieneService.cs). Coverage-/Reliability-Felder sind vorhanden. Positive bekannte Evidenz mit eingeschränkter Aussagekraft anzeigen; fehlende Evidenz bei Teilquellen weiterhin nicht als Abwesenheitsbeweis werten.

**Risiko / Tests:** Mittleres fachliches Risiko: stale positives als beobachtet mit Datum kennzeichnen, nicht als frisch bestätigt. `Assessment_SuppressesMissingForPartialData…` schützt zu Recht vor falschem Missing. Es fehlt ein Backend-Fall mit `Partial` **und** vorhandener Critical-Zahl. Der Frontendtest „keeps showing known per-client Nessus data … partial“ prüft nur die Anzeige einer Mock-Fixture, nicht die vorgelagerte Bewertung.

### T03 · P1 — Lesefehler werden zu „keine gespeicherten Daten“

**Nachweis:** Inventory setzt bei jedem fehlgeschlagenen `cacheOnly`-Read den Zustand auf `idle`. Security und Health fangen jeden Fehler beim Laden des letzten Ergebnisses ebenfalls als `idle` ab. Damit werden Bridge-/Datenbankfehler nicht von tatsächlich fehlenden Ergebnissen unterschieden. Das Inventarrepository behandelt zusätzlich syntaktisch ungültiges JSON als Cache-Miss.

**Wirkung:** Der Benutzer sieht eine Aufforderung zum neuen Scan, obwohl lediglich gespeicherte Daten nicht gelesen werden konnten. Er kann unnötige Remotezugriffe auslösen und historische Belege für nicht vorhanden halten. Die gespeicherte Overview und Compare können dasselbe Problem bereits als Fehler darstellen: innerhalb eines Clients entstehen widersprüchliche Aussagen.

**Dateien / Infrastruktur:** [InventorySection.tsx:40](../frontend/src/features/clients/sections/InventorySection.tsx), [SecuritySection.tsx:37](../frontend/src/features/clients/sections/SecuritySection.tsx), [HealthSection.tsx:29](../frontend/src/features/clients/sections/HealthSection.tsx), [EfHardwareSnapshotRepository.cs:35](../src/Modules/Wec.Modules.Inventory/Persistence/EfHardwareSnapshotRepository.cs). `BridgeInvokeError`, `NOT_FOUND`, `presentError` und der Compare-Ladezustand sind vorhanden.

**Risiko / Tests:** Missing-/Legacy-Fälle müssen kompatibel bleiben; kein automatischer Ersatzscan bei beschädigtem Cache. Compare testet diese Unterscheidung ausdrücklich, die betroffenen Abschnittspfade nicht ausreichend. Gemeinsame Tests für Null/NotFound, Timeout, InternalError und beschädigten gespeicherten Inhalt ergänzen.

### T04 · P1 — Vergleich erklärt unbekannte Software als abwesend

**Nachweis:** `compareSoftware` ersetzt `installedSoftware: null` durch eine leere Liste und ignoriert `installedSoftwareError`. Synthetisch reproduziert: Software auf A und „Read failed“ auf B führt zu „nur A“. Gleicher Name bei unterschiedlicher Version wird als gemeinsam ausgegeben. `compareInventory` prüft RAM-/Plattengrößen erst nach Rundung; 8 GiB und 8 GiB + 1024 Byte werden beide als `8.0 GB / same: true` bewertet.

**Wirkung / Klassifikation:** Ein Vergleich unbekannter Softwarebestände als „nur auf A“ ist ein Dateninterpretationsbug. Vergleich nur nach Namen kann fachlich beabsichtigt sein, muss dann ausdrücklich so heißen; er ist kein Versionsvergleich. Die Byte-Rundung benötigt ebenfalls eine explizite Toleranzdefinition. Security vergleicht nach Titel und verliert damit Identität/Resource; das ist nur als Vergleich von Problemarten belastbar.

**Dateien / Infrastruktur:** [compare.ts:36](../frontend/src/features/clients/compare.ts), [compare.ts:105](../frontend/src/features/clients/compare.ts), [ComparePage.tsx:406](../frontend/src/features/clients/ComparePage.tsx). Snapshot-Fehler und Security-Coverage sind bereits vorhanden; Set-Diff und Statusanzeige lassen sich erweitern.

**Risiko / Tests:** Keine aggressive Zusammenführung von Produkten/Editionen/Architekturen. Der Test „tolerating a null software list“ in [compare.test.ts:80](../frontend/src/features/clients/compare.test.ts) sichert die problematische Abwesenheitsaussage direkt ab. Zuerst Unknown/Partial/Empty unterscheiden, danach Identität, Versionen und numerische Toleranzen separat definieren. Änderungen betreffen auch F06/F07.

### T05 · P1 — Checkbox-Tastatur aktiviert die umgebende Zeile

**Nachweis:** `DataTable.handleKey` verarbeitet jedes hochgereichte Enter/Space, ruft `preventDefault()` und den Zeilenhandler auf. Clients stoppt bei seiner Checkbox nur Klickereignisse. Ein isolierter DOM-Test mit Original-DataTable bestätigt: Leertaste auf fokussierter Checkbox → Zeilenhandler einmal aufgerufen, Default verhindert.

**Wirkung:** Tastaturnutzer können beim Auswählen eines Batch-Clients stattdessen das Clientdetail öffnen. Das ist ein konkreter Bedienfehler, keine ausschließlich hypothetische Accessibility-Lücke. Darüber hinaus wird eine interaktive Tabellenzeile mit `role="button"` überschrieben; native Tabellenbeziehungen und verschachtelte Bedienelemente müssen bewusst modelliert werden.

**Dateien / Infrastruktur:** [DataTable.tsx:131](../frontend/src/shared/ui/DataTable.tsx), [DataTable.tsx:162](../frontend/src/shared/ui/DataTable.tsx), [ClientsPage.tsx:1](../frontend/src/features/clients/ClientsPage.tsx). Gemeinsame Tabelle, native Checkbox und Testing Library sind vorhanden. Weitere klickbare Tabellen mit Links/Buttons/Inputs auf denselben Ereignispfad prüfen.

**Risiko / Tests:** Hohe Reichweite einer gemeinsamen Änderung. Zeilenfokus und Aktivierung auf echter Zeile erhalten, Ereignisse von Kindcontrols nicht übernehmen. `DataTable.test.tsx` prüft Paging/Sortierung/Loading, aber weder Zeilenkeyboard noch Controls innerhalb einer Zeile. Ein Regressionstest muss explizit prüfen, dass keine Navigation ausgelöst wird.

### T06 · P2 — Unvollständige Zustandsübergänge und veraltete Auswahlobjekte

**Nachweis A:** `EnvironmentProvider.invalidate()` erhöht die Generation und bricht ab, setzt jedoch `loading` nicht zurück. Die alte Anfrage darf wegen des Generationguards in `finally` ebenfalls nicht mehr zurücksetzen. Ohne neue Anfrage bleibt Loading stehen. Gleichzeitig erhöht ein erzwungener Load die Generation nicht; konkurrierende Force-Aufrufe können dieselbe Generation teilen.

**Nachweis B:** Das Action Center behält nach neuen Daten das alte `selectedItem`-Objekt, sofern dessen ID in der Antwort noch vorkommt. Neue Erklärung/Zeit/Coverage derselben ID ersetzt das geöffnete Objekt nicht.

**Dateien / Infrastruktur:** [EnvironmentContext.tsx:85](../frontend/src/shared/environment/EnvironmentContext.tsx), [EnvironmentContext.tsx:112](../frontend/src/shared/environment/EnvironmentContext.tsx), [ActionCenterPage.tsx:122](../frontend/src/features/actioncenter/ActionCenterPage.tsx). Requestkennungen und Generationguards existieren bereits; in anderen Ansichten sind passendere Muster vorhanden.

**Risiko / Tests:** Alte Antworten dürfen einen neuen Request weder beenden noch überschreiben. Auswahl besser anhand stabiler ID aus dem aktuellen Ergebnis ableiten. Tests fehlen für Invalidate während Pending, alte Antwort nach neuem Load sowie geänderte Evidenz mit gleicher ID. Beide Punkte sind statisch belegte Pfade; ihre Häufigkeit in der damaligen Sitzung ist unbekannt.

### T07 · P1 — Inventarsnapshot wird nicht atomar ersetzt

**Nachweis:** `EfHardwareSnapshotRepository.SaveAsync` führt zuerst `ExecuteDeleteAsync` aus und fügt danach per `SaveChangesAsync` ein. Es gibt im untersuchten Aufrufpfad keine umspannende Transaktion. Der Hostindex ist nicht eindeutig. Die zugesagte Regel „ein Snapshot je Host“ wird dadurch nicht von der Datenbank abgesichert.

**Wirkung:** Fehler/Abbruch zwischen Löschen und Einfügen kann den zuvor vorhandenen Snapshot verlieren. Zwei überlappende Saves können nach beiden Löschvorgängen doppelte Hostzeilen erzeugen. Weil `ListHostsAsync` Zeilen und keine Gruppierung zurückgibt, können dann auch Zählungen und Auswahllisten abweichen. Dies ist eine aus dem Code abgeleitete Fehler-/Nebenläufigkeitsfolge, kein Nachweis eines produktiven Datenverlusts.

**Dateien / Infrastruktur:** [EfHardwareSnapshotRepository.cs:68](../src/Modules/Wec.Modules.Inventory/Persistence/EfHardwareSnapshotRepository.cs), [HardwareSnapshotRecordConfiguration.cs:27](../src/Modules/Wec.Modules.Inventory/Persistence/HardwareSnapshotRecordConfiguration.cs), [HardwareInfoService.cs:84](../src/Modules/Wec.Modules.Inventory/Application/HardwareInfoService.cs). EF-Transaktionen, Migrationen und isolierte SQLite-Integrationstests existieren.

**Risiko / Tests:** Eindeutiger Index erfordert vor einer Migration eine kontrollierte Strategie für mögliche Dubletten. Atomarer Ersatz/Upsert muss alten Zustand bei Fehler erhalten; Reihenfolge konkurrierender Ergebnisse fachlich festlegen. Der aktuelle Test prüft nur sequenzielles Ersetzen. Fault-Injection zwischen Delete/Save und zwei parallele DbContexts fehlen.

### T08 · P2 — Inventarlisten-Read löscht Legacy-Datensätze

**Nachweis:** `ListHostsAsync` löscht Datensätze mit leerem/Whitespace-Host direkt per `ExecuteDeleteAsync`, bevor es die Liste zurückgibt. Historische Bereinigung wurde in einen lesend benannten Pfad eingebaut.

**Wirkung:** Öffnen von Dashboard, Clients, Compare oder anderen Listen kann lokal gespeicherte Daten verändern. Dies sind keine AD-/Remote-Löschungen, aber es widerspricht einem strikten „gespeicherte Daten nur lesen“-Vertrag und erschwert die Nachvollziehbarkeit beschädigter Altbestände.

**Dateien / Infrastruktur:** [EfHardwareSnapshotRepository.cs:48](../src/Modules/Wec.Modules.Inventory/Persistence/EfHardwareSnapshotRepository.cs), [HardwareSnapshotPersistenceTests.cs:150](../tests/Wec.Infrastructure.IntegrationTests/Persistence/HardwareSnapshotPersistenceTests.cs). Historische Migration und lokale Wartungswege bieten einen geeigneteren Ort; im Read kann derselbe Filter ohne Mutation verwendet werden.

**Risiko / Tests:** Ungültige Hosts weiterhin nicht als Scanziele anbieten. Bereinigung separat und nachvollziehbar ausführen. `ListHostsAsync_RemovesLegacySnapshotsWithoutAHost` behauptet ausdrücklich nach dem Lesen null Datenbankzeilen und sichert damit die Nebenwirkung direkt ab. Bei einer Vertragskorrektur muss dieser Test ersetzt werden.

### T09 · P2 — Fehlerfilter arbeitet erst nach begrenzter Logauswahl

**Nachweis:** Frontend lädt die letzten 500 Warnungen/Fehler gemeinsam und filtert „errors“ danach lokal. Backend betrachtet maximal sieben Dateien und maximal 40 Fortsetzungszeilen pro Meldung; eine ausdrückliche Kürzungsmetadatenstruktur fehlt. Die Dateien werden vor abschließender Ergebnisbegrenzung vollständig in Zeilen eingelesen.

**Wirkung:** 500 neuere Warnungen können ältere Fehler aus dem Fehlerfilter verdrängen. Eine leere Liste beweist dann nicht, dass im relevanten Zeitraum keine Fehler vorhanden sind. Lange Details können unbemerkt gekürzt werden; sehr große Dateien begrenzen zwar die Zahl der angezeigten Einträge, nicht entsprechend den Leseaufwand.

**Dateien / Infrastruktur:** [ErrorLogPage.tsx:56](../frontend/src/features/verwaltung/ErrorLogPage.tsx), [ErrorLogPage.tsx:83](../frontend/src/features/verwaltung/ErrorLogPage.tsx), [RecentLogEntriesHandler.cs:1](../src/Wec.Host/Bridge/RecentLogEntriesHandler.cs). Parser, strukturierte Anzeige und klare „Hide previous entries“-Semantik existieren.

**Risiko / Tests:** Filter vor Limit verschieben, Abdeckungsfenster und Kürzung ausgeben; keine unbegrenzte Rückgabe als einfache Lösung. Reihenfolge und lokale Clear-Marker respektieren. Parsertests decken Parsing ab, nicht die Kombination aus Dateigrenzen, Fehlerfilter, 500 Warnungen und abgeschnittenem Volltext.

## 5. Testbefund und Verifikation

### Tatsächlich ausgeführt

| Prüfung | Ergebnis |
|---|---:|
| Frontend, vollständiger vorhandener Vitest-Lauf (`npm test -- --reporter=dot`) | 78 Testdateien, **467 Tests bestanden** |
| EmployeeLifecycle-Unitprojekt | **89 bestanden** |
| ActionCenter-Unitprojekt | **6 bestanden** |
| DeviceCleanup-Unitprojekt | **6 bestanden** |
| PatchManagement-Unitprojekt | **36 bestanden** |
| VulnerabilityManagement-Unitprojekt | **12 bestanden** |
| Isolierte SQLite-Integration: `HardwareSnapshotPersistenceTests` | **7 bestanden** |
| Host: `RecentLogEntriesParserTests` | **6 bestanden** |
| Synthetische Originalcode-Proben | GPU-Whitespace, Rundung, Software-Unknown/Version, IP-/Domainidentität und Checkbox-Ereignisweg bestätigt |

Damit wurden **629 vorhandene Tests erfolgreich ausgeführt**: 467 Frontend und 162 Backend-/Persistenz-/Parsertests. .NET-Aufrufe verwendeten `--no-restore`; die beiden letzten Prüfungen waren gezielt per Klassenfilter begrenzt. Der Frontendlauf meldete experimentelle Node-localStorage-Warnungen, keine fehlgeschlagenen Tests.

Nicht ausgeführt wurden die gesamte .NET-Suite, Live-System-/WMI-/AD-/Nessus-/opsi-Integrationstests, produktive Fehlerprovokation, ein erneuter visueller Computer-Use-Audit oder ein Screenreader-Konformitätsaudit. Die SQLite-Tests arbeiten auf jeweils eigenen temporären Datenbanken. Neue Probe- bzw. Testdateien wurden nicht in das Projekt aufgenommen.

### Was die grünen Tests nicht beweisen

1. **DOM ist nicht Sichtbarkeit:** F03–F05 bestehen trotz Tests, die nur vorhandene Überschriften/Regionen abfragen. jsdom liefert keine belastbare Fenstergeometrie.
2. **Ein korrekter Mock kann einen falschen Vertrag verdecken:** UI-Mocks für Partial-Nessus oder Refresh prüfen nicht die echte Hygienepolicy bzw. Force-Weitergabe.
3. **Statusform ist nicht Statussemantik:** Generierte Typen verhindern nicht `0 == unbekannt`, Produktinstallationen == Clients oder Kurzname == Geräteidentität.
4. **Erfolgspfad ist nicht Atomarität:** Sequenzielle Snapshottests decken Abbruch und konkurrierende Saves nicht ab.
5. **Bestehende Erwartungen können das Problem festhalten:** Null-Software als leere Menge, universelle Kurznamensnormalisierung und Datenlöschung beim Lesen sind ausdrücklich getestete Annahmen. Diese gezielt korrigieren, nicht Tests pauschal entfernen.

## 6. Konsequenz für die Umsetzungsvorbereitung

Zuerst die Verträge für Identität, Abdeckung, Fehler und Snapshotfrische klären. Danach die gemeinsamen Komponenten und die sichtbare Führung verbessern. Kennzahlen, Scope, Vergleichszeiten und Volltextzugang lassen sich teilweise früher korrigieren, ohne auf einen großen Navigationsumbau zu warten.

Ein vollständiger Neuaufbau ist aus diesem Audit nicht begründet. Die meisten benötigten Bausteine existieren. Die konsolidierte Maßnahmenliste ordnet die Arbeit nach Entscheidungsrisiko, Reichweite und Abhängigkeiten und beschreibt prüfbare Abnahmekriterien. Fachliche Detailentscheidungen und offene historische Belege sind dort ausdrücklich markiert.

**Unverändert:** Blind-Audit, Anwendungscode und vorhandene Projekttests. Die beiden neuen Berichte liegen außerhalb des Projekts.
