# Projektstruktur (Einfach erklärt)

Diese Datei beschreibt den Aufbau des Repositories so, dass man schnell versteht, wie die Anwendung funktioniert – auch ohne tiefes Architekturwissen.

## Überblick

Das Projekt ist ein Windows-Desktop-Programm für Administratoren.

- **Frontend**: Das sichtbare Fenster (React/TypeScript), also Buttons, Tabellen, Formulare.
- **Backend**: Die eigentliche Logik im gleichen Programm, z. B. Daten holen, Remote-Checks starten, Daten speichern.
- **Datenbank**: Lokales SQLite für Ergebnisse, Einstellungen, Verlauf.
- **Gemeinsamer „Vertrag“**: Backend und Frontend sprechen über dieselben Typen (verbindet die Teile sicher miteinander).

Ein wichtiges Designprinzip:
- Das Programm läuft als **einzelner Prozess** (ein Fenster + Logik in einem App-Prozess).
- Es gibt **keinen separaten Webserver** und keine offenen Netzwerkports zwischen Frontend und Backend.

## Architektur

Man kann es in fünf Blöcke einteilen:

1. `Wec.Host` (Host / Zusammensetzung)
   - Startet die App.
   - Öffnet das WebView2-Fenster.
   - Richtet „Dependency Injection“ (DI) ein.
   - Registriert alle Module und Handler.

2. `Wec.Core`
   - Enthält nur gemeinsame Verträge (kleine Regeln/Klassen), z. B. wie eine Anfrage oder Antwort aussieht.
   - Hat keine direkte Fachlogik.

3. `Wec.Infrastructure`
   - Technikschicht:
     - Datenbankzugriff
     - Logging
     - Windows/WMI/Registry/LDAP/PowerShell/SNMP-Aufrufe
     - Passwort- und Rechte-Management

4. `Modules`
   - Jedes Feature sitzt in einem eigenen Modul-Projekt.
   - Beispiele: Inventory, Security, Diagnostics, PatchManagement, PrintManagement …

5. `frontend`
   - SPA (Single Page Application) mit React.
   - Holt Daten nur über die Bridge und zeigt sie an.

Zusätzlich:
- `tools/Wec.ContractGenerator` erzeugt die TypeScript-Typen für das Frontend automatisch aus den Backend-Verträgen.

## Struktur

Top-Level:

- `.github/workflows/ci.yml` → CI-Pipeline
- `Directory.Build.props` → Build- und Qualitätsregeln
- `global.json` → SDK-Version
- `docs/` → ADRs, Architekturentscheidungen, Projektplanung
- `src/Wec.Host` → Host- und Bridge-Logik
- `src/Wec.Core` → gemeinsame Schnittstellen/DTOs
- `src/Wec.Infrastructure` → externe Integrationen + Datenbank
- `src/Modules/` → alle Business-Module
- `frontend/` → React-Anwendung
- `tests/` → automatisierte Tests
- `tools/Wec.ContractGenerator` → Typen-Generator

Modulstruktur (typisch pro Modul):

- `<Modul>.cs` → Modul registriert seine Dienste
- `Handlers/` → Aktionen, die von der UI aufgerufen werden können
- `Application/`, `Domain/`, `Persistence/` je nach Bedarf
- `README.md` im Modul für Feature-Details

## Feature Map (welcher Teil macht was)

| UI-Bereich | Modul | Kurzbeschreibung |
|---|---|---|
| Clients (`frontend/src/features/clients`) | `Wec.Modules.Inventory`, `Security`, `Diagnostics`, `PrintManagement`, `EmployeeLifecycle` | Zielsysteme (Clients) scannen, vergleichen, Ergebnisse anzeigen |
| Users (`frontend/src/features/users`) | `Wec.Modules.UserManagement` über `IDirectoryUserReadProvider` aus `Wec.Modules.ActiveDirectory` | AD-autoritative Benutzersuche und read-only User 360 |
| Action Center (`frontend/src/features/actioncenter`) | `Wec.Modules.ActionCenter` über konkrete Hygiene-, Inventory- und Security-Core-Projektionen | Berechnete read-only Arbeitsliste ohne Workflow-Persistenz |
| Device Cleanup (`frontend/src/features/devicecleanup`) | `Wec.Modules.DeviceCleanup` über konkrete Hygiene- und Inventory-Core-Projektionen | Geführte read-only Altgerätebewertung mit session-only Entscheidung und Export |
| Dashboard (`frontend/src/features/dashboard`) | `Wec.Modules.ActiveDirectory` | Gesamtübersicht und AD-Anbindung |
| Active Directory (`frontend/src/features/activedirectory`) | `Wec.Modules.ActiveDirectory` | Domain-/Computer-/Benutzerabfragen |
| Inventory (`frontend/src/features/inventory`) | `Wec.Modules.Inventory` | Hardware-, Software- und Statusdaten |
| Security (`frontend/src/features/security`) | `Wec.Modules.Security` | 13 Check-Arten mit Verlauf |
| Health (`frontend/src/features/diagnostics`) | `Wec.Modules.Diagnostics` | Vier gezielte Prüfungen für Update-Alter, konfigurierte Dienste, Event-Log-Zusammenfassung und freien Speicherplatz; interne Diagnostics-Namen bleiben kompatibilitätsbedingt bestehen |
| Patch Management (`frontend/src/features/patchmanagement`) | `Wec.Modules.PatchManagement` | opsi-Integration inkl. Freigabe-/Durchführungsfluss |
| Print Management (`frontend/src/features/printmanagement`) | `Wec.Modules.PrintManagement` | Drucker/ Geräte erfassen, vergleichen, verwalten |
| Network Scan (`frontend/src/features/networkscan`) | `Wec.Modules.NetworkScan` | Scan per Nmap, Reverse-DNS/DHCP-Anreicherung |
| Vulnerabilities (`frontend/src/features/vulnerabilities`) | `Wec.Modules.VulnerabilityManagement` | Nessus-Import und Befunde |
| Reporting (`frontend/src/features/reporting`) | `Wec.Modules.Reporting` | Berichte aus bereits vorhandenen Daten |
| Verwaltung (`frontend/src/features/verwaltung`) | `Wec.Modules.Targets` | Einstellungen, Log-Lesen, gespeicherte Ziele |

## Datenfluss

### Standardablauf (ein Klick in der UI)

1. UI ruft `invoke(module, action, payload)` auf.
2. Nachricht geht via WebView2-Bridge an den Host.
3. `ActionDispatcher` wählt den richtigen Handler aus.
4. Handler arbeitet:
   - liest/schreibt DB
   - ruft Windows/Netzwerk-Dienste auf
   - führt Regeln aus
5. Ergebnis wird als standardisierte Antwort zurückgeschickt.
6. UI zeigt Erfolg/Fehler/Status an.

### Beispiel: Leseflow (Inventar)

- UI fragt Inventar für einen Host an.
- Handler prüft, ob Cache vorhanden ist.
- Bei Bedarf wird neu gemessen (WMI etc.).
- Ergebnis wird gespeichert und sofort zurückgegeben.

### Beispiel: Schreib-Flow (kritische Aktion, z. B. Patch)

- User bestätigt gezielt die Aktion.
- Handler prüft Rechte, Vorbedingungen und Logik.
- Externe Ausführung (z. B. SSH/API) wird gestartet.
- Ergebnis, Verlauf und Audit werden gespeichert.

### Fehlerfluss

- Fehler werden nicht „versteckt“, sondern als strukturierte Fehlerobjekte zurückgegeben.
- Dazu zählen bspw. Verbindungsfehler, Berechtigungsfehler oder Timeouts.
- Jede Anfrage kann über CorrelationId nachvollzogen werden.

## DI (Dependency Injection) – einfach

DI bedeutet: Klassen bekommen ihre Abhängigkeiten „von außen“ statt selbst neu zu erstellen.

- Zentral gebaut in `Wec.Host/Program.cs`.
- Module registrieren ihre eigenen Services und Handler.
- Vorteile:
  - leichter testbar
  - weniger hart verdrahtete Abhängigkeiten
  - klarer Austausch von Implementierungen (z. B. in Tests)

Lebensdauern, die oft genutzt werden:
- **Singleton**: eine Instanz für die ganze App
- **Scoped**: eine Instanz pro Anfrage/Verarbeitungseinheit

## Frontend / Backend (und wie sie zusammenarbeiten)

- Frontend kennt keine direkte Datenbank.
- Backend ruft keine React-Logik auf.
- Die Verbindung ist eine klar abgegrenzte Bridge-Schicht.
- Kommunikation ist nur über definierte Aktionen.
- In der Praxis heißt das:  
  Frontend sendet Anfrage → Backend verarbeitet → Ergebnis zurück → UI rendert.

Wichtige gemeinsame Stellen im Frontend:
- `frontend/src/shared/bridge/bridgeClient.ts`
- `frontend/src/shared/api-types.generated.ts` (CI-überprüft)
- `frontend/src/shared/environment` und `frontend/src/shared/targets`
- `frontend/src/shared/ui` (Designsystem)

## Tests

- Backend:
  - `dotnet test` (aus dem Repo-Root)
  - Unit- und Integrationstests in `tests/`
- Frontend:
  - `npm test` im `frontend`-Ordner
- Vertragssicherheit:
  - `dotnet run --project tools/Wec.ContractGenerator -- --check` in der CI

Aktuelle Testprojekte:
- `tests/Wec.Core.Tests`
- `tests/Wec.Host.Tests`
- `tests/Wec.Infrastructure.IntegrationTests`
- `tests/Wec.Modules.*.Tests` (für die Fachmodule)

## Änderungswege (wenn du etwas ergänzen willst)

- **Neue Aktion in bestehendes Modul**
  1. Neue Handler-Klasse in `src/Modules/<Modul>/Handlers` anlegen.
  2. In Modul registrieren.
  3. Frontend-Call aufrufen.
  4. Tests ergänzen.
  5. Vertrags-Typen neu generieren.

- **Neues UI-Feature**
  1. Route in `frontend/src/app/App.tsx` ergänzen.
  2. Neue Komponente unter `frontend/src/features/...` anlegen.
  3. Handler im passenden Modul bereitstellen.
  4. Datenfluss prüfen (Laden → Anzeigen → Fehler).

- **Neues Modul**
  1. Neues Projekt unter `src/Modules` anlegen.
  2. `IModule` implementieren und im Host registrieren.
  3. Tests + Modul-README schreiben.
  4. Frontend-Route + Navigation ergänzen.

## Typische Änderungspfade

- **Datenstruktur ändern** → EF-Migration in `Wec.Infrastructure`, Backend-Tests, Frontend-Anpassungen.
- **Fehlerbehandlung ändern** → zentrale Fehler-Typen im Core, dann Frontend-UI auf neue Fehlerfälle vorbereiten.
- **Timeout-/Rechte-Regeln** → `BridgeExecutionTimeoutPolicy`, ggf. UX-Feedback anpassen.
- **Externe Systeme** (z. B. AD/Nessus/opsi) → Infrastrukturklasse ergänzen, Module anpassen, Tests hinzufügen.

## Kopplungen (was eng zusammenhängt)

- Module ↔ Infrastruktur: Sehr stark bei externen Aufrufen (AD, WMI, LDAP, SNMP, SSH, APIs).
- Frontend ↔ Backend-Vertrag: Stark, aber stabil durch Generator-Check.
- Host ↔ alle Module: Host ist „Kompositions-Einsatzpunkt“ (stellt den Rahmen bereit).
- Windows-spezifische Grenzen: Funktioniert gezielt auf Windows (UI/WebView2/WinRM etc.).

## Warum das so gebaut wurde (kurz)

- Einzelprozess-Ansatz vereinfacht Deployment und reduziert Angriffsfläche.
- Klare Modulgrenzen machen fachliche Erweiterungen kontrollierbarer.
- Ein gemeinsamer Vertrag verhindert „Frontend denkt anders als Backend“.
- Vollständige Test-Pfade (Unit/Integration/Frontend) schützen vor stillen Bruchstellen.

## Wichtige Referenzen im Repo

- Modul-Dokumentation: `src/Modules/*/README.md`
- Architekturentscheidungen: `docs/adr/*`
- Build/Release-Details: `.github/workflows/ci.yml`
- Gesamtprojektbeschreibung: `README.md`

