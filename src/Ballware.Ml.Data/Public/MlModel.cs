using Ballware.Ml.Data.Common;
using Ballware.Shared.Data.Public;

namespace Ballware.Ml.Data.Public;

public class MlModel : IEditable
{
    public Guid Id { get; set; }

    public string? Identifier { get; set; }

    public MlModelTypes Type { get; set; }

    public string? TrainSql { get; set; }

    public MlModelTrainingStates TrainState { get; set; }

    public string? TrainResult { get; set; }

    public string? Options { get; set; }
}