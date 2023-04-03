using System.Text.Json.Serialization;
using Newtonsoft.Json.Linq;

namespace tech_cards_creator;

public record TechCardItem(
    string Discipline,
    string Id,
    bool Agreed)
{
    public TechCardItem(JObject json)
    : this(json.Value<string>("discipline")!, json.Value<string>("disciplineLoad")!, json.Value<string>("agreed") == "true")
    {
    }
}