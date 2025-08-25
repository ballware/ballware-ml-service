using Ballware.Ml.Data.Common;
using Ballware.Shared.Data.Persistables;

namespace Ballware.Ml.Data.Persistables;

public class MlModel : IEntity, IAuditable, ITenantable
{
    public virtual long? Id { get; set; }

    public virtual Guid Uuid { get; set; }

    public virtual Guid TenantId { get; set; }

    public virtual string? Identifier { get; set; }

    public virtual MlModelTypes Type { get; set; }

    public virtual string? TrainSql { get; set; }

    public virtual MlModelTrainingStates TrainState { get; set; }

    public virtual string? TrainResult { get; set; }

    public virtual string? Options { get; set; }

    public virtual Guid? CreatorId { get; set; }

    public virtual DateTime? CreateStamp { get; set; }

    public virtual Guid? LastChangerId { get; set; }

    public virtual DateTime? LastChangeStamp { get; set; }
}