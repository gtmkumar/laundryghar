using core.Application.Engagement.Cms.Dtos;
using laundryghar.SharedDataModel.Entities.EngagementCms;

namespace core.Application.Engagement.Cms.MobileAppConfigs.Common;

internal static class MobileAppConfigMapping
{
    /// <summary>Copies every client-supplied field onto <paramref name="entity"/>. Server-owned
    /// fields (id, brand, audit, status) are set by the caller.</summary>
    public static MobileAppConfig ApplyFields(this MobileAppConfig entity, MobileAppConfigFields f)
    {
        entity.AppType = f.AppType;
        entity.Platform = f.Platform;
        entity.ConfigKey = f.ConfigKey;
        entity.ConfigValue = f.ConfigValue;
        entity.Description = f.Description;
        entity.IsForceUpdate = f.IsForceUpdate;
        entity.MinAppVersion = f.MinAppVersion;
        entity.MaxAppVersion = f.MaxAppVersion;
        entity.TargetSegments = f.TargetSegments;
        entity.RolloutPercent = f.RolloutPercent;
        entity.IsActive = f.IsActive;
        return entity;
    }
}
