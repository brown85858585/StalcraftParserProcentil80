-- Создаем временную таблицу для хранения дубликатов
CREATE TEMPORARY TABLE TempDuplicates AS
SELECT MIN(Id) AS KeepId, ItemUrl, DealDateTime, Price
FROM Deals
GROUP BY ItemUrl, DealDateTime, Price
HAVING COUNT(*) > 1;

-- Удаляем дубликаты, оставляя только одну строку для каждой комбинации
DELETE FROM Deals
WHERE Id NOT IN (
    SELECT KeepId FROM TempDuplicates
)
AND (ItemUrl, DealDateTime, Price) IN (
    SELECT ItemUrl, DealDateTime, Price FROM TempDuplicates
);

-- Получаем количество удаленных строк
SELECT changes() AS DeletedRowsCount;

-- Удаляем временную таблицу
DROP TABLE TempDuplicates;