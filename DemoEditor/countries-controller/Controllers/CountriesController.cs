using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.JsonPatch;
using MongoDB.Bson;
using MongoDB.Driver;
using Newtonsoft.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using countries_controller.Models;
using countries_controller.Tools;
using Microsoft.Extensions.Caching.Memory;
using System.Net.Mail;

namespace countries_controller.Controllers;

[ApiController]
[Route("[controller]")]
public class CountriesController : ControllerBase
{
    private readonly string ConnectionString;
    private readonly IMongoDatabase Database;
    private readonly ILogger<CountriesController> _logger;
    private IHttpClientFactory _clientFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IMemoryCache _memoryCache;

    private readonly IMongoCollection<Country> _countriesCollection;
    private const string _collectionName = "countries-bestsofar";
    private const string _cacheKey = "CountriesList";

    public CountriesController(IConfiguration config, ILogger<CountriesController> logger, IHttpClientFactory clientFactory, IHttpContextAccessor httpContextAccessor, IMemoryCache memoryCache)
    {
        _logger = logger;
        _clientFactory = clientFactory;
        _httpContextAccessor = httpContextAccessor;
        _memoryCache = memoryCache;
        ConnectionString = config.GetValue<string>("CountriesConnectionString") ?? "mongodb://db:27017";
        Database = new MongoClient(ConnectionString).GetDatabase("countries");

        _countriesCollection = Database.GetCollection<Country>(_collectionName);
    }

    [HttpGet] // GET /Countries
    [AllowAnonymous] // Permet au client Blazor de récupérer la liste
    public async Task<ActionResult<IEnumerable<Country>>> GetCountries()
    {
        _logger.LogInformation("Tentative de récupération de la liste des pays depuis le cache.");

        // Tente de récupérer depuis le cache
        if (!_memoryCache.TryGetValue(_cacheKey, out IEnumerable<Country> countriesList))
        {
            _logger.LogInformation("Cache vide. Récupération depuis la base de données.");
            
            // Si le cache est vide, on va chercher en BDD
            // On trie par nom pour un affichage cohérent
            countriesList = await _countriesCollection.Find(r => true)
                                                      .SortBy(c => c.Name)
                                                      .ToListAsync();

            // On configure les options du cache
            var cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(10)); // Expire après 10 min

            // On stocke dans le cache
            _memoryCache.Set(_cacheKey, countriesList, cacheEntryOptions);
        }
        else
        {
            _logger.LogInformation("Liste des pays récupérée depuis le cache.");
        }

        return Ok(countriesList);
    }

    // --- LECTURE (READ) ---
    [Authorize(Policy = "editor-apikey")]
    [HttpGet]
    public IActionResult Get(
    [FromQuery(Name = "$orderby")] string orderby = "",
    [FromQuery(Name = "$skip")] int skip = 0,
    [FromQuery(Name = "$top")] int top = 20,
    [FromQuery(Name = "$filter")] string filter = "")
    {
        // Démarrer avec le constructeur de filtre pour "Country"
        var builder = Builders<Country>.Filter;
        FilterDefinition<Country> queryFilter = null;

        if (!string.IsNullOrEmpty(filter))
        {
            // Filtrer par 'name' (ex: $filter=name eq 'France')
            int posName = filter.IndexOf("name eq '");
            if (posName > -1)
            {
                int posEndName = filter.IndexOf("'", posName + 9); // "name eq '" = 9 chars
                string name = filter.Substring(posName + 9, posEndName - posName - 9);
                if (queryFilter is null)
                    queryFilter = builder.Eq(x => x.Name, name);
                else
                    queryFilter &= builder.Eq(x => x.Name, name);
            }

            // Filtrer par 'isoCode' (ex: $filter=isoCode eq 'FR')
            int posISOCode = filter.IndexOf("isoCode eq '");
            if (posISOCode > -1)
            {
                int posEndISOCode = filter.IndexOf("'", posISOCode + 12); // "isoCode eq '" = 12 chars
                string isoCode = filter.Substring(posISOCode + 12, posEndISOCode - posISOCode - 12);
                if (queryFilter is null)
                    queryFilter = builder.Eq(x => x.ISOCode, isoCode);
                else
                    queryFilter &= builder.Eq(x => x.ISOCode, isoCode);
            }

            // Filtrer par 'entityId' (ex: $filter=entityId eq 'fr')
            int posEntityId = filter.IndexOf("entityId eq '");
            if (posEntityId > -1)
            {
                int posEndEntityId = filter.IndexOf("'", posEntityId + 13); // "entityId eq '" = 13 chars
                string entityId = filter.Substring(posEntityId + 13, posEndEntityId - posEntityId - 13);
                if (queryFilter is null)
                    queryFilter = builder.Eq(x => x.EntityId, entityId);
                else
                    queryFilter &= builder.Eq(x => x.EntityId, entityId);
            }
        }

        // Utiliser la variable _countriesCollection (supposée définie dans votre contrôleur)
        var query = queryFilter is null
            ? _countriesCollection.Find(x => true)
            : _countriesCollection.Find(queryFilter);

        // La logique de tri (ORDER BY) est générique et reste inchangée
        if (!string.IsNullOrEmpty(orderby))
        {
            string jsonSort = string.Empty;
            foreach (string item in orderby.Split(','))
            {
                if (item.ToLower().EndsWith(" desc"))
                    jsonSort += "," + item.Split(' ')[0] + ":-1";
                else if (item.ToLower().EndsWith(" asc"))
                    jsonSort += "," + item.Split(' ')[0] + ":1";
                else if (item.Length > 0)
                    jsonSort += "," + item + ":1";
            }
            if (!string.IsNullOrEmpty(jsonSort))
            {
                query.Sort("{" + jsonSort.Substring(1) + "}");
            }
        }

        // La logique de pagination (SKIP/TOP) est générique et reste inchangée
        var result = query.Skip(skip).Limit(top).ToList();
        return new JsonResult(result);
    }

    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<ActionResult<Country>> GetCountryById(string id)
    {
        var filter = Builders<Country>.Filter.Eq(c => c.EntityId, id);
        var country = await _countriesCollection.Find(filter).FirstOrDefaultAsync();

        if (country == null)
        {
            return NotFound($"Country with EntityId '{id}' not found.");
        }

        return Ok(country);
    }

    [Authorize(Policy = "editor")]
    [HttpGet]
    [Route("$count")]
    public long GetCountriesCount()
    {
        return _countriesCollection.CountDocuments(r => true);
    }

    // --- CRÉATION (CREATE) ---
    [Authorize(Policy = "editor")]
    public async Task<ActionResult<Country>> CreateCountry([FromBody] Country newCountry)
    {
        if (newCountry == null)
        {
            return BadRequest("Country object is null.");
        }

        var existing = await _countriesCollection.Find(c => c.EntityId == newCountry.EntityId).FirstOrDefaultAsync();
        if (existing != null)
        {
            return Conflict($"Un pays avec l'EntityId '{newCountry.EntityId}' existe déjà.");
        }

        await _countriesCollection.InsertOneAsync(newCountry);

        _memoryCache.Remove(_cacheKey);
        _logger.LogInformation("Cache des pays invalidé (création).");

        return CreatedAtAction(nameof(GetCountryById), new { id = newCountry.EntityId }, newCountry);
    }

    // --- MISE À JOUR (UPDATE) ---
    [HttpPut("{id}")]
    [Authorize(Policy = "editor")]
    public async Task<IActionResult> UpdateCountry(string id, [FromBody] Country updatedCountry)
    {
        if (id != updatedCountry.EntityId)
        {
            return BadRequest("L'ID de la route ne correspond pas à l'EntityId du pays.");
        }

        var filter = Builders<Country>.Filter.Eq(c => c.EntityId, id);

        var existingCountry = await _countriesCollection.Find(filter).FirstOrDefaultAsync();
        if (existingCountry == null)
        {
            return NotFound($"Country with EntityId '{id}' not found.");
        }

        updatedCountry.TechnicalId = existingCountry.TechnicalId;

        var result = await _countriesCollection.ReplaceOneAsync(filter, updatedCountry);

        if (result.ModifiedCount == 0 && result.MatchedCount == 0)
        {
            return NotFound($"Country with EntityId '{id}' not found.");
        }

        _memoryCache.Remove(_cacheKey);
        _logger.LogInformation("Cache des pays invalidé (mise à jour PUT).");

        return NoContent();
    }

    [HttpPatch("{id}")]
    [Authorize(Policy = "editor")]
    public async Task<IActionResult> PatchCountry(string id, [FromBody] JsonPatchDocument<Country> patchDoc)
    {
        if (patchDoc == null)
        {
            return BadRequest("Patch document is null.");
        }

        var filter = Builders<Country>.Filter.Eq(c => c.EntityId, id);
        var country = await _countriesCollection.Find(filter).FirstOrDefaultAsync();

        if (country == null)
        {
            return NotFound($"Country with EntityId '{id}' not found.");
        }

        patchDoc.ApplyTo(country, ModelState);

        if (!TryValidateModel(country))
        {
            return ValidationProblem(ModelState);
        }

        await _countriesCollection.ReplaceOneAsync(filter, country);

        _memoryCache.Remove(_cacheKey);
        _logger.LogInformation("Cache des pays invalidé (mise à jour PATCH).");

        return NoContent();
    }

    // --- SUPPRESSION (DELETE) ---
    [HttpDelete("{id}")]
    [Authorize(Policy = "editor")]
    public async Task<IActionResult> DeleteCountry(string id)
    {
        var filter = Builders<Country>.Filter.Eq(c => c.EntityId, id);
        var result = await _countriesCollection.DeleteOneAsync(filter);

        if (result.DeletedCount == 0)
        {
            return NotFound($"Country with EntityId '{id}' not found.");
        }

        _memoryCache.Remove(_cacheKey);
        _logger.LogInformation("Cache des pays invalidé (suppression).");

        return NoContent();
    }
}