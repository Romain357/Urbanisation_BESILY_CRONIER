using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace countries_controller.Models;

// This class corresponds to a country as cached by the book referential
// It may not contain all the data in the complete country model
public class CountryCache
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

    // Mandatory constructor if another one is created, otherwise serialization does not work anymore
    public CountryCache()
    {
    }

    public CountryCache(Country country)
    {
        EntityId = country.EntityId;
        Name = country.Name;
        ISOCode = country.ISOCode;
    }

    public Country ConvertToCountry()
    {
        return new Country()
        {
            EntityId = this.EntityId,
            Name = this.Name,
            ISOCode = this.ISOCode
        };
    }
}