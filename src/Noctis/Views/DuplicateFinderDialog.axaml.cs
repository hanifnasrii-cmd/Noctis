using Avalonia.Controls;
using Avalonia.Input;
using Noctis.ViewModels;

namespace Noctis.Views;

public partial class DuplicateFinderDialog : Window
{
    public DuplicateFinderDialog()
    {
        InitializeComponent();
    }

    public DuplicateFinderDialog(DuplicateFinderViewModel vm) : this()
    {
        DataContext = vm;
        vm.Closed += (_, _) => Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Escape closes the same way the header X does.
        if (e.Key == Key.Escape && DataContext is DuplicateFinderViewModel vm)
        {
            e.Handled = true;
            vm.CloseCommand.Execute(null);
            return;
        }
        base.OnKeyDown(e);
    }
}
