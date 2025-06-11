using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Ballware.Ml.Api.Endpoints;

public class BodyValueBag
{
    public Dictionary<string, object> Value { get; private set; } = new();

    public static async ValueTask<BodyValueBag?> BindAsync(HttpContext context, ParameterInfo parameter)
    {
        context.Request.EnableBuffering();
        
        if (context.Request.Body.CanRead)
        {
            using var ms = new MemoryStream();
            await context.Request.Body.CopyToAsync(ms);
            context.Request.Body.Position = 0;

            try
            {
                JsonDocument doc = JsonDocument.Parse(ms.ToArray());

                var value = (Dictionary<string, object>)ReadElement(doc.RootElement);

                return new BodyValueBag() { Value = value };    
            }
            catch 
            {
                return null;
            }
        }

        return null;
    }
    
    private static object ReadElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(prop => prop.Name, prop => ReadElement(prop.Value)),
            JsonValueKind.Array => element.EnumerateArray()
                .Select(ReadElement).ToList(),
            JsonValueKind.String => element.GetString()!,
            JsonValueKind.Number when element.TryGetInt64(out var l) => l,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null!,
            _ => element.ToString()!
        };
    }
}