using Ballware.Ml.Metadata;
using Microsoft.ML;

namespace Ballware.Ml.Engine.AutoMl.Internal;

public class AutoMlPredictionContext
{
    public Type InputType { get; set; }
    public Type OutputType { get; set; }
    public ModelMetadata Metadata { get; set; }
    public MLContext Context { get; set; }
    public object PredictionEngine { get; set; }
}