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

    // --- LECTURE (READ) ---
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<Country>>> GetCountries()
    {
        _logger.LogInformation("Tentative de récupération de la liste des pays depuis le cache.");

        if (!_memoryCache.TryGetValue(_cacheKey, out IEnumerable<Country> countriesList))
        {
            _logger.LogInformation("Cache vide. Récupération depuis la base de données.");
            
            countriesList = await _countriesCollection.Find(r => true)
                                                        .SortBy(c => c.Name)
                                                        .ToListAsync();

            var cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(10));

            _memoryCache.Set(_cacheKey, countriesList, cacheEntryOptions);
        }
        else
        {
            _logger.LogInformation("Liste des pays récupérée depuis le cache.");
        }

        return Ok(countriesList);
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