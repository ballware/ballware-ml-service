using System.Runtime.Serialization;

namespace Ballware.Ml.Metadata;

public enum MlModelTrainingStates
{
    [EnumMember(Value = @"Unknown")]
    Unknown = 0,

    [EnumMember(Value = @"Outdated")]
    Outdated = 1,

    [EnumMember(Value = @"Queued")]
    Queued = 2,

    [EnumMember(Value = @"InProgress")]
    InProgress = 3,

    [EnumMember(Value = @"UpToDate")]
    UpToDate = 4,

    [EnumMember(Value = @"Error")]
    Error = 5,
}

public class UpdateMlModelTrainingStatePayload
{
    public Guid Id { get; set; }
    public MlModelTrainingStates State { get; set; }
    public string? Result { get; set; }
}

public enum ModelTypes
{
    [EnumMember(Value = "Regression")]
    Regression = 1
}

public class ModelMetadata
{
    public Guid Id { get; set; }
    public required string Identifier { get; set; }
    public ModelTypes Type { get; set; }
    public string? Options { get; set; }
}

public class ModelOptions
{
    public uint TrainingTime { get; set; }
    public string TrainingFileName { get; set; }
    public string TrainingLogFileName { get; set; }
}

public enum FieldTypes : int
{
    Bool = 0,
    Int = 1,
    Decimal = 2,
    Double = 3,
    String = 4,
    Date = 5,
    Datetime = 6,
    Time = 7
}

public class RegressionField
{
    public string Name { get; set; }
    public FieldTypes Type { get; set; }
}

public class RegressionOptions : ModelOptions
{
    public IEnumerable<RegressionField> FeatureFields { get; set; }
    public RegressionField PredictionField { get; set; }
}