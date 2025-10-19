using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SA_Project_API.Data;
using MySqlConnector;

namespace SA_Project_API.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class WeatherForecastController : ControllerBase
    {
        private static readonly string[] Summaries = new[]
        {
            "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
        };

        private readonly ILogger<WeatherForecastController> _logger;
        private readonly AppDbContext _db;

        public WeatherForecastController(ILogger<WeatherForecastController> logger, AppDbContext db)
        {
            _logger = logger;
            _db = db;
        }

        [HttpGet(Name = "GetWeatherForecast")]
        public IEnumerable<WeatherForecast> Get()
        {
            return Enumerable.Range(1, 5).Select(index => new WeatherForecast
            {
                Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                TemperatureC = Random.Shared.Next(-20, 55),
                Summary = Summaries[Random.Shared.Next(Summaries.Length)]
            })
            .ToArray();
        }

        // GET: /WeatherForecast/dbtest
        [HttpGet("dbtest")]
        public async Task<IActionResult> TestDatabaseConnection()
        {
            try
            {
                var canConnect = await _db.Database.CanConnectAsync();
                if (canConnect)
                {
                    return Ok("Database connection successful");
                }

                return StatusCode(500, "Cannot connect to the database");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while testing database connection");
                return StatusCode(500, ex.Message);
            }
        }

        // GET: /WeatherForecast/dbsetup
        // Attempts to create the database (if missing) and the tables based on the EF model.
        [HttpGet("dbsetup")]
        public async Task<IActionResult> EnsureDatabaseAndTables()
        {
            try
            {
                // Get the current connection string from the DbContext
                var fullCs = _db.Database.GetDbConnection().ConnectionString;
                var originalBuilder = new MySqlConnectionStringBuilder(fullCs);
                var dbName = originalBuilder.Database;

                if (string.IsNullOrEmpty(dbName))
                {
                    return BadRequest("Connection string does not specify a database name.");
                }

                // Build a connection string without the database to create the database if needed
                var masterBuilder = new MySqlConnectionStringBuilder(fullCs)
                {
                    Database = string.Empty
                };

                // Connect to server and create database if it doesn't exist
                await using (var conn = new MySqlConnection(masterBuilder.ConnectionString))
                {
                    await conn.OpenAsync();
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = $"CREATE DATABASE IF NOT EXISTS `{MySqlHelper.EscapeString(dbName)}` CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci;";
                    await cmd.ExecuteNonQueryAsync();
                }

                // Ensure EF creates tables for the model
                await _db.Database.EnsureCreatedAsync();

                return Ok($"Database '{dbName}' ensured and tables created (if missing).");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while creating database or tables");
                return StatusCode(500, ex.Message);
            }
        }
    }
}
