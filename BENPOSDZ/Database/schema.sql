-- تم تحويل جميع المعرفات إلى TEXT (لتخزين GUID)
-- تمت إضافة حقول المزامنة لكل جدول

CREATE TABLE IF NOT EXISTS Users (
    Id TEXT PRIMARY KEY NOT NULL,
    User_Name TEXT NOT NULL,
    User_Password TEXT NOT NULL,
    User_Type INTEGER NOT NULL DEFAULT 0, -- 1: Admin, 0: Vendeur
    User_FullName TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    IsSynced INTEGER NOT NULL DEFAULT 0,
    IsDeleted INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS Products (
    Id TEXT PRIMARY KEY NOT NULL,
    Pro_Ref TEXT,
    Pro_Name TEXT NOT NULL,
    Pro_Mark TEXT,
    Pro_Propr TEXT,
    Pro_BuyPrice REAL NOT NULL,
    Pro_SalePriceG REAL NOT NULL,
    Pro_SalePrice_Min REAL,
    Pro_SalePrice_Max REAL NOT NULL,
    Pro_Qty REAL NOT NULL,
    Pro_QtyMin REAL,
    Pro_Unit TEXT NOT NULL,
    Pro_Image BLOB,
    Pro_date_exp TEXT NOT NULL,
    Pro_date_alert TEXT,
    Pro_Barcode TEXT,
    Pro_Type_ID TEXT,
    Pro_Unit_G TEXT,
    Pro_Pack_G REAL,
    Pro_Qty_Inv REAL,
    Is_Counted INTEGER NOT NULL DEFAULT 0,
    Pro_ImageUrl TEXT,
    UpdatedAt TEXT NOT NULL,
    IsSynced INTEGER NOT NULL DEFAULT 0,
    IsDeleted INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS Orders (
    Id TEXT PRIMARY KEY NOT NULL,
    Order_Type INTEGER NOT NULL,
    Order_Date TEXT NOT NULL,
    Person_ID TEXT,
    User_ID TEXT NOT NULL,
    Price REAL NOT NULL,
    Unpaid REAL NOT NULL,
    Paid REAL NOT NULL,
    Person_Name TEXT,
    Parent_Order_Id TEXT,
    UpdatedAt TEXT NOT NULL,
    IsSynced INTEGER NOT NULL DEFAULT 0,
    IsDeleted INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS Order_Details (
    Id TEXT PRIMARY KEY NOT NULL,
    Order_ID TEXT NOT NULL,
    Pro_ID TEXT NOT NULL,
    Pro_Qty REAL NOT NULL,
    Pro_Price REAL NOT NULL,
    Pro_BuyPrice REAL,
    UpdatedAt TEXT NOT NULL,
    IsSynced INTEGER NOT NULL DEFAULT 0,
    IsDeleted INTEGER NOT NULL DEFAULT 0
);

-- (يمكنك إضافة باقي الجداول بنفس النمط لاحقاً: Persons, Expenses, Partners, etc)