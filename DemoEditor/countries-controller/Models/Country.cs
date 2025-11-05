using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization.Attributes;

namespace countries_controller.Models;

public class Country
{

    [BsonId()]
    [BsonRepresentation(BsonType.ObjectId)]
    [JsonIgnore]
    public ObjectId TechnicalId { get; set; }

    [BsonElement("entityId")]
    [Required(ErrorMessage = "L'identifiant métier du pays est obligatoire")]
    public string EntityId { get; set; } = string.Empty;

    [BsonElement("name")]
    [Required(ErrorMessage = "Le nom du pays est obligatoire")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("isoCode")]
    [Required(ErrorMessage = "Le code ISO est obligatoire")]
    public string ISOCode { get; set; } = string.Empty;

    public object Clone()
    {
        string serialized = JsonSerializer.Serialize(this);
        return JsonSerializer.Deserialize<Country>(serialized);
    }
}