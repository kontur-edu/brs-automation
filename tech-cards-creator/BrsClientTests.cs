using NUnit.Framework;

namespace tech_cards_creator;

[TestFixture]
public class BrsClientTests
{
    [Test]
    public async Task GetTechCards()
    {
        using var client = new BrsClient("BF69EC5F397ADC24AE4E93BF7AE9EAFF");
        var cards = await client.GetDisciplineTechCards(2022, 2, 1, false);
        foreach (var techCardItem in cards)
        {
            Console.WriteLine(techCardItem);
        }
    }
}