# Abs.DBCC – Database Collation Changer

[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](LICENSE)

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
- Pre-Flight-Check vor dem eigentlichen Lauf: andere aktive Verbindungen, geschätzte betroffene
  Zeilenanzahl, aktuelle Transaktionsprotokoll-Auslastung
- Live-Fortschrittsanzeige mit Schritt-für-Schritt-Protokoll, Abbruch-Möglichkeit
- Automatische Verifikation nach der Migration: struktureller Diff (alles bis auf die Collation muss
  identisch sein) sowie zeilenweiser Datenvergleich pro Tabelle
- Robust gegenüber Verbindungsabbrüchen während der Migration (siehe Abschnitt
  [Transaktionsstrategie](#transaktionsstrategie--fehlerverhalten))

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
vor dem Start noch einmal überprüft (inkl. Pre-Flight-Check).

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
