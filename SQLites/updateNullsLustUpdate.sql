-- Обновляем столбец LastUpdated, заменяя NULL на '0001-01-01 00:00:00'
UPDATE Items
SET LastUpdated = '0001-01-01 00:00:00'
WHERE LastUpdated IS NULL;

-- Проверяем результат
SELECT Url, Name, LastUpdated
FROM Items;