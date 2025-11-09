/*
    BreakfastShop database schema
    ---------------------------------
    This script creates the core tables used by the application together with
    relational constraints, defaults, and indexes to keep data consistent.
    Re-run safely: objects are dropped if they already exist.
*/

USE BreakfastShop;
GO

/* ============================
   Drop existing constraints
   ============================ */
IF OBJECT_ID(N'dbo.FK_Meals_Shop', N'F') IS NOT NULL
    ALTER TABLE dbo.Meals DROP CONSTRAINT FK_Meals_Shop;
IF OBJECT_ID(N'dbo.FK_Combo_Shop', N'F') IS NOT NULL
    ALTER TABLE dbo.Combo DROP CONSTRAINT FK_Combo_Shop;
GO

/* ============================
   Drop tables in dependency order
   ============================ */
IF OBJECT_ID(N'dbo.Meals', N'U') IS NOT NULL DROP TABLE dbo.Meals;
IF OBJECT_ID(N'dbo.Combo', N'U') IS NOT NULL DROP TABLE dbo.Combo;
IF OBJECT_ID(N'dbo.[Table]', N'U') IS NOT NULL DROP TABLE dbo.[Table];
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Shop_Account' AND object_id = OBJECT_ID(N'dbo.Shop'))
    DROP INDEX IX_Shop_Account ON dbo.Shop;
IF OBJECT_ID(N'dbo.Shop', N'U') IS NOT NULL DROP TABLE dbo.Shop;
GO

/* ============================
   Shop
   ============================ */
CREATE TABLE dbo.Shop
(
    Id          UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_Shop_Id DEFAULT (NEWSEQUENTIALID()),
    Name        NVARCHAR(50)     NOT NULL,
    Phone       NVARCHAR(20)     NULL,
    Account     NVARCHAR(50)     NULL,
    [Password]  NVARCHAR(100)    NULL,
    Addr        NVARCHAR(100)    NULL,
    IsActive    BIT              NOT NULL CONSTRAINT DF_Shop_IsActive DEFAULT (1),
    CreateDate  DATETIME2(0)     NOT NULL CONSTRAINT DF_Shop_CreateDate DEFAULT (SYSUTCDATETIME()),
    UpdateDate  DATETIME2(0)     NOT NULL CONSTRAINT DF_Shop_UpdateDate DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_Shop PRIMARY KEY (Id),
    CONSTRAINT UQ_Shop_Name UNIQUE (Name)
);
GO

CREATE UNIQUE INDEX IX_Shop_Account ON dbo.Shop (Account) WHERE Account IS NOT NULL;
GO

/* ============================
   Meals
   ============================ */
CREATE TABLE dbo.Meals
(
    Id          UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_Meals_Id DEFAULT (NEWSEQUENTIALID()),
    ShopId      UNIQUEIDENTIFIER NOT NULL,
    Name        NVARCHAR(50)     NOT NULL,
    Money       DECIMAL(10,2)    NOT NULL CONSTRAINT DF_Meals_Money DEFAULT (0),
    IsActive    BIT              NOT NULL CONSTRAINT DF_Meals_IsActive DEFAULT (1),
    CreateDate  DATETIME2(0)     NOT NULL CONSTRAINT DF_Meals_CreateDate DEFAULT (SYSUTCDATETIME()),
    UpdateDate  DATETIME2(0)     NOT NULL CONSTRAINT DF_Meals_UpdateDate DEFAULT (SYSUTCDATETIME()),
    Element     NVARCHAR(200)    NULL,
    CONSTRAINT PK_Meals PRIMARY KEY (Id),
    CONSTRAINT FK_Meals_Shop FOREIGN KEY (ShopId)
        REFERENCES dbo.Shop (Id) ON DELETE CASCADE,
    CONSTRAINT CK_Meals_Money CHECK (Money >= 0)
);

CREATE UNIQUE INDEX IX_Meals_Shop_Name ON dbo.Meals (ShopId, Name);
CREATE INDEX IX_Meals_Shop_Active ON dbo.Meals (ShopId, IsActive) INCLUDE (Name, Money);
GO

/* ============================
   Combo
   ============================ */
CREATE TABLE dbo.Combo
(
    Id          UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_Combo_Id DEFAULT (NEWSEQUENTIALID()),
    ShopId      UNIQUEIDENTIFIER NOT NULL,
    ComboMeal   NVARCHAR(200)    NOT NULL,
    Money       DECIMAL(10,2)    NOT NULL CONSTRAINT DF_Combo_Money DEFAULT (0),
    IsActive    BIT              NOT NULL CONSTRAINT DF_Combo_IsActive DEFAULT (1),
    CreateDate  DATETIME2(0)     NOT NULL CONSTRAINT DF_Combo_CreateDate DEFAULT (SYSUTCDATETIME()),
    UpdateDate  DATETIME2(0)     NOT NULL CONSTRAINT DF_Combo_UpdateDate DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_Combo PRIMARY KEY (Id),
    CONSTRAINT FK_Combo_Shop FOREIGN KEY (ShopId)
        REFERENCES dbo.Shop (Id) ON DELETE CASCADE,
    CONSTRAINT CK_Combo_Money CHECK (Money >= 0)
);

CREATE UNIQUE INDEX IX_Combo_Shop_Name ON dbo.Combo (ShopId, ComboMeal);
CREATE INDEX IX_Combo_Shop_Active ON dbo.Combo (ShopId, IsActive) INCLUDE (Money);
GO

/* ============================
   Table (dining tables)
   ============================ */
CREATE TABLE dbo.[Table]
(
    Id          UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_Table_Id DEFAULT (NEWSEQUENTIALID()),
    Number      INT              NOT NULL,
    IsActive    BIT              NOT NULL CONSTRAINT DF_Table_IsActive DEFAULT (1),
    CreateDate  DATETIME2(0)     NOT NULL CONSTRAINT DF_Table_CreateDate DEFAULT (SYSUTCDATETIME()),
    UpdateDate  DATETIME2(0)     NOT NULL CONSTRAINT DF_Table_UpdateDate DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_Table PRIMARY KEY (Id),
    CONSTRAINT UQ_Table_Number UNIQUE (Number),
    CONSTRAINT CK_Table_Number CHECK (Number > 0)
);
GO

/* ============================
   Helper triggers to keep UpdateDate fresh
   ============================ */
CREATE OR ALTER TRIGGER TR_Shop_SetUpdateDate
ON dbo.Shop
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE s SET UpdateDate = SYSUTCDATETIME()
    FROM dbo.Shop AS s
    INNER JOIN inserted AS i ON s.Id = i.Id;
END;
GO

CREATE OR ALTER TRIGGER TR_Meals_SetUpdateDate
ON dbo.Meals
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE m SET UpdateDate = SYSUTCDATETIME()
    FROM dbo.Meals AS m
    INNER JOIN inserted AS i ON m.Id = i.Id;
END;
GO

CREATE OR ALTER TRIGGER TR_Combo_SetUpdateDate
ON dbo.Combo
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE c SET UpdateDate = SYSUTCDATETIME()
    FROM dbo.Combo AS c
    INNER JOIN inserted AS i ON c.Id = i.Id;
END;
GO

CREATE OR ALTER TRIGGER TR_Table_SetUpdateDate
ON dbo.[Table]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE t SET UpdateDate = SYSUTCDATETIME()
    FROM dbo.[Table] AS t
    INNER JOIN inserted AS i ON t.Id = i.Id;
END;
GO
