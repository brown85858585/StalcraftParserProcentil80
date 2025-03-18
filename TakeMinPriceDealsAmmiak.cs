using System;
using System.Collections.Generic;
using System.Data.SQLite;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System.Text.RegularExpressions;
using System.Linq;

class Program
{
    static void Main(string[] args)
    {
        var driverPath = @"C:\my_jobs(only)\Auction_Stalcraft\StalcraftParser";
        var options = new ChromeOptions();
        options.AddArgument("--headless"); // Без графического интерфейса
        var driver = new ChromeDriver(driverPath, options);

        try
        {
            // Список предметов, для которых нужно выполнить парсинг
            var itemNames = new List<string>
            {
                "Нестабильная аномальная батарея",
                "Камень Жмых-Вжух-Плюх",
                "Армейский аккумулятор",
                "Аммиак",
                "Стандартные инструменты",
                "Стандартные запчасти",
                "Продвинутые инструменты",
                "Продвинутые запчасти",
                "Дешевые инструменты",
                "Дешевые запчасти",
                "Цветущий рыжий папоротник",
                "Пси-маячок",
                "Портативный квантовый генератор",
                "Очищенное вещество 07270",
                "Блок данных «Гамма»"
            };

            // Получаем URL для каждого предмета из таблицы Items
            var items = GetItemsFromDatabase(itemNames);
            WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(100));

            // Парсим данные для каждого предмета
            foreach (var item in items)
            {
                Console.WriteLine($"Обрабатываем: {item.Name}");

                // Проверяем время последнего обновления
                DateTime lastUpdated = GetLastUpdatedFromDatabase(item.Url);
                if ((DateTime.Now - lastUpdated).TotalMinutes < 60)
                {
                    Console.WriteLine($"Последнее обновление было менее 10 минут назад. Пропускаем предмет: {item.Name}");
                    continue; // Переходим к следующему предмету
                }

                driver.Navigate().GoToUrl(item.Url);

                // Проверяем, активно ли модальное окно с сообщением о лимите запросов
                WaitLimitModalActive(driver, wait);

                float minPrice = CatchPrice(driver, wait);

                // Парсим сделки, если прошло больше часа с последнего обновления
                if ((DateTime.Now - lastUpdated).TotalHours >= 1)
                {
                    var deals = ParseDealsWithRetry(driver, wait);
                    SaveDataToDeals(deals, item.Url);
                }

                SaveDataToItems(minPrice, item.Url);
            }

            float percentile = 0.90f;
            Console.WriteLine($"\nПроцентиль {percentile * 100}");
            foreach (var item in items)
            {
                Percentile(item.Url, item.Name, percentile);
            }
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

    static void WaitLimitModalActive(IWebDriver driver, WebDriverWait wait){
        if (IsRateLimitModalActive(driver))
        {
            Console.WriteLine("Обнаружено сообщение 'Лимит запросов превышен'. Ожидание...");
            // Ждем, пока окно не исчезнет
            wait.Until(d => !IsRateLimitModalActive(d));
        }
    }

    static bool IsRateLimitModalActive(IWebDriver driver)
    {
        try
        {
            // Ищем модальное окно по ID
            var modal = driver.FindElement(By.Id("modelWaitRequest"));

            // Проверяем, видимо ли окно
            return modal.Displayed;
        }
        catch (NoSuchElementException)
        {
            // Если элемент не найден, окно не активно
            return false;
        }
    }

    static void Percentile(string itemUrl, string itemName, float percentile)
    {
        string databasePath = "AuctionData.db";
        using (var connection = new SQLiteConnection($"Data Source={databasePath};Version=3;"))
        {
            connection.Open();

            // Получаем минимальную цену из таблицы Items
            float minPrice = 0;
            string itemQuery = @"
                SELECT MinPrice
                FROM Items
                WHERE Url = @ItemUrl;";
            using (var itemCommand = new SQLiteCommand(itemQuery, connection))
            {
                itemCommand.Parameters.AddWithValue("@ItemUrl", itemUrl);
                using (var reader = itemCommand.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        minPrice = reader.GetFloat(0);
                    }
                }
            }

            // Выбираем данные за последние сутки для конкретного предмета
            string query = @"
                SELECT Price / Quantity AS PricePerItem, DealDateTime, Quantity
                FROM Deals
                WHERE ItemUrl = @ItemUrl
                  AND DealDateTime >= datetime('now', '-1 day')
                ORDER BY PricePerItem ASC;";

            using (var command = new SQLiteCommand(query, connection))
            {
                command.Parameters.AddWithValue("@ItemUrl", itemUrl);

                var prices = new List<float>();
                var quantities = new Dictionary<int, int>(); // Количество предметов и их частота
                DateTime oldestDeal = DateTime.MaxValue;
                DateTime newestDeal = DateTime.MinValue;

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        float pricePerItem = reader.GetFloat(0);
                        DateTime dealDateTime = reader.GetDateTime(1);
                        int quantity = reader.GetInt32(2);

                        prices.Add(pricePerItem);

                        // Обновляем частоту количества предметов
                        if (quantities.ContainsKey(quantity))
                            quantities[quantity]++;
                        else
                            quantities[quantity] = 1;

                        // Обновляем временной диапазон
                        if (dealDateTime < oldestDeal)
                            oldestDeal = dealDateTime;
                        if (dealDateTime > newestDeal)
                            newestDeal = dealDateTime;
                    }
                }

                if (prices.Count == 0)
                {
                    Console.WriteLine($"Нет данных за последние сутки для {itemName}.");
                    return;
                }

                // Вычисляем индекс для процентиля
                int index = (int)Math.Ceiling(prices.Count * percentile) - 1;
                index = Math.Max(0, Math.Min(index, prices.Count - 1)); // Ограничиваем индекс

                float percentileValue = prices[index];
                float purchasePrice = percentileValue * 0.9f; // Цена покупки

                // Находим наиболее частое количество предметов с ценой, близкой к percentileValue
                var relevantQuantities = new Dictionary<int, int>();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        float pricePerItem = reader.GetFloat(0);
                        int quantity = reader.GetInt32(2);

                        // Если цена за штуку близка к percentileValue, учитываем количество
                        if (Math.Abs(pricePerItem - percentileValue) < 0.01f)
                        {
                            if (relevantQuantities.ContainsKey(quantity))
                                relevantQuantities[quantity]++;
                            else
                                relevantQuantities[quantity] = 1;
                        }
                    }
                }

                int mostCommonQuantity = relevantQuantities.Count > 0
                    ? relevantQuantities.OrderByDescending(q => q.Value).First().Key
                    : quantities.OrderByDescending(q => q.Value).First().Key;

                string formattedPrice = FormatPrice(percentileValue);
                string formattedMinPrice = FormatPrice(minPrice);
                string formattedPurchasePrice = FormatPrice(purchasePrice);

                string comandToMen = minPrice < purchasePrice
                    ? $"Покупай ниже {formattedPurchasePrice}. Есть варианты"
                    : $"Покупай ниже {formattedPurchasePrice}. Жди рынка";

                // Выводим результат
                Console.WriteLine($"Аук {formattedMinPrice}, покупали {formattedPrice} х {mostCommonQuantity} штук. |{itemName}| {comandToMen}");
                // Console.WriteLine($"");
                // Console.WriteLine($"");
            }
        }
    }

    static List<(string Url, string Name)> GetItemsFromDatabase(List<string> itemNames)
    {
        var items = new List<(string Url, string Name)>();
        string databasePath = "AuctionData.db";

        using (var connection = new SQLiteConnection($"Data Source={databasePath};Version=3;"))
        {
            connection.Open();

            // Формируем SQL-запрос для поиска URL по названиям предметов
            string query = @"
                SELECT Url, Name
                FROM Items
                WHERE Name IN (" + string.Join(",", itemNames.ConvertAll(name => $"'{name}'")) + ");";

            using (var command = new SQLiteCommand(query, connection))
            {
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string url = reader.GetString(0);
                        string name = reader.GetString(1);
                        items.Add((url, name));
                    }
                }
            }
        }

        return items;
    }

    static string FormatPrice(float price)
    {
        // Разделяем целую и дробную части
        string[] parts = price.ToString("F2", System.Globalization.CultureInfo.InvariantCulture).Split('.');

        string integerPart = parts[0]; // Целая часть
        string fractionalPart = parts.Length > 1 ? parts[1] : "00"; // Дробная часть

        // Добавляем пробелы каждые три цифры в целой части
        for (int i = integerPart.Length - 3; i > 0; i -= 3)
        {
            integerPart = integerPart.Insert(i, " ");
        }

        // Собираем итоговую строку
        return $"{integerPart}.{fractionalPart} ₽";
    }

    static DateTime GetLastUpdatedFromDatabase(string itemUrl)
    {
        string databasePath = "AuctionData.db";
        using (var connection = new SQLiteConnection($"Data Source={databasePath};Version=3;"))
        {
            connection.Open();

            string query = @"
                SELECT LastUpdated
                FROM Items
                WHERE Url = @ItemUrl;";

            using (var command = new SQLiteCommand(query, connection))
            {
                command.Parameters.AddWithValue("@ItemUrl", itemUrl);
                var result = command.ExecuteScalar();
                if (result != null && result != DBNull.Value)
                {
                    return DateTime.Parse(result.ToString()!); // Добавляем "!" для уверенности, что значение не null
                }
            }
        }
        return DateTime.MinValue;
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
        // Console.WriteLine("Текст после обработки: " + priceText);

        if (float.TryParse(priceText, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float price))
        {
            return price;
        }
        Console.WriteLine("Не удалось преобразовать цену в число. Текст: " + priceText);
        return 0;
    }

    static List<(DateTime DealDateTime, float Price, int Quantity, int EnchantLevel, string RowColor)> ParseDealsWithRetry(IWebDriver driver, WebDriverWait wait, int retryCount = 3)
    {
        for (int i = 0; i < retryCount; i++)
        {
            try
            {
                return ParseDeals(driver, wait);
            }
            catch (StaleElementReferenceException)
            {
                Console.WriteLine($"Попытка {i + 1} из {retryCount}: stale element reference, повторяем...");
                if (i == retryCount - 1) throw;
            }
        }
        return new List<(DateTime DealDateTime, float Price, int Quantity, int EnchantLevel, string RowColor)>();
    }

    static List<(DateTime DealDateTime, float Price, int Quantity, int EnchantLevel, string RowColor)> ParseDeals(IWebDriver driver, WebDriverWait wait)
    {
        IWebElement dropdownButton = wait.Until(d => d.FindElement(By.Id("dropdownMenuLinkCountLoadItems")));
        dropdownButton.Click();

        IWebElement option200 = wait.Until(d => d.FindElement(By.Id("200")));
        option200.Click();

        IWebElement loadButton = wait.Until(d => d.FindElement(By.XPath("//button[contains(text(), 'Загрузить')]")));
        loadButton.Click();
        WaitLimitModalActive(driver, wait);

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
                var (dealDateTime, quantity, enchantLevel) = ParseDateTime(dateTimeText);

                Console.WriteLine("Дата: " + dealDateTime + ", Цена: " + price);
                deals.Add((dealDateTime, price, quantity, enchantLevel, rowColor));
            }
        }

        return deals;
    }

    static (DateTime DealDateTime, int Quantity, int EnchantLevel) ParseDateTime(string dateTimeText)
    {
        // Регулярное выражение для извлечения даты, времени, количества и заточки
        var match = Regex.Match(dateTimeText, @"(\d{2}\.\d{2}\.\d{4}, \d{2}:\d{2}:\d{2})(?:\s*(x\d+|\+\d+))?");
        if (!match.Success)
        {
            Console.WriteLine($"Не удалось распознать дату: {dateTimeText}");
            return (new DateTime(1970, 1, 1), 1, 1); // Возвращаем значения по умолчанию
        }

        // Извлекаем дату и время
        string cleanDateTimeText = match.Groups[1].Value.Trim();

        // Парсим дату и время
        if (!DateTime.TryParse(cleanDateTimeText, out DateTime dealDateTime))
        {
            Console.WriteLine($"Не удалось распознать дату: {cleanDateTimeText}");
            return (new DateTime(1970, 1, 1), 1, 1); // Возвращаем значения по умолчанию
        }

        // Извлекаем количество или заточку
        int quantity = 1; // По умолчанию
        int enchantLevel = 1; // По умолчанию

        if (match.Groups[2].Success)
        {
            string extraInfo = match.Groups[2].Value.Trim();
            if (extraInfo.StartsWith("x"))
            {
                quantity = int.Parse(extraInfo.Substring(1));
            }
            else if (extraInfo.StartsWith("+"))
            {
                enchantLevel = int.Parse(extraInfo.Substring(1));
            }
        }
        // Console.WriteLine("Дата: " + dealDateTime);
        return (dealDateTime, quantity, enchantLevel);
    }

    static void SaveDataToDeals(List<(DateTime DealDateTime, float Price, int Quantity, int EnchantLevel, string RowColor)> deals, string itemUrl)
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
                        command.Parameters.AddWithValue("@ItemUrl", itemUrl);
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

        Console.WriteLine("Данные успешно записаны в таблицу Deals.");
    }

    static void SaveDataToItems(float minPrice, string itemUrl)
    {
        string databasePath = "AuctionData.db";
        using (var connection = new SQLiteConnection($"Data Source={databasePath};Version=3;"))
        {
            connection.Open();

            // Обновление таблицы Items
            string updateItemQuery = @"
                UPDATE Items
                SET MinPrice = @MinPrice, LastUpdated = @LastUpdated
                WHERE Url = @Url;";
            using (var command = new SQLiteCommand(updateItemQuery, connection))
            {
                command.Parameters.AddWithValue("@MinPrice", minPrice);
                command.Parameters.AddWithValue("@LastUpdated", DateTime.Now);
                command.Parameters.AddWithValue("@Url", itemUrl);
                command.ExecuteNonQuery();
            }
        }

        Console.WriteLine("Данные успешно записаны в таблицу Items.");
    }

    static DateTime GetLatestDealDateTime(SQLiteConnection connection)
    {
        string query = "SELECT MAX(DealDateTime) FROM Deals;";
        using (var command = new SQLiteCommand(query, connection))
        {
            var result = command.ExecuteScalar();
            if (result != null && result != DBNull.Value)
            {
                string dateTimeString = result.ToString()!; // Добавляем "!" для уверенности, что значение не null
                if (!string.IsNullOrEmpty(dateTimeString))
                {
                    return DateTime.Parse(dateTimeString);
                }
            }
        }
        return DateTime.MinValue; // Если база данных пуста
    }
}