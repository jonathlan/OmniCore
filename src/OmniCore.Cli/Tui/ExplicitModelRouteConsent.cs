using System.Text.Json;
using OmniCore.Client;
using OmniCore.Host;

namespace OmniCore.Cli;

internal sealed record ExplicitModelRouteConsent(string SessionId, ModelRouteIdentityDto Route)
{
    internal bool Matches(string? sessionId, InteractionOverlayModel interaction)
    {
        if (sessionId != SessionId || interaction.Kind != "ModelRouteConsent"
            || !interaction.OptionIds.Contains("allow_route") || interaction.SubjectJson is null)
            return false;
        try
        {
            using var document = JsonDocument.Parse(interaction.SubjectJson);
            var subject = document.RootElement;
            return subject.GetProperty("routingConsent").GetInt32() == 1
                && subject.GetProperty("policyRevision").GetInt32() > 0
                && subject.GetProperty("providerId").GetString() == Route.ProviderId
                && subject.GetProperty("routeId").GetString() == Route.RouteId
                && subject.GetProperty("identityHash").GetString() == Route.IdentityHash
                && subject.GetProperty("billingMode").GetString() == Route.BillingMode;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
            or KeyNotFoundException or FormatException or OverflowException)
        {
            return false;
        }
    }
}
