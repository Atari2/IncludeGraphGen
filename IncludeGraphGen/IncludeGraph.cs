using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;

namespace IncludeGraphGen
{
    class IncludeGraphCreationException : Exception
    {
        public IncludeGraphCreationException(string message) : base(message) { }
    }

    internal class GraphNodeComparer : IEqualityComparer<IncludeGraphNode>
    {
        public bool Equals(IncludeGraphNode? x, IncludeGraphNode? y)
        {
            if (x == null && y == null)
                return true;
            if (x == null || y == null)
                return false;
            return x.Name == y.Name;
        }

        public int GetHashCode([DisallowNull] IncludeGraphNode obj)
        {
            return obj.Name.GetHashCode();
        }
    }

    internal class IncludeGraphNode
    {
        public string Name { get; set; }
        public string OriginDir { get; set; }
        public Dictionary<Uri, IncludeGraphNode> Nodes { get; set; }
        public HashSet<IncludeGraphNode> IncludedBy { get; set; }
        public bool NonLocal { get; set; }
        public List<string> IncludePaths { get; set; }

        public IncludeGraphNode(string filename, IncludeGraphNode? parent, bool nonlocal, List<string> includePaths)
        {
            IncludePaths = includePaths;
            var normalizedFilename = new Uri(System.IO.Path.GetFullPath(filename));
            Nodes = new Dictionary<Uri, IncludeGraphNode>();
            IncludedBy = new HashSet<IncludeGraphNode>(new GraphNodeComparer());
            if (parent != null)
            {
                IncludedBy.Add(parent);
            }
            Name = normalizedFilename.AbsolutePath;
            var origin_dir = System.IO.Path.GetDirectoryName(Name);
            if (origin_dir == null)
                throw new IncludeGraphCreationException($"Invalid path {Name}");
            OriginDir = origin_dir;
            NonLocal = nonlocal;
        }

        public IncludeGraphNode? Find(Uri filename)
        {
            if (Nodes.TryGetValue(filename, out var foundNode))
                return foundNode;
            else
            {
                foreach (var node in Nodes)
                {
                    var val = node.Value.Find(filename);
                    if (val != null) return val;
                }
            }
            return null;
        }

        public void VisitSubNodes(HashSet<IncludeGraphNode> visited, Microsoft.Msagl.Drawing.Graph graph, CMakeProject project)
        {
            if (visited.Contains(this)) return;
            visited.Add(this);
            var name = System.IO.Path.GetRelativePath(project.DestinationDir, Name);
            var node = graph.AddNode(name);
            if (NonLocal)
                node.Attr.Shape = Microsoft.Msagl.Drawing.Shape.Diamond;
            else
                node.Attr.Shape = Microsoft.Msagl.Drawing.Shape.Circle;
            foreach (var subnode in Nodes)
            {
                subnode.Value.VisitSubNodes(visited, graph, project);
                graph.AddEdge(name, System.IO.Path.GetRelativePath(project.DestinationDir, subnode.Value.Name));
            }
        }

        public async Task PopulateNodes(IncludeGraph graph)
        {
            if (System.IO.File.GetAttributes(Name) == System.IO.FileAttributes.Directory)
                return;
            var lines = await System.IO.File.ReadAllTextAsync(Name);
            var rx = new Regex(@"#include ""(.+)""", RegexOptions.Compiled);
            var nonLocalRx = new Regex(@"#include <(.+)>", RegexOptions.Compiled);
            var matches = await Task.Run(() => rx.Matches(lines));
            var nonLocalMatches = await Task.Run(() => nonLocalRx.Matches(lines));
            foreach (Match match in nonLocalMatches)
            {
                var fileName = match.Groups[1].Value;
                var nonLocalFilename = new Uri(fileName, UriKind.Relative);
                var possible_node = graph.Find(nonLocalFilename);
                if (possible_node != null)
                {
                    possible_node.IncludedBy.Add(this);
                    Nodes.Add(nonLocalFilename, possible_node);
                }
                else
                {
                    var new_node = new IncludeGraphNode(fileName, this, true, IncludePaths);
                    graph.Nodes.Add(nonLocalFilename, new_node);
                    Nodes.Add(nonLocalFilename, new_node);
                }
            }

            foreach (Match match in matches)
            {
                var partial_filename = match.Groups[1].Value;
                var full_filename = System.IO.Path.GetFullPath(System.IO.Path.Combine(OriginDir, partial_filename));
                var i = 0;
                while (!System.IO.File.Exists(full_filename) && i < IncludePaths.Count)
                {
                    full_filename = System.IO.Path.GetFullPath(System.IO.Path.Combine(IncludePaths[i], partial_filename));
                    i++;
                }
                var normalized = new Uri(full_filename);
                // check if a file is included twice
                if (Nodes.ContainsKey(normalized))
                    continue;
                var possible_node = graph.Find(normalized);
                if (possible_node != null)
                {
                    possible_node.IncludedBy.Add(this);
                    Nodes.Add(normalized, possible_node);
                }
                else
                {
                    var new_node = new IncludeGraphNode(full_filename, this, false, IncludePaths);
                    graph.Nodes.Add(normalized, new_node);
                    Nodes.Add(normalized, new_node);
                    await new_node.PopulateNodes(graph);
                }
            }

        }

    }

    internal class IncludeGraph
    {
        public Dictionary<Uri, IncludeGraphNode> Nodes = new();
        public IncludeGraph()
        {

        }

        public async Task Init(List<string> filenames, List<string> includePaths)
        {
            foreach (var filename in filenames)
            {
                if (filename == null)
                    throw new IncludeGraphCreationException("Filename was null");
                if (System.IO.File.GetAttributes(filename) == System.IO.FileAttributes.Directory)
                    continue;
                var normalized = new Uri(filename, UriKind.RelativeOrAbsolute);

                var workingdir = System.IO.Path.GetDirectoryName(filename);
                if (workingdir == null)
                    throw new IncludeGraphCreationException($"Can't find the directory of {filename}");

                var node = Find(normalized);
                if (node == null)
                {
                    node = new IncludeGraphNode(filename, null, false, includePaths);
                    Nodes.Add(normalized, node);
                    await node.PopulateNodes(this);
                }
            }
        }

        public IncludeGraphNode? Find(Uri filename)
        {
            if (Nodes.Count == 0)
                return null;
            if (Nodes.TryGetValue(filename, out var foundNode))
            {
                return foundNode;
            }
            else
            {
                return null;
            }
        }
    }
}
