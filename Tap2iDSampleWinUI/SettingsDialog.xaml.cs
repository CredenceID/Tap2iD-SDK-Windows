using Microsoft.UI.Xaml.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Tap2iDSampleWinUI
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class SettingsDialog : ContentDialog
    {
        public SettingsDialog()
        {
            this.InitializeComponent();
        }

        // Helper properties to get/set values from the UI
        public string NfcTimeout
        {
            get => NfcTimeoutInput.Text;
            set => NfcTimeoutInput.Text = value;
        }

        public string BleConnectionTimeout
        {
            get => BleConnectionTimeoutInput.Text;
            set => BleConnectionTimeoutInput.Text = value;
        }

        public string BleDataTimeout
        {
            get => BleDataTimeoutInput.Text;
            set => BleDataTimeoutInput.Text = value;
        }
    }
}
