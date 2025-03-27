-- 1. Динамически создаем noPars на основе структуры Items
CREATE TABLE noPars AS 
SELECT * FROM Items 
WHERE 1 = 0;  -- Создаем пустую таблицу с такой же структурой

-- 2. Переносим данные
INSERT INTO noPars 
SELECT * FROM Items 
WHERE Liquidity = '0001-01-01 00:00:00';

-- 3. Удаляем перенесенные строки
DELETE FROM Items 
WHERE Liquidity = '0001-01-01 00:00:00';