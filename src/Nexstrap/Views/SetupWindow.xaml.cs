using System.Windows;
using Nexstrap.ViewModels;

namespace Nexstrap.Views;

public partial class SetupWindow : Window
{
    public SetupWindow(SetupViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Finished += (_, _) => Close();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
