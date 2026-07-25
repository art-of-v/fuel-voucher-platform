using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace FuelFlow.API.Features.Providers.SharedModels
{
    [Table("providers")]
    public class Provider
    {
        [Key]
        [StringLength(50)]
        public string Id { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        [Required]
        public bool IsActive { get; set; } = true;

        [Required]
        public ProviderConfig Config { get; set; } = new();

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    // Provider Configuration - Settings for each provider
    public class ProviderConfig
    {
        [Required]
        [StringLength(50)]
        public string LogoText { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        public string DefaultColor { get; set; } = "#00ff80";

        [Required]
        public string Template { get; set; } = "custom";

        [Required]
        public List<string> StationIds { get; set; } = new();

        [Required]
        public Dictionary<string, JsonElement> Settings { get; set; } = new();

        [Required]
        public Dictionary<string, JsonElement> ParsingRules { get; set; } = new();

        [Required]
        public List<string> DetectionKeywords { get; set; } = new();

        [Required]
        public List<string> FuelTypePatterns { get; set; } = new();
    }

    // Template definitions for quick provider setup
    public class ProviderTemplate
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string LogoText { get; set; } = string.Empty;
        public string DefaultColor { get; set; } = "#00ff80";
        public Dictionary<string, JsonElement> DefaultSettings { get; set; } = new();
        public Dictionary<string, JsonElement> ParsingRules { get; set; } = new();
        public List<string> DetectionKeywords { get; set; } = new();
        public List<string> FuelTypePatterns { get; set; } = new();
        public List<string> SampleVouchers { get; set; } = new();
    }

    // DTOs for API communication
    public record ProviderDto
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string? Description { get; init; }
        public bool IsActive { get; init; } = true;
        public ProviderConfigDto Config { get; init; } = new();
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
    }

    public record ProviderConfigDto
    {
        public string LogoText { get; init; } = string.Empty;
        public string DefaultColor { get; init; } = "#00ff80";
        public string Template { get; init; } = "custom";
        public List<string> StationIds { get; init; } = new();
        public Dictionary<string, JsonElement> Settings { get; init; } = new();
        public Dictionary<string, JsonElement> ParsingRules { get; init; } = new();
        public List<string> DetectionKeywords { get; init; } = new();
        public List<string> FuelTypePatterns { get; init; } = new();
    }

    public record ProviderTemplateDto
    {
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public string? LogoUrl { get; init; }
        public string DefaultColor { get; init; } = "#00ff80";
        public Dictionary<string, JsonElement> DefaultSettings { get; init; } = new();
        public Dictionary<string, JsonElement> ParsingRules { get; init; } = new();
        public List<string> DetectionKeywords { get; init; } = new();
        public List<string> FuelTypePatterns { get; init; } = new();
        public List<string> SampleVouchers { get; init; } = new();
    }

    // Create/Update DTOs
    public record CreateProviderDto
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public string? Description { get; init; }
        public bool IsActive { get; init; } = true;
        public ProviderConfigDto Config { get; init; } = new();
    }

    public record UpdateProviderDto
    {
        public required string Name { get; init; }
        public string? Description { get; init; }
        public bool IsActive { get; init; }
        public ProviderConfigDto Config { get; init; } = new();
    }
}