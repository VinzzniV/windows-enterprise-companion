# Blinder UX- und Frontend-Audit: Windows Enterprise Companion

**Prüfdatum:** 8. September 2026  
**Perspektive:** erfahrener Windows-Administrator ohne Produktkenntnis.

Die Anwendung wurde ausschließlich anhand ihrer sichtbaren Oberfläche mit Computer Use geprüft. Es wurden keine Projektdateien oder Quelltexte untersucht. Ausgeführt wurden Navigation, Suche, Filter, ein Vergleich gespeicherter Clientdaten sowie ausdrücklich als lesend gekennzeichnete Management- und Ereignisabfragen. Speicher-, Lösch-, Installations-, Update- und Remediation-Aktionen wurden nicht ausgelöst; Zugangsdaten wurden nicht eingegeben.

Geprüft wurden ein maximiertes und ein kleineres Fenster. Eine unveränderte Ersteinrichtung ohne vorhandene Daten war nicht verfügbar. Aussagen über technische Ursachen oder nicht durchlaufene Bestätigungsdialoge sind daher nicht Bestandteil dieses Berichts.

## 1. Kurzfazit

Die Anwendung vermittelt den Eindruck eines umfangreichen Administrationswerkzeugs mit brauchbaren Suchmöglichkeiten, vielen Informationsquellen und überwiegend nachvollziehbarer Fachnavigation. Ein neuer Administrator erkennt den grundsätzlichen Zweck: Geräte, Verzeichnisinformationen, Sicherheitsbefunde und Managementsysteme gemeinsam untersuchen.

**Der erste sinnvolle Arbeitsschritt ist nur teilweise erkennbar.** Das Dashboard empfiehlt allgemein, ein Modul zu öffnen. Es beantwortet jedoch nicht unmittelbar, ob zuerst eine Verbindung geprüft, ein Client gesucht oder ein dringender Befund untersucht werden sollte.

Positiv fallen die globale Tastatursuche, der verständliche Ablauf des Clientvergleichs, zahlreiche Herkunfts- und Zeitangaben sowie ausdrücklich lesende Ansichten auf. Besonders gut erklärt die Ereignisabfrage, dass sie aktuelle Daten liest und Ergebnisse nicht speichert.

Die drei größten UX-Probleme sind:

1. **Unvollständige Daten können wie ein abschließendes Ergebnis wirken.** Das Action Center zeigte zunächst „0 Critical“. Erst die aufgeklappte Quellenabdeckung erklärte, dass Nessus nicht berücksichtigt war. Nach einer lesenden Aktualisierung erschienen 120 kritische Einträge.
2. **Wichtige Rückmeldungen und Arbeitsinhalte liegen außerhalb des sichtbaren Bereichs.** „Review evidence“ öffnete Details unterhalb von 25 Tabellenzeilen, ohne dorthin zu führen. Im kleineren Fenster war zunächst kein einziger Client sichtbar.
3. **Statuswerte und Kennzahlen haben wechselnde Bedeutungen.** Verfügbarkeit einer Quelle, Vorhandensein eines Geräts, Aktualität und fachlicher Zustand werden mit ähnlichen Farben und Begriffen dargestellt. Dazu kommen unerklärte Zahlenunterschiede und ein Vergleichsergebnis, das identische sichtbare GPU-Bezeichnungen als unterschiedlich markiert.

Kein P0-Befund wurde beobachtet. Die wichtigsten Probleme betreffen die Verlässlichkeit der Entscheidungsgrundlage und vermeidbare Sucharbeit.

## 2. Aufgabenprotokoll

Die Angaben zu Umwegen beziehen sich auf tatsächlich beobachtete zusätzliche Navigation oder Sucharbeit. Werkzeugbedingte Fokusprobleme werden nicht als Anwendungsfehler gewertet.

| Aufgabe | Erwartung und gewählter Weg | Tatsächliches Ergebnis | Zögern, Umwege und nächster Schritt | Wahrgenommene Sicherheit |
|---|---|---|---|---|
| Zweck und Einstieg verstehen | Dashboard lesen, Navigation erkunden; Überblick und Einstieg erwarten | **Teilweise erfolgreich.** Breiter Funktionsumfang erkennbar, aber keine klare Arbeitspriorität | Mehrere Karten müssen gelesen werden. Nächster Schritt abhängig vom eigenen Vorwissen | Navigation unbedenklich; allgemeiner Hinweis auf neue Scans erklärt deren Wirkung noch nicht |
| Verfügbare Bereiche finden | Hauptnavigation öffnen und Module ansehen | **Erfolgreich.** Fachbereiche und Administration auffindbar | Im kleinen Fenster zusätzlicher Menüklick; Settings und Error log liegen weiter unten im scrollbaren Menü | Reine Navigation |
| Bestimmten Client finden | Clients → Filter → im Dashboard genannter PK-CLNB126 → Treffer | **Erfolgreich.** Filter reduziert unmittelbar auf einen Client | Kein Fehlversuch. Beim späteren Rückweg über „Clients“ war der Filter zurückgesetzt | Filter und Öffnen nachvollziehbar lesend |
| Clientzustand beurteilen | Overview, Managementquellen und Security ansehen; Gesamtzustand erwarten | **Teilweise erfolgreich.** Inventar 61 Tage alt, Security 66 Tage alt, Health fehlt; Quellenstatus zusätzlich verfügbar | Mehrere Ebenen und verschiedene Statusbegriffe müssen zusammengeführt werden | Gespeicherte Ansicht und „Load management sources“ ausdrücklich lesend |
| Managementzuordnung verstehen | Quellen laden → „Management details and device relationships“ → Map/List | **Erfolgreich, mit Reibung.** AD-, Kaspersky-, opsi- und Nessus-Beziehungen sichtbar | Zwei zusätzliche Öffnungsschritte plus Scrollen; große Darstellung für wenige Beziehungen | Lesender Charakter erklärt |
| Zwei Clients vergleichen | Clients → Compare → A und B wählen → Compare | **Erfolgreich für Hardware und Software; teilweise für Security.** Fehlender Security-Scan bei Client B wird erklärt | Sechs Klicks ab Clientliste, ohne Fehlweg. Erfassungszeiten fehlen im Ergebnis; GPU-Markierung irritiert | Sehr klar: gespeicherte Daten, kein neuer Scan |
| Globale Suche bedienen | Ctrl+K → Clientname → Enter | **Erfolgreich.** Ein eindeutiger Treffer öffnet den Client | Kein unnötiger Schritt; gute Tastaturbedienung | Ergebnis als gespeicherte Inventar-/Security-Daten beschrieben |
| Externe Systeme konfigurieren können | Settings öffnen; Kaspersky, opsi und Nessus suchen | **Erfolgreich hinsichtlich Auffindbarkeit.** Konfigurationsfelder und gespeicherte Konten/Schlüssel erkennbar | Kaspersky zunächst hinter „Environment Health“ suchen; mehrere Abschnittswechsel und Scrollen | Speichern und Entfernen erkennbar; nicht ausgeführt |
| Einrichtung und Verfügbarkeit unterscheiden | Settings mit Quellenanzeigen und Fachmodulen vergleichen | **Teilweise erfolgreich.** opsi zeigt Verbindung; Kaspersky gespeichertes Konto und verfügbare Quelle; Nessus gespeicherte Schlüssel | Nessus erschien je nach Ansicht und Aktualisierungsstand als fehlgeschlagen, verfügbar oder erfolgreich synchronisiert | Kein Anmeldeversuch; nur sichtbare Zustände und erlaubte Leseabfragen |
| Dringenden Handlungsbedarf finden | Action Center → Quellenabdeckung → Refresh sources | **Nach Aktualisierung erfolgreich.** Kritische Einträge und nächste Untersuchungsschritte sichtbar | Zwei zusätzliche Schritte, um die zunächst unvollständige Zusammenfassung richtig einzuordnen | Read-only-Kennzeichnung deutlich |
| Bereinigungskandidat prüfen | Device Cleanup → erstes „Review evidence“ | **Erfolgreich mit hoher Reibung.** Belege unterhalb der Tabelle gefunden | Zunächst keine sichtbare Reaktion; zusätzliche Kontrolle, Sprung ans Seitenende und Rückscrollen nötig | Gute Erklärung: Bewertung, keine Löschung; Entscheidungen nicht ausgeführt |
| Schwachstellen nachvollziehen | Vulnerabilities → Findings → erster kritischer Befund | **Erfolgreich.** Detailbereich mit Beschreibung, Lösung und betroffenen Assets | Kein Fehlweg; seitliche Details deutlich sichtbar | Fachbereich ausdrücklich read-only |
| Anwendungsfehler und technische Details finden | Error log → Zeile anklicken → auf Fehler filtern | **Teilweise erfolgreich.** Meldungen auffindbar, Filter funktioniert auch per Tastatur | Zeilenklick öffnet keine sichtbaren Details; viele Wiederholungen und abgeschnittene Texte | „Hide previous entries“ nicht betätigt |
| Client-Ereignisse lesen | Client → Event logs → „System errors (last 24h)“ → Run query | **Teilweise erfolgreich.** 11 Ereignisse angezeigt | Vollständige Meldung durch Zeilenklick und anschließendes Warten nicht erreichbar | Vorbildlich erklärt: Live-Abfrage, Ergebnisse werden nicht gespeichert |
| Weitere Bereiche einordnen | Active Directory, Users, Patch Management, Print Management und Report export öffnen | **Teilweise erfolgreich.** Zwecke überwiegend erklärt | Patch-Kennzahl „1608 Outdated clients“ bei „Clients (475)“ irritiert; globaler Bericht betrifft „This machine“ | Schreibende und unklare Aktionen nicht ausgeführt |
| Empty States und Scanwirkung prüfen | Health und Network Scan öffnen | **Teilweise erfolgreich.** Nächster Button sichtbar; Health erklärt Prüfumfang | Kein Health-Ergebnis vorhanden. Netzwerkscan erläutert Reichweite und Auswirkungen nur begrenzt | Health speichert laut Oberfläche Ergebnisse; Netzwerkscan nicht gestartet |
| Kleineres Fenster bedienen | Fenster verkleinern → Menü → Clients → scrollen | **Teilweise erfolgreich.** Navigation funktioniert, Tabellen bleiben erreichbar | Kein Client im ersten sichtbaren Ausschnitt; zusätzliche Scrollarbeit und starke Textumbrüche | Nur Fensterdarstellung geändert; anschließend wieder maximiert |

## 3. Priorisierte Befunde

### F01 · P1 · Verständlichkeit — Nullwerte trotz fehlender Quelle

**Bereich:** Action Center und Clients.

**Ausgangssituation:** Nessus war in der zentralen Bewertung nicht verfügbar.  
**Erwartung:** Die Zusammenfassung kennzeichnet das Ergebnis unmittelbar als unvollständig.  
**Beobachtung:** „Critical 0“ stand ohne gleichwertig sichtbaren Vollständigkeitshinweis oben. Die Erklärung lag unter „Source coverage“. In Clients wurden bei ausgefallener Nessus-Quelle ebenfalls grüne Nullwerte für Nessus-Kategorien angezeigt. Nach „Refresh sources“ enthielt das Action Center 120 kritische Einträge.  
**Auswirkung:** Ein Administrator kann fehlende Erkenntnisse als Abwesenheit kritischer Probleme interpretieren.  
**Empfehlung:** Betroffene Kennzahlen als „Nicht bewertbar“ darstellen oder ausdrücklich „0 bekannte — Quelle fehlt“ nennen. Fehlende Quellen und Zeitpunkt der Bewertung direkt neben der Zusammenfassung anzeigen.  
**Sicherheit:** hoch.

### F02 · P1 · Verständlichkeit — Quellenstatus wirkt aktueller, als er nachvollziehbar ist

**Bereich:** Client Overview → Management Systems; Vulnerabilities; Action Center.

**Ausgangssituation:** Zwischen mehreren Ansichten desselben Managementsystems wechseln.  
**Erwartung:** Erkennbar ist, wann und in welchem Umfang der jeweilige Status ermittelt wurde.  
**Beobachtung:** Vulnerabilities zeigte eine erfolgreiche Nessus-Synchronisierung. Das aktualisierte Action Center zeigte Nessus als verfügbar. Beim erneuten Öffnen des zuvor besuchten Clients stand weiterhin „Nessus Failed / Source unavailable“, ohne direkt sichtbaren Prüfzeitpunkt in dieser Statuszeile.  
**Auswirkung:** Der Benutzer weiß nicht, ob ein aktueller Fehler, ein älterer Zustand oder eine abweichende Datenbasis gemeint ist.  
**Empfehlung:** Jede Quellenanzeige mit „zuletzt geprüft“, Datenstand und Geltungsbereich versehen. Einen klar zugeordneten lesenden Aktualisierungsweg anbieten.  
**Sicherheit:** hoch für die beobachtete Abweichung; ihre Ursache bleibt offen.

### F03 · P1 · Hohe Reibung — Belegaufruf liefert keine sichtbare Rückmeldung

**Bereich:** Device Cleanup → Review evidence.

**Ausgangssituation:** Ersten Kandidaten in einer Tabelle mit 25 sichtbaren Seiteneinträgen prüfen.  
**Erwartung:** Die Belege erscheinen beim ausgewählten Gerät oder der sichtbare Ausschnitt wechselt zu ihnen.  
**Beobachtung:** Der obere Ausschnitt blieb unverändert. Der Detailbereich war erst unter der gesamten Tabelle zu finden.  
**Auswirkung:** Die Aktion wirkt erfolglos; wiederholtes Klicken oder Abbruch sind naheliegend.  
**Empfehlung:** Details unmittelbar bei der Auswahl oder in einem seitlichen Bereich öffnen. Alternativ gezielt zum Detailbereich führen und dort den Fokus setzen.  
**Sicherheit:** hoch.

### F04 · P1 · Visuelle Qualität — Verwaltungsrahmen verdrängt die eigentliche Clientliste

**Bereich:** Clients, besonders im kleineren Fenster.

**Ausgangssituation:** Einen Client finden oder mehrere Geräte überblicken.  
**Erwartung:** Suche und erste Ergebnisse sind schnell gemeinsam sichtbar.  
**Beobachtung:** Kennzahlen, Filter und „Bulk Scan Workbench“ nehmen nahezu den gesamten oberen Bereich ein. Im kleinen Fenster war zunächst kein Client sichtbar. Tabellenzeilen werden durch viele Metadaten sehr hoch; „Cleanup candidate“ brach bis auf einen einzelnen Buchstaben um.  
**Auswirkung:** Weniger Geräte sind vergleichbar; Such- und Scrollaufwand steigen.  
**Empfehlung:** Kennzahlen kompakter darstellen, Stapelaktionen erst nach einer Auswahl ausklappen und Metadaten pro Gerät begrenzen. Statusbeschreibungen bei geringer Breite unter dem Gerätenamen sinnvoll umbrechen.  
**Sicherheit:** hoch.

### F05 · P2 · Blocker — Vollständige Fehlermeldung im getesteten Weg nicht erreichbar

**Bereich:** Error log und Client → Event logs.

**Ausgangssituation:** Eine abgeschnittene Meldung vollständig lesen.  
**Erwartung:** Zeilenauswahl oder ein erkennbarer Detailaufruf zeigt den vollständigen Inhalt.  
**Beobachtung:** Texte enden mit Auslassungspunkten. Die geprüften Zeilenklicks öffneten keine Details; bei der Ereignismeldung erschien anschließend auch kein ergänzender Hinweis.  
**Auswirkung:** Die konkrete Teilaufgabe, die vollständige Fehlermeldung zu lesen, blieb über den getesteten Weg ungelöst.  
**Empfehlung:** Expliziten Detailaufruf mit umbrochenem Volltext anbieten. Zeitpunkt, Quelle, betroffenen Client und gegebenenfalls eine verständliche Zusammenfassung gemeinsam zeigen.  
**Sicherheit:** hoch für den getesteten Weg; eine andere, nicht entdeckte Bedienmöglichkeit ist nicht ausgeschlossen.

### F06 · P2 · Konsistenz — Identische sichtbare GPU-Werte werden als unterschiedlich markiert

**Bereich:** Compare clients → Hardware inventory.

**Ausgangssituation:** PK-CLNB126 und PK-CLNB135 vergleichen.  
**Erwartung:** „differs“ ist durch einen sichtbaren Unterschied nachvollziehbar.  
**Beobachtung:** Beide GPU-Zellen zeigten „AMD Radeon(TM) Graphics“, daneben stand „differs“.  
**Auswirkung:** Das Vertrauen in weitere Vergleichsergebnisse sinkt.  
**Empfehlung:** Den tatsächlich verglichenen Unterschied sichtbar machen oder die Markierung an die dargestellten Werte anpassen.  
**Sicherheit:** hoch.

### F07 · P2 · Verständlichkeit — Erfassungszeiten verschwinden aus dem Vergleichsergebnis

**Bereich:** Compare clients.

**Ausgangssituation:** Zwei verfügbare Inventarstände auswählen.  
**Erwartung:** Das Ergebnis zeigt weiterhin, aus welchen Zeitpunkten die Daten stammen.  
**Beobachtung:** Die Auswahlliste nannte Zeitstempel; die Ergebnisspalten zeigten sie nicht. Beim geprüften Paar lagen die Inventarstände im Juli beziehungsweise September.  
**Auswirkung:** Unterschiede können als gegenwärtiger Zustand beider Clients gelesen werden.  
**Empfehlung:** Pro Client und Datenkategorie Erfassungsdatum und Alter im Ergebnis fest anzeigen. Große zeitliche Abstände ausdrücklich kennzeichnen.  
**Sicherheit:** hoch.

### F08 · P2 · Informationsarchitektur — Kaspersky ist unter einem unerwarteten Namen einsortiert

**Bereich:** Settings.

**Ausgangssituation:** Die Einrichtung von Kaspersky finden.  
**Erwartung:** Herstellername oder ein gemeinsamer Bereich „Integrationen“ führt zur Konfiguration.  
**Beobachtung:** Kaspersky liegt unter „Environment Health“ beziehungsweise „IT Lifecycle“. Nessus und opsi sind über andere fachliche Begriffe organisiert.  
**Auswirkung:** Neue Benutzer müssen die interne Begriffswelt erst lernen.  
**Empfehlung:** Einen Bereich „Integrationen“ mit direkt benannten Abschnitten für Kaspersky, opsi und Nessus anbieten. Verbindung, Zugangsdatenstatus und fachliche Optionen dort jeweils gruppieren.  
**Sicherheit:** hoch.

### F09 · P2 · Verständlichkeit — Kennzahlen besitzen keinen ausreichend sichtbaren Bezugsrahmen

**Bereich:** Dashboard, Clients, Compare clients und Patch Management.

**Ausgangssituation:** Umfang der verwalteten Umgebung einschätzen.  
**Erwartung:** Geräte-, Inventar- und Paketanzahlen sind eindeutig benannt.  
**Beobachtung:** Dashboard: „41 hosts“; Clients: 686 Geräte; Vergleichsauswahl: 508 Treffer; Patch Management: „Clients (475)“ und gleichzeitig „1608 Outdated clients“. Die unterschiedlichen Mengen sind an diesen Stellen nicht unmittelbar erklärt.  
**Auswirkung:** Reichweite und Bedeutung von Problemen lassen sich schwer beurteilen.  
**Empfehlung:** Einheiten und Grundgesamtheit direkt nennen, beispielsweise „41 Geräte mit gespeichertem WEC-Inventar“. Für „Outdated clients“ klarstellen, ob eindeutige Geräte oder mehrere Geräte-Paket-Zuordnungen gezählt werden.  
**Sicherheit:** hoch für die dargestellten Zahlen; keine Aussage über deren rechnerische Richtigkeit.

### F10 · P2 · Verständlichkeit — Aktionswirkungen werden unterschiedlich gut erklärt

**Bereich:** Clientaktionen, Network Scan und Print Management.

**Ausgangssituation:** Entscheiden, welche Aktion im laufenden Betrieb gefahrlos ist.  
**Erwartung:** Ziel, Lesezugriff, Speicherung und mögliche Änderungen sind vor dem Auslösen erkennbar.  
**Beobachtung:** Event logs erklärt Live-Zugriff und fehlende Speicherung sehr gut. „Save client“ erklärt dagegen nicht, was zusätzlich gespeichert wird. „Re-run scan“ nennt in der geprüften Security-Ansicht keinen näheren Umfang. Der Netzwerkscan zeigt einen vorausgefüllten Bereich mit aktivierter Portprüfung, aber keine Umfangs- oder Wirkungsvorschau.  
**Auswirkung:** Vorsichtige Benutzer brechen ab oder müssen Annahmen treffen. Diese Aktionen wurden im Audit nicht ausgeführt.  
**Empfehlung:** Ein einheitliches Wirkungsschema verwenden: Ziel, „nur lesen“, „speichert lokal“ oder „ändert extern“, erwarteter Umfang und verwendetes Konto.  
**Sicherheit:** hoch für die sichtbare Informationslücke; tatsächliche Auswirkungen wurden nicht geprüft.

### F11 · P2 · Informationsarchitektur — Globale und lokale Perspektive wechseln ohne klare Vorankündigung

**Bereich:** Dashboard und globaler Report export.

**Ausgangssituation:** Aus einer Flottenübersicht einen Bericht öffnen.  
**Erwartung:** Der Geltungsbereich ist bereits am Einstieg klar.  
**Beobachtung:** Das Dashboard kombiniert Flotteninformationen mit Security-Befunden eines einzelnen Clients. „Report export“ steht in der globalen Navigation, zeigt anschließend jedoch „This machine“.  
**Auswirkung:** Ein Benutzer kann einen Gesamtbericht erwarten und erst nach dem Öffnen die lokale Begrenzung erkennen.  
**Empfehlung:** Karten und Navigation mit „Gesamte Umgebung“ beziehungsweise „Dieser Computer“ kennzeichnen. Einen lokalen Bericht entsprechend benennen oder eine sichtbare Zielauswahl anbieten.  
**Sicherheit:** hoch.

### F12 · P2 · Visuelle Qualität — Trenddiagramm lässt Größenordnung und Zeitpunkte offen

**Bereich:** Vulnerabilities → Overview.

**Ausgangssituation:** Den angezeigten 30-Tage-Trend beurteilen.  
**Erwartung:** Zeitachse, Wertebereich und gezählte Einheit sind erkennbar.  
**Beobachtung:** Vier farbige Linien und eine Legende waren sichtbar, aber keine beschrifteten Achsen oder Skalen. „Worse“ erklärt die Bewertungsgrundlage nicht.  
**Auswirkung:** Die Richtung ist grob erkennbar, konkrete Veränderungen lassen sich nicht belastbar ablesen.  
**Empfehlung:** Datumsmarken, Werteskala und Einheit ergänzen; „Worse“ durch eine nachvollziehbare Veränderung mit Vergleichszeitraum erläutern.  
**Sicherheit:** hoch für die Standardansicht; mögliche nicht entdeckte Interaktionen wurden nicht bewertet.

### F13 · P2 · Konsistenz — Filter und Rückwege folgen unterschiedlichen Regeln

**Bereich:** Clients, Action Center, Device Cleanup und Users.

**Ausgangssituation:** Zwischen ähnlichen Such- und Filteraufgaben wechseln.  
**Erwartung:** Vergleichbare Eingaben werden gleich angewendet und bleiben beim Rückweg möglichst erhalten.  
**Beobachtung:** Clients filtert sofort. Andere Bereiche verlangen „Apply filters“ beziehungsweise „Apply search“. Der Rückweg vom Clientdetail über „Clients“ setzte den zuvor verwendeten Filter zurück.  
**Auswirkung:** Benutzer warten auf Ergebnisse oder müssen Eingaben wiederholen.  
**Empfehlung:** Filterverhalten vereinheitlichen und Suchzustand beim Rückweg zur Liste erhalten. Falls explizites Anwenden nötig ist, ausstehende Änderungen deutlich anzeigen.  
**Sicherheit:** hoch.

### F14 · P3 · Verbesserung — Wiederholungen und Fachsprache erhöhen die Leselast

**Bereich:** Client Overview, Beziehungen, Error log und Settings.

**Ausgangssituation:** Wesentliche Informationen schnell erfassen.  
**Erwartung:** Kernzustand zuerst, ergänzende Erläuterungen bei Bedarf.  
**Beobachtung:** Client Overview wiederholt Hinweise auf gespeicherte Daten und bietet mehrere Wege zu denselben Details. Die Beziehungsansicht braucht viel Platz für wenige Quellen. Das Fehlerprotokoll zeigt zahlreiche gleichartige technische Meldungen. Begriffe wie „posture“, „evidence“, „canonical“ und „read contract“ erhöhen die Einstiegshürde.  
**Auswirkung:** Relevante Befunde konkurrieren mit erklärendem und wiederholtem Text.  
**Empfehlung:** Einen kompakten Datenhinweis pro Ansicht verwenden, gleiche Fehler gruppieren und Begriffe näher an der Arbeitsaufgabe formulieren.  
**Sicherheit:** mittel, da die empfundene Belastung durch weitere Benutzertests überprüft werden sollte.

## 4. Zusammenführen, entfernen oder umbenennen

### Zusammenführen

- Quellenverfügbarkeit, Prüfzeitpunkt und Einrichtungsstatus in einer gemeinsamen Integrationsübersicht bündeln.
- Im Clientdetail die gespeicherte Datenübersicht und die darunter wiederholten Zusammenfassungen enger zusammenführen.
- Wiederholte Anwendungsfehler nach Meldung gruppieren, mit Anzahl sowie erstem und letztem Auftreten.
- Dashboard und Action Center enger verbinden: Vom Überblick direkt zu priorisierten, ausreichend belegten Aufgaben führen.

### Redundant oder entbehrlich wirkende Elemente

- Die dauerhaft große Bulk Scan Workbench vor einer Clientauswahl.
- Mehrfach wiederholte Hinweise, dass gespeicherte Daten beim Öffnen nicht neu gescannt werden.
- „Configuration policy“ mit Aussagen darüber, wo zukünftige Module ihre Einstellungen ablegen sollen; das hilft bei der aktuellen Bedienung kaum.
- Die gleichzeitige Darstellung vieler Einzelstatus-Karten ohne klare Gewichtung.

### Begriffe und Beschriftungen ändern

| Aktuell | Verständlichere Richtung |
|---|---|
| Fleet posture | Gerätezustand |
| Assessed devices | Bewertete Geräte |
| Open evidence | Offene Befunde |
| Device Cleanup | Bereinigungskandidaten prüfen |
| Environment Health in Settings | Kaspersky |
| Save client | Tatsächlichen Speicherzweck nennen, beispielsweise „Als Ziel speichern“, falls dies die Wirkung ist |
| Report export | Geltungsbereich ergänzen, etwa „Bericht für diesen Computer“ |
| Available / Connected | „Quelle erreichbar“, „Daten vorhanden“ oder „Gerät zugeordnet“ entsprechend der tatsächlichen Aussage |

### Früher oder deutlicher sichtbar machen

- Fehlende Quellen direkt neben Gesamtzahlen.
- Alter und Umfang jeder Bewertung.
- Unterschied zwischen Quellenverbindung und Gerätezustand.
- Umfang und Speicherwirkung einer Aktion.
- Zielgerät und verwendetes Konto.
- Dass ein Vergleich zeitlich unterschiedliche Datenstände verwendet.

## 5. Visuelle Verbesserungen

| Thema | Konkrete Änderung und Nutzen |
|---|---|
| **Hierarchie** | Im Dashboard zuerst Vollständigkeit und dringenden Handlungsbedarf zeigen. Reine Verbindungsinformationen kleiner gewichten. |
| **Gruppierung** | Quellenzustand, Datenalter und fachlichen Befund getrennt anordnen. Dadurch ist klar, welche Frage ein Status beantwortet. |
| **Abstände** | Oberen Bereich von Clients verdichten. Weniger Innenabstand bei Kennzahlen; mehr Platz für erste Ergebnisse. |
| **Typografie** | Kleine graue Metadaten und Quellenhinweise größer und kontrastreicher darstellen. Technische Pfade nur dort dominant zeigen, wo sie gebraucht werden. |
| **Farben** | Grün für eine eindeutige positive Aussage verwenden. „Quelle verfügbar“ darf „Gerät nicht gefunden“ optisch nicht überstrahlen. Bedeutung immer zusätzlich beschriften. |
| **Tabellen** | Lange Meldungen aufklappbar machen, Detailzeilen reduzieren und Spaltenüberschriften beim Scrollen sichtbar halten. Im kleinen Fenster wichtige Inhalte priorisieren. |
| **Formulare** | Dauerhafte Feldbeschriftungen auch für Benutzername, Domain und Passwort verwenden. Verbindungseinstellungen und Zugangsdaten jeweils zusammenhalten. |
| **Buttons** | Hauptaktion nach Aufgabe gewichten. Seltene Speicher- oder Entfernungsaktionen zurückhaltender platzieren; lesende und schreibende Wirkungen textlich unterscheiden. |
| **Statusdarstellung** | Ein kompaktes Muster verwenden: „Daten vorhanden · 61 Tage alt · unvollständig“. Unbekannte Werte nicht als Null darstellen. |
| **Responsives Verhalten** | Bei geringerer Breite Kennzahlen und Stapelaktionen einklappen. Lange Statuswörter nicht in einzelne Buchstaben zerlegen. Details dort öffnen, wo die Auswahl erfolgte. |

## 6. Empfohlene Reihenfolge

### Sofortmaßnahmen mit geringem Aufwand und hoher Wirkung

1. Nullwerte bei fehlenden Quellen als unvollständig oder nicht bewertbar kennzeichnen.
2. Fehlende Quellen unmittelbar an der Zusammenfassung anzeigen.
3. Nach „Review evidence“ sichtbar zum Ergebnis führen.
4. Erfassungszeiten im Vergleichsergebnis ergänzen.
5. Den GPU-Widerspruch für Benutzer nachvollziehbar auflösen.
6. Kennzahlen mit eindeutigen Einheiten und Bezugsgrößen beschriften.
7. Vollständige Fehlertexte über einen klaren Detailaufruf zugänglich machen.

### Mittlere strukturelle Verbesserungen

1. Quellenstatus, Aktualität und Gerätezustand als getrennte Informationsarten darstellen.
2. Clientübersicht verdichten und Stapelaktionen an die Auswahl koppeln.
3. Filterverhalten und Rücknavigation vereinheitlichen.
4. Wirkungshinweise für alle Abfragen und Aktionen nach demselben Muster gestalten.
5. Wiederholte Fehler bündeln und nach verständlichen Bereichen filterbar machen.

### Größere Änderungen an Navigation oder Informationsarchitektur

1. Gemeinsamen Bereich „Integrationen“ für Kaspersky, opsi und Nessus schaffen.
2. Globale Umgebung und einzelnen Computer sichtbar voneinander abgrenzen.
3. Dashboard als Einstieg zu priorisierten Aufgaben gestalten.
4. Client Overview auf eine kompakte Zusammenfassung mit gezielten Vertiefungen reduzieren.

### Durch weitere Benutzertests bestätigen

- Ob Administratoren bevorzugt über Clientnamen oder über zentrale Befunde einsteigen.
- Ob die Beziehungsdarstellung als Karte einen Mehrwert gegenüber einer kompakten Liste bietet.
- Ob „Device Cleanup“ eine unerwünschte Erwartung tatsächlicher Löschfunktionen erzeugt.
- Welche Statusbegriffe ohne zusätzliche Erklärung verstanden werden.
- Wie eine echte Ersteinrichtung ohne gespeicherte Konten und Daten funktioniert.
- Ob höhere Zoomstufen weitere Probleme bei Tabellen, Dialogen oder Navigation erzeugen.

Bestätigungsdialoge und Rückmeldungen schreibender Aktionen bleiben unbewertet, da diese Aktionen entsprechend dem Auditauftrag nicht ausgeführt wurden.