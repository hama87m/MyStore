-- ١. هەموو داتاکان بسڕەوە
DELETE FROM CustomerLedgers;
DELETE FROM SalePayments;
DELETE FROM CustomerPayments;
DELETE FROM SaleItems;
DELETE FROM Sales;
DELETE FROM StockAdjustments;



-- ٢. Reset کردنی Identity Seeds
DBCC CHECKIDENT ('Sales', RESEED, 0);
DBCC CHECKIDENT ('Customers', RESEED, 0);
DBCC CHECKIDENT ('Products', RESEED, 0);
DBCC CHECKIDENT ('CustomerLedgers', RESEED, 0);