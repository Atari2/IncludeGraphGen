using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Drawing.Imaging;

namespace IncludeGraphGen
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        Microsoft.Msagl.WpfGraphControl.GraphViewer? g_viewer;
        Microsoft.Msagl.WpfGraphControl.VNode? selected_node;
        CMakeProject? cmakeProject;
        IncludeGraph? graph;

        public MainWindow()
        {
            InitializeComponent();
            selectFileButton.Click += SelectFileButton_Click;
        }

        private static async Task<IncludeGraph> CreateIncludeGraph(List<string> filenames)
        {
            var graph = new IncludeGraph();
            await graph.Init(filenames);
            return graph;
        }

        private async void SelectFileButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new OpenFileDialog()
                {
                    Filter = "CMakeLists files (*.txt)|*.txt",
                    Multiselect = false
                };
                var result = dlg.ShowDialog();
                if (result == false) return;
                string filename = dlg.FileName;
                if (filename == null) return;
                cmakeProject = new CMakeProject();
                var loading = new LoadingSpinner();
                selectFileButton.Visibility = Visibility.Hidden;
                selectFileButton.IsEnabled = false;
                dockPanel.Children.Add(loading);
                await cmakeProject.Init(filename);
                var prevCurrDir = Directory.GetCurrentDirectory();
                Directory.SetCurrentDirectory(cmakeProject.DestinationDir);
                graph = await CreateIncludeGraph(cmakeProject.Sources);
                Directory.SetCurrentDirectory(prevCurrDir);
                var w_graph = new Microsoft.Msagl.Drawing.Graph("graph");
                w_graph.Attr.LayerDirection = Microsoft.Msagl.Drawing.LayerDirection.LR;
                foreach (var p in graph.Nodes)
                {
                    var name = Path.GetRelativePath(cmakeProject.DestinationDir, p.Value.Name);
                    var node = w_graph.AddNode(name);
                    node.Attr.Uri = Path.Combine(cmakeProject.OriginalDirectory, name);
                    if (p.Value.NonLocal)
                        node.Attr.Shape = Microsoft.Msagl.Drawing.Shape.Diamond;
                    else
                        node.Attr.Shape = Microsoft.Msagl.Drawing.Shape.Circle;
                }
                foreach (var p in graph.Nodes)
                {
                    var name = Path.GetRelativePath(cmakeProject.DestinationDir, p.Value.Name);
                    foreach (var s in p.Value.Nodes)
                    {
                        var s_name = Path.GetRelativePath(cmakeProject.DestinationDir, s.Value.Name);
                        w_graph.AddEdge(name, s_name);
                    }
                }
                g_viewer = new Microsoft.Msagl.WpfGraphControl.GraphViewer();
                dockPanel.Children.Remove(loading);   
                g_viewer.BindToPanel(dockPanel);
                g_viewer.Graph = w_graph;
                g_viewer.MouseDown += G_viewer_MouseDown;

            }
            catch (Exception ex)
            {
                dockPanel.Children.RemoveAt(dockPanel.Children.Count - 1);
                selectFileButton.Visibility = Visibility.Visible;
                selectFileButton.IsEnabled = true;
                MessageBox.Show(ex.ToString(), "Error", MessageBoxButton.OK);
            }
        }

        private void G_viewer_MouseDown(object? sender, Microsoft.Msagl.Drawing.MsaglMouseEventArgs e)
        {
            if (g_viewer == null || cmakeProject == null || graph == null) return;
            if (e.RightButtonIsPressed && g_viewer.ObjectUnderMouseCursor is Microsoft.Msagl.WpfGraphControl.VNode g_node)
            {
                if (FindResource("cmGraphNode") is ContextMenu cm)
                {
                    var key = new Uri(Path.Combine(cmakeProject.DestinationDir, g_node.Node.Attr.Id));
                    var node = graph.Find(key);
                    if (node == null) return;
                    selected_node = g_node;
                    cm.IsOpen = !node.NonLocal;
                }
            }
            else if (e.RightButtonIsPressed)
            {
                if (FindResource("cmSaveContextMenu") is ContextMenu cm)
                {
                    cm.IsOpen = true;
                }
            }
        }

        private void SaveToImage(object sender, RoutedEventArgs e)
        {
            if (graph == null || cmakeProject == null || g_viewer == null) return;
            var dlg = new SaveFileDialog()
            {
                Filter = "Image files (*.jpg)|*.jpg",
                DefaultExt = ".jpg"
            };

            var res = dlg.ShowDialog();
            if (res != null && res.Value)
            {
                Microsoft.Msagl.GraphViewerGdi.GraphRenderer renderer = new(g_viewer.Graph);
                renderer.CalculateLayout();
                using Bitmap bitmap = new((int)g_viewer.Graph.Width, (int)g_viewer.Graph.Height, PixelFormat.Format32bppPArgb);
                bitmap.SetResolution((float)g_viewer.DpiX, (float)g_viewer.DpiY);
                renderer.Render(bitmap);
                bitmap.Save(dlg.FileName);
            }

        }

        private void OpenFileExplorer(object sender, RoutedEventArgs e)
        {
            if (selected_node == null) return;
            var dirName = Path.GetDirectoryName(selected_node.Node.Attr.Uri);
            if (dirName == null) return;
            Process.Start("explorer.exe", dirName);
        }

        private void OpenTextEditor(object sender, RoutedEventArgs e)
        {
            if (selected_node == null) return;
            Process.Start("explorer.exe", selected_node.Node.Attr.Uri);
        }

        private void DependencyTree_Click(object sender, RoutedEventArgs e)
        {
            if (selected_node == null || cmakeProject == null || graph == null) return;
            var wd = new Window()
            {
                SizeToContent = SizeToContent.WidthAndHeight,
                Title = "Dependency tree"
            };
            wd.Owner = this;
            var pane = new DockPanel();
            wd.Content = pane;
            wd.Visibility = Visibility.Visible;
            wd.SizeToContent = SizeToContent.Manual;
            var g_sub_graph = new Microsoft.Msagl.Drawing.Graph("sub_graph");
            g_sub_graph.Attr.LayerDirection = Microsoft.Msagl.Drawing.LayerDirection.TB;
            var key = new Uri(Path.Combine(cmakeProject.DestinationDir, selected_node.Node.Attr.Id));
            var node = graph.Find(key);
            if (node == null) return;
            node.VisitSubNodes(new HashSet<IncludeGraphNode>(new GraphNodeComparer()), g_sub_graph, cmakeProject);
            var g_sub_viewer = new Microsoft.Msagl.WpfGraphControl.AutomaticGraphLayoutControl();
            pane.LastChildFill = true;
            pane.Children.Add(g_sub_viewer);
            g_sub_viewer.Graph = g_sub_graph;
            wd.Show();
        }

        private void Includes_Click(object sender, RoutedEventArgs e)
        {
            if (selected_node == null || cmakeProject == null || graph == null) return;
            var key = new Uri(Path.Combine(cmakeProject.DestinationDir, selected_node.Node.Attr.Id));
            var node = graph.Find(key);
            if (node == null) return;
            string currentDir = Directory.GetCurrentDirectory();
            var main = new Window()
            {
                SizeToContent = SizeToContent.WidthAndHeight,
                Title = "Includes: ",
            };
            main.Owner = this;
            var pane = new StackPanel();
            main.Content = pane;
            foreach (var n in node.Nodes.Values)
            {
                var btn = new Button
                {
                    Content = Path.GetRelativePath(cmakeProject.DestinationDir, n.Name)
                };
                btn.Click += IncludedByPane_Click;

                pane.Children.Add(btn);
            }
            main.Show();
        }

        private void IncludedByPane_Click(object sender, RoutedEventArgs e)
        {
            if (g_viewer == null) return;
            if (sender is not Button btn) return;
            var node = g_viewer.Graph.FindNode(btn.Content as string);
            if (node == null) return;
            g_viewer.NodeToCenterWithScale(node, 3.0);
        }

        private void IncludedBy_Click(object sender, RoutedEventArgs e)
        {
            if (selected_node == null || cmakeProject == null || graph == null) return;
            var key = new Uri(Path.Combine(cmakeProject.DestinationDir, selected_node.Node.Attr.Id));
            if (key.AbsoluteUri.EndsWith(".cpp"))
            {
                MessageBox.Show(".cpp files shouldn't be included by anyone");
                return;
            }
            var node = graph.Find(key);
            if (node == null) return;
            string currentDir = Directory.GetCurrentDirectory();
            var main = new Window
            {
                SizeToContent = SizeToContent.WidthAndHeight,
                Title = "Included by: "
            };
            main.Owner = this;
            var pane = new StackPanel();
            main.Content = pane;
            foreach (var n in node.IncludedBy)
            {
                var btn = new Button
                {
                    Content = Path.GetRelativePath(cmakeProject.DestinationDir, n.Name)
                };
                btn.Click += IncludedByPane_Click;

                pane.Children.Add(btn);
            }
            main.Show();
        }
    }
}
