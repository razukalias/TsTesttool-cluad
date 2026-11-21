using System.Globalization;
using System.Text.Json;

namespace LoadTestingTool.Reporting;

public enum ResponseNodeKind { Object, Array, Value }

/// <summary>One node of a JSON response, with the path the runner addresses it by (<c>items[0].id</c>; the root has the empty path).</summary>
public sealed class ResponseNode
{
    public string Name { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public ResponseNodeKind Kind { get; init; }
    /// <summary>The text of a value (a string as it is, a number or true/false as written, null as empty).</summary>
    public string Value { get; init; } = string.Empty;
    /// <summary>Elements of an array or properties of an object, whatever the tree keeps of them.</summary>
    public int Count { get; init; }
    public int Depth { get; init; }
    public ResponseNode? Parent { get; init; }
    public List<ResponseNode> Children { get; } = [];
    /// <summary>The name has a dot or bracket in it, so a path to it cannot be read back unambiguously by the runner.</summary>
    public bool OddName { get; init; }
    /// <summary>An array has more elements than the tree keeps.</summary>
    public bool Cut => Kind == ResponseNodeKind.Array && Count > Children.Count;
    public bool IsLeaf => Kind == ResponseNodeKind.Value;
}

public enum NodeMode { AsOne, Flatten }

/// <summary>What the user chose for a node: take it as one header (a value, or the count of an array or object), or flatten what is below it.</summary>
/// <param name="Depth">Levels below the node to flatten; 0 = down to <see cref="ResponseTree.MaxDepth"/>.</param>
/// <param name="Elements">How many elements of an array are taken: 1 = the first only.</param>
public sealed record NodeChoice(NodeMode Mode, int Depth = 0, int Elements = 1);

public enum GeneratedKind { Value, Count }

/// <summary>One header the choices produce: the path without a verb or step (<c>items[0].id</c>, <c>items.#count</c>) and its value in the response shown.</summary>
public sealed record GeneratedHeader(string Path, string Value, GeneratedKind Kind, string SourcePath);

/// <summary>The JSON of a response as a tree the user can point at, and the headers that pointing produces.</summary>
public static class ResponseTree
{
    public const int MaxDepth = 8;
    public const int MaxHeaders = 500;
    public const int MaxElements = 100;
    private const int MaxNodes = 20_000;
    public const string CountSegment = "#count";

    public static ResponseNode? Parse(string json, out string? note)
    {
        note = null;
        if (string.IsNullOrWhiteSpace(json)) { note = "The response is empty."; return null; }
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var nodes = 0;
            var root = Build(document.RootElement, string.Empty, string.Empty, 0, null, ref nodes);
            if (nodes >= MaxNodes) note = $"A large response: only the first {MaxNodes:N0} nodes are shown.";
            return root;
        }
        catch (JsonException) { note = "The response is not JSON (XML and plain text have no tree)."; return null; }
    }

    private static ResponseNode Build(JsonElement element, string name, string path, int depth, ResponseNode? parent, ref int nodes)
    {
        nodes++;
        var odd = name.IndexOfAny(['.', '[', ']']) >= 0;
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var node = new ResponseNode { Name = name, Path = path, Kind = ResponseNodeKind.Object, Count = element.EnumerateObject().Count(), Depth = depth, Parent = parent, OddName = odd };
                foreach (var property in element.EnumerateObject())
                {
                    if (nodes >= MaxNodes) break;
                    node.Children.Add(Build(property.Value, property.Name, path.Length == 0 ? property.Name : path + "." + property.Name, depth + 1, node, ref nodes));
                }
                return node;
            }
            case JsonValueKind.Array:
            {
                var node = new ResponseNode { Name = name, Path = path, Kind = ResponseNodeKind.Array, Count = element.GetArrayLength(), Depth = depth, Parent = parent, OddName = odd };
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    if (index >= MaxElements || nodes >= MaxNodes) break;
                    var label = "[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                    node.Children.Add(Build(item, label, path + label, depth + 1, node, ref nodes));
                    index++;
                }
                return node;
            }
            case JsonValueKind.Null:
                return new ResponseNode { Name = name, Path = path, Kind = ResponseNodeKind.Value, Value = string.Empty, Depth = depth, Parent = parent, OddName = odd };
            case JsonValueKind.String:
                return new ResponseNode { Name = name, Path = path, Kind = ResponseNodeKind.Value, Value = element.GetString() ?? string.Empty, Depth = depth, Parent = parent, OddName = odd };
            default:
                return new ResponseNode { Name = name, Path = path, Kind = ResponseNodeKind.Value, Value = element.GetRawText(), Depth = depth, Parent = parent, OddName = odd };
        }
    }

    public static ResponseNode? Find(ResponseNode root, string path)
    {
        if (path.Length == 0) return root;
        var current = root;
        while (current is not null && current.Path != path)
            current = current.Children.FirstOrDefault(c => c.Path == path || path.StartsWith(c.Path + ".", StringComparison.Ordinal) || path.StartsWith(c.Path + "[", StringComparison.Ordinal));
        return current;
    }

    public static string CountPath(string path) => path.Length == 0 ? CountSegment : path + "." + CountSegment;

    /// <summary>
    /// The headers the choices produce, in document order. A node with a choice is handled by it (its own choices below it win over the flatten that reaches them);
    /// a node without one is only searched for chosen nodes. Deeper containers than the depth asked for are not expanded (<paramref name="skipped"/> counts them).
    /// </summary>
    public static List<GeneratedHeader> Generate(ResponseNode root, IReadOnlyDictionary<string, NodeChoice> choices, out bool truncated, out int skipped, int maxHeaders = MaxHeaders)
    {
        var result = new List<GeneratedHeader>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cut = false;
        var deep = 0;

        void Add(GeneratedHeader header)
        {
            if (result.Count >= maxHeaders) { cut = true; return; }
            if (seen.Add(header.Path)) result.Add(header);
        }

        void Emit(ResponseNode node, NodeChoice choice)
        {
            if (node.IsLeaf) { Add(new GeneratedHeader(node.Path, node.Value, GeneratedKind.Value, node.Path)); return; }
            if (choice.Mode == NodeMode.AsOne) { Add(new GeneratedHeader(CountPath(node.Path), node.Count.ToString(CultureInfo.InvariantCulture), GeneratedKind.Count, node.Path)); return; }
            Flatten(node, choice, choice.Depth <= 0 ? MaxDepth : choice.Depth);
        }

        void Flatten(ResponseNode node, NodeChoice choice, int levelsLeft)
        {
            var children = node.Kind == ResponseNodeKind.Array ? node.Children.Take(Math.Max(1, choice.Elements)) : node.Children;
            foreach (var child in children)
            {
                if (choices.TryGetValue(child.Path, out var own)) { Emit(child, own); continue; }
                if (child.IsLeaf) Add(new GeneratedHeader(child.Path, child.Value, GeneratedKind.Value, child.Path));
                else if (levelsLeft <= 1 || child.Depth >= MaxDepth) { if (child.Count > 0) deep++; }
                else Flatten(child, choice, levelsLeft - 1);
            }
        }

        void Visit(ResponseNode node)
        {
            if (choices.TryGetValue(node.Path, out var choice))
            {
                Emit(node, choice);
                if (choice.Mode == NodeMode.Flatten || node.IsLeaf) return;   // a flatten already handled what is below; a count leaves chosen descendants to be found
            }
            foreach (var child in node.Children) Visit(child);
        }

        Visit(root);
        truncated = cut;
        skipped = deep;
        return result;
    }

    /// <summary>The value of a generated header's path in another response (the row being filled), or null when that response does not have it.</summary>
    public static string? ValueAt(ResponseNode root, string path)
    {
        if (path.EndsWith("." + CountSegment, StringComparison.Ordinal) || path == CountSegment)
        {
            var basePath = path == CountSegment ? string.Empty : path[..^(CountSegment.Length + 1)];
            return Find(root, basePath) is { IsLeaf: false } container ? container.Count.ToString(CultureInfo.InvariantCulture) : null;
        }
        return Find(root, path) is { IsLeaf: true } leaf && leaf.Path == path ? leaf.Value : null;
    }
}
