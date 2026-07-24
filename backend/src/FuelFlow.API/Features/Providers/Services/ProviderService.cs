using FuelFlow.API.Features.Providers.Repositories;
using FuelFlow.API.Features.Providers.SharedModels;

namespace FuelFlow.API.Features.Providers.Services
{
    public interface IProviderService
    {
        Task<Provider> CreateAsync(CreateProviderDto dto);
        Task<ProviderDto> GetByIdAsync(string id);
        Task<IEnumerable<ProviderDto>> GetAllAsync();
        Task<IEnumerable<ProviderDto>> GetActiveAsync();
        Task UpdateAsync(string id, UpdateProviderDto dto);
        Task DeleteAsync(string id);
    }

    public class ProviderService(
        IProviderRepository repository) : IProviderService
    {
        public async Task<Provider> CreateAsync(CreateProviderDto dto)
        {
            // Validate provider ID format
            var providerId = dto.Id.ToLowerInvariant();
            if (!System.Text.RegularExpressions.Regex.IsMatch(providerId, "^[a-z0-9]+(?:-[a-z0-9]+)*$"))
            {
                throw new ArgumentException("Provider ID must be URL-friendly (lowercase letters, numbers, and hyphens only)");
            }

            if (await repository.ExistsAsync(providerId, CancellationToken.None))
            {
                throw new InvalidOperationException($"Provider with ID '{providerId}' already exists");
            }

            var provider = new Provider
            {
                Id = providerId,
                Name = dto.Name,
                Description = dto.Description,
                IsActive = dto.IsActive,
                Config = new ProviderConfig
                {
                    LogoText = dto.Config.LogoText,
                    DefaultColor = dto.Config.DefaultColor,
                    Template = dto.Config.Template,
                    StationIds = dto.Config.StationIds,
                    Settings = dto.Config.Settings
                },
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            return await repository.CreateAsync(provider, CancellationToken.None);
        }

        public async Task<ProviderDto> GetByIdAsync(string id)
        {
            var provider = await repository.GetByIdAsync(id, CancellationToken.None)
                ?? throw new KeyNotFoundException($"Provider with ID '{id}' not found");

            return MapToDto(provider);
        }

        public async Task<IEnumerable<ProviderDto>> GetAllAsync()
        {
            var providers = await repository.GetAllAsync(CancellationToken.None);
            return providers.Select(MapToDto);
        }

        public async Task<IEnumerable<ProviderDto>> GetActiveAsync()
        {
            var providers = await repository.GetActiveAsync(CancellationToken.None);
            return providers.Select(MapToDto);
        }

        public async Task UpdateAsync(string id, UpdateProviderDto dto)
        {
            var provider = await repository.GetByIdAsync(id, CancellationToken.None)
                ?? throw new KeyNotFoundException($"Provider with ID '{id}' not found");

            // Update properties
            provider.Name = dto.Name;
            provider.Description = dto.Description;
            provider.IsActive = dto.IsActive;
            provider.Config.LogoText = dto.Config.LogoText;
            provider.Config.DefaultColor = dto.Config.DefaultColor;
            provider.Config.Template = dto.Config.Template;
            provider.Config.StationIds = dto.Config.StationIds;
            provider.Config.Settings = dto.Config.Settings;
            provider.UpdatedAt = DateTime.UtcNow;

            await repository.UpdateAsync(provider, CancellationToken.None);
        }

        public async Task DeleteAsync(string id)
        {
            await repository.DeleteAsync(id, CancellationToken.None);
        }

        private static ProviderDto MapToDto(Provider provider)
        {
            return new ProviderDto
            {
                Id = provider.Id,
                Name = provider.Name,
                Description = provider.Description,
                IsActive = provider.IsActive,
                Config = new ProviderConfigDto
                {
                    LogoText = provider.Config.LogoText,
                    DefaultColor = provider.Config.DefaultColor,
                    Template = provider.Config.Template,
                    StationIds = provider.Config.StationIds,
                    Settings = provider.Config.Settings
                },
                CreatedAt = provider.CreatedAt,
                UpdatedAt = provider.UpdatedAt
            };
        }
    }
}