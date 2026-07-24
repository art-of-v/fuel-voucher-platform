using FuelFlow.API.Features.Providers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.API.Features.Providers.Repositories
{
    public interface IProviderRepository
    {
        Task<Provider?> GetByIdAsync(string id, CancellationToken cancellationToken);
        Task<IEnumerable<Provider>> GetAllAsync(CancellationToken cancellationToken);
        Task<Provider> CreateAsync(Provider provider, CancellationToken cancellationToken);
        Task UpdateAsync(Provider provider, CancellationToken cancellationToken);
        Task DeleteAsync(string id, CancellationToken cancellationToken);
        Task<bool> ExistsAsync(string id, CancellationToken cancellationToken);
        Task<IEnumerable<Provider>> GetActiveAsync(CancellationToken cancellationToken);
    }

    public class ProviderRepository(ApplicationDbContext context) : IProviderRepository
    {
        public async Task<Provider?> GetByIdAsync(string id, CancellationToken cancellationToken)
        {
            return await context.Providers
                .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        }

        public async Task<IEnumerable<Provider>> GetAllAsync(CancellationToken cancellationToken)
        {
            return await context.Providers
                .OrderBy(p => p.Name)
                .ToListAsync(cancellationToken);
        }

        public async Task<Provider> CreateAsync(Provider provider, CancellationToken cancellationToken)
        {
            context.Providers.Add(provider);
            await context.SaveChangesAsync(cancellationToken);
            return provider;
        }

        public async Task UpdateAsync(Provider provider, CancellationToken cancellationToken)
        {
            context.Providers.Update(provider);
            await context.SaveChangesAsync(cancellationToken);
        }

        public async Task DeleteAsync(string id, CancellationToken cancellationToken)
        {
            var provider = await context.Providers.FindAsync(new object[] { id }, cancellationToken);
            if (provider != null)
            {
                context.Providers.Remove(provider);
                await context.SaveChangesAsync(cancellationToken);
            }
        }

        public async Task<bool> ExistsAsync(string id, CancellationToken cancellationToken)
        {
            return await context.Providers.AnyAsync(p => p.Id == id, cancellationToken);
        }

        public async Task<IEnumerable<Provider>> GetActiveAsync(CancellationToken cancellationToken)
        {
            return await context.Providers
                .Where(p => p.IsActive)
                .OrderBy(p => p.Name)
                .ToListAsync(cancellationToken);
        }
    }
}