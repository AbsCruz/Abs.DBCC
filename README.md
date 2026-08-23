# Abs.DBCC – Database Collation Changer

[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](LICENSE)

*English version below – siehe [English](#english) weiter unten.*

Plattformunabhängiges Tool zum Ändern der **Sortierung (Collation)** einer bereits produktiven,
datenbefüllten Microsoft-SQL-Server-Datenbank – ohne Datenverlust und ohne Änderung an Struktur
oder Inhalt der Datenbank, abgesehen von der Collation selbst.

Der Wechsel der Collation nachträglich ist mit reinem T-SQL mühsam und fehleranfällig, weil
`ALTER TABLE ... ALTER COLUMN ... COLLATE` fehlschlägt, solange abhängige Objekte (Indizes,
Constraints, Views, Volltextindizes, ...) existieren. Abs.DBCC übernimmt das komplett: Es erfasst
den kompletten Ist-Zustand der Datenbank, entfernt vorübergehend nur das, was den Wechsel
blockiert, ändert die Spalten, und stellt anschließend alles exakt so wieder her, wie es vorher war.

## Funktionsumfang

- Anzeige der aktuellen Collation – datenbankweit sowie pro Tabelle/Spalte
- Auswahl der Ziel-Collation aus allen vom Server unterstützten Sortierungen
  (`sys.fn_helpcollations()`)
- Vollständige Abdeckung aller relevanten Objekttypen beim Drop/Recreate rund um den Spaltenwechsel:
  Tabellen/Spalten, Primary/Foreign Keys, Unique-/Check-/Default-Constraints, Indizes (inkl.
  gefilterter Indizes und Indizes auf indizierten Views), berechnete Spalten, Volltextkataloge und
  -indizes, schema-gebundene Views/Funktionen, Trigger, Sequenzen, Synonyme, Berechtigungen
  (Datenbank-, Schema- und Objekt-/Spaltenebene) sowie Extended Properties
- Optionaler Wechsel der Datenbank-Default-Collation (`ALTER DATABASE ... COLLATE`)
- Export des Migrationsplans als eigenständiges T-SQL-Skript (statt Ausführung direkt aus der
  Anwendung) – z. B. zur Prüfung durch einen DBA oder zur Ausführung über sqlcmd/SSMS
- Pre-Flight-Check vor dem eigentlichen Lauf: andere aktive Verbindungen, geschätzte betroffene
  Zeilenanzahl, aktuelle Transaktionsprotokoll-Auslastung
- Live-Fortschrittsanzeige mit Schritt-für-Schritt-Protokoll, Abbruch-Möglichkeit
- Automatische Verifikation nach der Migration: struktureller Diff (alles bis auf die Collation muss
  identisch sein) sowie zeilenweiser Datenvergleich pro Tabelle
- Robust gegenüber Verbindungsabbrüchen während der Migration (siehe Abschnitt
  [Transaktionsstrategie](#transaktionsstrategie--fehlerverhalten))
- Mehrsprachige Oberfläche (Deutsch/Englisch) – die Sprache wird beim Start automatisch anhand der
  Systemsprache gewählt: Deutsch, wenn das Betriebssystem auf Deutsch eingestellt ist, sonst Englisch

## Architektur

Clean Architecture mit strikter Schichtentrennung:

```
src/
  Abs.DBCC.SharedKernel/       Result<T>, Guard, IClock – keine Abhängigkeiten
  Abs.DBCC.Domain/             reines Modell (Snapshot-, Migrations-, Collation-Typen), keine SqlClient-Abhängigkeit
  Abs.DBCC.Application/        Commands/Queries (MediatR) + Ports (Interfaces) + FluentValidation
  Abs.DBCC.Infrastructure/     T-SQL-Introspektion gegen sys.*-Katalogsichten, DDL-Generierung,
                                Migrations-Orchestrierung, Verifikation
  Abs.DBCC.Desktop/            Avalonia-Desktop-UI (MVVM, CommunityToolkit.Mvvm)
  Abs.DBCC.TestCommon/         gemeinsame Test-Helfer (FakeSqlScriptRunner, Snapshot-Builder)

  *.Test/                      ein Unit-Test-Projekt je Schicht (xUnit + Moq, keine echte DB nötig)
  Abs.DBCC.IntegrationTest/    End-to-End-Test gegen einen echten SQL-Server-Container (Testcontainers)
```

Die Schema-Introspektion läuft bewusst über eigene, handgeschriebene T-SQL-Abfragen gegen
`sys.*`-Katalogsichten statt über SMO – dadurch ist die Ablaufplanung und DDL-Generierung mit
In-Memory-Fixtures unit-testbar, und die Korrektheit der Katalogabfragen selbst wird durch den
Testcontainers-Integrationstest gegen einen echten Server abgesichert.

### Migrationsablauf

1. **Snapshot** des kompletten Ist-Zustands (dient als Wiederherstellungs-Vorlage und als Baseline
   für die Verifikation).
2. **Abhängige Objekte entfernen**, die `ALTER COLUMN` blockieren würden.
3. **Spalten ändern** (`ALTER COLUMN ... COLLATE <Ziel>`).
4. Optional: **`ALTER DATABASE ... COLLATE <Ziel>`** für die Default-Collation.
5. **Objekte in korrekter Reihenfolge wiederherstellen** (inkl. Berechtigungen/Extended Properties).
6. **Verifikation**: erneuter Snapshot, struktureller Diff, zeilenweiser Datenvergleich.

### Transaktionsstrategie & Fehlerverhalten

Ohne Wechsel der Datenbank-Default-Collation läuft die gesamte Migration in **einer einzigen
Transaktion** – schlägt ein Schritt fehl, macht `ROLLBACK` Struktur und Daten vollständig rückgängig.
Da `ALTER DATABASE ... COLLATE` in SQL Server nicht innerhalb einer expliziten Transaktion laufen
darf, wird bei aktiviertem Default-Collation-Wechsel in drei Segmente aufgeteilt (Drop+Alter →
`ALTER DATABASE` außerhalb jeder Transaktion → Recreate); ein Fehlschlag in Segment 1 rollt
vollständig zurück, ein Fehlschlag danach hinterlässt einen sicheren, im Bericht beschriebenen
Zustand ohne Daten- oder Strukturverlust. Bricht die Verbindung mitten im Lauf ab, rollt SQL Server
serverseitig automatisch zurück; die Anwendung fängt einen fehlschlagenden eigenen Rollback-Versuch
ab und meldet dies klar im Ergebnis, statt mit einer unbehandelten Ausnahme abzubrechen.

## Voraussetzungen

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Zugriff auf eine Microsoft-SQL-Server-Instanz (lokal, Docker oder remote) für den eigentlichen
  Einsatz der Desktop-App
- [Docker](https://www.docker.com/) für den Integrationstest (startet automatisch einen echten
  `mcr.microsoft.com/mssql/server`-Container über [Testcontainers](https://testcontainers.com/))

## Erste Schritte

```bash
# Solution bauen
dotnet build src/Abs.DBCC.slnx

# Desktop-App starten
dotnet run --project src/Abs.DBCC.Desktop

# Unit-Tests (schnell, ohne Docker/DB)
dotnet test src/Abs.DBCC.SharedKernel.Test
dotnet test src/Abs.DBCC.Domain.Test
dotnet test src/Abs.DBCC.Application.Test
dotnet test src/Abs.DBCC.Infrastructure.Test
dotnet test src/Abs.DBCC.Desktop.Test

# Integrationstest (benötigt einen laufenden Docker-Daemon)
dotnet test src/Abs.DBCC.IntegrationTest
```

In der App werden Server, Datenbank, Benutzer und Passwort eingegeben, die Verbindung getestet, der
aktuelle Collation-Report geprüft, eine Ziel-Collation ausgewählt und der generierte Migrationsplan
vor dem Start noch einmal überprüft (inkl. Pre-Flight-Check). Auf dieser letzten Seite kann statt
„Migration starten“ auch „Als SQL-Skript exportieren“ gewählt werden: Die App erzeugt daraus ein
eigenständiges `.sql`-Skript mit exakt denselben Schritten (inkl. der Aufteilung in Transaktionen,
falls die Datenbank-Default-Collation mitgeändert wird) zum Speichern und späteren Ausführen über
sqlcmd, SSMS oder ein beliebiges anderes Werkzeug – ohne dass die Anwendung selbst dabei verbunden
sein muss.

### Veröffentlichen (self-contained, single-file)

Die Desktop-App ist für Windows, macOS (x64/arm64) und Linux als selbstständige Single-File-Anwendung
publizierbar:

```bash
dotnet publish src/Abs.DBCC.Desktop -r osx-arm64   # oder win-x64 / osx-x64 / linux-x64
```

## Tech-Stack

| Bereich | Technologie |
|---|---|
| Runtime | .NET 10 |
| Desktop-UI | Avalonia 12, CommunityToolkit.Mvvm |
| Anwendungslogik | MediatR, FluentValidation |
| Datenbankzugriff | Microsoft.Data.SqlClient (eigene T-SQL-Introspektion, kein SMO/EF) |
| Tests | xUnit, Moq, Testcontainers.MsSql |

## Lizenz

[GNU General Public License v3.0](LICENSE)

---

## English

Cross-platform tool for changing the **collation** of an already-live, populated Microsoft SQL
Server database – without data loss and without changing anything about the database's structure
or content other than the collation itself.

Changing the collation after the fact is tedious and error-prone with plain T-SQL, because
`ALTER TABLE ... ALTER COLUMN ... COLLATE` fails as long as dependent objects (indexes,
constraints, views, full-text indexes, ...) exist. Abs.DBCC handles this end to end: it captures
the complete current state of the database, temporarily drops only what's blocking the change,
alters the columns, and then restores everything exactly as it was before.

### Features

- Displays the current collation – database-wide as well as per table/column
- Choose the target collation from every collation supported by the server
  (`sys.fn_helpcollations()`)
- Full coverage of all relevant object types when dropping/recreating around the column change:
  tables/columns, primary/foreign keys, unique/check/default constraints, indexes (including
  filtered indexes and indexes on indexed views), computed columns, full-text catalogs and
  indexes, schema-bound views/functions, triggers, sequences, synonyms, permissions (database,
  schema, and object/column level) as well as extended properties
- Optional change of the database's default collation (`ALTER DATABASE ... COLLATE`)
- Export the migration plan as a stand-alone T-SQL script (instead of running it from the
  application) – e.g. for DBA review, or to run it via sqlcmd/SSMS
- Pre-flight check before the actual run: other active connections, estimated affected row count,
  current transaction log usage
- Live progress display with a step-by-step log, with the option to cancel
- Automatic verification after the migration: structural diff (everything except the collation
  must be identical) as well as a row-by-row data comparison per table
- Resilient to connection drops during the migration (see the
  [Transaction Strategy](#transaction-strategy--error-handling) section)
- Multilingual UI (German/English) – the language is chosen automatically at startup based on the
  OS language: German if the operating system is set to German, English otherwise

### Architecture

Clean Architecture with strict separation of layers:

```
src/
  Abs.DBCC.SharedKernel/       Result<T>, Guard, IClock – no dependencies
  Abs.DBCC.Domain/             pure model (snapshot, migration, collation types), no SqlClient dependency
  Abs.DBCC.Application/        commands/queries (MediatR) + ports (interfaces) + FluentValidation
  Abs.DBCC.Infrastructure/     T-SQL introspection against sys.* catalog views, DDL generation,
                                migration orchestration, verification
  Abs.DBCC.Desktop/            Avalonia desktop UI (MVVM, CommunityToolkit.Mvvm)
  Abs.DBCC.TestCommon/         shared test helpers (FakeSqlScriptRunner, snapshot builders)

  *.Test/                      one unit-test project per layer (xUnit + Moq, no real DB required)
  Abs.DBCC.IntegrationTest/    end-to-end test against a real SQL Server container (Testcontainers)
```

Schema introspection deliberately runs through hand-written T-SQL queries against `sys.*` catalog
views instead of SMO – this keeps plan building and DDL generation unit-testable with in-memory
fixtures, while the correctness of the catalog queries themselves is verified by the
Testcontainers integration test against a real server.

#### Migration flow

1. **Snapshot** of the complete current state (serves as the restore template and as the baseline
   for verification).
2. **Drop dependent objects** that would block `ALTER COLUMN`.
3. **Alter columns** (`ALTER COLUMN ... COLLATE <target>`).
4. Optional: **`ALTER DATABASE ... COLLATE <target>`** for the default collation.
5. **Recreate objects in the correct order** (including permissions/extended properties).
6. **Verification**: another snapshot, structural diff, row-by-row data comparison.

#### Transaction strategy & error handling

Without changing the database's default collation, the entire migration runs in a **single
transaction** – if a step fails, `ROLLBACK` fully undoes both structure and data changes. Since
SQL Server doesn't allow `ALTER DATABASE ... COLLATE` inside an explicit transaction, the run is
split into three segments when the default-collation change is enabled (drop+alter →
`ALTER DATABASE` outside any transaction → recreate); a failure in segment 1 rolls back
completely, while a failure after that leaves a safe state, described in the report, with no loss
of data or structure. If the connection drops mid-run, SQL Server automatically rolls back
server-side; the application catches a failing rollback attempt of its own and reports this
clearly in the result instead of aborting with an unhandled exception.

### Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Access to a Microsoft SQL Server instance (local, Docker, or remote) to actually use the desktop
  app
- [Docker](https://www.docker.com/) for the integration test (automatically starts a real
  `mcr.microsoft.com/mssql/server` container via [Testcontainers](https://testcontainers.com/))

### Getting started

```bash
# Build the solution
dotnet build src/Abs.DBCC.slnx

# Run the desktop app
dotnet run --project src/Abs.DBCC.Desktop

# Unit tests (fast, no Docker/DB required)
dotnet test src/Abs.DBCC.SharedKernel.Test
dotnet test src/Abs.DBCC.Domain.Test
dotnet test src/Abs.DBCC.Application.Test
dotnet test src/Abs.DBCC.Infrastructure.Test
dotnet test src/Abs.DBCC.Desktop.Test

# Integration test (requires a running Docker daemon)
dotnet test src/Abs.DBCC.IntegrationTest
```

In the app you enter the server, database, user, and password, test the connection, review the
current collation report, pick a target collation, and review the generated migration plan once
more before starting (including the pre-flight check). On that last screen, instead of "Start
Migration" you can also choose "Export as SQL Script": the app renders the exact same steps
(including the transaction split, if the database default collation is being changed too) as a
stand-alone `.sql` file to save and run later via sqlcmd, SSMS, or any other tool – without the
application itself needing to stay connected. The UI itself displays in German or English
automatically depending on your operating system's language setting.

#### Publishing (self-contained, single-file)

The desktop app can be published for Windows, macOS (x64/arm64), and Linux as a self-contained
single-file application:

```bash
dotnet publish src/Abs.DBCC.Desktop -r osx-arm64   # or win-x64 / osx-x64 / linux-x64
```

### Tech stack

| Area | Technology |
|---|---|
| Runtime | .NET 10 |
| Desktop UI | Avalonia 12, CommunityToolkit.Mvvm |
| Application logic | MediatR, FluentValidation |
| Database access | Microsoft.Data.SqlClient (custom T-SQL introspection, no SMO/EF) |
| Tests | xUnit, Moq, Testcontainers.MsSql |

### License

[GNU General Public License v3.0](LICENSE)
