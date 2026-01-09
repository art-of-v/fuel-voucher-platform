using Dapper;
using FuelFlow.Api.Infrastructure;

namespace FuelFlow.Api.Features.Stations;

public interface IStationRepository
{
    Task<IEnumerable<Station>> GetAllAsync();
    Task<Station?> GetByIdAsync(string id);
    Task<Station> CreateAsync(CreateStationRequest request);
    Task<Station?> UpdateAsync(string id, UpdateStationRequest request);
    Task<bool> DeleteAsync(string id);
}

public class StationRepository : IStationRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public StationRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IEnumerable<Station>> GetAllAsync()
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryAsync<Station>(
            "SELECT id, name, color, logo_text as LogoText, lat, lng, created_at as CreatedAt FROM stations ORDER BY created_at DESC");
    }

    public async Task<Station?> GetByIdAsync(string id)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<Station>(
            "SELECT id, name, color, logo_text as LogoText, lat, lng, created_at as CreatedAt FROM stations WHERE id = @Id",
            new { Id = id });
    }

    public async Task<Station> CreateAsync(CreateStationRequest request)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<Station>(
            @"INSERT INTO stations (id, name, color, logo_text) 
              VALUES (@Id, @Name, @Color, @LogoText) 
              RETURNING id, name, color, logo_text as LogoText, lat, lng, created_at as CreatedAt",
            request);
    }

    public async Task<Station?> UpdateAsync(string id, UpdateStationRequest request)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<Station>(
            @"UPDATE stations 
              SET name = @Name, color = @Color, logo_text = @LogoText 
              WHERE id = @Id 
              RETURNING id, name, color, logo_text as LogoText, lat, lng, created_at as CreatedAt",
            new { Id = id, request.Name, request.Color, request.LogoText });
    }

    public async Task<bool> DeleteAsync(string id)
    {
        using var connection = _connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync("DELETE FROM stations WHERE id = @Id", new { Id = id });
        return affected > 0;
    }
}
