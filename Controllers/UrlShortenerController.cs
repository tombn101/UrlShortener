using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Swashbuckle.AspNetCore.Annotations;



namespace UrlShortener.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UrlShortenerController : ControllerBase
    {
        private readonly UrlShortenerContext _context;

        private readonly object _readLock = new object();  // Lock for reading operations
        private readonly object _writeLock = new object(); // Lock for writing operations
        private readonly Dictionary<string, UrlMapping> _cache = new Dictionary<string, UrlMapping>();
        private readonly Queue<string> _cacheOrder = new Queue<string>();
        private readonly int _maxCacheSize = 100; 


        public UrlShortenerController(UrlShortenerContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Shortens a given URL by generating a unique shortened URL.
        /// </summary>
        /// <remarks>
        /// If the original URL already exists in the database, the existing shortened URL will be returned.
        /// </remarks>
        /// <param name="originalUrl">The original URL to be shortened.</param>
        /// <returns>
        /// Returns an IActionResult with the shortened URL or an error response.
        /// </returns>
        /// <response code="200">Returns the shortened URL if successful.</response>
        /// <response code="400">If the original URL is null or empty.</response>
        /// <response code="500">If an error occurs during the process.</response>
        [HttpPost]
        [SwaggerOperation(Summary = "Shorten a URL", Description = "Returns a shortened URL.")]
        [SwaggerResponse(200, "Successfully shortened URL", typeof(string))]
        [SwaggerResponse(400, "Bad request", typeof(string))]
        public async Task<IActionResult> ShortenUrl([FromBody] string originalUrl)
        {
            try
            {
                if (string.IsNullOrEmpty(originalUrl))
                {
                    return BadRequest("The original URL cannot be empty.");
                }

                var existingMapping = await _context.UrlMappings.Find(m => m.OriginalUrl == originalUrl).FirstOrDefaultAsync();

                if (existingMapping != null)
                {
                    return Ok(existingMapping.ShortenedUrl);
                }

                string generatedShortenedUrl;
                UrlMapping urlMapping;

                lock (_writeLock)
                {                            
                    do
                    {
                        generatedShortenedUrl = GenerateShortenedUrl();
                    } while (_cache.ContainsKey(generatedShortenedUrl));

                    urlMapping = new UrlMapping
                    {
                        OriginalUrl = originalUrl,
                        ShortenedUrl = generatedShortenedUrl
                    };

                    lock (_readLock)
                    {
                        _cache[generatedShortenedUrl] = urlMapping;
                        _cacheOrder.Enqueue(generatedShortenedUrl);                    
                    }

                    lock (_readLock)
                    {
                        while (_cache.Count > _maxCacheSize)
                        {
                            var oldestKey = _cacheOrder.Dequeue();
                            _cache.Remove(oldestKey);
                        }
                    }

                }
                await _context.UrlMappings.InsertOneAsync(urlMapping);

                return Ok(urlMapping.ShortenedUrl);    
            }
            catch (ArgumentNullException ex)
            {
                Console.Error.WriteLine($"ArgumentNullException in ShortenUrl: {ex}");
                return BadRequest("OriginalUrl cannot be null.");
            }
            catch (InvalidOperationException ex)
            {
                Console.Error.WriteLine($"InvalidOperationException in ShortenUrl: {ex}");
                return StatusCode(500, "An error occurred while processing the request.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"An unexpected error occurred in ShortenUrl: {ex}");
                return StatusCode(500, "An error occurred while processing the request.");
            }
            
        }

        /// <summary>
        /// Generates a unique shortened URL.
        /// </summary>
        /// <remarks>
        /// The shortened URL is created using a cryptographic random number generator to ensure uniqueness.
        /// </remarks>
        /// <returns>
        /// Returns a string representing the unique shortened URL.
        /// </returns>
        /// <exception cref="Exception">
        /// Thrown if an error occurs during the URL generation process.
        /// </exception>
        private string GenerateShortenedUrl()
        {
            try
            {
                const string allowedChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
                const int length = 8;

                using var rng = new RNGCryptoServiceProvider();
                byte[] randomBytes = new byte[length];
                rng.GetBytes(randomBytes);

                var chars = randomBytes
                    .Select(b => allowedChars[b % allowedChars.Length]);

                var shortId = new string(chars.ToArray());
                return $"{Request.Scheme}://{Request.Host}/{shortId}";   
            }
            catch (Exception ex)
            {                
                Console.Error.WriteLine($"An error occurred in GenerateShortenedUrl: {ex}");                
                return "An error occurred in GenerateShortenedUrl";
            } 
        }

        /// <summary>
        /// Redirects to the original URL associated with the given shortened URL.
        /// </summary>
        /// <remarks>
        /// The method first checks the in-memory cache for the mapping. If the mapping is not found in the cache,
        /// it fetches the mapping from the database and adds it to the cache for future use.
        /// </remarks>
        /// <param name="shortenedUrl">The shortened URL to redirect to.</param>
        /// <returns>
        /// Returns an IActionResult representing the redirect to the original URL.
        /// If the shortened URL is not found, returns a 404 Not Found response.
        /// If an error occurs during the process, returns a 500 Internal Server Error response.
        /// </returns>
        [HttpGet("{shortenedUrl}")]
        [SwaggerOperation(Summary = "Redirect to original URL", Description = "Redirects to the original URL.")]
        [SwaggerResponse(302, "Successfully redirected", typeof(void))]
        [SwaggerResponse(404, "Shortened URL not found", typeof(string))]
        public async Task<IActionResult> RedirectUrl(string shortenedUrl)
        {
            try
            {
                shortenedUrl = WebUtility.UrlDecode(shortenedUrl);
                UrlMapping mapping;

                lock (_readLock)
                {
                    if (_cache.TryGetValue(shortenedUrl, out mapping))
                    {
                        lock (_writeLock)
                        {
                            _cacheOrder.Enqueue(shortenedUrl);
                        }
                        return Redirect(mapping.OriginalUrl);
                    }
                }

                mapping = await _context.UrlMappings.Find(m => m.ShortenedUrl == shortenedUrl).FirstOrDefaultAsync();
                if (mapping == null)
                {   
                    return NotFound("Shortened URL not found.");
                }

                lock (_writeLock)
                {
                    lock (_readLock)
                    {
                        _cache[shortenedUrl] = mapping;
                        _cacheOrder.Enqueue(shortenedUrl);
                    }

                    lock (_readLock)
                    {
                        while (_cache.Count > _maxCacheSize)
                        {
                            var oldestKey = _cacheOrder.Dequeue();
                            _cache.Remove(oldestKey);
                        }
                    }

                    return Redirect(mapping.OriginalUrl);                    
                }

            }
            catch (ArgumentNullException ex)
            {
                Console.Error.WriteLine($"ArgumentNullException in RedirectUrl: {ex}");
                return BadRequest("Shortened URL cannot be null.");
            }
            catch (InvalidOperationException ex)
            {
                Console.Error.WriteLine($"InvalidOperationException in RedirectUrl: {ex}");
                return StatusCode(500, "An error occurred while processing the request.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"An unexpected error occurred in RedirectUrl: {ex}");
                return StatusCode(500, "An error occurred while processing the request.");
            }
            
        }        
    }
}