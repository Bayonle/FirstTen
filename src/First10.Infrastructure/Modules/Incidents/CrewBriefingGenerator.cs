using First10.Infrastructure.Modules.Intake.OpenAI;
using First10.Modules.Incidents;

namespace First10.Infrastructure.Modules.Incidents;

public sealed class CrewBriefingGenerator(
    ICrewBriefingOrderProvider provider,
    OpenAiSafetyIdentifier safetyIdentifier)
{
    public async Task<CrewBriefing> GenerateAsync(
        Incident incident,
        CancellationToken cancellationToken = default)
    {
        var projection = CrewBriefingProjection.From(incident);
        try
        {
            var result = await provider.OrderAsync(
                new CrewBriefingOrderRequest(
                    projection,
                    safetyIdentifier.Create($"incident:{incident.Id:N}")),
                cancellationToken);
            return CrewBriefingRenderer.Render(projection, result.OrderedClaimIds, true);
        }
        catch (CrewBriefingProviderException)
        {
            return CrewBriefingRenderer.RenderDeterministic(projection);
        }
        catch (CrewBriefingValidationException)
        {
            return CrewBriefingRenderer.RenderDeterministic(projection);
        }
    }
}
