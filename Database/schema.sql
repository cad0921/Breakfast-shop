USE BreakfastShop;
GO

/* ============================
   Drop existing constraints
   ============================ */
IF OBJECT_ID(N'dbo.FK_Meals_Shop', N'F') IS NOT NULL
    ALTER TABLE dbo.Meals DROP CONSTRAINT FK_Meals_Shop;
IF OBJECT_ID(N'dbo.FK_Meals_Category', N'F') IS NOT NULL
    ALTER TABLE dbo.Meals DROP CONSTRAINT FK_Meals_Category;
IF OBJECT_ID(N'dbo.FK_Combo_Shop', N'F') IS NOT NULL
    ALTER TABLE dbo.Combo DROP CONSTRAINT FK_Combo_Shop;
IF OBJECT_ID(N'dbo.FK_MealCategory_Shop', N'F') IS NOT NULL
    ALTER TABLE dbo.MealCategory DROP CONSTRAINT FK_MealCategory_Shop;
GO

/* ============================
   Drop tables in dependency order
   ============================ */
IF OBJECT_ID(N'dbo.Meals', N'U') IS NOT NULL DROP TABLE dbo.Meals;
IF OBJECT_ID(N'dbo.Combo', N'U') IS NOT NULL DROP TABLE dbo.Combo;
IF OBJECT_ID(N'dbo.[Table]', N'U') IS NOT NULL DROP TABLE dbo.[Table];
IF OBJECT_ID(N'dbo.MealCategory', N'U') IS NOT NULL DROP TABLE dbo.MealCategory;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Shop_Account' AND object_id = OBJECT_ID(N'dbo.Shop'))
    DROP INDEX IX_Shop_Account ON dbo.Shop;
IF OBJECT_ID(N'dbo.Shop', N'U') IS NOT NULL DROP TABLE dbo.Shop;
GO

/* ============================
   Shop
   ============================ */
CREATE TABLE dbo.Shop
(
    Id          UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_Shop_Id DEFAULT (NEWID()),
    Name        NVARCHAR(50)     NOT NULL,
    Phone       NVARCHAR(20)     NULL,
    Account     NVARCHAR(50)     NULL,
    [Password]  NVARCHAR(100)    NULL,
    Addr        NVARCHAR(100)    NULL,
    IsActive    BIT              NOT NULL CONSTRAINT DF_Shop_IsActive DEFAULT (1),
    CreateDate  DATETIME2(0)     NOT NULL CONSTRAINT DF_Shop_CreateDate DEFAULT (GETDATE()),
    UpdateDate  DATETIME2(0)     NOT NULL CONSTRAINT DF_Shop_UpdateDate DEFAULT (GETDATE()),
    CONSTRAINT PK_Shop PRIMARY KEY (Id),
    CONSTRAINT UQ_Shop_Name UNIQUE (Name)
);
GO
CREATE UNIQUE INDEX IX_Shop_Account ON dbo.Shop (Account) WHERE Account IS NOT NULL;
GO

/* ============================
   MealCategory  (FK 改為 NO ACTION)
   ============================ */
CREATE TABLE dbo.MealCategory
(
    Id          UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_MealCategory_Id DEFAULT (NEWID()),
    ShopId      UNIQUEIDENTIFIER NOT NULL,
    Name        NVARCHAR(50)     NOT NULL,
    SortOrder   INT              NOT NULL CONSTRAINT DF_MealCategory_SortOrder DEFAULT (0),
    IsActive    BIT              NOT NULL CONSTRAINT DF_MealCategory_IsActive DEFAULT (1),
    CreateDate  DATETIME2(0)     NOT NULL CONSTRAINT DF_MealCategory_CreateDate DEFAULT (GETDATE()),
    UpdateDate  DATETIME2(0)     NOT NULL CONSTRAINT DF_MealCategory_UpdateDate DEFAULT (GETDATE()),
    CONSTRAINT PK_MealCategory PRIMARY KEY (Id),
    -- 這裡把 ON DELETE CASCADE 拿掉，避免多重路徑
    CONSTRAINT FK_MealCategory_Shop FOREIGN KEY (ShopId)
        REFERENCES dbo.Shop (Id) ON DELETE NO ACTION
);
CREATE UNIQUE INDEX IX_MealCategory_Shop_Name ON dbo.MealCategory (ShopId, Name);
CREATE INDEX IX_MealCategory_Shop_Active ON dbo.MealCategory (ShopId, IsActive) INCLUDE (Name, SortOrder);
GO

/* ============================
   Meals  (保留 Shop→Meals 的 CASCADE；Category 保持 SET NULL)
   ============================ */
CREATE TABLE dbo.Meals
(
    Id          UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_Meals_Id DEFAULT (NEWID()),
    ShopId      UNIQUEIDENTIFIER NOT NULL,
    Name        NVARCHAR(50)     NOT NULL,
    Money       DECIMAL(10,2)    NOT NULL CONSTRAINT DF_Meals_Money DEFAULT (0),
    IsActive    BIT              NOT NULL CONSTRAINT DF_Meals_IsActive DEFAULT (1),
    CreateDate  DATETIME2(0)     NOT NULL CONSTRAINT DF_Meals_CreateDate DEFAULT (GETDATE()),
    UpdateDate  DATETIME2(0)     NOT NULL CONSTRAINT DF_Meals_UpdateDate DEFAULT (GETDATE()),
    Element     NVARCHAR(200)    NULL,
    CategoryId  UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_Meals PRIMARY KEY (Id),
    CONSTRAINT FK_Meals_Shop FOREIGN KEY (ShopId)
        REFERENCES dbo.Shop (Id) ON DELETE CASCADE,
    CONSTRAINT FK_Meals_Category FOREIGN KEY (CategoryId)
        REFERENCES dbo.MealCategory (Id) ON DELETE SET NULL,
    CONSTRAINT CK_Meals_Money CHECK (Money >= 0)
);
CREATE UNIQUE INDEX IX_Meals_Shop_Name ON dbo.Meals (ShopId, Name);
CREATE INDEX IX_Meals_Shop_Active ON dbo.Meals (ShopId, IsActive) INCLUDE (Name, Money);
CREATE INDEX IX_Meals_Category ON dbo.Meals (CategoryId);
GO

/* ============================
   Combo
   ============================ */
CREATE TABLE dbo.Combo
(
    Id          UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_Combo_Id DEFAULT (NEWID()),
    ShopId      UNIQUEIDENTIFIER NOT NULL,
    Title       NVARCHAR(100)    NOT NULL CONSTRAINT DF_Combo_Title DEFAULT (N''),
    ComboMeal   NVARCHAR(MAX)    NULL,
    Money       DECIMAL(10,2)    NOT NULL CONSTRAINT DF_Combo_Money DEFAULT (0),
    IsActive    BIT              NOT NULL CONSTRAINT DF_Combo_IsActive DEFAULT (1),
    CreateDate  DATETIME2(0)     NOT NULL CONSTRAINT DF_Combo_CreateDate DEFAULT (GETDATE()),
    UpdateDate  DATETIME2(0)     NOT NULL CONSTRAINT DF_Combo_UpdateDate DEFAULT (GETDATE()),
    CONSTRAINT PK_Combo PRIMARY KEY (Id),
    CONSTRAINT FK_Combo_Shop FOREIGN KEY (ShopId)
        REFERENCES dbo.Shop (Id) ON DELETE CASCADE,
    CONSTRAINT CK_Combo_Money CHECK (Money >= 0)
);
CREATE UNIQUE INDEX IX_Combo_Shop_Name ON dbo.Combo (ShopId, Title);
CREATE INDEX IX_Combo_Shop_Active ON dbo.Combo (ShopId, IsActive) INCLUDE (Money);
GO

/* ============================
   Table (dining tables)
   ============================ */
CREATE TABLE dbo.[Table]
(
    Id          UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_Table_Id DEFAULT (NEWID()),
    Number      INT              NOT NULL,
    IsActive    BIT              NOT NULL CONSTRAINT DF_Table_IsActive DEFAULT (1),
    CreateDate  DATETIME2(0)     NOT NULL CONSTRAINT DF_Table_CreateDate DEFAULT (GETDATE()),
    UpdateDate  DATETIME2(0)     NOT NULL CONSTRAINT DF_Table_UpdateDate DEFAULT (GETDATE()),
    CONSTRAINT PK_Table PRIMARY KEY (Id),
    CONSTRAINT UQ_Table_Number UNIQUE (Number),
    CONSTRAINT CK_Table_Number CHECK (Number > 0)
);
GO

/* ============================
   AFTER DELETE 觸發器：刪 Shop 後，清掉該店的分類
   （避免多重串聯路徑，但保有原本業務語意）
   ============================ */
CREATE OR ALTER TRIGGER TR_Shop_CleanupCategories
ON dbo.Shop
AFTER DELETE
AS
BEGIN
    SET NOCOUNT ON;
    -- 此時由於 FK_Meals_Shop 已經把該 Shop 的 Meals 刪掉
    -- 再刪 MealCategory 不會再觸發任何路徑碰撞
    DELETE mc
    FROM dbo.MealCategory mc
    INNER JOIN deleted d ON mc.ShopId = d.Id;
END;
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
    UPDATE s SET UpdateDate = GETDATE()
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
    UPDATE m SET UpdateDate = GETDATE()
    FROM dbo.Meals AS m
    INNER JOIN inserted AS i ON m.Id = i.Id;
END;
GO

CREATE OR ALTER TRIGGER TR_MealCategory_SetUpdateDate
ON dbo.MealCategory
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE c SET UpdateDate = GETDATE()
    FROM dbo.MealCategory AS c
    INNER JOIN inserted AS i ON c.Id = i.Id;
END;
GO

CREATE OR ALTER TRIGGER TR_Combo_SetUpdateDate
ON dbo.Combo
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE c SET UpdateDate = GETDATE()
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
    UPDATE t SET UpdateDate = GETDATE()
    FROM dbo.[Table] AS t
    INNER JOIN inserted AS i ON t.Id = i.Id;
END;
GO
