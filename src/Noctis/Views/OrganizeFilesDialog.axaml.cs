using Avalonia.Controls;
using Avalonia.Input;
using Noctis.ViewModels;

namespace Noctis.Views;

public partial class OrganizeFilesDialog : Window
{
    public OrganizeFilesDialog()
    {
        InitializeComponent();
    }

    public OrganizeFilesDialog(OrganizeFilesViewModel vm) : this()
    {
        DataContext = vm;
        vm.Closed += (_, _) => Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Escape closes the same way the header X does.
        if (e.Key == Key.Escape && DataContext is OrganizeFilesViewModel vm)
        {
            e.Handled = true;
            vm.CloseCommand.Execute(null);
            return;
        }
        base.OnKeyDown(e);
    }
}
