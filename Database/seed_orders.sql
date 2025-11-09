USE BreakfastShop;
GO

-- Sample data seeding for Orders and OrderItems
DECLARE @ShopId UNIQUEIDENTIFIER = (
    SELECT TOP (1) Id FROM dbo.Shop WHERE IsActive = 1 ORDER BY CreateDate ASC
);

IF @ShopId IS NULL
BEGIN
    PRINT 'No active shops found. Skipping sample Orders/OrderItems seeding.';
    RETURN;
END;

DECLARE @TableId UNIQUEIDENTIFIER = (
    SELECT TOP (1) Id FROM dbo.[Table]
    WHERE ShopId = @ShopId AND IsActive = 1
    ORDER BY Number ASC
);

DECLARE @Meals TABLE
(
    MealId   UNIQUEIDENTIFIER,
    MealName NVARCHAR(100),
    Price    DECIMAL(10,2)
);

INSERT INTO @Meals (MealId, MealName, Price)
SELECT TOP (5) Id, Name, Money
FROM dbo.Meals
WHERE ShopId = @ShopId AND IsActive = 1
ORDER BY Name ASC;

IF NOT EXISTS (SELECT 1 FROM @Meals)
BEGIN
    PRINT 'No active meals found. Skipping sample Orders/OrderItems seeding.';
    RETURN;
END;

DECLARE @DineInOrderId UNIQUEIDENTIFIER = NEWID();
IF @TableId IS NOT NULL AND NOT EXISTS (
    SELECT 1 FROM dbo.Orders WHERE Notes = N'示範內用訂單'
)
BEGIN
    INSERT INTO dbo.Orders (Id, ShopId, OrderType, TableId, TakeoutCode, Notes, Status)
    VALUES (@DineInOrderId, @ShopId, N'DineIn', @TableId, NULL, N'示範內用訂單', N'Pending');

    INSERT INTO dbo.OrderItems (Id, OrderId, MealId, MealName, Quantity, UnitPrice, Notes)
    SELECT NEWID(), @DineInOrderId, m.MealId, m.MealName, 1, m.Price, NULL
    FROM @Meals AS m;
END
ELSE IF @TableId IS NULL
BEGIN
    PRINT 'No active tables found. Skipping dine-in sample order.';
END

DECLARE @TakeoutOrderId UNIQUEIDENTIFIER = NEWID();
IF NOT EXISTS (
    SELECT 1 FROM dbo.Orders WHERE TakeoutCode = N'DEMO001'
)
BEGIN
    INSERT INTO dbo.Orders (Id, ShopId, OrderType, TableId, TakeoutCode, Notes, Status)
    VALUES (@TakeoutOrderId, @ShopId, N'TakeOut', NULL, N'DEMO001', N'示範外帶訂單', N'Pending');

    WITH MealsWithRow AS
    (
        SELECT MealId, MealName, Price,
               ROW_NUMBER() OVER (ORDER BY MealName) AS RowNum
        FROM @Meals
    )
    INSERT INTO dbo.OrderItems (Id, OrderId, MealId, MealName, Quantity, UnitPrice, Notes)
    SELECT NEWID(), @TakeoutOrderId, MealId, MealName,
           CASE WHEN RowNum = 1 THEN 2 ELSE 1 END,
           Price, NULL
    FROM MealsWithRow;
END
ELSE
BEGIN
    PRINT 'Sample take-out order already exists. Skipping creation.';
END
GO
