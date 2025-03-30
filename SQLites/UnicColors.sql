SELECT 
    RowColor,
    COUNT(*) AS Count
FROM Deals
WHERE RowColor IS NOT NULL
GROUP BY RowColor
ORDER BY Count DESC, RowColor;