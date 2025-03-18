-- Проверка данных для предмета "Блок данных «Гамма»"
SELECT 
    i.Name, 
    i.Url, 
    d.DealDateTime, 
    d.Price, 
    d.Quantity
FROM 
    Items i
LEFT JOIN 
    Deals d ON i.Url = d.ItemUrl
WHERE 
    i.Name = 'Блок данных «Гамма»'
ORDER BY 
    d.DealDateTime DESC;