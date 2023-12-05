using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

public class UrlMapping
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; }

    [BsonElement("OriginalUrl")]
    public string OriginalUrl { get; set; }

    [BsonElement("ShortenedUrl")]
    public string ShortenedUrl { get; set; }
}