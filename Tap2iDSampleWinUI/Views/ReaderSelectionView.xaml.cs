using Microsoft.UI.Xaml.Controls;
using Tap2iDSampleWinUI.ViewModels;

namespace Tap2iDSampleWinUI.Views
{
    /// <summary>
    /// Per-capability reader selection control. Binds to a
    /// <see cref="PeripheralSelectionViewModel"/> supplied at construction, so it holds no
    /// selection logic of its own — the view model owns options and selection state.
    /// </summary>
    public sealed partial class ReaderSelectionView : UserControl
    {
        public PeripheralSelectionViewModel ViewModel { get; }

        public ReaderSelectionView(PeripheralSelectionViewModel viewModel)
        {
            ViewModel = viewModel;
            this.InitializeComponent();
        }
    }
}
