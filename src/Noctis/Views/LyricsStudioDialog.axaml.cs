using Avalonia.Controls;
using Noctis.ViewModels;

namespace Noctis.Views;

public partial class LyricsStudioDialog : Window
{
    public LyricsStudioDialog()
    {
        InitializeComponent();
    }

    public LyricsStudioDialog(LyricsStudioViewModel vm) : this()
    {
        // Confirm is set before DataContext so the panel (which only fills a null Confirm)
        // leaves this owner-bound prompt in place. Tap-mode keys live in the panel.
        vm.Confirm = message => ConfirmationDialog.ShowAsync(this, message);
        DataContext = vm;
        vm.Closed += (_, _) => Close();
    }
}
