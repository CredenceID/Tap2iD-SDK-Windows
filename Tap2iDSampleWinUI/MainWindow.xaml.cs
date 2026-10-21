using Microsoft.UI;
using Microsoft.UI.Xaml;
using System.Linq;
using Tap2iDSdk.Model.Pdf417;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Tap2iDBluetoothCommon;
using Tap2iDSampleWinUI.Hardware;
using Tap2iDSampleWinUI.Services;
using Tap2iDSampleWinUI.ViewModels;
using Tap2iDSampleWinUI.Views;
using Tap2iDSdk;
using Tap2iDSdk.Extension;
using Tap2iDSdk.HidAtom;
using Tap2iDSdk.Model;
using Windows.ApplicationModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using WinRT.Interop;
using AppWindow = Microsoft.UI.Windowing.AppWindow;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Tap2iDSampleWinUI
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private AppWindow m_AppWindow;
        private IVerifyMdoc tap2idVerifier;
        private InitSdkResultListener _initSdkResultListener;
        private DelegateVerifyState stateDelegate;
        public delegate void OnQrCodeReadEventHandler(object sender, QrCodeReadEventArgs qrCodeReadEventArgs);
        public event OnQrCodeReadEventHandler OnQrCodeReadEvent;

        private CameraScanViewModel _mainWindowViewModel;

        // Peripheral API (per-capability reader selection), resolved from the DI composition root.
        private readonly IPeripheralService _peripheralService;
        private readonly PeripheralSelectionViewModel _peripheralVm;

        public static Windows.UI.Color UI_DARK_GREEN = Windows.UI.Color.FromArgb(0xff, 0x1e, 0x53, 0x4B);
        public static Windows.UI.Color UI_WHITE_GREEN = Windows.UI.Color.FromArgb(0xff, 0xd1, 0xe4, 0xe3);

        private bool _isPcscReaderActivated = false;

        // HID ATOM / ATOM3K, read through Tap2iDSdk.HidAtom. A capture only receives scans while it
        // waits, so the sample keeps one waiting per kind and restarts it after each scan or timeout.
        private const int AtomListenTimeoutSeconds = 300;
        private bool _isAtomListening;

        private SerialPortManager _serialPortManager;
        private UsbSerialScannerService _p35Scanner;
        private UsbSerialScannerService _haloScanner;

        // Duplicate-scan guard: the ATOM keeps reading while a phone or licence stays on the glass,
        // and the same barcode can be decoded again. Held only briefly and cleared with the fields (PII).
        private static readonly TimeSpan DuplicateScanWindow = TimeSpan.FromSeconds(3);
        private string _lastScan;
        private DateTime _lastScanUtc;

        private DispatcherTimer hideResultsTimer;
        private SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private CancellationTokenSource? _verifyCancellationTokenSource;
        private DeviceEngagementMode _currentEngagementMode;

        // NFC always-on (CIE-8000 POC): once NFC Tap is clicked, the NFC wait is
        // re-armed after every transaction, so phones can be tapped one after another without
        // clicking NFC Tap again. The SDK reacts to a tag *arriving*, so a phone left on the
        // reader is not read twice: it has to leave the field and come back.
        private bool _nfcAlwaysOn;
        private volatile bool _nfcWaitingForTag;      // armed, no tag yet: a barcode scan may pre-empt it
        private volatile bool _nfcTagSeenThisAttempt; // DeviceEngagementStarted fired for the current NFC wait
        private bool _nfcStoppedByApp;                // the current wait was cancelled by the app, not the user
        private int _nfcFailuresWithoutTag;           // backoff while the reader is missing or failing
        private int _nfcRearmGeneration;
        private bool _windowClosed;
        private bool _errorDialogOpen;
        private static readonly TimeSpan NfcRearmDelay = TimeSpan.FromMilliseconds(500);
        private const int NfcMaxBackoffSeconds = 30;

        private const string KEY_NFC_TIMEOUT = "NfcTimeout";
        private const string KEY_BLE_CONN_TIMEOUT = "BleConnTimeout";
        private const string KEY_BLE_DATA_TIMEOUT = "BleDataTimeout";

        public MainWindow()
        {
            this.InitializeComponent();

            // Stop the NFC loop with the window, so no wait outlives it.
            this.Closed += (_, _) =>
            {
                _windowClosed = true;
                _nfcAlwaysOn = false;
                try { _verifyCancellationTokenSource?.Cancel(); } catch (ObjectDisposedException) { }
            };

            m_AppWindow = GetAppWindowForCurrentWindow();
            m_AppWindow.Title = "Tap2iD SDK Sample for Windows";
            m_AppWindow.SetIcon("Assets/credence.ico");

            // Open maximized: the left panel has fixed-width controls, so at the default window
            // size (or with 125-150 % display scaling) the results column had almost no room.
            if (m_AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
                presenter.Maximize();

            _mainWindowViewModel = new CameraScanViewModel(this);

            // Resolve the peripheral service + selection view model from the composition root,
            // and host the reader-selection control bound to that view model.
            _peripheralService = App.Services.GetRequiredService<IPeripheralService>();
            _peripheralVm = App.Services.GetRequiredService<PeripheralSelectionViewModel>();
            ReaderSelectionHost.Content = new ReaderSelectionView(_peripheralVm);

            try
            {
                // Shared SDK verifier instance (created by the DI factory).
                tap2idVerifier = App.Services.GetRequiredService<IVerifyMdoc>();
                UpdateSdkVersionInformation(tap2idVerifier.GetVersion());
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);

            }
            stateDelegate = new DelegateVerifyState();
            stateDelegate.OnVerifyState = OnStateChanged;
            _initSdkResultListener = new();
            _initSdkResultListener.OnInitializationSuccess = OnSdkInitializationSuccess;
            _initSdkResultListener.OnInitializationFailure = OnSdkInitializationFailure;

            ResetUiInterfaceWhenSdkNotInitialized();

            OnQrCodeReadEvent += QrCodeScanned_ExecuteVerifyMdoc;

            // Update the window title with the app name and version
            this.Title += $" - v{GetAppVersion()}";

            _ = RunBarcodeServerAsync();

            _ = StartSerialScannersAsync();

            // Initialize the timer
            hideResultsTimer = new DispatcherTimer();
            hideResultsTimer.Interval = TimeSpan.FromSeconds(20);
            hideResultsTimer.Tick += HideResultsAfterTimeout;
        }

        #region App Information
        private string GetAppVersion()
        {
            PackageVersion version = Package.Current.Id.Version;
            return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }

        private AppWindow GetAppWindowForCurrentWindow()
        {
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId wndId = Win32Interop.GetWindowIdFromWindow(hWnd);
            return AppWindow.GetFromWindowId(wndId);
        }
        #endregion

        #region NFC and QR Code Handling
        void QrCodeScanned_ExecuteVerifyMdoc(object sender, QrCodeReadEventArgs qrCodeReadEventArgs)
        {
            var value = qrCodeReadEventArgs.QrCodeValue;
            if (value.StartsWith("@", StringComparison.Ordinal))
                ExecuteVerifyPdf417Async(value);
            else
                ExecuteVerifyMdoc(value, DeviceEngagementMode.QrCode);
        }
        private void StartNfcDeviceEngagement()
        {
            //Start reading
            if (_isPcscReaderActivated)
            {
                UpdatePcscReaderName();
                ExecuteVerifyMdoc("", DeviceEngagementMode.NFC);
            }
        }

        private async void ExecuteVerifyMdoc(string deviceEngagementString, DeviceEngagementMode deviceEngagementMode)
        {
            // The semaphore correctly prevents multiple operations at once.
            if (!await TryAcquireOperationAsync())
                return;

            _verifyCancellationTokenSource = new CancellationTokenSource();
            _currentEngagementMode = deviceEngagementMode;
            bool isNfc = deviceEngagementMode == DeviceEngagementMode.NFC;
            _nfcWaitingForTag = isNfc;
            _nfcTagSeenThisAttempt = false;
            _nfcStoppedByApp = false;
            Tap2iDResultError? outcome = null; // null = exception
            var cancelButton = isNfc ? CancelNfcButton : CancelQrButton;
            cancelButton.Visibility = Visibility.Visible;

            try
            {
                if (deviceEngagementMode == DeviceEngagementMode.NFC)
                    AppendLogs("Waiting for mDoc");
                else
                    AppendLogs("Start mDoc Reading with QrCode Device Engagement");

                ProgressRing.IsActive = true;
                ProgressPanel.Visibility = Visibility.Visible;

                var mdocConfig = CreateMdocConfig(deviceEngagementMode, deviceEngagementString);
                mdocConfig.BleWriteOption = BleWriteOption.WriteWithoutResponse;

                var tap2IdResult = await Task.Run(async () =>
                {
                    return await tap2idVerifier.VerifyMdocAsync(mdocConfig, stateDelegate, _verifyCancellationTokenSource.Token);
                });

                outcome = tap2IdResult.ResultError;
                HandleVerificationResult(tap2IdResult, mdocConfig);
            }
            catch (OperationCanceledException)
            {
                outcome = Tap2iDResultError.OperationCancelled;
                AppendLogs("Verification cancelled.");
                ProgressRing.IsActive = false;
                ProgressPanel.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                // In the NFC loop, a failure before any tag (e.g. no reader) is retried with a
                // backoff, so it is logged rather than raising a dialog on every attempt.
                if (isNfc && _nfcAlwaysOn && !_nfcTagSeenThisAttempt)
                    AppendLogs($"NFC wait failed: {ex.Message}");
                else
                    ShowError(ex.Message);
                ProgressRing.IsActive = false;
                ProgressPanel.Visibility = Visibility.Collapsed;
            }
            finally
            {
                _nfcWaitingForTag = false;
                cancelButton.Visibility = Visibility.Collapsed;
                _verifyCancellationTokenSource?.Dispose();
                _verifyCancellationTokenSource = null;
                ProgressRing.IsActive = false;
                ProgressPanel.Visibility = Visibility.Collapsed;
                _semaphore.Release();

                if (isNfc)
                    AfterNfcAttempt(outcome);
                else if (_nfcAlwaysOn)
                    ScheduleNfcRearm(NfcRearmDelay); // a QR transaction paused the NFC loop
            }
        }

        /// <summary>
        /// Decides what follows an NFC wait: re-arm (always-on), stay quiet (the app cancelled it
        /// for a barcode or a mode change, and re-arms itself), or return to the method choice
        /// (the user cancelled, which also switches always-on off).
        /// </summary>
        private void AfterNfcAttempt(Tap2iDResultError? outcome)
        {
            if (_nfcStoppedByApp || _windowClosed)
                return;

            if (!_nfcAlwaysOn || outcome == Tap2iDResultError.OperationCancelled)
            {
                _nfcAlwaysOn = false;
                ActivateDeviceEngagementOptions();
                return;
            }

            bool failedWithoutTag = !_nfcTagSeenThisAttempt
                && outcome != Tap2iDResultError.OK
                && outcome != Tap2iDResultError.NoNFCTagDetected;
            if (failedWithoutTag)
            {
                // Reader missing, PC/SC down…: retry with a growing pause instead of spinning.
                _nfcFailuresWithoutTag++;
                int seconds = Math.Min(NfcMaxBackoffSeconds, 1 << Math.Min(_nfcFailuresWithoutTag, 5));
                AppendLogs($"NFC always-on: retrying in {seconds} s.");
                ScheduleNfcRearm(TimeSpan.FromSeconds(seconds));
            }
            else
            {
                _nfcFailuresWithoutTag = 0;
                ScheduleNfcRearm(NfcRearmDelay);
            }
        }

        /// <summary>
        /// Starts the next NFC wait after <paramref name="delay"/>, unless a newer re-arm, a mode
        /// change, a running operation or the window closing supersedes it.
        /// </summary>
        private async void ScheduleNfcRearm(TimeSpan delay)
        {
            int generation = ++_nfcRearmGeneration;
            await Task.Delay(delay);
            if (generation != _nfcRearmGeneration || !_nfcAlwaysOn || _windowClosed)
                return;
            if (_semaphore.CurrentCount == 0)
                return; // something else is running; it re-arms when it ends

            _isPcscReaderActivated = true;
            DisplayNfcDeviceEngagementFeature();
            StartNfcDeviceEngagement();
        }

        /// <summary>
        /// Takes the one-operation-at-a-time slot. An NFC wait that has not seen a tag yet gives
        /// way to a scanned barcode (QR or PDF417); the NFC loop re-arms after that transaction.
        /// </summary>
        private async Task<bool> TryAcquireOperationAsync()
        {
            if (await _semaphore.WaitAsync(0))
                return true;

            if (_nfcWaitingForTag && StopNfcWait("NFC wait paused for the scanned barcode."))
            {
                if (await _semaphore.WaitAsync(TimeSpan.FromSeconds(5)))
                    return true;
                // The NFC wait did not let go in time: keep the loop alive rather than lose it.
                if (_nfcAlwaysOn)
                    ScheduleNfcRearm(NfcRearmDelay);
            }

            AppendLogs("An operation is currently running, please wait...");
            return false;
        }

        /// <summary>Cancels an idle NFC wait on the app's behalf (not a user cancel).</summary>
        private bool StopNfcWait(string reason)
        {
            if (!_nfcWaitingForTag || _verifyCancellationTokenSource == null)
                return false;
            _nfcStoppedByApp = true;
            AppendLogs(reason);
            try { _verifyCancellationTokenSource.Cancel(); }
            catch (ObjectDisposedException) { return false; }
            return true;
        }

        private void CancelVerifyButton_Click(object sender, RoutedEventArgs e)
        {
            // A user cancel also switches the NFC loop off; NFC Tap switches it back on.
            if (_currentEngagementMode == DeviceEngagementMode.NFC)
                _nfcAlwaysOn = false;
            _verifyCancellationTokenSource?.Cancel();
            AppendLogs("Cancelling verification...");
        }

        private async void ExecuteVerifyPdf417Async(string pdf417Data)
        {
            if (!await TryAcquireOperationAsync())
                return;

            try
            {
                AppendLogs("Start PDF417 barcode verification...");
                ProgressRing.IsActive = true;
                ProgressPanel.Visibility = Visibility.Visible;

                Debug.WriteLine($"[PDF417] Sending to SDK: length={pdf417Data.Length}");
                var result = await tap2idVerifier.VerifyPdf417Async(new Pdf417VerificationRequest(pdf417Data));
                AppendLogs($"PDF417 result: {result.Verdict} (confidence: {result.ConfidenceLevel}%)");
                if (result.Errors?.Count > 0)
                    AppendLogs($"PDF417 errors: {string.Join(", ", result.Errors.Select(e => $"[{e.Code}] {e.Message}"))}");
                DisplayPdf417Result(result);
            }
            catch (Pdf417ImplException ex)
            {
                AppendLogs($"PDF417 SDK error: [{ex.Error}] {ex.Message}");
                DispatcherQueue.TryEnqueue(() => ShowError($"PDF417 error: {ex.Message}"));
            }
            catch (Exception ex)
            {
                DispatcherQueue.TryEnqueue(() => ShowError(ex.Message));
            }
            finally
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    ProgressRing.IsActive = false;
                    ProgressPanel.Visibility = Visibility.Collapsed;
                });
                _semaphore.Release();
                if (_nfcAlwaysOn)
                    ScheduleNfcRearm(NfcRearmDelay); // the PDF417 scan paused the NFC loop
            }
        }

        private void DisplayPdf417Result(Pdf417VerificationResult result)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                // Verdict badge colour
                var (badgeColor, verdictLabel) = result.Verdict switch
                {
                    Pdf417Verdict.Real       => (Windows.UI.Color.FromArgb(0xff, 0x1e, 0x53, 0x4b), "REAL"),
                    Pdf417Verdict.Fake       => (Windows.UI.Color.FromArgb(0xff, 0xb2, 0x22, 0x22), "FAKE"),
                    Pdf417Verdict.Suspicious => (Windows.UI.Color.FromArgb(0xff, 0xff, 0x8c, 0x00), "SUSPICIOUS"),
                    Pdf417Verdict.Error      => (Windows.UI.Color.FromArgb(0xff, 0x80, 0x80, 0x80), "ERROR"),
                    _                        => (Windows.UI.Color.FromArgb(0xff, 0x80, 0x80, 0x80), "ERROR"),
                };
                Pdf417VerdictBadge.Background = new SolidColorBrush(badgeColor);
                Pdf417VerdictText.Text = verdictLabel;

                // Attestations — independent of the REAL/FAKE verdict (an authentic but expired
                // or under-age card is still REAL), so shown as separate badges. Attestations is
                // always non-null.
                //   AgeAttestationPassed: true = meets the profile's minimum age, false = does not
                //     (VwC gets 316 ERROR_AGE_VERIFICATION_FAIL), null = not requested.
                //   DocumentExpired: expiry strictly before today; false when not requested.
                var attestations = result.Attestations;
                var agePassed = attestations.AgeAttestationPassed;
                var ageLabel = agePassed switch
                {
                    true  => "✓ Passed",
                    false => "✗ Failed (under the minimum age)",
                    null  => "Not requested",
                };
                Pdf417AgeText.Text = ageLabel;
                Pdf417AgeBadge.Visibility = agePassed.HasValue ? Visibility.Visible : Visibility.Collapsed;
                if (agePassed.HasValue)
                {
                    // Same colours as the mDL "Age Over 21+" badge
                    Pdf417AgeBadge.Background = new SolidColorBrush(agePassed.Value ? Microsoft.UI.Colors.DarkGreen : Microsoft.UI.Colors.DarkRed);
                    Pdf417AgeBadgeText.Text = agePassed.Value ? "Age ✓" : "Age ✗";
                }

                var expiryLabel = attestations.DocumentExpired ? "⚠ Expired" : "Not expired (or not requested)";
                Pdf417ExpiryText.Text = expiryLabel;
                Pdf417ExpiryBadge.Visibility = attestations.DocumentExpired
                    ? Visibility.Visible
                    : Visibility.Collapsed;

                AppendLogs($"PDF417 attestations: age {ageLabel}, expiry {expiryLabel}");

                // Summary
                Pdf417ConfidenceText.Text = $"{result.ConfidenceLevel}%";
                Pdf417StateText.Text = result.StateCode ?? "—";

                // Authority crypto check — one card has at most one jurisdiction check;
                // result.Crypto.Authority tells you which (CaDmv / NyDmv / VaDmv / ...).
                Pdf417CryptoText.Text = result.Crypto is { Checked: true } crypto
                    ? $"{crypto.Authority}: {(crypto.Verified ? "✓ Verified" : "✗ Failed")}"
                    : "No crypto check";

                // Key parsed fields
                var displayFields = new[] { "given_name", "family_name", "birth_date", "expiry_date", "issue_date", "document_number", "issuing_jurisdiction", "resident_city", "resident_state" };
                var items = displayFields
                    .Where(k => result.Fields.TryGetValue(k, out var v) && v != null)
                    .Select(k => new DisplayItem(ViewModels.DocumentViewModel.PrettifyLabel(k) + ":", result.Fields[k].ToString()))
                    .ToList();
                Pdf417FieldsControl.ItemsSource = items;

                // Validation errors — also show container when Verdict==Error with no error list
                if (result.Errors?.Count > 0)
                {
                    Pdf417ErrorsText.Text = string.Join(Environment.NewLine,
                        result.Errors.Select(e => $"[{e.Code}] {e.Message}"));
                    Pdf417ErrorsContainer.Visibility = Visibility.Visible;
                }
                else if (result.Verdict == Pdf417Verdict.Error)
                {
                    Pdf417ErrorsText.Text = "Verification could not be completed. The barcode may be blank, unreadable, or not in AAMVA PDF417 format.";
                    Pdf417ErrorsContainer.Visibility = Visibility.Visible;
                }
                else
                {
                    Pdf417ErrorsContainer.Visibility = Visibility.Collapsed;
                }

                Pdf417ResultPanel.Visibility = Visibility.Visible;
                ShowResults();
            });
        }

        private async void HandleVerificationResult(Tap2iDResult tap2IdResult, MdocConfig mdocConfig)
        {
            ShowResults();
            if (tap2IdResult.ResultError == Tap2iDResultError.OK || tap2IdResult.ResultError == Tap2iDResultError.ERROR_CONSENT_DENIED_FOR_FEW)
            {
                // --- NEW MULTI-DOC ---
                var viewModels = new ObservableCollection<DocumentViewModel>();

                if (tap2IdResult.Documents != null && tap2IdResult.Documents.Count > 0)
                {
                    foreach (var doc in tap2IdResult.Documents)
                    {
                        // Convert SDK Model -> UI ViewModel (Async for image processing)
                        var vm = await DocumentViewModel.FromVerifiedDocumentAsync(doc);
                        viewModels.Add(vm);
                        // Log Validation Errors specific to this document
                        if (doc.Authentication.ValidationErrors.Count > 0)
                        {
                            foreach (var err in doc.Authentication.ValidationErrors)
                            {
                                // Log format: "[org.iso...mDL] Validation Error: Message"
                                AppendLogs($"[{doc.DocType}] Validation Error: {err.Message}");
                            }
                        }
                    }
                }
                else
                {
                    AppendLogs("No documents were returned in the response.");
                }

                // Bind the list
                this.DocumentsListControl.ItemsSource = viewModels;
                // ---------------------------              
            }
            else if (tap2IdResult.ResultError == Tap2iDResultError.OperationCancelled)
            {
                AppendLogs("Verification cancelled by user.");
            }
            else
            {
                AppendLogs($"Error reading mDoc: [{(int)tap2IdResult.ResultError}] {tap2IdResult.ResultError.GetDescription()}");
                // In the NFC loop, failures before any tag (no reader…) are retried and only logged.
                bool nfcLoopIdleFailure = mdocConfig.EngagementMode == DeviceEngagementMode.NFC
                    && _nfcAlwaysOn && !_nfcTagSeenThisAttempt;
                if (tap2IdResult.ResultError != Tap2iDResultError.NoNFCTagDetected && !nfcLoopIdleFailure)
                {
                    ShowError($"[{(int)tap2IdResult.ResultError}] {tap2IdResult.ResultError.GetDescription()}");
                }
            }

        }

        /// <summary>
        /// Starts listening on the HID ATOM / ATOM3K through Tap2iDSdk.HidAtom. The package owns the
        /// process's single Access-IS VideoOCR engine (loaded from the installed Access-IS Document
        /// Reader), so the sample never calls VideoOCR itself. Started once, after SDK init, so a
        /// scan never reaches an uninitialised SDK; the ATOM3K then needs ~40 s for its camera.
        /// </summary>
        private void StartHidAtomListening()
        {
            if (_isAtomListening) return;
            try
            {
                if (!HidAtomBarcodeReader.Start())
                {
                    AppendLogs("HID ATOM: the Access-IS Document Reader (ADRS) is not installed; ATOM readers are unavailable.");
                    return;
                }
            }
            catch (Exception ex)
            {
                AppendLogs($"HID ATOM: the reader could not be started: {ex.Message}");
                return;
            }
            _isAtomListening = true;

            // The engine sorts each scan by kind: an mdoc: engagement QR or an AAMVA PDF417. Other
            // barcodes are dropped. Only one capture per kind can wait, and the SDK's own QR capture
            // (QrPeripheral = HidAtom with no engagement string) uses the same slot, so the captured
            // string is always passed to VerifyMdocAsync.
            _ = ListenOnAtomAsync("QR", ct => HidAtomBarcodeReader.CaptureEngagementAsync(AtomListenTimeoutSeconds, ct), isPdf417: false);
            _ = ListenOnAtomAsync("PDF417", ct => HidAtomBarcodeReader.CapturePdf417Async(AtomListenTimeoutSeconds, ct), isPdf417: true);
        }

        private async Task ListenOnAtomAsync(string kind, Func<CancellationToken, Task<string>> capture, bool isPdf417)
        {
            while (true)
            {
                try
                {
                    OnScannerBarcode(await capture(CancellationToken.None), "ATOM", isPdf417);
                }
                catch (TimeoutException)
                {
                    // Nothing scanned in this window: wait again.
                }
                catch (Exception ex)
                {
                    AppendLogs($"HID ATOM: {kind} capture failed ({ex.Message}); retrying in 10 s.");
                    await Task.Delay(TimeSpan.FromSeconds(10));
                }
            }
        }

        private async Task RunBarcodeServerAsync()
        {
            try
            {
                await BarcodeServer.BarcodeServer.StartServerAsync(dataRead => OnScannerBarcode(dataRead, "network relay"));
            }
            catch (Exception ex)
            {
                AppendLogs($"Network relay unavailable: {ex.Message}");
            }
        }

        public void QRCodeRead(string value)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                DeviceEngagementStringInputTextBox.Text = value;
                OnQrCodeReadEvent(this, new QrCodeReadEventArgs(value));
            });
        }

        /// <summary>
        /// Opens the barcode scanners that deliver scans as serial data rather than keystrokes: the
        /// P35's built-in scanner (CIE-7857) and the HID TripTick Halo 210's scanner (CIE-6244), both
        /// found by their USB id and followed across plug and unplug, and the port configured in
        /// appsettings.json for other serial scanners. The configured port is only a fallback: it is
        /// not opened when a Halo is connected, and skipped when it is the P35's own port, which needs
        /// different settings.
        /// </summary>
        private async Task StartSerialScannersAsync()
        {
            _p35Scanner = new UsbSerialScannerService("P35 scanner", 0xE851, 0x1002, AppendLogs);
            // The Halo's scanner is 0DB5:0142. Its NFC unit (0DB5:0184) has a second serial port that
            // carries no barcode data; matching the scanner's own product id never picks it.
            _haloScanner = new UsbSerialScannerService("Halo scanner", 0x0DB5, 0x0142, AppendLogs, rtsEnable: true);
            string p35Port = await _p35Scanner.FindPortAsync();
            string haloPort = await _haloScanner.FindPortAsync();

            try
            {
                // Load the configuration from appsettings.json. ConfigLoader logs the resolved
                // path and outcome through AppendLogs itself (CIE-6244) — the port actually
                // opened is then logged by SerialPortManager.StartListening below, so the full
                // "config path -> resolved port -> port opened" chain is visible in the Logs
                // panel without needing a debugger or DebugView.
                PortConfig barcodeConfig = Hardware.ConfigLoader.LoadBarcodeConfig(AppendLogs);

                if (haloPort != null)
                {
                    AppendLogs($"[SerialPortManager] Halo scanner found on {haloPort}; the configured {barcodeConfig.PortName} is not used.");
                }
                else if (string.Equals(barcodeConfig.PortName, p35Port, StringComparison.OrdinalIgnoreCase))
                {
                    AppendLogs($"[SerialPortManager] {barcodeConfig.PortName} is the P35 scanner; it is read by the P35 scanner service instead.");
                }
                else
                {
                    _serialPortManager = new SerialPortManager(barcodeConfig);
                    _serialPortManager.StartListening(AppendLogs, dataRead => OnScannerBarcode(dataRead, "serial scanner"));
                }
            }
            catch (Exception ex)
            {
                AppendLogs($"Error initializing serial manager: {ex.Message}");
            }

            _p35Scanner.BarcodeScanned += scan => OnScannerBarcode(scan, "P35");
            _p35Scanner.Start();
            _haloScanner.BarcodeScanned += scan => OnScannerBarcode(scan, "Halo");
            _haloScanner.Start();
        }

        /// <summary>
        /// Single entry point for a barcode captured by the app itself rather than typed into the
        /// text box: the ATOM / ATOM3K (Tap2iDSdk.HidAtom), the P35 scanner, the configured serial
        /// scanner and the network relay. Safe to call from any thread.
        ///
        /// The raw payload goes to the SDK unchanged: a PDF417's DMV signature (CA/NY/VA) is checked
        /// over the exact bytes, so its CR/LF/RS separators must survive. The text box only shows a
        /// one-line copy, which also keeps its change handler from starting a second verification.
        /// </summary>
        public void OnScannerBarcode(string raw, string source) =>
            OnScannerBarcode(raw, source, raw?.StartsWith("@", StringComparison.Ordinal) == true);

        /// <param name="isPdf417">True for a licence PDF417, false for an engagement QR. The ATOM
        /// already knows which one it read; the other scanners are told apart by the "@" prefix.</param>
        private void OnScannerBarcode(string raw, string source, bool isPdf417)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (string.IsNullOrWhiteSpace(raw)) return;

                var now = DateTime.UtcNow;
                if (raw == _lastScan && now - _lastScanUtc < DuplicateScanWindow)
                    return;
                _lastScan = raw;
                _lastScanUtc = now;

                if (isPdf417)
                {
                    AppendLogs($"PDF417 scanned by {source} ({raw.Length} chars).");
                    DeviceEngagementStringInputTextBox.Text = raw.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ");
                    ExecuteVerifyPdf417Async(raw);
                }
                else
                {
                    var engagement = raw.Trim();
                    AppendLogs($"QR code scanned by {source} ({engagement.Length} chars).");
                    DeviceEngagementStringInputTextBox.Text = engagement;
                    ExecuteVerifyMdoc(engagement, DeviceEngagementMode.QrCode);
                }
            });
        }
        #endregion

        #region UI Handling
        // Clear all the text block fields
        private void ClearResultsFields()
        {
            DocumentsListControl.ItemsSource = null;
            Pdf417ResultPanel.Visibility = Visibility.Collapsed;
            Pdf417FieldsControl.ItemsSource = null;
            Pdf417ErrorsContainer.Visibility = Visibility.Collapsed;
        }

        private void ClearEngagementFields()
        {
            DeviceEngagementStringInputTextBox.Text = String.Empty;
            // Drop the held copy of the last scan too: a licence barcode is personal data.
            _lastScan = null;
        }

        private void ClearLicenseFields()
        {
            InputLicenseTextBox.Text = String.Empty;
        }

        // Activate/Deactivate the MDoc reading functionalities
        private void ResetUiInterfaceWhenSdkNotInitialized()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                DeativateDeviceEngagementFeatures();
            });
        }

        private void ShowResults()
        {
            hideResultsTimer.Stop();  // Stop any previous running timer
            hideResultsTimer.Start();
        }

        // Hide results after the timer completes
        private void HideResultsAfterTimeout(object sender, object e)
        {
            ClearResultsFields();
            // The scanned string (e.g. a licence's PDF417) would otherwise stay on screen.
            ClearEngagementFields();
            hideResultsTimer.Stop();  // Stop the timer
        }

        private async void ShowError(string message)
        {
            var xamlRoot = this.Content?.XamlRoot;
            if (xamlRoot == null)
            {
                // Visual tree not ready yet — log instead of crashing
                AppendLogs($"Error (dialog suppressed, XamlRoot not ready): {message}");
                return;
            }

            // Only one ContentDialog can be open at a time; a second ShowAsync throws, which would
            // crash this async void. With the NFC loop, errors can follow each other quickly.
            if (_errorDialogOpen)
            {
                AppendLogs($"Error (dialog already open): {message}");
                return;
            }

            ContentDialog dialog = new()
            {
                Title = "Error",
                Content = message,
                PrimaryButtonText = "OK",
                XamlRoot = xamlRoot,
            };

            _errorDialogOpen = true;
            try
            {
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                AppendLogs($"Error (dialog could not be shown: {ex.Message}): {message}");
            }
            finally
            {
                _errorDialogOpen = false;
            }
        }

        private void UpdatePcscReaderName()
        {
            String pcscReaderName = "Build-in reader";
            if (_peripheralVm.SelectedNfcModel is PeripheralModel.HidAtom or PeripheralModel.HidAtom3K)
            {
                pcscReaderName = "Hid Atom";
            }
            UpdateNfcEngagementStatus($"{pcscReaderName} is waiting for mDoc");
        }

        private void UpdateNfcEngagementStatus(String newStatus)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                NfcReaderTextStatus.Text = newStatus;
            });
        }

        private void UpdateProfileInformation(String profile)
        {
            if (String.IsNullOrEmpty(profile))
            {
                profile = "UNKNOWN";
            }
            DispatcherQueue.TryEnqueue(() =>
            {
                profileTextBlock.Text = profile;
            });
        }

        private void UpdateSdkVersionInformation(String version)
        {
            if (String.IsNullOrEmpty(version))
            {
                version = "UNKNOWN";
            }
            DispatcherQueue.TryEnqueue(() =>
            {
                VersionTextBlock.Text = $"Version: {version}";
            });
        }

        private void DisplayQrCodeDeviceEngagementFeature()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                DeviceEngagementTitle.Text = "QR Code Device Engagement / PDF417 DL";
                CameraCommandsPanel.Visibility = Visibility.Visible;
                DeviceEngagementStringInputTextBox.IsFocusEngaged = false;
                NfcCommandsPanel.Visibility = Visibility.Collapsed;
                ProgressRing.IsActive = false;
                ActivateScanQrCodeButton.Resources["ButtonBorderBrushDisabled"] = new SolidColorBrush(UI_DARK_GREEN);
                ActivateScanQrCodeButton.Resources["ButtonBackgroundDisabled"] = new SolidColorBrush(Colors.White);
                ActivateScanQrCodeButton.Resources["ButtonForegroundDisabled"] = new SolidColorBrush(UI_DARK_GREEN);
                ActivateScanQrCodeButton.IsEnabled = false;
                ActivateNfcButton.IsEnabled = true;
            });
        }

        private void DisplayNfcDeviceEngagementFeature()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                DeviceEngagementTitle.Text = "NFC Device Engagement";
                CameraCommandsPanel.Visibility = Visibility.Collapsed;
                NfcCommandsPanel.Visibility = Visibility.Visible;
                ProgressRing.IsActive = true;
                ActivateNfcButton.Resources["ButtonBorderBrushDisabled"] = new SolidColorBrush(UI_DARK_GREEN);
                ActivateNfcButton.Resources["ButtonBackgroundDisabled"] = new SolidColorBrush(Colors.White);
                ActivateNfcButton.Resources["ButtonForegroundDisabled"] = new SolidColorBrush(UI_DARK_GREEN);
                ActivateNfcButton.IsEnabled = false;
                ActivateScanQrCodeButton.IsEnabled = true;
            });
        }

        private void DeativateDeviceEngagementFeatures()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                DeviceEngagementTitle.Text = "";
                CameraCommandsPanel.Visibility = Visibility.Collapsed;
                NfcCommandsPanel.Visibility = Visibility.Collapsed;
                ActivateNfcButton.IsEnabled = false;
                ActivateScanQrCodeButton.IsEnabled = false;
            });
        }

        private void ActivateDeviceEngagementOptions()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                DeviceEngagementTitle.Text = "Select device engagement method";
                CameraCommandsPanel.Visibility = Visibility.Collapsed;
                NfcCommandsPanel.Visibility = Visibility.Collapsed;
                ActivateNfcButton.IsEnabled = true;
                ActivateScanQrCodeButton.IsEnabled = true;
            });
        }
        #endregion               

        #region SDK Interaction
        private void OnStateChanged(VerifyState verifyState)
        {
            // For NFC the SDK raises DeviceEngagementStarted only once a tag is in the field
            // (CIE-4519), so the status stays at "waiting for mDoc" until then. QR raises it
            // straight away, but the NFC status panel is not shown in QR mode.
            if (verifyState == VerifyState.DeviceEngagementStarted && _currentEngagementMode == DeviceEngagementMode.NFC)
            {
                // A phone is on the reader: from here a barcode scan no longer pre-empts this wait.
                _nfcWaitingForTag = false;
                _nfcTagSeenThisAttempt = true;
                UpdateNfcEngagementStatus("mDoc detected, device engagement started");
            }
            else if (verifyState == VerifyState.DeviceConnectionStarted)
            {
                UpdateNfcEngagementStatus("Device Engagement completed");
            }
            AppendLogs(Utils.GetHumanReadableVerifyStateMessage(verifyState));
        }
        private void OnSdkInitializationFailure(Tap2iDResultError error, string ErrorMessage)
        {
            string issue = $"Sdk initialization failed ERROR: {error} - {ErrorMessage}";
            AppendLogs(issue);
            ResetUiInterfaceWhenSdkNotInitialized();

            DispatcherQueue.TryEnqueue(() =>
            {
                ShowError(ErrorMessage);
            });
        }

        private void OnSdkInitializationSuccess(SdkInitializationResult result)
        {
            AppendLogs("Initialization successfull");
            AppendLogs($"License Validity: {DateTime.UnixEpoch.AddMilliseconds(Convert.ToDouble(result.LicenseVerificationResult.ExpiryDate))}");
            ActivateDeviceEngagementOptions();
            StartHidAtomListening();
            UpdateProfileInformation(result.LicenseVerificationResult.ReaderProfile);
            _ = LogPeripheralStatusAsync();
        }

        /// <summary>
        /// Queries the SDK for per-capability peripheral status and populates the
        /// reader-selection view model (auto-selecting any detected reader).
        /// </summary>
        private async Task LogPeripheralStatusAsync()
        {
            try
            {
                AppendLogs("Checking peripheral hardware...");
                var status = await tap2idVerifier.CheckAllPeripheralStatusAsync();
                var available = string.Join(", ", status.Available.Select(p => p.Model));
                AppendLogs($"Available readers: {(string.IsNullOrEmpty(available) ? "none" : available)}");
                DispatcherQueue.TryEnqueue(() => _peripheralVm.Populate(status));
            }
            catch (Exception ex)
            {
                AppendLogs($"Peripheral status check failed: {ex.Message}");
            }
        }
        #endregion

        #region UI Event Handlers
        private async void VerifyLicenseAndInitButton_Click(object sender, RoutedEventArgs e)
        {
            AppendLogs("Start SDK Initialization...");

            string apiKey = string.IsNullOrWhiteSpace(InputLicenseTextBox.Text)
                ? new LicenseManager().GetLicense()
                : InputLicenseTextBox.Text;

            // Check if apiKey is null or empty
            if (string.IsNullOrEmpty(apiKey))
            {
                AppendLogs("Error: apiKey is null or empty.");
                DeativateDeviceEngagementFeatures();
                return;
            }

            try
            {
                VerifyLicenseAndInitButton.IsEnabled = false;
                AppendLogs("Checking Bluetooth hardware compatibility...");

                // Check if bluetooth is correctly enabled.
                BluetoothStatus status = await tap2idVerifier.CheckBlePeripheralSupportAsync();

                // Check the result of the status check.
                if (status.IsSupported)
                {
                    AppendLogs($"Bluetooth Check Success: {status.Message}");
                    AppendLogs("Proceeding with SDK Initialization...");

                    // 4. Only call InitSdk if the Bluetooth check passed.
                    tap2idVerifier.InitSdk(
                        new CoreSdkConfig
                        {
                            ApiKey = apiKey
                        },
                        _initSdkResultListener
                    );
                }
                else
                {
                    // If the check fails, log the detailed message and stop.
                    AppendLogs($"Bluetooth Check Failed: {status.Message}");
                    AppendLogs("Please ensure a compatible Bluetooth dongle is connected and supports the BLE Peripheral role.");
                    DeativateDeviceEngagementFeatures();
                    // 4. Only call InitSdk if the Bluetooth check passed.
                    tap2idVerifier.InitSdk(
                        new CoreSdkConfig
                        {
                            ApiKey = apiKey
                        },
                        _initSdkResultListener
                    );
                }
            }
            catch (Exception ex)
            {
                AppendLogs("Initialization Exception");
                AppendLogs($"Error: {ex.Message}");
                AppendLogs($"Stack Trace: {ex.StackTrace}");
                DeativateDeviceEngagementFeatures();
            }

            VerifyLicenseAndInitButton.IsEnabled = true;
        }

        private void ClearEngagementStringButton_Click(object sender, RoutedEventArgs e)
        {
            ClearEngagementFields();
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            ClearLicenseFields();
            ClearEngagementFields();
        }

        private void ScanQRCodeButton_Click(object sender, RoutedEventArgs e)
        {
            _mainWindowViewModel.ScanQrCodeWithCamera();
        }

        private void ActivateNfcButton_Click(object sender, RoutedEventArgs e)
        {
            // NFC Tap switches the always-on loop on; Cancel or Scan Qr Code switches it off.
            AppendLogs("NFC always-on: waiting for phones until Cancel.");
            _nfcAlwaysOn = true;
            _nfcFailuresWithoutTag = 0;
            DisplayNfcDeviceEngagementFeature();
            _isPcscReaderActivated = true;
            StartNfcDeviceEngagement();
        }

        private void ActivateScanQrCodeButton_Click(object sender, RoutedEventArgs e)
        {
            // Choosing QR switches the NFC loop off and releases a pending NFC wait, so a phone
            // tapped while in QR mode is not read over NFC.
            _nfcAlwaysOn = false;
            _nfcRearmGeneration++;
            StopNfcWait("NFC wait stopped: QR mode selected.");
            _isPcscReaderActivated = false;
            DisplayQrCodeDeviceEngagementFeature();
        }

        private void DeviceEngagementStringInputTextChangenHandler(object sender, Microsoft.UI.Xaml.Controls.TextChangedEventArgs args)
        {
            var text = DeviceEngagementStringInputTextBox.Text;
            if (text.Length > 0 && (text.Contains("\r") || text.Contains("\n")))
            {
                if (text.StartsWith("@", StringComparison.Ordinal))
                {
                    // PDF417: save original data (with separators) before flattening for display.
                    var pdf417Data = text;
                    DeviceEngagementStringInputTextBox.Text = text.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ");
                    ExecuteVerifyPdf417Async(pdf417Data);
                    return;
                }

                // QR code device engagement: strip the trailing \r or \n scanners append, then verify.
                int termIdx = text.IndexOf("\r") >= 0 ? text.IndexOf("\r") : text.IndexOf("\n");
                DeviceEngagementStringInputTextBox.Text = text.Substring(0, termIdx);
                ExecuteVerifyMdoc(DeviceEngagementStringInputTextBox.Text, DeviceEngagementMode.QrCode);
            }
        }

        private async void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SettingsDialog();

            // 1. Load current values from LocalSettings (or use Defaults)
            var localSettings = ApplicationData.Current.LocalSettings.Values;

            dialog.NfcTimeout = localSettings[KEY_NFC_TIMEOUT]?.ToString() ?? MdocConfig.DefaultNfcTimeout.ToString();
            dialog.BleConnectionTimeout = localSettings[KEY_BLE_CONN_TIMEOUT]?.ToString() ?? MdocConfig.DefaultBleConnectionTimeout.ToString();
            dialog.BleDataTimeout = localSettings[KEY_BLE_DATA_TIMEOUT]?.ToString() ?? MdocConfig.DefaultBleDataTimeout.ToString();

            // 2. Show Dialog (Must set XamlRoot)
            dialog.XamlRoot = this.Content.XamlRoot;
            var result = await dialog.ShowAsync();

            // 3. Save if user clicked "Save"
            if (result == ContentDialogResult.Primary)
            {
                localSettings[KEY_NFC_TIMEOUT] = dialog.NfcTimeout;
                localSettings[KEY_BLE_CONN_TIMEOUT] = dialog.BleConnectionTimeout;
                localSettings[KEY_BLE_DATA_TIMEOUT] = dialog.BleDataTimeout;

                AppendLogs("Timeout settings saved.");
            }
        }

        private MdocConfig CreateMdocConfig(DeviceEngagementMode mode, string engagementString = "")
        {
            var config = new MdocConfig
            {
                EngagementMode = mode,
                DeviceEngagementString = engagementString,
                BleWriteOption = BleWriteOption.Write,
                IsReaderAuthentication = false
            };

            // Load Timeouts from Persistence
            var localSettings = ApplicationData.Current.LocalSettings.Values;

            // Attempt to parse saved values. MdocConfig setters will handle range enforcement (5-300s) automatically.
            if (int.TryParse(localSettings[KEY_NFC_TIMEOUT]?.ToString(), out int nfc))
                config.NfcDeviceEngagementTimeout = nfc;

            if (int.TryParse(localSettings[KEY_BLE_CONN_TIMEOUT]?.ToString(), out int bleConn))
                config.BleConnectionTimeout = bleConn;

            if (int.TryParse(localSettings[KEY_BLE_DATA_TIMEOUT]?.ToString(), out int bleData))
                config.BleConsentAndDataTransferTimeout = bleData;

            // Per-capability reader selection from the view model (null = SDK default).
            //
            // Each capability is independent — you can target one reader for NFC/QR and let a
            // different transport handle BLE. Only set a peripheral when that reader's hardware
            // for that capability is actually present.
            //
            // Combo readers (e.g. the P35: NFC + QR scanner + BLE modem) expose multiple
            // capabilities, but the machine may only have some of them wired up. Example: a P35
            // plugged in for NFC only — its BLE modem interface (USB E851:3503) is absent.
            //   • Correct:   NfcPeripheral = P35, BlePeripheral = null  -> BLE falls back to the
            //                configured transport (e.g. Bumble); P35 NFC + Bumble BLE work together.
            //   • Incorrect: BlePeripheral = P35 with no P35 BLE modem attached -> the SDK tries
            //                the P35 BLE modem and fails with "No P35 BLE modem (E851:3503) detected".
            // Only set BlePeripheral to a combo reader when its BLE modem is physically connected.
            //
            // The P35 is never handed over as the QR peripheral: its BLE data transfer is broken in
            // firmware (ERROR_DR_CONNECTION_TIMEOUT), and the app reads its QR scanner itself
            // (UsbSerialScannerService). Left unset, the SDK runs the post-scan handoff over
            // BlePeripheral / the configured dongle instead.
            var selectedQr = _peripheralVm.SelectedQrModel;
            if (selectedQr == PeripheralModel.P35)
            {
                AppendLogs("QR reader P35: scans are read by the sample itself; the BLE handoff uses the selected BLE reader.");
                selectedQr = null;
            }
            config.QrPeripheral = selectedQr;
            config.NfcPeripheral = _peripheralVm.SelectedNfcModel;
            config.BlePeripheral = _peripheralVm.SelectedBleModel;
            AppendLogs($"Selected readers -> QR: {config.QrPeripheral?.ToString() ?? "default"}, " +
                      $"NFC: {config.NfcPeripheral?.ToString() ?? "default"}, " +
                      $"BLE: {config.BlePeripheral?.ToString() ?? "default"}");

            AppendLogs($"MdocConfig initialized with Timeouts -> NFC: {config.NfcDeviceEngagementTimeout}, BLE-Conn: {config.BleConnectionTimeout}, BLE-Data: {config.BleConsentAndDataTransferTimeout}");

            return config;
        }
        #endregion  

        #region Logs
        internal void AppendLogs(String toAppend)
        {
            if (toAppend != null)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    Debug.WriteLine(toAppend);
                    if (LogsValueTextBlock.Text.Length > 10000)
                    {
                        // Keep the last 500 characters. Substring(start, length) with the full text
                        // length as the count overran the string and crashed the app on the UI thread.
                        LogsValueTextBlock.Text =
                            LogsValueTextBlock.Text.Substring(LogsValueTextBlock.Text.Length - 500)
                            + toAppend
                            + Environment.NewLine;
                    }
                    else
                    {
                        LogsValueTextBlock.Text += toAppend + Environment.NewLine;
                    }
                });
            }
        }
        // Height always left for the logs panel (title bar, Copy Logs button and some lines).
        private const double LogsMinHeight = 220;

        /// <summary>
        /// Caps the left panel's height so the logs below it never shrink under
        /// <see cref="LogsMinHeight"/>; the left panel scrolls instead when the window is short.
        /// </summary>
        private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            const double logsMargins = 20; // logs panel margin, top + bottom
            LeftPanelScrollViewer.MaxHeight = Math.Max(150, e.NewSize.Height - LogsMinHeight - logsMargins);
        }

        private void LogsValueTextBlock_Loaded(object sender, RoutedEventArgs e)
        {
            ScrollToBottom();
        }

        private void LogsValueTextBlock_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ScrollToBottom();
        }

        private void ScrollToBottom()
        {
            LogsScrollViewer.ChangeView(null, LogsScrollViewer.ScrollableHeight, null);
        }

        private void CopyLogsButton_Click(object sender, RoutedEventArgs e)
        {
            DataPackage dataPackage = new();
            dataPackage.RequestedOperation = DataPackageOperation.Copy;
            dataPackage.SetText(LogsValueTextBlock.Text);
            Clipboard.SetContent(dataPackage);
        }
        #endregion

    }

    public class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            string text = value as string;
            // Collapse if the string is null, empty, or equals "0"
            return string.IsNullOrEmpty(text) || text == "0"
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public class QrCodeReadEventArgs(string qrCodeValue) : EventArgs
    {
        public string QrCodeValue { get; set; } = qrCodeValue;
    }
}
