delete from SaleItems;
delete from SalePayments;
delete from Sales;
delete from PurchaseItems;
delete from Purchases;
delete from StockAdjustments;
delete from CustomerPayments;
delete from CustomerLedgers
update Customers set balance=0;
update Products set CurrentStock=0,LastPurchasePrice=0;

select *from CustomerPayments;

select * from CustomerLedgers; select * from Customers;