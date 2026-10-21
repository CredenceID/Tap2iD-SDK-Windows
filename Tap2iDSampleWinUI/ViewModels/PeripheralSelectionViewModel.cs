using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tap2iDBluetoothCommon;
using Tap2iDSampleWinUI.Services;
using Tap2iDSdk.Extension;
using Tap2iDSdk.Model;

namespace Tap2iDSampleWinUI.ViewModels
{
    /// <summary>
    /// Presentation state for the per-capability reader selection UI. Holds the available
    /// reader options for QR / NFC / BLE and the current selection, and exposes the selected
    /// <see cref="PeripheralModel"/> per capability so the verification flow can populate
    /// <c>MdocConfig.{Qr,Nfc,Ble}Peripheral</c> without touching any UI control.
    /// </summary>
    public sealed partial class PeripheralSelectionViewModel : ObservableObject
    {
        private readonly IPeripheralService _peripheralService;

        public PeripheralSelectionViewModel(IPeripheralService peripheralService)
        {
            _peripheralService = peripheralService ?? throw new ArgumentNullException(nameof(peripheralService));
        }

        public ObservableCollection<ReaderOptionViewModel> QrOptions { get; } = new();
        public ObservableCollection<ReaderOptionViewModel> NfcOptions { get; } = new();
        public ObservableCollection<ReaderOptionViewModel> BleOptions { get; } = new();

        [ObservableProperty] private ReaderOptionViewModel _selectedQr;
        [ObservableProperty] private ReaderOptionViewModel _selectedNfc;
        [ObservableProperty] private ReaderOptionViewModel _selectedBle;

        /// <summary>
        /// Per-capability state and explanation straight from the SDK status snapshot. A reader
        /// that is attached but not working (e.g. driver failed to load) is absent from
        /// <c>status.Available</c> and so has no dropdown entry; this is the only place the user
        /// is told why, so it is shown even when the dropdown itself looks empty.
        /// </summary>
        [ObservableProperty] private string _qrStatus = string.Empty;
        [ObservableProperty] private string _nfcStatus = string.Empty;
        [ObservableProperty] private string _bleStatus = string.Empty;

        /// <summary>Set when a refresh fails, so the failure isn't silent.</summary>
        [ObservableProperty] private string _refreshError = string.Empty;

        /// <summary>Gates the dropdowns only — see <see cref="CanRefresh"/> for why Refresh
        /// deliberately does not wait on this.</summary>
        [ObservableProperty] private bool _isEnabled;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
        private bool _isRefreshing;

        /// <summary>Selected reader model per capability; null = SDK default (config).</summary>
        public PeripheralModel? SelectedQrModel => SelectedQr?.Model;
        public PeripheralModel? SelectedNfcModel => SelectedNfc?.Model;
        public PeripheralModel? SelectedBleModel => SelectedBle?.Model;

        // Only re-entrancy is guarded. Refresh deliberately does not wait on IsEnabled:
        // peripheral detection needs no license, so it does not depend on SDK init, and
        // IsEnabled is only set by a successful Populate. Gating on it would mean that a
        // failed first status check disabled Refresh permanently - leaving a restart as the
        // only recovery, which is the exact problem this control exists to remove.
        private bool CanRefresh => !IsRefreshing;

        /// <summary>
        /// Re-queries the SDK and rebuilds the lists, so a reader plugged in after startup is
        /// picked up without restarting the app. The SDK sweep is pushed off the UI thread; the
        /// continuation resumes on it, so <see cref="Populate"/> still mutates the bound
        /// collections from the UI thread.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanRefresh))]
        private async Task RefreshAsync()
        {
            IsRefreshing = true;
            RefreshError = string.Empty;
            try
            {
                var status = await Task.Run(() => _peripheralService.GetStatusAsync());
                Populate(status);
            }
            catch (Exception ex)
            {
                RefreshError = $"Refresh failed: {ex.Message}";
            }
            finally
            {
                IsRefreshing = false;
            }
        }

        /// <summary>
        /// Rebuilds each capability's option list from an SDK status snapshot. On the first call
        /// the detected reader is auto-selected; on a refresh the current selection is kept when
        /// that model is still present, falling back to "Default (config)" when it has gone.
        /// Must be called on the UI thread (mutates bound collections).
        /// </summary>
        public void Populate(AllPeripheralStatus status)
        {
            SelectedQr = Fill(QrOptions, status, PeripheralCapability.Qr, SelectedQr);
            SelectedNfc = Fill(NfcOptions, status, PeripheralCapability.Nfc, SelectedNfc);
            SelectedBle = Fill(BleOptions, status, PeripheralCapability.Ble, SelectedBle);

            QrStatus = Describe(status.Qr);
            NfcStatus = Describe(status.Nfc);
            BleStatus = Describe(status.Ble);

            IsEnabled = true;
        }

        private static ReaderOptionViewModel Fill(
            ObservableCollection<ReaderOptionViewModel> target,
            AllPeripheralStatus status,
            PeripheralCapability capability,
            ReaderOptionViewModel previous)
        {
            target.Clear();
            target.Add(new ReaderOptionViewModel("Default (config)", null));
            // Don't list SystemDefault as its own option — it's the OS radio / generic PC-SC
            // reader, which is what "Default (config)" already represents. Without this filter
            // the list shows two default-looking rows (CIE-6732).
            foreach (var p in status.Available.Where(p =>
                         p.Serves(capability) && p.Model != PeripheralModel.SystemDefault))
                target.Add(new ReaderOptionViewModel(p.Model.ToString(), p.Model));

            // No previous selection means this is the initial populate: auto-select the detected
            // reader for this capability when there is one.
            if (previous == null)
                return target.Count > 1 ? target[1] : target[0];

            // Refresh: keep what the user had if it survived, otherwise drop to "Default (config)"
            // rather than silently switching them to some other reader. A previous selection of
            // "Default (config)" (null model) matches target[0] and is preserved too.
            return target.FirstOrDefault(o => o.Model == previous.Model) ?? target[0];
        }

        /// <summary>
        /// One-line "state — message (fw x.y.z)" for a capability, blank when the SDK said
        /// nothing. Firmware is included when the SDK reports it, since it is often the
        /// deciding detail when triaging a reader that is present but misbehaving.
        /// </summary>
        private static string Describe(PeripheralStatus status)
        {
            if (status == null)
                return string.Empty;

            var text = string.IsNullOrWhiteSpace(status.Message)
                ? $"{status.State}"
                : $"{status.State} — {status.Message}";

            return string.IsNullOrWhiteSpace(status.FirmwareVersion)
                ? text
                : $"{text} (fw {status.FirmwareVersion})";
        }
    }
}
