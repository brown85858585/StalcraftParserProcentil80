-- Предметы с 2+ разными цветами (без учета заточки)
WITH MultiColorItems AS (
    SELECT 
        ItemUrl,
        Name,
        COUNT(DISTINCT RowColor) AS ColorCount
    FROM Deals
    GROUP BY ItemUrl, Name
    HAVING COUNT(DISTINCT RowColor) >= 2
),

-- Предметы с заточкой >1 (без учета цветов)
EnchantedItems AS (
    SELECT DISTINCT
        ItemUrl,
        Name,
        MAX(EnchantLevel) AS MaxEnchant
    FROM Deals
    WHERE EnchantLevel > 1
    GROUP BY ItemUrl, Name
)

-- Первая таблица: предметы с разными цветами
SELECT 
    'MultiColor' AS ItemType,
    ItemUrl,
    Name,
    ColorCount AS Value
FROM MultiColorItems

UNION ALL

-- Вторая таблица: предметы с заточкой
SELECT 
    'HighEnchant' AS ItemType,
    ItemUrl,
    Name,
    MaxEnchant AS Value
FROM EnchantedItems

ORDER BY ItemType DESC, Value DESC, Name;