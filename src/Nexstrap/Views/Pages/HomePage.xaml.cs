using System.Windows.Controls;
using CommunityToolkit.Mvvm.Messaging;
using Nexstrap.Services.Navigation;

namespace Nexstrap.Views.Pages;

public partial class HomePage : UserControl
{
    public HomePage()
    {
        InitializeComponent();
        Loaded += (_, _) => WeakReferenceMessenger.Default.Register<ShowUpdatesMessage>(this, (_, _) => UpdatesCard.BringIntoView());
        Unloaded += (_, _) => WeakReferenceMessenger.Default.Unregister<ShowUpdatesMessage>(this);
    }
}
