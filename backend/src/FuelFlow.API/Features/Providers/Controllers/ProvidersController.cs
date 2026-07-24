using FuelFlow.Features.Providers.Repositories;
using FuelFlow.Features.Providers.Services;
using FuelFlow.SharedKernel.Domain;
using Microsoft.OpenApi;

namespace FuelFlow.Features.Providers.Controllers
{
    [ApiController]
    [Route("api/admin/providers")]
    [Authorize(Roles = "Admin")]
    public sealed class ProvidersController : ControllerBase
    {
        private readonly IProviderService _providerService;
        private readonly IMapper _mapper;

        public ProvidersController(
            IProviderService providerService,
            IMapper mapper)
        {
            _providerService = providerService;
            _mapper = mapper;
        }

        // GET: api/admin/providers
        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<ProviderDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
        {
            var providers = await _providerService.GetAllAsync();
            return Ok(providers);
        }

        // GET: api/admin/providers/{id}
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(ProviderDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> GetById(string id, CancellationToken cancellationToken)
        {
            var provider = await _providerService.GetByIdAsync(id);
            return Ok(provider);
        }

        // POST: api/admin/providers
        [HttpPost]
        [ProducesResponseType(typeof(ProviderDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> Create([FromBody] CreateProviderDto dto, CancellationToken cancellationToken)
        {
            var provider = await _providerService.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = provider.Id }, provider);
        }

        // PUT: api/admin/providers/{id}
        [HttpPut("{id}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> Update(string id, [FromBody] UpdateProviderDto dto, CancellationToken cancellationToken)
        {
            await _providerService.UpdateAsync(id, dto);
            return NoContent();
        }

        // DELETE: api/admin/providers/{id}
        [HttpDelete("{id}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
        {
            await _providerService.DeleteAsync(id);
            return NoContent();
        }
    }
}