-- Добавляем новый столбец Name в таблицу Deals
ALTER TABLE Deals
ADD COLUMN Name TEXT;

-- Заполняем столбец Name значениями из таблицы Items
UPDATE Deals
SET Name = (
    SELECT Items.Name
    FROM Items
    WHERE Items.Url = Deals.itemUrl
);

-- Проверяем результат
SELECT Id, itemUrl, Name
FROM Deals;