using System;
using System.Globalization;
using System.IO;
using System.Net;
using Microsoft.Playwright;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Cookie = Microsoft.Playwright.Cookie;

namespace tech_cards_creator
{
    public class Program
    {
        // Нужно залогиниться вручную в браузере и скопировать из DevTools - Application - Cookie JSESSIONID:
        public const string JsessionidCookie = "";

        // На будущее: вероятно лучше полностью перейти на использование API.
        // Потому что фронт нечеловечески неудобен для его обхода через любые средства автоматизации браузеров :(
        // См, как реализовано получение всех дисциплин GetDisciplineTechCardsViaApi

        static async Task Main2()
        {
            using var client = CreateHttpClient();
            var res = await client.GetStringAsync($"https://brs.urfu.ru/mrd/mvc/mobile/discipline/fetch?year={2022}&termType={1}&course={3}&total=43&page=1&pageSize=300&search=%D0%A1%D0%BF%D0%B5%D1%86%D0%B8%D0%B0%D0%BB%D1%8C%D0%BD%D1%8B%D0%B9");
            var ans = JsonConvert.DeserializeObject<JObject>(res);
            var disciplines = (JArray)ans["content"];
            foreach (var discipline in disciplines)
            {
                discipline["group"] = discipline["group"].ToString().Replace(" ", "");
            }
        }

        public static async Task Main()
        {
            var techCards = await GetDisciplineTechCardsViaApi(2022, 2, 2, false);
            var context = await CreateBrowserContext();
            var page = await context.NewPageAsync();
            await page.GotoAsync("https://brs.urfu.ru/mrd/mvc/mobile#/");
            await AddAuthCookie(context); // можно добавить куки, только когда мы уже открыли какую-то страницу в нужном домене.
            foreach (var techCard in techCards)
            {
                // Вот этот if надо править, если хочется делать техкарты для чего-то другого:
                if (techCard.Discipline.StartsWith("Специальный курс") || techCard.Agreed)
                    continue;

                Console.WriteLine("Create tech card for course: " + techCard);
                await CreateTechCard(techCard, page);
            }
        }

        private static async Task CreateTechCard(TechCardItem techCard, IPage page)
        {
            await page.GotoAsync($"https://brs.urfu.ru/mrd/mvc/mobile/view/technologyCard/{techCard.Id}/intermediate#/");
            await SelectExamLoadType(page);
            await NextButtonClick(page);
            var buttons = page.Locator("#buttonpractice,#buttonlecture,#buttonlaboratory");
            var (loadTypeCount, lectureIndex) = await GetLectureLoadIndex(buttons);
            await buttons.Nth(0).ClickAsync();
            await page.WaitForNavigationAsync();
            for (var i = 0; i < loadTypeCount; i++)
            {
                await FillLoadType(page);
                if (i < loadTypeCount - 1)
                    await OpenLoadTypeSettings(page, i + 1);
            }

            await NextButtonClick(page);
            var inputFactors = page.Locator(".input-factor");
            var inputFactorsCount = await inputFactors.CountAsync();
            var iFactor = 0;
            // Заполняем таблицу по три числа в строке, по строке на каждую loadType
            while (iFactor < inputFactorsCount)
            {
                if (iFactor / 3 == lectureIndex)
                    await SetFactors(inputFactors, iFactor, 0.5, 0.5, 1.0 / loadTypeCount);
                else
                    await SetFactors(inputFactors, iFactor, 1, 0, 1.0 / loadTypeCount);
                iFactor += 3;
            }

            Thread.Sleep(1000); // Появляются всякие крутилки, которые если быстро кликать зависают.
            buttons = page.GetByText("Далее");
            await buttons.ClickAsync(); // этот клик не срабатывает как клик, а только как потеря фокуса у редактирования.
            await buttons.ClickAsync(); // поэтому есть второй Click

            // Тут должна быть плашка "Ошибок не обнаружено"

            Thread.Sleep(1000); // Появляются всякие крутилки, которые если быстро кликать зависают.
            buttons = page.GetByText("Согласовать тех. карту");
            if (await buttons.CountAsync() > 0)
            {
                await buttons.First.ClickAsync();

                // Это нужно, чтобы упасть, если не согласовалась ↓
                await page.GetByText("Тех. карта успешно согласована.").FocusAsync();
                Console.WriteLine("СОГЛАСОВАЛИ!");
            }
            else
            {
                // Это если у нас нет прав на согласование техкарт
                Console.WriteLine("Готова к согласованию");
            }
        }

        private static async Task<IBrowserContext> CreateBrowserContext()
        {
            var pw = await Playwright.CreateAsync();
            var browser = await pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions()
            {
                Headless = false,
                Timeout = 60000
            });

            var context = await browser.NewContextAsync();
            return context;
        }

        private static async Task AddAuthCookie(IBrowserContext context)
        {
            var cookie = new Cookie
            {
                Name = "JSESSIONID",
                Value = JsessionidCookie,
                Domain = "brs.urfu.ru",
                Path = "/mrd"
            };
            await context.AddCookiesAsync(new[] { cookie });
        }

        /// <summary>
        /// Реализовано без браузера через API, потому что так оказалось проще вытащить идентификаторы.
        /// Этот запрос самый тяжелый в БРС, поэтому хочется сделать его один раз, и запомнить все идентификаторы курсов,
        /// а не повторять этот запрос после обработки каждого курса.
        /// </summary>
        /// <param name="year">2022 - астрономический год, в котором начался учебный год</param>
        /// <param name="yearPart">1 для осеннего или 2 для весеннего</param>
        /// <param name="courseYear">1-4 - год обучения студентов</param>
        /// <returns></returns>
        private static async Task<List<TechCardItem>> GetDisciplineTechCardsViaApi(int year, int yearPart, int courseYear, bool its)
        {
            var path = its ? "module/fetch" : "discipline/page";
            var url = $"https://brs.urfu.ru/mrd/mvc/mobile/technologyCard/{path}?year={year}&termType={yearPart}&course={courseYear}&page=1&pageSize=100&search=";

            var cookieContainer = new CookieContainer();
            cookieContainer.Add(new System.Net.Cookie
            {
                Name = "JSESSIONID",
                Value = JsessionidCookie,
                Domain = "brs.urfu.ru",
                Path = "/mrd"
            });
            using var client = new HttpClient(new HttpClientHandler()
            {
                CookieContainer = cookieContainer
            });
            var res = await client.GetStringAsync(url);
            JArray disciplinesArray;
            if (its)
            {
                disciplinesArray = JsonConvert.DeserializeObject<JArray>(res)!;
            }
            else
            {
                var response = JsonConvert.DeserializeObject<JObject>(res);
                disciplinesArray = (JArray)response?["content"]!;
            }

            try
            {
                var disciplines = disciplinesArray
                    .Select(obj => new TechCardItem(
                        obj["discipline"]!.Value<string>()!,
                        obj["disciplineLoad"]!.Value<string>()!,
                        obj["agreed"]!.Value<string?>()?.ToLower() == "true" // эта дич, потому что в agreed иногда бывает null
                    ));
                return disciplines.ToList();
            }
            catch
            {
                Console.WriteLine(res);
                throw;
                throw;
            }

        }

        private static HttpClient CreateHttpClient()
        {
            var cookieContainer = new CookieContainer();
            cookieContainer.Add(new System.Net.Cookie
            {
                Name = "JSESSIONID",
                Value = JsessionidCookie,
                Domain = "brs.urfu.ru",
                Path = "/mrd"
            });
            var client = new HttpClient(new HttpClientHandler()
            {
                CookieContainer = cookieContainer
            });
            return client;
        }

        private static async Task SelectExamLoadType(IPage page)
        {
            var form = page.Locator(".k-input");
            var element = form.GetByText("Выберите вид занятий...");
            var count = await element.CountAsync();
            Console.WriteLine(count);
            if (count == 0)
                return;
            await element.First.ClickAsync();

            var lectures = page.Locator(".km-popup").GetByText("лекции");
            var practics = page.Locator(".km-popup").GetByText("практические занятия");
            var labs = page.Locator(".km-popup").GetByText("лабораторные занятия");
            if (await lectures.CountAsync() > 0)
            {
                await lectures.ClickAsync();
            }
            else if (await practics.CountAsync() > 0)
            {
                await practics.ClickAsync();
            }
            else if (await labs.CountAsync() > 0)
            {
                await labs.ClickAsync();
            }
            else
            {
                throw new InvalidOperationException("Alarm!");
            }
        }

        private static async Task OpenNotConfirmedCourse(IPage page, int index)
        {
            var candidate = page.GetByText("Нет");
            await candidate.Nth(index).ClickAsync();
            Console.WriteLine(await page.TitleAsync());
        }


        private static async Task OpenLoadTypeSettings(IPage page, int loadTypeIndex)
        {
            await page.Locator("#technologyCardTypeId").ClickAsync();
            var buttons = page.Locator("#buttonpractice,#buttonlecture,#buttonlaboratory");
            await buttons.Nth(loadTypeIndex).ClickAsync();
            await page.WaitForNavigationAsync();
        }

        private static async Task SelectLoadTypeSettings(IPage page, int loadTypeIndex)
        {
            var buttons = page.Locator("#buttonpractice,#buttonlecture,#buttonlaboratory");
            await buttons.Nth(loadTypeIndex + 1).ClickAsync();
            await page.WaitForNavigationAsync();
        }

        private static async Task<(int loadTypeCount, int lectureIndex)> GetLectureLoadIndex(ILocator buttons)
        {
            var loadTypeCount = await buttons.CountAsync();
            var lectureIndex = 0;
            for (var i = 0; i < loadTypeCount; i++)
            {
                var name = await buttons.Nth(i).InnerTextAsync();
                if (name == "лекции")
                    lectureIndex = i;
            }

            return (loadTypeCount, lectureIndex);
        }

        private static async Task SetFactors(ILocator inputFactors, int iFactor, double factorCurrent, double factorIntermediate, double loadTypeFactor)
        {
            await SetText(inputFactors.Nth(iFactor), factorCurrent.ToString(CultureInfo.CurrentCulture));
            Thread.Sleep(100); // Появляются всякие крутилки, которые если быстро кликать зависают.
            await SetText(inputFactors.Nth(iFactor + 1), factorIntermediate.ToString(CultureInfo.CurrentCulture));
            Thread.Sleep(100);
            await SetText(inputFactors.Nth(iFactor + 2), loadTypeFactor.ToString(CultureInfo.CurrentCulture));
            Thread.Sleep(100);
        }

        private static async Task NextButtonClick(IPage page)
        {
            var button = page.GetByText("Далее");
            await button.FocusAsync();
            await button.ClickAsync();
            await page.WaitForNavigationAsync();
        }

        private static async Task FillLoadType(IPage page)
        {
            var dz = page.Locator("#current-items li");
            var kmCount = await dz.CountAsync() - 1;
            if (kmCount == 0)
            {
                await page.GetByText("Добавить").ClickAsync();
                await SetText(page.Locator("#name"), "Домашняя работа");
                await SetText(page.Locator("#maxValue"), "100");
                await page.GetByText("Сохранить").ClickAsync();
            }
            else
            {
                var value = 100 / kmCount;
                for (int i = 0; i < kmCount; i++)
                {
                    await dz.Nth(i).ClickAsync();
                    await SetText(page.Locator("#maxValue"), value.ToString());
                    await page.GetByText("Сохранить").ClickAsync();
                }
            }
        }

        private static async Task SetText(ILocator editBox, string value)
        {
            // Вместо FillTextAsync, потому что он падает на попытке заполнить числовое поле.
            await editBox.SelectTextAsync();
            await editBox.TypeAsync(value);
        }
    }
}