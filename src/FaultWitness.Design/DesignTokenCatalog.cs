using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FaultWitness.Design;

public sealed class DesignTokenCatalog
{
    private static readonly Regex Alias = new("^\\{(?<id>[^{}]+)\\}$", RegexOptions.Compiled);
    private readonly IReadOnlyDictionary<string, Token> tokens;
    private readonly Dictionary<string, object> resolved = new(StringComparer.Ordinal);

    private DesignTokenCatalog(IReadOnlyDictionary<string, Token> tokens) => this.tokens = tokens;

    public static DesignTokenCatalog LoadDefault()
    {
        using var stream = typeof(DesignTokenCatalog).Assembly.GetManifestResourceStream("FaultWitness.Design.faultwitness.tokens.json")
            ?? throw new InvalidOperationException("Embedded FaultWitness token source is missing.");
        return Load(stream);
    }

    public static DesignTokenCatalog Load(Stream stream)
    {
        using var document = JsonDocument.Parse(stream);
        var flat = new Dictionary<string, Token>(StringComparer.Ordinal);
        Flatten(document.RootElement, string.Empty, null, flat);
        return new DesignTokenCatalog(flat);
    }

    public IReadOnlyCollection<string> Keys => tokens.Keys.ToArray();
    public object Resolve(string id) => Resolve(id, new HashSet<string>(StringComparer.Ordinal));
    public string Text(string id) => Convert.ToString(Resolve(id), CultureInfo.InvariantCulture) ?? string.Empty;
    public double Number(string id) => Convert.ToDouble(Resolve(id), CultureInfo.InvariantCulture);

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        foreach (var key in tokens.Keys)
        {
            try { _ = Resolve(key); }
            catch (Exception exception) { errors.Add($"{key}: {exception.Message}"); }
        }
        return errors;
    }

    private object Resolve(string id, HashSet<string> path)
    {
        if (resolved.TryGetValue(id, out var cached)) return cached;
        if (!tokens.TryGetValue(id, out var token)) throw new KeyNotFoundException($"Unknown token '{id}'.");
        if (!path.Add(id)) throw new InvalidOperationException($"Reference cycle at '{id}'.");
        object value = token.Value.ValueKind switch
        {
            JsonValueKind.String => ResolveString(token.Value.GetString()!, path),
            JsonValueKind.Number => token.Value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new InvalidOperationException("Only scalar design token values are supported by the Avalonia bridge.")
        };
        path.Remove(id); resolved[id] = value; return value;
    }

    private object ResolveString(string value, HashSet<string> path)
    {
        var match = Alias.Match(value);
        return match.Success ? Resolve(match.Groups["id"].Value, path) : value;
    }

    private static void Flatten(JsonElement node, string prefix, string? inheritedType, IDictionary<string, Token> target)
    {
        var type = node.TryGetProperty("$type", out var typeNode) ? typeNode.GetString() : inheritedType;
        foreach (var property in node.EnumerateObject())
        {
            if (property.Name.StartsWith('$')) continue;
            var id = string.IsNullOrEmpty(prefix) ? property.Name : prefix + "." + property.Name;
            if (property.Value.ValueKind != JsonValueKind.Object) continue;
            if (property.Value.TryGetProperty("$value", out var value))
            {
                var tokenType = property.Value.TryGetProperty("$type", out var ownType) ? ownType.GetString() : type;
                target.Add(id, new(tokenType, value.Clone()));
            }
            else Flatten(property.Value, id, property.Value.TryGetProperty("$type", out var childType) ? childType.GetString() : type, target);
        }
    }

    private sealed record Token(string? Type, JsonElement Value);
}
