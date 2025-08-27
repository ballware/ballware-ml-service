using Ballware.Shared.Authorization;

namespace Ballware.Ml.Metadata;

public class Tenant : ITenantAuthorizationMetadata
{
    public Guid Id { get; set; }
    public string? RightsCheckScript { get; set; }
}