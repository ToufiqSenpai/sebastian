namespace Sebastian.Configuration;

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;

// CA1812: options types are created by the options infrastructure, not by visible `new`.
[SuppressMessage("Performance", "CA1812", Justification = "Instantiated by the options infrastructure.")]
internal sealed class SebastianConfig
{
    [Required]
    [ValidateObjectMembers]
    public GitHubConfig GitHub { get; set; } = null!;

    [Required]
    [ValidateObjectMembers]
    public AgentConfig Agent { get; set; } = null!;
}

internal sealed class GitHubConfig
{
    [Required]
    public string WebhookSecret { get; set; } = null!;

    [Range(1, int.MaxValue)]
    public int AppId { get; set; }

    [Required]
    public string PrivateKeyPath { get; set; } = null!;

    [Required]
    public string InstallationId { get; set; } = null!;
}

internal sealed class AgentConfig : IValidatableObject
{
    [Required]
    public string Name { get; set; } = null!;

    [Required]
    [ValidateObjectMembers]
    public AgentGeneralConfig General { get; set; } = null!;

    [Required]
    [ValidateEnumeratedItems]
    public List<ProviderConfig> Providers { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Providers is null)
        {
            yield break;
        }

        if (Providers.Count == 0)
        {
            yield return new ValidationResult("At least one agent provider must be configured.");
            yield break;
        }

        if (General is null)
        {
            yield break;
        }

        foreach (var duplicate in Providers
            .GroupBy(provider => provider.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1))
        {
            yield return new ValidationResult($"Agent provider '{duplicate.Key}' is configured more than once.");
        }

        if (!Providers.Any(provider =>
            string.Equals(provider.Name, General.Provider, StringComparison.OrdinalIgnoreCase)))
        {
            yield return new ValidationResult($"Agent provider '{General.Provider}' is not configured.");
        }
    }
}

internal sealed class AgentGeneralConfig
{
    [Required]
    public string Provider { get; set; } = null!;

    [Required]
    public string Model { get; set; } = null!;
}

internal sealed class ProviderConfig : IValidatableObject
{
    [Required]
    public string Name { get; set; } = null!;

    public string? Endpoint { get; set; }

    [Required]
    public string ApiKey { get; set; } = null!;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Endpoint is not null && !Uri.TryCreate(Endpoint, UriKind.Absolute, out _))
        {
            yield return new ValidationResult($"Agent provider '{Name}' has an invalid endpoint.");
        }
    }
}

[OptionsValidator]
internal sealed partial class ValidateSebastianConfig : IValidateOptions<SebastianConfig> { }
