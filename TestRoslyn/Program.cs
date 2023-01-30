using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Differencing;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.MSBuild;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using System.Security;
using System.Xml.Linq;

var eventFinder = new EventFinder("D:\\repos\\TestRoslyn\\TestRoslyn.sln");
await eventFinder.Initialice();
await eventFinder.PrintReferences();

public partial class EventFinder
{
    private readonly string _path;
    private Solution Solution { get; set; }
    private List<Compilation> Compilations { get; set; } = new();

    public EventFinder(string path)
    {
        _path = path;
    }

    public async Task Initialice()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            var instances = MSBuildLocator.QueryVisualStudioInstances().ToArray();
            MSBuildLocator.RegisterInstance(instances.OrderByDescending(x => x.Version).First());
        }

        var workspace = MSBuildWorkspace.Create();
        workspace.SkipUnrecognizedProjects = true;
        Solution = await workspace.OpenSolutionAsync(_path);
        foreach (var project in Solution.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation is not null)
            {
                Compilations.Add(compilation);
            }
        }
        await FindEventsAsync();
    }
    public async Task PrintReferences()
    {
        var graph = new Graph();

        foreach (var compilation in Compilations)
        {
            var symbols = FindPublish(compilation);
            foreach (var reference in await symbols)
            {
                var argument = await GetTypeArgument(reference);
                var functionName = reference.CalledSymbol.ToString().Replace("<T>", $"<{argument}>").Replace("(T)", "()");
                var leaf = graph.GetOrCreateNode(functionName);
                var parent = graph.GetOrCreateNode(await GetNameAsync(reference));
                parent.AddChild(leaf);
                await PrintReferenceTree(reference, parent, graph);
            }
        }
        var publishingRegex = new Regex(".*Publish<(.*)>.*");
        var publisingNodes = graph.GetNodes().Where(n => publishingRegex.IsMatch(n.Name)).Select(n => (Node: n, publishingRegex.Match(n.Name).Groups[1].Value));

        var subscribingRegex = new Regex(@".*Execute\((.*)\).*");
        var subscribingNodes = graph.GetNodes().Where(n => subscribingRegex.IsMatch(n.Name)).Select(n => (Node: n, subscribingRegex.Match(n.Name).Groups[1].Value));

        foreach (var (PubNode, PubValue) in publisingNodes)
        {
            foreach (var (SubNode, SubValue) in subscribingNodes.Where(n => n.Value == PubValue))
            {
                PubNode.AddChild(SubNode);
            }
        }

        graph.Print();
    }

    private async Task<string> GetTypeArgument(SymbolCallerInfo caller)
    {
        foreach (var location in caller.Locations)
        {
            if (location.IsInSource)
            {
                var callerSemanticModel = await Solution.GetDocument(location.SourceTree).GetSemanticModelAsync();
                var node = location.SourceTree.GetRoot()
                    .FindToken(location.SourceSpan.Start)
                    .Parent;
                var symbolInfo = callerSemanticModel.GetSymbolInfo(node);
                var calledMethod = symbolInfo.Symbol as IMethodSymbol;

                if (calledMethod != null)
                {
                    var argument = calledMethod.TypeArguments.FirstOrDefault();
                    var value = GetValue(argument);
                    return value;
                }
            }
        }
        return "";
    }
    [GeneratedRegex("\\{([^\\{\\}]+)\\}", RegexOptions.Compiled)]
    private static partial Regex InterpolatedRegex();
    private Regex interpolated = InterpolatedRegex();
    private string GetValue(ITypeSymbol? argument)
    {
        var tree = argument.OriginalDefinition.Locations.First().SourceTree;
        foreach (var compilation in Compilations)
        {
            var model = compilation.GetSemanticModel(tree);

            // find the declaration of the const variable
            var declarations = tree.GetRoot().DescendantNodes()
                                  .OfType<FieldDeclarationSyntax>();
            var declaration = declarations.FirstOrDefault(f => f.Declaration.Variables.Any(v => v.Identifier.Text == "test"));

            if (declaration != null)
            {
                var variable = declaration.Declaration.Variables.First();

                if (variable.Initializer.Value is not InterpolatedStringExpressionSyntax ises)
                {
                    continue;
                }
                var contents = ises.Contents.ToString();

                var replaced = interpolated.Replace(contents, match =>
                {
                    var token = match.Groups[1].Value;
                    var declaration = declarations.FirstOrDefault(f => f.Declaration.Variables.Any(v => v.Identifier.Text == token));
                    var variable = declaration.Declaration.Variables.First();

                    var symbol = (IFieldSymbol)model.GetDeclaredSymbol(variable);
                    var initialValue = symbol.ConstantValue;
                    return initialValue?.ToString() ?? "";
                });
                return replaced;
            }
        }
        return "";
    }

    private async Task PrintReferenceTree(SymbolCallerInfo symbol, Node cameFrom, Graph graph)
    {
        foreach (var reference in await SymbolFinder.FindCallersAsync(symbol.CallingSymbol, Solution))
        {
            string name = await GetNameAsync(reference);

            if (graph.TryGetNode(name, out var node) && node.Children.Contains(cameFrom))
            {
                continue;
            }
            node = graph.GetOrCreateNode(name);
            node.AddChild(cameFrom);

            await PrintReferenceTree(reference, node, graph);
        }
    }
    List<ISymbol> events = new();

    private async Task FindEventsAsync()
    {
        foreach (var compilation in Compilations)
        {

            var iEventType = compilation.GetTypeByMetadataName("TestRoslyn.Dummy.IEvent");

            var types = await SymbolFinder.FindImplementationsAsync(iEventType, Solution);
            events.AddRange(types);
        }
    }
    private async Task<string> GetNameAsync(SymbolCallerInfo reference)
    {
        var name = reference.CallingSymbol.ToString();

        if (reference.CallingSymbol is IMethodSymbol methodSymbol)
        {
            if (methodSymbol.MethodKind == MethodKind.Constructor)
            {
                name = $"new {methodSymbol?.ReceiverType?.ToString()}()";
                return name;
            }
            var parameters = methodSymbol.Parameters;
            foreach (var parameter in methodSymbol.Parameters.Where(p => events.Contains(p.Type)))
            {
                var argument = GetValue(parameter.Type);
                var typeString = parameter.Type.ToString();
                name = name.Replace(typeString, argument);
            }
        }

        return name ?? "";
    }

    private async Task<List<SymbolCallerInfo>> FindPublish(Compilation compilation)
    {
        var iEventBusType = compilation.GetTypeByMetadataName("TestRoslyn.Dummy.IEventBus");
        var publishMethod = iEventBusType?.GetMembers("Publish").First();
        var publishReferences = new List<SymbolCallerInfo>();
        if (publishMethod is null)
        {
            return publishReferences;
        }

        var references = await SymbolFinder.FindCallersAsync(publishMethod, Solution);
        foreach (var reference in references)
        {
            publishReferences.Add(reference);
        }
        return publishReferences;
    }

    private async Task<List<SymbolCallerInfo>> FindSubscribe(Compilation compilation)
    {
        var iEventBusType = compilation.GetTypeByMetadataName("TestRoslyn.Dummy.IEventBus");
        var publishMethod = iEventBusType.GetMembers("Subscribe").First();
        var publishReferences = new List<SymbolCallerInfo>();


        var references = await SymbolFinder.FindCallersAsync(publishMethod, Solution);
        foreach (var reference in references)
        {
            publishReferences.Add(reference);
        }
        return publishReferences;
    }

    public async Task PrintTree()
    {
        foreach (var compilation in Compilations)
        {
            foreach (var tree in compilation.SyntaxTrees)
            {
                var semanticModel = compilation.GetSemanticModel(tree);
                foreach (var node in tree.GetRoot().DescendantNodes())
                {
                    var symbol = semanticModel.GetSymbolInfo(node).Symbol;

                    if (symbol is not IMethodSymbol)
                        continue;
                    if (!node.IsKind(SyntaxKind.InvocationExpression))
                    {
                        continue;
                    }

                    var symbolLocation = symbol.Locations.SingleOrDefault();

                    if (!(symbolLocation?.IsInSource ?? false))
                    {
                        continue;
                    }
                    if (!symbolLocation.SourceTree.FilePath.Contains("Dummy"))
                    {
                        continue;
                    }
                    Console.WriteLine($"{node.Kind()}: {node}");
                    Console.WriteLine($"Method: {symbol}");
                    Console.WriteLine($"Class: {symbol.ContainingType}");
                    Console.WriteLine($"Source file: {symbolLocation.SourceTree.FilePath}");

                    Console.WriteLine();
                }
            }
        }
    }

}

partial class Graph
{
    private readonly Dictionary<string, Node> _nodes = new Dictionary<string, Node>();

    private HashSet<Node> VisitedNodesDuringPrinting { get; set; } = new();
    public Node GetOrCreateNode(string name)
    {
        if (!_nodes.ContainsKey(name))
        {
            _nodes[name] = new Node(name);
        }

        return _nodes[name];
    }

    public bool TryGetNode(string name, out Node node)
    {
        return _nodes.TryGetValue(name, out node);
    }

    public List<Node> GetNodes()
    {
        return _nodes.Values.ToList();
    }

    public void Print()
    {
        VisitedNodesDuringPrinting.Clear();
        foreach (var root in GetNodes().Where(n => n.IsRoot))
        {
            PrintGraphRecursive(root, 0);
        }
    }

    private void PrintGraphRecursive(Node node, int depth)
    {
        if (VisitedNodesDuringPrinting.Contains(node))
        {
            //Console.WriteLine($"{new string(' ', depth)}({node.Name.Replace('(', '{').Replace(')', '}')})");
            return;
        }

        VisitedNodesDuringPrinting.Add(node);

        //Console.WriteLine(new string(' ', depth) + node.Name.Replace('(', '{').Replace(')', '}'));
        if (!node.Name.Contains("Startup"))
        {
            foreach (var child in node.Children)
            {
                var line = $"{node.ToMermaidString()} --> {child.ToMermaidString()}";
                Console.WriteLine(line);
            }
        }

        foreach (var child in node.Children)
        {
            PrintGraphRecursive(child, depth + 1);
        }
    }


}
class Node : IEquatable<Node?>
{
    private readonly HashSet<Node> _children = new();
    public IReadOnlySet<Node> Children => _children;
    private List<Node> _parents = new();
    public bool IsRoot => !_parents.Any();
    public string Name { get; }
    private static int MaxId { get; set; }
    public int Id { get; }
    public Node(string name)
    {
        Id = MaxId;
        MaxId++;
        Name = name;
    }

    public void AddChild(Node child)
    {
        child.AddParent(this);
        _children.Add(child);
    }

    private void AddParent(Node node)
    {
        _parents.Add(node);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as Node);
    }

    public bool Equals(Node? other)
    {
        return other is not null &&
               Name == other.Name;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Name);
    }

    public static bool operator ==(Node? left, Node? right)
    {
        return EqualityComparer<Node>.Default.Equals(left, right);
    }

    public static bool operator !=(Node? left, Node? right)
    {
        return !(left == right);
    }

    public string ToMermaidString()
    {
        return $"{Id}[\"{ReplaceForMermaid(Name)}\"]";
    }

    public static string ReplaceForMermaid(string input)
    {
        Dictionary<string, string> mapping = new()
        {
            ["("] = "#40;",
            [")"] = "#41;",
            ["."] = "#46;",
            ["<"] = "#60;",
            [">"] = "#62;"
        };
        var output = input;
        foreach (var kv in mapping)
        {
            output = output.Replace(kv.Key, kv.Value);
        }
        return output;
    }
}