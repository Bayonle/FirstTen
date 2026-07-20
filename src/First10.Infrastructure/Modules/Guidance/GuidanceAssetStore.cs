using First10.Infrastructure.Persistence;
using First10.Modules.Guidance;
using Microsoft.EntityFrameworkCore;

namespace First10.Infrastructure.Modules.Guidance;

public sealed class GuidanceAssetStore(First10DbContext database)
{
    public async Task<GuidanceTemplateSet?> SelectEnabledAsync(
        GuidancePurpose purpose,
        string trigger,
        GuidanceCategory category,
        GuidanceSeverityBand severity,
        Guid? proposedTemplateId = null,
        CancellationToken cancellationToken = default)
    {
        var candidates = await database.GuidanceTemplateSets
            .Include(x => x.Assets)
            .Where(x => x.Purpose == purpose
                        && x.Trigger == trigger
                        && x.EnabledAtUtc != null
                        && x.SupersededAtUtc == null)
            .ToArrayAsync(cancellationToken);
        return GuidanceTemplateSelector.Select(
            candidates,
            purpose,
            trigger,
            category,
            severity,
            proposedTemplateId);
    }
}
