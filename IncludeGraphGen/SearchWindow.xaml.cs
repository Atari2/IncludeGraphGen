using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace IncludeGraphGen
{
    /// <summary>
    /// Interaction logic for SearchWindow.xaml
    /// </summary>
    public partial class SearchWindow : Window
    {
        public MainWindow? parentWindow;
        public bool isParentCommand;
        string? filenameToSearch;

        public SearchWindow()
        {
            InitializeComponent();
        }

        private void Tb_TextChanged(object sender, TextChangedEventArgs e)
        {
            filenameToSearch = (sender as TextBox)?.Text;
        }

        private void ExecutedSubFindCommand(object sender, ExecutedRoutedEventArgs e)
        {
            if (parentWindow == null)
                return;
            if (filenameToSearch == null || parentWindow.g_viewer == null)
                return;
            foreach (var n in parentWindow.g_viewer.Graph.Nodes)
            {
                if (n.Id.Contains(filenameToSearch))
                {
                    parentWindow.g_viewer.NodeToCenterWithScale(n, 3.0);
                    Hide();
                    parentWindow.Activate();
                    return;
                }
            }
        }


        private void CanExecuteSubFindCommand(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = true;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!isParentCommand)
            {
                e.Cancel = true;
                Visibility = Visibility.Hidden;
            }
        }
    }

}
