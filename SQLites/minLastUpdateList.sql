SELECT COUNT(*) as MinLiquidityCount
FROM Items
WHERE Liquidity = (SELECT MIN(Liquidity) FROM Items);

SELECT COUNT(*) as MinMinPriceCount
FROM Items
WHERE MinPrice = (SELECT MIN(MinPrice) FROM Items);

SELECT 
    MIN(Liquidity) as MinLiquidity,
    MAX(Liquidity) as MinLiquidity,
    MIN(MinPrice) as MinMinPrice,
    MAX(MinPrice) as MaxMinPrice,
    AVG(MinPrice) as AvgMinPrice,
    (
        SELECT MinPrice
        FROM Items
        ORDER BY MinPrice
        LIMIT 1
        OFFSET (SELECT COUNT(*) FROM Items) / 2
    ) AS MedianMinPrice
FROM Items;

SELECT *
FROM Items
ORDER BY MinPrice DESC;