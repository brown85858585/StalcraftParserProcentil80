-- Обновляем столбец LastUpdated, заменяя NULL на '0001-01-01 00:00:00'
UPDATE Items
SET Liquidity = '0001-01-01 00:00:00'
WHERE Liquidity IS NULL;

-- Проверяем результат
SELECT Url, Name, Liquidity
FROM Items;