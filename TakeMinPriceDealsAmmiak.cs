using System;
using System.Collections.Generic;
using System.Data.SQLite;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System.Text.RegularExpressions;

class Program
{
    static void Main(string[] args)
    {
        var driverPath = @"C:\my_jobs(only)\Auction_Stalcraft\StalcraftParser";
        var options = new ChromeOptions();
        // options.AddArgument("--headless"); // Без графического интерфейса
        var driver = new ChromeDriver(driverPath, options);

        try
        {
            driver.Navigate().GoToUrl("https://stalcraft-monitor.ru/auction?item=40vn");
            WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(60));

            float minPrice = CatchPrice(driver, wait);
            var deals = ParseDeals(driver, wait);

            SaveDataToDatabase(minPrice, deals);

            // Вычисляем процентиль 80% для аммиака за последние сутки
            Percentile("https://stalcraft-monitor.ru/auction?item=40vn", 0.8f);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Произошла ошибка: " + ex.Message);
        }
        finally
        {
            driver.Quit();
        }
    }

    static void Percentile(string itemUrl, float percentile)
    {
        string databasePath = "AuctionData.db";
        using (var connection = new SQLiteConnection($"Data Source={databasePath};Version=3;"))
        {
            connection.Open();

            // Выбираем данные за последние сутки для конкретного предмета
            string query = @"
                SELECT Price / Quantity AS PricePerItem
                FROM Deals
                WHERE ItemUrl = @ItemUrl
                  AND DealDateTime >= datetime('now', '-1 day')
                ORDER BY PricePerItem ASC;";

            using (var command = new SQLiteCommand(query, connection))
            {
                command.Parameters.AddWithValue("@ItemUrl", itemUrl);

                var prices = new List<float>();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        float pricePerItem = reader.GetFloat(0);
                        prices.Add(pricePerItem);
                    }
                }

                if (prices.Count == 0)
                {
                    Console.WriteLine("Нет данных за последние сутки.");
                    return;
                }

                // Вычисляем индекс для процентиля
                int index = (int)Math.Ceiling(prices.Count * percentile) - 1;
                index = Math.Max(0, Math.Min(index, prices.Count - 1)); // Ограничиваем индекс

                float percentileValue = prices[index];
                Console.WriteLine($"Процентиль {percentile * 100}% для {itemUrl}: {percentileValue}");
            }
        }
    }

    static float CatchPrice(IWebDriver driver, WebDriverWait wait)
    {
        IWebElement сatchPriceElement = wait.Until(d =>
        {
            var element = d.FindElement(By.Id("tempMinPrice"));
            return !string.IsNullOrEmpty(element.Text) ? element : null;
        });

        Console.WriteLine("Текст цены: " + сatchPriceElement.Text);
        return ExtractPrice(сatchPriceElement.Text);
    }

    static float ExtractPrice(string priceText)
    {
        // Заменяем запятую на точку
        priceText = priceText.Replace(",", "."); 
        // Удаляем все нецифровые символы, кроме точки
        priceText = Regex.Replace(priceText, @"[^\d.]", "");
        Console.WriteLine("Текст после обработки: " + priceText);
        
        if (float.TryParse(priceText, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float price))
        {
            return price;
        }
        Console.WriteLine("Не удалось преобразовать цену в число. Текст: " + priceText);
        return 0;
    }

    static List<(DateTime DealDateTime, float Price, int Quantity, int EnchantLevel, string RowColor)> ParseDeals(IWebDriver driver, WebDriverWait wait)
    {
        IWebElement dropdownButton = wait.Until(d => d.FindElement(By.Id("dropdownMenuLinkCountLoadItems")));
        dropdownButton.Click();

        IWebElement option200 = wait.Until(d => d.FindElement(By.Id("200")));
        option200.Click();

        IWebElement loadButton = wait.Until(d => d.FindElement(By.XPath("//button[contains(text(), 'Загрузить')]")));
        loadButton.Click();

        wait.Until(d =>
        {
            var table = d.FindElement(By.Id("contentHistoryLoots"));
            return table.FindElements(By.TagName("tr")).Count > 0;
        });

        var rows = driver.FindElements(By.CssSelector("#contentHistoryLoots tr"));
        var deals = new List<(DateTime DealDateTime, float Price, int Quantity, int EnchantLevel, string RowColor)>();
        Console.WriteLine("Найдено строк: " + rows.Count);

        foreach (var row in rows)
        {
            var cells = row.FindElements(By.TagName("td"));
            if (cells.Count >= 2)
            {
                string dateTimeText = cells[0].GetAttribute("innerText");
                string priceText = cells[1].GetAttribute("innerText");
                string rowColor = cells[0].GetAttribute("style").Replace("color: ", "").Replace(";", "").Trim();
                
                float price = ExtractPrice(priceText);

                // Разделение даты и количества/заточки
                string[] dateTimeParts = dateTimeText.Split(new[] { 'x', '+' }, StringSplitOptions.RemoveEmptyEntries);
                string cleanDateTimeText = dateTimeParts[0].Trim();
                int quantity = 1; // По умолчанию
                int enchantLevel = 1; // По умолчанию

                if (dateTimeText.Contains("x") && dateTimeParts.Length > 1)
                {
                    quantity = int.Parse(dateTimeParts[1]);
                }
                else if (dateTimeText.Contains("+") && dateTimeParts.Length > 1)
                {
                    enchantLevel = int.Parse(dateTimeParts[1]);
                }

                if (DateTime.TryParse(dateTimeText, out DateTime dealDateTime))
                {
                    Console.WriteLine("Найдена сделка с датой: " + dealDateTime);
                }
                else
                {
                    dealDateTime = new DateTime(1970, 1, 1);
                    Console.WriteLine("Не удалось распознать дату: " + dateTimeText);
                }

                deals.Add((dealDateTime, price, quantity, enchantLevel, rowColor));
            }
        }

        return deals;
    }

    static void SaveDataToDatabase(float minPrice, List<(DateTime DealDateTime, float Price, int Quantity, int EnchantLevel, string RowColor)> deals)
    {
        string databasePath = "AuctionData.db";
        using (var connection = new SQLiteConnection($"Data Source={databasePath};Version=3;"))
        {
            connection.Open();

            // Создание таблицы Deals, если она не существует
            string createDealsTableQuery = @"
                CREATE TABLE IF NOT EXISTS Deals (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ItemUrl TEXT NOT NULL,
                    DealDateTime DATETIME NOT NULL,
                    Quantity INTEGER NOT NULL,
                    Price REAL NOT NULL,
                    EnchantLevel INTEGER NOT NULL,
                    RowColor TEXT NOT NULL
                );";
            using (var command = new SQLiteCommand(createDealsTableQuery, connection))
            {
                command.ExecuteNonQuery();
            }

            // Получаем самую "молодую" дату из базы данных
            DateTime latestDealDateTime = GetLatestDealDateTime(connection);

            float liquidity = CalculateLiquidity(deals);
            DateTime lastUpdated = DateTime.Now;

            // Обновление таблицы Items
            string updateItemQuery = @"
                UPDATE Items
                SET MinPrice = @MinPrice, Liquidity = @Liquidity, LastUpdated = @LastUpdated
                WHERE Url = @Url;";
            using (var command = new SQLiteCommand(updateItemQuery, connection))
            {
                command.Parameters.AddWithValue("@MinPrice", minPrice);
                command.Parameters.AddWithValue("@Liquidity", liquidity);
                command.Parameters.AddWithValue("@LastUpdated", lastUpdated);
                command.Parameters.AddWithValue("@Url", "https://stalcraft-monitor.ru/auction?item=40vn");
                command.ExecuteNonQuery();
            }

            // Вставка данных в таблицу Deals
            string insertDealQuery = @"
                INSERT INTO Deals (ItemUrl, DealDateTime, Quantity, Price, EnchantLevel, RowColor)
                VALUES (@ItemUrl, @DealDateTime, @Quantity, @Price, @EnchantLevel, @RowColor);";
            foreach (var deal in deals)
            {
                // Пропускаем сделки, которые старше или равны самой "молодой" дате
                if (deal.DealDateTime >= latestDealDateTime)
                {
                    using (var command = new SQLiteCommand(insertDealQuery, connection))
                    {
                        command.Parameters.AddWithValue("@ItemUrl", "https://stalcraft-monitor.ru/auction?item=40vn");
                        command.Parameters.AddWithValue("@DealDateTime", deal.DealDateTime);
                        command.Parameters.AddWithValue("@Quantity", deal.Quantity);
                        command.Parameters.AddWithValue("@Price", deal.Price);
                        command.Parameters.AddWithValue("@EnchantLevel", deal.EnchantLevel);
                        command.Parameters.AddWithValue("@RowColor", deal.RowColor);
                        command.ExecuteNonQuery();
                    }
                }
            }
        }

        Console.WriteLine("Данные успешно записаны в таблицу.");
    }

    static DateTime GetLatestDealDateTime(SQLiteConnection connection)
    {
        string query = "SELECT MAX(DealDateTime) FROM Deals;";
        using (var command = new SQLiteCommand(query, connection))
        {
            var result = command.ExecuteScalar();
            if (result != null && result != DBNull.Value)
            {
                string dateTimeString = result.ToString();
                if (!string.IsNullOrEmpty(dateTimeString))
                {
                    return DateTime.Parse(dateTimeString);
                }
            }
        }
        return DateTime.MinValue; // Если база данных пуста
    }

    static float CalculateLiquidity(List<(DateTime DealDateTime, float Price, int Quantity, int EnchantLevel, string RowColor)> deals)
    {
        if (deals.Count == 0) return 24; // Если сделок нет, ликвидность 24 часа

        DateTime lastDealTime = deals.Last().DealDateTime;
        TimeSpan timeSinceLastDeal = DateTime.Now - lastDealTime;

        return (float)Math.Min(timeSinceLastDeal.TotalHours, 24); // Максимум 24 часа
    }
}