using System.Windows.Controls;

namespace DS4Handler.App.Views;

/// <summary>Card for one controller; DataContext is a <see cref="ViewModels.ControllerViewModel"/>.</summary>
public partial class ControllerCard : UserControl
{
    public ControllerCard() => InitializeComponent();
}
