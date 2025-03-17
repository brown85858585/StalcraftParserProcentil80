using System;
using System.Collections.Generic;
using System.Data.SQLite;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;

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

            WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(20));

            decimal minPrice = ExtractMinPrice(driver, wait);
            ParseAndSaveDeals(driver, wait, minPrice);
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

    static decimal ExtractMinPrice(IWebDriver driver, WebDriverWait wait)
    {
        IWebElement minPriceElement = wait.Until(d =>
        {
            var element = d.FindElement(By.Id("tempMinPrice"));
            return !string.IsNullOrEmpty(element.Text) ? element : null;
        });

        if (minPriceElement == null)
        {
            throw new Exception("Элемент с минимальной ценой пуст или не найден.");
        }

        Console.WriteLine("Текст минимальной цены: " + minPriceElement.Text); // Отладочная информация
        return ExtractPrice(minPriceElement.Text);
    }

    static void ParseAndSaveDeals(IWebDriver driver, WebDriverWait wait, decimal minPrice)
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

            // Обновление таблицы Items
            double liquidity = CalculateLiquidity(rows);
            DateTime lastUpdated = DateTime.Now;

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

            // Парсинг и запись данных
            foreach (var row in rows)
            {
                var cells = row.FindElements(By.TagName("td"));
                if (cells.Count >= 2)
                {
                    string dateTimeText = cells[0].Text;
                    string priceText = cells[1].Text;
                    string rowColor = cells[0].GetAttribute("style").Split(';')[0].Split(':')[1].Trim();

                    Console.WriteLine($"Ячейки: Дата/Время: {dateTimeText}, Цена: {priceText}, Цвет: {rowColor}");

                    if (DateTime.TryParse(dateTimeText, out DateTime dealDateTime))
                    {
                        decimal price = ExtractPrice(priceText);

                        // Вставка данных в таблицу Deals
                        string insertDealQuery = @"
                            INSERT INTO Deals (ItemUrl, DealDateTime, Quantity, Price, EnchantLevel, RowColor)
                            VALUES (@ItemUrl, @DealDateTime, @Quantity, @Price, @EnchantLevel, @RowColor);";
                        using (var command = new SQLiteCommand(insertDealQuery, connection))
                        {
                            command.Parameters.AddWithValue("@ItemUrl", "https://stalcraft-monitor.ru/auction?item=40vn");
                            command.Parameters.AddWithValue("@DealDateTime", dealDateTime);
                            command.Parameters.AddWithValue("@Quantity", 1); // Пример количества
                            command.Parameters.AddWithValue("@Price", price);
                            command.Parameters.AddWithValue("@EnchantLevel", 0); // Пример уровня заточки
                            command.Parameters.AddWithValue("@RowColor", rowColor);
                            command.ExecuteNonQuery();
                        }

                        Console.WriteLine($"Сделка добавлена: {dealDateTime}, {price}, {rowColor}");
                    }
                    else
                    {
                        Console.WriteLine("Не удалось распознать дату: " + dateTimeText);
                    }
                }
            }
        }

        Console.WriteLine("Данные успешно записаны в таблицу.");
    }

    static decimal ExtractPrice(string priceText)
    {
        Console.WriteLine("Исходный текст цены: " + priceText); // Отладочная информация

        priceText = priceText.Replace("&nbsp;", "").Replace("₽", "").Trim();
        priceText = priceText.Replace(" ", "").Replace(",", ".");
        Console.WriteLine("Текст после обработки: " + priceText); // Отладочная информация

        if (decimal.TryParse(priceText, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal price))
        {
            return price;
        }
        throw new FormatException("Не удалось преобразовать цену в число. Текст: " + priceText);
    }

    static double CalculateLiquidity(IReadOnlyCollection<IWebElement> rows)
    {
        if (rows.Count == 0) return 24; // Если сделок нет, ликвидность 24 часа

        // Пример расчета ликвидности на основе последней строки
        var lastRow = rows.Last();
        var cells = lastRow.FindElements(By.TagName("td"));
        if (cells.Count >= 2 && DateTime.TryParse(cells[0].Text, out DateTime lastDealTime))
        {
            TimeSpan timeSinceLastDeal = DateTime.Now - lastDealTime;
            return Math.Min(timeSinceLastDeal.TotalHours, 24); // Максимум 24 часа
        }

        return 24;
    }
}