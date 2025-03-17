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

            decimal minPrice = CatchPrice(driver, wait);
            var deals = ParseDeals(driver, wait);

            SaveDataToDatabase(minPrice, deals);
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

    static decimal CatchPrice(IWebDriver driver, WebDriverWait wait)
    {
        IWebElement сatchPriceElement = wait.Until(d =>
        {
            var element = d.FindElement(By.Id("tempMinPrice"));
            return !string.IsNullOrEmpty(element.Text) ? element : null;
        });

        Console.WriteLine("Текст цены: " + сatchPriceElement.Text);
        return ExtractPrice(сatchPriceElement.Text);
    }

    static decimal ExtractPrice(string priceText)
    {
        Console.WriteLine("Исходный текст цены: " + priceText);
        priceText = priceText.Replace("&nbsp;", "").Replace("₽", "").Trim();
        priceText = priceText.Replace(" ", "").Replace(",", ".");
        Console.WriteLine("Текст после обработки: " + priceText);
        
        if (decimal.TryParse(priceText, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal price))
        {
            return price;
        }
        throw new FormatException("Не удалось преобразовать цену в число. Текст: " + priceText);
    }

    static List<(DateTime DealDateTime, decimal Price)> ParseDeals(IWebDriver driver, WebDriverWait wait)
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
        var deals = new List<(DateTime DealDateTime, decimal Price)>();

        foreach (var row in rows)
        {
            var cells = row.FindElements(By.TagName("td"));
            if (cells.Count >= 2)
            {
                string dateTimeText = cells[0].Text;
                string priceText = cells[1].Text;

                Console.WriteLine("cells[0].Text: " + cells[0].Text);
                Console.WriteLine("cells[1].Text: " + cells[1].Text);

                if (DateTime.TryParse(dateTimeText, out DateTime dealDateTime))
                {
                    decimal price = ExtractPrice(priceText);
                    deals.Add((dealDateTime, price));
                    Console.WriteLine("Найдена сделка с датой: " + dealDateTime);
                }
                else
                {
                    Console.WriteLine("Не удалось распознать дату: " + dateTimeText);
                }
            }
        }

        return deals;
    }

}