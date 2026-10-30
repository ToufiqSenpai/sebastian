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

internal sealed class AgentConfig
{
    [Required]
    public string Name { get; set; } = null!;

    [Required]
    [ValidateObjectMembers]
    public AgentGeneralConfig General { get; set; } = null!;

    [Required]
    [ValidateEnumeratedItems]
    public List<ProviderConfig> Providers { get; set; } = [];
}

internal sealed class AgentGeneralConfig
{
    [Required]
    public string Provider { get; set; } = null!;

    [Required]
    public string Model { get; set; } = null!;
}

internal sealed class ProviderConfig
{
    [Required]
    public string Name { get; set; } = null!;

    public string? Endpoint { get; set; }

    [Required]
    public string ApiKey { get; set; } = null!;
}

[OptionsValidator]
internal sealed partial class ValidateSebastianConfig : IValidateOptions<SebastianConfig> { }
