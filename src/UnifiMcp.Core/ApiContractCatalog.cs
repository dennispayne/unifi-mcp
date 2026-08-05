using System.Text.Json.Nodes;
using Unifi.Mcp.Client;

namespace UnifiMcp.Core;

internal static class ApiContractCatalog
{
    private static readonly Lazy<IReadOnlyDictionary<UniFiServiceKind, JsonObject>> Contracts = new(Load);

    public static JsonObject GetOperationDetails(UniFiServiceKind service, string operationId)
    {
        if (!Contracts.Value.TryGetValue(service, out var contract))
        {
            throw new InvalidOperationException($"No API contract is available for {service}.");
        }

        var paths = contract["paths"]?.AsObject()
            ?? throw new InvalidOperationException($"The {service} API contract has no paths.");
        foreach (var pathEntry in paths)
        {
            var pathItem = pathEntry.Value?.AsObject();
            if (pathItem is null)
            {
                continue;
            }

            foreach (var method in new[] { "get", "post", "put", "patch", "delete" })
            {
                var operation = pathItem[method]?.AsObject();
                if (operation is null ||
                    !string.Equals(operation["operationId"]?.GetValue<string>(), operationId, StringComparison.Ordinal))
                {
                    continue;
                }

                var definitions = new JsonObject();
                var parameters = new JsonArray();
                AddArrayItems(parameters, pathItem["parameters"] as JsonArray);
                AddArrayItems(parameters, operation["parameters"] as JsonArray);
                var requestBody = operation["requestBody"]?.DeepClone();
                CollectReferences(contract, parameters, definitions);
                CollectReferences(contract, requestBody, definitions);

                return new JsonObject
                {
                    ["service"] = service.ToString(),
                    ["method"] = method.ToUpperInvariant(),
                    ["path"] = pathEntry.Key,
                    ["operationId"] = operationId,
                    ["summary"] = operation["summary"]?.DeepClone(),
                    ["description"] = operation["description"]?.DeepClone(),
                    ["tags"] = operation["tags"]?.DeepClone(),
                    ["parameters"] = parameters,
                    ["requestBody"] = requestBody,
                    ["referencedSchemas"] = definitions
                };
            }
        }

        throw new KeyNotFoundException($"No {service} API operation named '{operationId}' was found.");
    }

    private static IReadOnlyDictionary<UniFiServiceKind, JsonObject> Load() =>
        new Dictionary<UniFiServiceKind, JsonObject>
        {
            [UniFiServiceKind.SiteManager] = LoadResource("site-manager-openapi.json"),
            [UniFiServiceKind.Network] = LoadResource("network-openapi.json"),
            [UniFiServiceKind.Protect] = LoadResource("protect-openapi.json"),
            [UniFiServiceKind.Access] = LoadResource("access-openapi.json"),
            [UniFiServiceKind.Mobility] = LoadResource("mobility-openapi.json")
        };

    private static JsonObject LoadResource(string suffix)
    {
        var assembly = typeof(ApiContractCatalog).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(suffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded API contract '{suffix}' was not found.");
        return JsonNode.Parse(stream)?.AsObject()
            ?? throw new InvalidOperationException($"Embedded API contract '{suffix}' was invalid.");
    }

    private static void AddArrayItems(JsonArray target, JsonArray? source)
    {
        if (source is null)
        {
            return;
        }

        foreach (var item in source)
        {
            target.Add(item?.DeepClone());
        }
    }

    private static void CollectReferences(JsonObject contract, JsonNode? node, JsonObject definitions)
    {
        if (node is null)
        {
            return;
        }

        if (node is JsonObject objectNode)
        {
            if (objectNode["$ref"]?.GetValue<string>() is { } reference &&
                reference.StartsWith("#/components/schemas/", StringComparison.Ordinal))
            {
                var schemaName = reference["#/components/schemas/".Length..]
                    .Replace("~1", "/", StringComparison.Ordinal)
                    .Replace("~0", "~", StringComparison.Ordinal);
                if (!definitions.ContainsKey(schemaName) &&
                    contract["components"]?["schemas"]?[schemaName] is { } schema)
                {
                    definitions[schemaName] = schema.DeepClone();
                    CollectReferences(contract, definitions[schemaName], definitions);
                }
            }

            foreach (var property in objectNode.ToArray())
            {
                CollectReferences(contract, property.Value, definitions);
            }
        }
        else if (node is JsonArray arrayNode)
        {
            foreach (var item in arrayNode)
            {
                CollectReferences(contract, item, definitions);
            }
        }
    }
}
