namespace Abs.DBCC.IntegrationTest.TestSchema;

/// <summary>
/// One interconnected schema covering every object type this tool understands: tables with
/// PK/unique/check/default constraints and a persisted computed column, a filtered index, two foreign
/// keys (one touching an altered column, one not), a schema-bound view, an *indexed* schema-bound view
/// (with its own unique clustered index), a plain view, a trigger, a stored procedure, a scalar and a
/// table-valued function, a sequence, a synonym, a full-text catalog and index, table/column/schema-level
/// GRANT+DENY, and extended properties on a table/column/computed column/check constraint/index/the
/// indexed view's own index. Batches are separated by a line containing only "GO", matching how SQL
/// Server requires CREATE VIEW/PROCEDURE/FUNCTION/TRIGGER to be the first statement in their batch.
///
/// dbo.RegistrationLog and everything hanging off it exist purely to prove the database-default-collation
/// sweep (MigrationPlanBuilder, updateDatabaseDefaultCollation branch): it has no character column at all,
/// so it never appears in AffectedTables, yet its schema-bound view, default/check constraints, computed
/// column and filtered index must still be dropped and recreated around the ALTER DATABASE ... COLLATE
/// step, purely because that statement's dependency check is database-wide. Its default/check constraints
/// each call the schema-bound dbo.GetMinRegistrationDate function (requiring them to be dropped before,
/// and recreated after, schema-bound objects), and its IsValidFlag computed column both calls that same
/// function AND is selected by the schema-bound RegistrationLogSummary view - a three-link chain
/// (view -> computed column -> function) that exercises the combined drop/recreate ordering graph in
/// both directions at once.
///
/// dbo.OrdersByCustomerCodeRenamed is created under one name and immediately renamed with sp_rename to
/// prove RawDefinitionScriptGenerator correctly rewrites a recreated object's header when its stored
/// sys.sql_modules.definition (never touched by sp_rename) still embeds the pre-rename name.
/// </summary>
public static class IntegrationTestSchema
{
    public const string Ddl = """
        CREATE TABLE dbo.Customers (
            Id INT NOT NULL CONSTRAINT PK_Customers PRIMARY KEY,
            Name NVARCHAR(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
            Email VARCHAR(200) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
            CONSTRAINT UQ_Customers_Email UNIQUE (Email)
        );

        CREATE TABLE dbo.Orders (
            Id INT NOT NULL CONSTRAINT PK_Orders PRIMARY KEY,
            CustomerId INT NOT NULL,
            CustomerCode VARCHAR(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL
                CONSTRAINT CK_Orders_CustomerCode CHECK (CustomerCode <> ''),
            Description NVARCHAR(200) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
            Amount DECIMAL(10,2) NOT NULL CONSTRAINT DF_Orders_Amount DEFAULT (0),
            Label AS (CustomerCode + '-' + CONVERT(VARCHAR(10), Id)) PERSISTED,
            CONSTRAINT FK_Orders_Customers FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(Id),
            CONSTRAINT CK_Orders_DescriptionOrCode CHECK (Description IS NOT NULL OR CustomerCode IS NOT NULL)
        );

        CREATE NONCLUSTERED INDEX IX_Orders_CustomerCode ON dbo.Orders (CustomerCode) WHERE Amount > 0;

        CREATE TABLE dbo.CustomerNotes (
            Id INT NOT NULL CONSTRAINT PK_CustomerNotes PRIMARY KEY,
            CustomerEmail VARCHAR(200) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
            Note NVARCHAR(500) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
            CONSTRAINT FK_CustomerNotes_Customers FOREIGN KEY (CustomerEmail) REFERENCES dbo.Customers(Email)
        );

        CREATE TABLE dbo.Articles (
            Id INT NOT NULL CONSTRAINT PK_Articles PRIMARY KEY,
            Title NVARCHAR(200) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
            Body NVARCHAR(MAX) COLLATE SQL_Latin1_General_CP1_CI_AS NULL
        );
        GO

        CREATE VIEW dbo.CustomerSummary WITH SCHEMABINDING AS
            SELECT Id, Name, Email FROM dbo.Customers;
        GO

        CREATE FUNCTION dbo.GetMinRegistrationDate() RETURNS DATETIME WITH SCHEMABINDING AS
        BEGIN
            RETURN CONVERT(DATETIME, '2000-01-01');
        END;
        GO

        CREATE TABLE dbo.RegistrationLog (
            Id INT NOT NULL CONSTRAINT PK_RegistrationLog PRIMARY KEY,
            RegisteredAt DATETIME NOT NULL CONSTRAINT DF_RegistrationLog_RegisteredAt DEFAULT (dbo.GetMinRegistrationDate()),
            IsValidFlag AS (CASE WHEN dbo.GetMinRegistrationDate() IS NOT NULL THEN 1 ELSE 0 END),
            CONSTRAINT CK_RegistrationLog_RegisteredAt CHECK (RegisteredAt >= dbo.GetMinRegistrationDate())
        );
        CREATE INDEX IX_RegistrationLog_Id_Filtered ON dbo.RegistrationLog (Id) WHERE Id > 0;
        GO

        -- IsValidFlag is both selected by this schema-bound view (view must drop before the column) and
        -- itself calls dbo.GetMinRegistrationDate (the column must drop before the function) - the full
        -- three-link chain the combined drop/recreate ordering in MigrationPlanBuilder must get right.
        CREATE VIEW dbo.RegistrationLogSummary WITH SCHEMABINDING AS
            SELECT Id, RegisteredAt, IsValidFlag FROM dbo.RegistrationLog;
        GO

        -- Reproduces a real-world failure: sp_rename updates sys.objects.name but never touches
        -- sys.sql_modules.definition, so this view's stored CREATE VIEW text still says
        -- "OrdersByCustomerCodeOriginal" forever after. Replaying that text verbatim after a DROP would
        -- silently recreate the view under the *old* name - its own index (scoped to the current,
        -- renamed name) would then fail to create with "object not found".
        CREATE VIEW dbo.OrdersByCustomerCodeOriginal WITH SCHEMABINDING AS
            SELECT Id, CustomerCode FROM dbo.Orders;
        GO
        EXEC sp_rename 'dbo.OrdersByCustomerCodeOriginal', 'OrdersByCustomerCodeRenamed';
        GO
        CREATE UNIQUE CLUSTERED INDEX IX_OrdersByCustomerCodeRenamed_Id ON dbo.OrdersByCustomerCodeRenamed (Id);
        GO

        SET ANSI_NULLS ON;
        SET QUOTED_IDENTIFIER ON;
        GO
        CREATE VIEW dbo.OrdersByCustomerCode WITH SCHEMABINDING AS
            SELECT Id, CustomerCode FROM dbo.Orders;
        GO
        CREATE UNIQUE CLUSTERED INDEX IX_OrdersByCustomerCode_Id ON dbo.OrdersByCustomerCode (Id);
        GO

        CREATE VIEW dbo.OrdersPlain AS
            SELECT o.Id, o.CustomerCode, c.Name FROM dbo.Orders o JOIN dbo.Customers c ON c.Id = o.CustomerId;
        GO

        CREATE TRIGGER dbo.trg_Orders_AfterInsert ON dbo.Orders AFTER INSERT AS
        BEGIN
            SET NOCOUNT ON;
            DECLARE @code VARCHAR(50);
            SELECT TOP (1) @code = CustomerCode FROM inserted;
        END;
        GO

        CREATE PROCEDURE dbo.GetOrdersByCustomerCode @Code VARCHAR(50) AS
        BEGIN
            SET NOCOUNT ON;
            SELECT * FROM dbo.Orders WHERE CustomerCode = @Code;
        END;
        GO

        CREATE FUNCTION dbo.FormatCustomerCode (@Code VARCHAR(50)) RETURNS VARCHAR(60) AS
        BEGIN
            RETURN 'CODE:' + @Code;
        END;
        GO

        CREATE FUNCTION dbo.OrdersForCode (@Code VARCHAR(50)) RETURNS TABLE AS
        RETURN SELECT Id, Amount FROM dbo.Orders WHERE CustomerCode = @Code;
        GO

        CREATE SEQUENCE dbo.OrderNumbers AS BIGINT START WITH 1 INCREMENT BY 1;
        CREATE SYNONYM dbo.OrdersSyn FOR dbo.Orders;
        GO

        CREATE USER TestPrincipal WITHOUT LOGIN;
        GRANT SELECT ON dbo.Orders TO TestPrincipal;
        GRANT UPDATE ON dbo.Orders(Description) TO TestPrincipal;
        DENY DELETE ON dbo.Orders TO TestPrincipal;
        GRANT SELECT ON SCHEMA::dbo TO TestPrincipal;
        GO

        EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'Bestellungen (Kopftabelle)',
            @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Orders';
        EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'Kundencode der Bestellung',
            @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Orders',
            @level2type = N'COLUMN', @level2name = N'CustomerCode';
        EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'Anzeigename der Bestellung',
            @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Orders',
            @level2type = N'COLUMN', @level2name = N'Label';
        EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'Kundencode darf nicht leer sein',
            @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Orders',
            @level2type = N'CONSTRAINT', @level2name = N'CK_Orders_CustomerCode';
        EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'Beschleunigt Suche nach Kundencode',
            @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Orders',
            @level2type = N'INDEX', @level2name = N'IX_Orders_CustomerCode';
        EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'Eindeutiger Index der indizierten View',
            @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'VIEW', @level1name = N'OrdersByCustomerCode',
            @level2type = N'INDEX', @level2name = N'IX_OrdersByCustomerCode_Id';
        """;

    /// <summary>
    /// Separate from <see cref="Ddl"/> because the standard mcr.microsoft.com/mssql/server Linux image
    /// does not ship the Full-Text Search component by default (it requires installing the
    /// mssql-server-fts OS package, which a plain Testcontainers.MsSql image does not do) - the test
    /// attempts this batch and treats "Full-Text Search is not installed" as a skip, not a failure,
    /// for that portion of coverage. The rest of the schema (M2/M3/M5 object types) does not depend on it.
    /// </summary>
    public const string FullTextDdl = """
        CREATE FULLTEXT CATALOG TestCatalog AS DEFAULT;
        CREATE FULLTEXT INDEX ON dbo.Articles (Title LANGUAGE 1033, Body LANGUAGE 1033)
            KEY INDEX PK_Articles ON TestCatalog WITH CHANGE_TRACKING AUTO;
        """;

    public const string SeedData = """
        INSERT INTO dbo.Customers (Id, Name, Email) VALUES
            (1, N'Müller Café', 'mueller@example.com'),
            (2, N'O''Brien', NULL),
            (3, N'ÄÖÜ ß', 'aou@example.com');

        INSERT INTO dbo.Orders (Id, CustomerId, CustomerCode, Description, Amount) VALUES
            (1, 1, 'ABC-001', N'Café Bestellung – äöü', 12.50),
            (2, 2, 'abc-001', NULL, 0),
            (3, 3, REPLICATE('X', 50), N'Randlänge Test (varchar(50))', 999.99);

        INSERT INTO dbo.CustomerNotes (Id, CustomerEmail, Note) VALUES
            (1, 'mueller@example.com', N'Wichtiger Kunde – bevorzugt Café-Artikel');

        INSERT INTO dbo.Articles (Id, Title, Body) VALUES
            (1, N'Über uns', N'Willkommen bei unserem Café. Öffnungszeiten: Mo-Fr.'),
            (2, N'ÄÖÜ Special', N'Text mit Umlauten und ß, Groß- und Kleinschreibung.');

        INSERT INTO dbo.RegistrationLog (Id, RegisteredAt) VALUES (1, '2001-01-01');
        INSERT INTO dbo.RegistrationLog (Id) VALUES (2);
        """;
}
