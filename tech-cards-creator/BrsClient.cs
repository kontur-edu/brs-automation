using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Net;

namespace tech_cards_creator;

public class BrsClient : IDisposable
{
    private readonly string jsessionidCookie;
    private readonly HttpClient client;

    public BrsClient(string jsessionidCookie)
    {
        this.jsessionidCookie = jsessionidCookie;
        client = CreateHttpClient();
    }

    /// <summary>
    /// Реализовано без браузера через API, потому что так оказалось проще вытащить идентификаторы.
    /// Этот запрос самый тяжелый в БРС, поэтому хочется сделать его один раз, и запомнить все идентификаторы курсов,
    /// а не повторять этот запрос после обработки каждого курса.
    /// </summary>
    /// <param name="eduYear">2022 - астрономический год, в котором начался учебный год</param>
    /// <param name="yearPart">1 для осеннего или 2 для весеннего</param>
    /// <param name="courseNumber">1-4 - год обучения студентов</param>
    /// <returns></returns>
    public async Task<List<TechCardItem>> GetDisciplineTechCards(int eduYear, int yearPart, int courseNumber, bool its)
    {
        var path = its ? "module/fetch" : "discipline/page";
        var url = $"https://brs.urfu.ru/mrd/mvc/mobile/technologyCard/{path}?year={eduYear}&termType={yearPart}&course={courseNumber}&page=1&pageSize=100&search=";

        var res = await client.GetStringAsync(url);
        try
        {

            var disciplinesArray = its
                ? JsonConvert.DeserializeObject<JArray>(res)!
                : JsonConvert.DeserializeObject<JObject>(res)!["content"]!;
            return disciplinesArray.Select(json => new TechCardItem((JObject)json)).ToList();
        }
        catch
        {
            Console.WriteLine(res);
            throw;
        }

        //var disciplines = disciplinesArray
        //    .Select(obj => new TechCardItem(
        //        obj["discipline"]!.Value<string>()!,
        //        obj["disciplineLoad"]!.Value<string>()!,
        //        obj["agreed"]!.Value<bool>()!
        //        ));
        //return disciplines.ToList();

    }

    private HttpClient CreateHttpClient()
    {
        var cookieContainer = new CookieContainer();
        cookieContainer.Add(new System.Net.Cookie
        {
            Name = "JSESSIONID",
            Value = jsessionidCookie,
            Domain = "brs.urfu.ru",
            Path = "/mrd"
        });
        var client = new HttpClient(new HttpClientHandler()
        {
            CookieContainer = cookieContainer
        });
        return client;
    }

    public void Dispose()
    {
        client.Dispose();
    }
}