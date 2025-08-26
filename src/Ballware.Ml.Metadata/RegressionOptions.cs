namespace Ballware.Ml.Metadata;

public class RegressionOptions : ModelOptions
{
    public IEnumerable<RegressionField> FeatureFields { get; set; }
    public RegressionField PredictionField { get; set; }
}