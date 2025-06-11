using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Ballware.Ml.Api.Endpoints;

public class QueryValueBag
{
    public Dictionary<string, object> Query { get; private set; } = new();

    public static ValueTask<QueryValueBag> BindAsync(HttpContext context, ParameterInfo parameter)
    {
        var dict = context.Request.Query.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray() as object);
        return ValueTask.FromResult<QueryValueBag?>(new QueryValueBag { Query = dict });
    }
}