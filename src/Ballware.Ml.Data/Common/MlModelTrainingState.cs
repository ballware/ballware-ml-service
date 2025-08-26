namespace Ballware.Ml.Data.Common;

public class MlModelTrainingState
{
    public Guid Id { get; set; }
    public MlModelTrainingStates State { get; set; }
    public string? Result { get; set; }
}