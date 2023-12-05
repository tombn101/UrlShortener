using Microsoft.Extensions.Configuration;
using MongoDB.Driver;

public class UrlShortenerContext
{
    private readonly IMongoDatabase _database;

    public UrlShortenerContext(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("MongoDB");
        var client = new MongoClient(connectionString);
        _database = client.GetDatabase("UrlShortenerDb");
    }

    public IMongoCollection<UrlMapping> UrlMappings => _database.GetCollection<UrlMapping>("UrlMappings");
}
