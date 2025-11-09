/*
    BreakfastShop database schema (no multiple cascade path + no INSTEAD OF trigger)
    - Shop→Meals: ON DELETE CASCADE
    - Shop→MealCategory: ON DELETE CASCADE
    - Meals→MealCategory: NO ACTION (避免多重級聯路徑)
    - 以儲存程序 usp_DeleteMealCategory 處理個別分類刪除（先 NULL 化 Meals.CategoryId 再刪分類）
*/

USE BreakfastShop;
GO

/* ============ 清理可能殘留的觸發器與物件 ============ */
-- Row-level UpdateDate triggers
IF EXISTS(SELECT 1 FROM sys.triggers WHERE name=N'TR_Shop_SetUpdateDate' AND parent_class_desc='OBJECT_OR_COLUMN')
    DROP TRIGGER dbo.TR_Shop_SetUpdateDate;
IF EXISTS(SELECT 1 FROM sys.triggers WHERE name=N'TR_Meals_SetUpdateDate' AND parent_class_desc='OBJECT_OR_COLUMN')
    DROP TRIGGER dbo.TR_Meals_SetUpdateDate;
IF EXISTS(SELECT 1 FROM sys.triggers WHERE name=N'TR_MealCategory_SetUpdateDate' AND parent_class_desc='OBJECT_OR_COLUMN')
    DROP TRIGGER dbo.TR_MealCategory_SetUpdateDate;
IF EXISTS(SELECT 1 FROM sys.triggers WHERE name=N'TR_Combo_SetUpdateDate' AND parent_class_desc='OBJECT_OR_COLUMN')
    DROP TRIGGER dbo.TR_Combo_SetUpdateDate;
IF EXISTS(SELECT 1 FROM sys.triggers WHERE name=N'TR_Table_SetUpdateDate' AND parent_class_desc='OBJECT_OR_COLUMN')
    DROP TRIGGER dbo.TR_Table_SetUpdateDate;

-- 移除可能存在的 DB-level 同名觸發器（保險）
IF EXISTS(SELECT 1 FROM sys.triggers WHERE name=N'TR_Shop_SetUpdateDate' AND parent_class_desc='DATABASE')
    DROP TRIGGER TR_Shop_SetUpdateDate ON DATABASE;
IF EXISTS(SELECT 1 FROM sys.triggers WHERE name=N'TR_Meals_SetUpdateDate' AND parent_class_desc='DATABASE')
    DROP TRIGGER TR_Meals_SetUpdateDate ON DATABASE;
IF EXISTS(SELECT 1 FROM sys.triggers WHERE name=N'TR_MealCategory_SetUpdateDate' AND parent_class_desc='DATABASE')
    DROP TRIGGER TR_MealCategory_SetUpdateDate ON DATABASE;
IF EXISTS(SELECT 1 FROM sys.triggers WHERE name=N'TR_Combo_SetUpdateDate' AND parent_class_desc='DATABASE')
    DROP TRIGGER TR_Combo_SetUpdateDate ON DATABASE;
IF EXISTS(SELECT 1 FROM sys.triggers WHERE name=N'TR_Table_SetUpdateDate' AND parent_class_desc='DATABASE')
    DROP TRIGGER TR_Table_SetUpdateDate ON DATABASE;

-- 刪除可能殘留的 FK
IF OBJECT_ID(N'dbo.FK_Meals_Shop', N'F') IS NOT NULL
    ALTER TABLE dbo.Meals DROP CONSTRAINT FK_Meals_Shop;
IF OBJECT_ID(N'dbo.FK_Meals_Category', N'F') IS NOT NULL
    ALTER TABLE dbo.Meals DROP CONSTRAINT FK_Meals_Category;
IF OBJECT_ID(N'dbo.FK_Meals_Category_Shop', N'F') IS NOT NULL
    ALTER TABLE dbo.Meals DROP CONSTRAINT FK_Meals_Category_Shop;
IF OBJECT_ID(N'dbo.FK_Combo_Shop', N'F') IS NOT NULL
    ALTER TABLE dbo.Combo DROP CONSTRAINT FK_Combo_Shop;
IF OBJECT_ID(N'dbo.FK_MealCategory_Shop', N'F') IS NOT NULL
    ALTER TABLE dbo.MealCategory DROP CONSTRAINT FK_MealCategory_Shop;
GO

/* ============ 依相依順序刪表 ============ */
IF OBJECT_ID(N'dbo.Meals', N'U') IS NOT NULL DROP TABLE dbo.Meals;
IF OBJECT_ID(N'dbo.Combo', N'U') IS NOT NULL DROP TABLE dbo.Combo;
IF OBJECT_ID(N'dbo.[Table]', N'U') IS NOT NULL DROP TABLE dbo.[Table];
IF OBJECT_ID(N'dbo.MealCategory', N'U') IS NOT NULL DROP TABLE dbo.MealCategory;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Shop_Account' AND object_id = OBJECT_ID(N'dbo.Shop'))
    DROP INDEX IX_Shop_Account ON dbo.Shop;
IF OBJECT_ID(N'dbo.Shop', N'U') IS NOT NULL DROP TABLE dbo.Shop;
GO

/* ============ Shop ============ */
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

/* ============ MealCategory ============ */
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
    CONSTRAINT FK_MealCategory_Shop FOREIGN KEY (ShopId)
        REFERENCES dbo.Shop (Id) ON DELETE CASCADE
);
-- 供複合 FK 使用（確保 Id, ShopId 唯一）
CREATE UNIQUE INDEX UX_MealCategory_Id_Shop ON dbo.MealCategory (Id, ShopId);
CREATE UNIQUE INDEX IX_MealCategory_Shop_Name ON dbo.MealCategory (ShopId, Name);
CREATE INDEX IX_MealCategory_Shop_Active ON dbo.MealCategory (ShopId, IsActive) INCLUDE (Name, SortOrder);
GO

/* ============ Meals ============ */
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
    -- 關鍵：不做級聯，避免多重級聯路徑
    CONSTRAINT FK_Meals_Category_Shop FOREIGN KEY (CategoryId, ShopId)
        REFERENCES dbo.MealCategory (Id, ShopId) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT CK_Meals_Money CHECK (Money >= 0)
);
CREATE UNIQUE INDEX IX_Meals_Shop_Name ON dbo.Meals (ShopId, Name);
CREATE INDEX IX_Meals_Shop_Active ON dbo.Meals (ShopId, IsActive) INCLUDE (Name, Money);
CREATE INDEX IX_Meals_Category ON dbo.Meals (CategoryId);
GO

/* ============ Combo ============ */
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

/* ============ Table (dining tables) ============ */
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

/* ============ UpdateDate 觸發器（AFTER UPDATE） ============ */
CREATE TRIGGER dbo.TR_Shop_SetUpdateDate
ON dbo.Shop
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF UPDATE(UpdateDate) RETURN;
    UPDATE s
      SET UpdateDate = GETDATE()
    FROM dbo.Shop AS s
    INNER JOIN inserted AS i ON s.Id = i.Id;
END;
GO

CREATE TRIGGER dbo.TR_Meals_SetUpdateDate
ON dbo.Meals
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF UPDATE(UpdateDate) RETURN;
    UPDATE m
      SET UpdateDate = GETDATE()
    FROM dbo.Meals AS m
    INNER JOIN inserted AS i ON m.Id = i.Id;
END;
GO

CREATE TRIGGER dbo.TR_MealCategory_SetUpdateDate
ON dbo.MealCategory
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF UPDATE(UpdateDate) RETURN;
    UPDATE c
      SET UpdateDate = GETDATE()
    FROM dbo.MealCategory AS c
    INNER JOIN inserted AS i ON c.Id = i.Id;
END;
GO

CREATE TRIGGER dbo.TR_Combo_SetUpdateDate
ON dbo.Combo
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF UPDATE(UpdateDate) RETURN;
    UPDATE c
      SET UpdateDate = GETDATE()
    FROM dbo.Combo AS c
    INNER JOIN inserted AS i ON c.Id = i.Id;
END;
GO

CREATE TRIGGER dbo.TR_Table_SetUpdateDate
ON dbo.[Table]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF UPDATE(UpdateDate) RETURN;
    UPDATE t
      SET UpdateDate = GETDATE()
    FROM dbo.[Table] AS t
    INNER JOIN inserted AS i ON t.Id = i.Id;
END;
GO

/* ============ 刪分類用的安全儲存程序（請用這個來刪單一分類） ============ */
CREATE OR ALTER PROCEDURE dbo.usp_DeleteMealCategory
    @CategoryId UNIQUEIDENTIFIER,
    @ShopId     UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRAN;

    -- 先把該分類底下的餐點分類清空（同店家保障）
    UPDATE m
       SET m.CategoryId = NULL
    FROM dbo.Meals AS m
    WHERE m.CategoryId = @CategoryId
      AND m.ShopId     = @ShopId;

    -- 再刪分類
    DELETE FROM dbo.MealCategory
    WHERE Id = @CategoryId
      AND ShopId = @ShopId;

    COMMIT TRAN;
END;
GO

/* （選用）阻擋直接刪 MealCategory，強制走 SP
   需要時再打開：
-- DENY DELETE ON dbo.MealCategory TO PUBLIC;
*/
