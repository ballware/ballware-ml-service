namespace Ballware.Ml.Data.Public;

public class ReportDatasourceTable
{
    public required string Name { get; set; }
    public required string Query { get; set; }

    public IEnumerable<ReportDatasourceRelation> Relations { get; set; } = [];
}

public class ReportDatasourceRelation
{
    public required string Name { get; set; }
    public required string ChildTable { get; set; }
    public required string MasterColumn { get; set; }
    public required string ChildColumn { get; set; }
}

public class ReportDatasourceDefinition
{
    public required string Name { get; set; }
    public required string Provider { get; set; }
    public required string ConnectionString { get; set; }
    public IEnumerable<ReportDatasourceTable> Tables { get; set; } = [];
}