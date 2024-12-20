using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Tap2iDSdk;
using Tap2iDSdk.Extension;
using Tap2iDSdk.Model;
using Windows.ApplicationModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;
using Windows.UI;
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

        private MainWindowViewModel _mainWindowViewModel;
        public static Color UI_DARK_GREEN = Windows.UI.Color.FromArgb(0xff, 0x1e, 0x53, 0x4B);
        public static Color UI_WHITE_GREEN = Windows.UI.Color.FromArgb(0xff, 0xd1, 0xe4, 0xe3);

        //HID barcode reader
        public ViewModel videoOCR;
        private bool _isPcscReaderActivated = false;
        private bool _isPcscHidReaderAvailable = false;


        public MainWindow()
        {
            this.InitializeComponent();

            m_AppWindow = GetAppWindowForCurrentWindow();
            m_AppWindow.Title = "Tap2iD SDK Sample for Windows";
            m_AppWindow.SetIcon("Assets/credence.ico");

            _mainWindowViewModel = new MainWindowViewModel(this);

            tap2idVerifier = VerifyMdocFactory.CreateVerifyMdoc();
            stateDelegate = new DelegateVerifyState();
            stateDelegate.OnVerifyState = OnStateChanged;
            _initSdkResultListener = new();
            _initSdkResultListener.OnInitializationSuccess = OnSdkInitializationSuccess;
            _initSdkResultListener.OnInitializationFailure = OnSdkInitializationFailure;

            ResetUiInterfaceWhenSdkNotInitialized();

            OnQrCodeReadEvent += QrCodeScanned_ExecuteVerifyMdoc;

            // Update the window title with the app name and version
            this.Title += $" - v{GetAppVersion()}";

            // Initialise HID Scanner
            videoOCR = new ViewModel(this);
            videoOCR.initialiseReader();
            videoOCR.StartBarcodeServer((dataRead) =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    DeviceEngagementStringInputTextBox.Text = dataRead;
                    ExecuteVerifyMdoc(dataRead, DeviceEngagementMode.QrCode);
                });

            }
            );
        }

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

        // Clear all the text block fields
        private void ClearResultsFields()
        {
            AgeOver21TextBlockBackground.Visibility = Visibility.Collapsed;
            foreach (var child in ResultsGrid.Children)
            {
                if (child is TextBlock textBlock && textBlock.Tag?.ToString() == "clearable")
                {
                    textBlock.Text = string.Empty; // Clear the TextBlock
                }
            }
            ProfileImage.Source = null;
        }

        private void ClearEngagementFields()
        {
            DeviceEngagementStringInputTextBox.Text = String.Empty;
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

        private void StartNfcDeviceEngagement()
        {
            //Start reading
            if (_isPcscReaderActivated)
            {
                UpdatePcscReaderName();
                ExecuteVerifyMdoc("", DeviceEngagementMode.NFC);
            }

        }

        void QrCodeScanned_ExecuteVerifyMdoc(object sender, QrCodeReadEventArgs qrCodeReadEventArgs)
        {
            ExecuteVerifyMdoc(qrCodeReadEventArgs.QrCodeValue, DeviceEngagementMode.QrCode);
        }

        private SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        private async void ExecuteVerifyMdoc(string deviceEngagementString, DeviceEngagementMode deviceEngagementMode)
        {
            var acquiredSemaphore = false;

            try
            {
                // Try to enter the semaphore, if already in use, it will wait
                acquiredSemaphore = await _semaphore.WaitAsync(0);
                if (!acquiredSemaphore)
                    return;  // If semaphore is in use, exit the method

                if (deviceEngagementMode == DeviceEngagementMode.NFC)
                {
                    AppendLogs("Waiting for mDoc");
                }
                else
                {
                    AppendLogs("Start mDoc Reading with QrCode Device Engagement");
                }
                ProgressRing.IsActive = true;
                ProgressPanel.Visibility = Visibility.Visible;

                BleWriteOption writeOption = BleWriteOption.WriteWithoutResponse;
                var mdocConfig = new MdocConfig
                {
                    DeviceEngagementString = deviceEngagementString,
                    EngagementMode = deviceEngagementMode,
                    BleWriteOption = writeOption,
                };

                var tap2IdResult = await Task.Run(() => tap2idVerifier.VerifyMdocAsync(mdocConfig, stateDelegate));

                ShowResults();
                if (tap2IdResult.ResultError == Tap2iDResultError.OK || tap2IdResult.ResultError == Tap2iDResultError.ERROR_CONSENT_DENIED_FOR_FEW)
                {
                    var result = tap2IdResult.Identity;
                    UpdateAgeOver21(result.isAgeOver21);
                    FirstNameTextBlock.Text = result.givenNames;
                    LastNameTextBlock.Text = result.familyName;
                    SexTextBlock.Text = result.sex.ToString() == "UNKNOWN" ? "" : result.sex.ToString();
                    BirthDateTextBlock.Text = result.birthDate?.ToString("dd-MM-yyyy");
                    DocumentTextBlock.Text = result.documentNumber;
                    IssuingTextBlock.Text = result.issuingAuthority;
                    IssueDateTextBlock.Text = result.issueDate?.ToString("dd-MM-yyyy");
                    ExpiryDateTextBlock.Text = result.expiryDate?.ToString("dd-MM-yyyy");
                    EyeColorTextBlock.Text = result.eyeColor;
                    HeightTextBlock.Text = result.height.ToString();
                    ResidentAddressTextBlock.Text = result.residentAddress;

                    var portrait = result.portrait;
                    if (portrait != null && portrait.Length > 0)
                    {
                        await SetImageAsync(ProfileImage, portrait);
                    }
                    //display if any error or warning for certificate:
                    if (tap2IdResult.Authentication?.CertValidationError.Count > 0)
                    {
                        var exceptionToDisplay = string.Join("\n", tap2IdResult.Authentication.CertValidationError.Cast<Exception>().Select(ex => ex.ToString()));
                        AppendLogs(exceptionToDisplay);
                    }
                }
                else
                {
                    AppendLogs($"Error reading mDoc: {(int)tap2IdResult.ResultError}");
                    AppendLogs($"Error reading mDoc: {tap2IdResult.ResultError.GetDescription()}");
                    if (tap2IdResult.ResultError != Tap2iDResultError.ERROR_GENERAL_NFC_DEVICE_ENGAGEMENT)
                    {
                        ShowError(tap2IdResult.ResultError.GetDescription());
                    }
                    ProgressRing.IsActive = false;
                    ProgressPanel.Visibility = Visibility.Collapsed;
                }

                if (deviceEngagementMode == DeviceEngagementMode.NFC)
                {
                    ActivateDeviceEngagementOptions();

                }

            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
                ProgressRing.IsActive = false;
                ProgressPanel.Visibility = Visibility.Collapsed;
            }
            finally
            {
                if (acquiredSemaphore)
                {
                    // Only release if semaphore was successfully acquired
                    _semaphore.Release();
                }
            }
        }

        private void OnStateChanged(VerifyState verifyState)
        {
            if (verifyState == VerifyState.DeviceConnectionStarted)
            {
                UpdateNfcEngagementStatus("Device Engagement completed");
            }
            AppendLogs(GetHumanReadableVerifyStateMessage(verifyState));
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
            VerifyAndInitializePcscReader();
            UpdateProfileInformation(result.LicenseVerificationResult.ReaderProfile);
            UpdateSdkVersionInformation(tap2idVerifier.GetVersion());
        }

        private void VerifyLicenseAndInitButton_Click(object sender, RoutedEventArgs e)
        {
            AppendLogs("Start SDK Initialization...");
            string apiKey = InputLicenseTextBox.Text;
            try
            {
                tap2idVerifier.InitSdk(
                    new CoreSdkConfig
                    {
                        ApiKey = apiKey
                    },
                    _initSdkResultListener
                );
            }
            catch (Exception ex)
            {
                AppendLogs("Initialization Exception");
                AppendLogs($"Error: {ex.Message}");
                DeativateDeviceEngagementFeatures();
            }
        }

        private void ClearEngagementStringButton_Click(object sender, RoutedEventArgs e)
        {
            ClearEngagementFields();
            ClearResultsFields();
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            ClearLicenseFields();
            ClearEngagementFields();
            ClearResultsFields();
        }

        private async Task SetImageAsync(Microsoft.UI.Xaml.Controls.Image imageControl, byte[] imageBytes)
        {
            using (InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream())
            {
                await stream.WriteAsync(imageBytes.AsBuffer());
                stream.Seek(0);
                BitmapImage bitmapImage = new BitmapImage();
                await bitmapImage.SetSourceAsync(stream);
                imageControl.Source = bitmapImage;
            }
        }

        private void ScanQRCodeButton_Click(object sender, RoutedEventArgs e)
        {
            _mainWindowViewModel.ScanQrCodeWithCamera();
        }

        private void ShowResults()
        {
            ResultsGrid.Visibility = Visibility.Visible;
        }

        private void StartCamera()
        {
            // Hide results and progress, show camera
            ResultPanel.Visibility = Visibility.Collapsed;
        }

        public void QRCodeRead(string value)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                DeviceEngagementStringInputTextBox.Text = value;
                OnQrCodeReadEvent(this, new QrCodeReadEventArgs(value));
            });
        }

        private void ActivateNfcButton_Click(object sender, RoutedEventArgs e)
        {
            DisplayNfcDeviceEngagementFeature();
            _isPcscReaderActivated = true;
            StartNfcDeviceEngagement();
        }

        private void ActivateScanQrCodeButton_Click(object sender, RoutedEventArgs e)
        {
            _isPcscReaderActivated = false;
            DisplayQrCodeDeviceEngagementFeature();
        }

        private void DisplayQrCodeDeviceEngagementFeature()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                DeviceEngagementTitle.Text = "QRCode Device Engagement";
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

        private void DeviceEngagementStringInputTextChangenHandler(object sender, Microsoft.UI.Xaml.Controls.TextChangedEventArgs args)
        {
            if (DeviceEngagementStringInputTextBox.Text.Length > 0)
            {
                if (DeviceEngagementStringInputTextBox.Text.Contains("\r"))
                {
                    DeviceEngagementStringInputTextBox.Text = DeviceEngagementStringInputTextBox.Text.Substring(0, DeviceEngagementStringInputTextBox.Text.IndexOf("\r"));
                    ExecuteVerifyMdoc(DeviceEngagementStringInputTextBox.Text, DeviceEngagementMode.QrCode);
                }
            }
        }

        private void VerifyAndInitializePcscReader()
        {

            videoOCR.startReader();
            string hidReaderSerialNumber = videoOCR.getUnitSerialNumber();
            if ((hidReaderSerialNumber != null) && (hidReaderSerialNumber.Length > 0))
            {
                AppendLogs($"HID reader available. Serial Number: {hidReaderSerialNumber}");
                _isPcscHidReaderAvailable = true;
            }
            else
            {
                AppendLogs($"No HID reader found.");
                _isPcscHidReaderAvailable = false;
            }
            UpdatePcscReaderName();
        }

        private async void ShowError(string message)
        {

            ContentDialog dialog = new()
            {
                Title = "Error",
                Content = message,
                PrimaryButtonText = "OK",
                XamlRoot = this.Content.XamlRoot,
            };

            await dialog.ShowAsync();
        }

        private void UpdatePcscReaderName()
        {
            String pcscReaderName = "Build-in reader";
            if (_isPcscHidReaderAvailable)
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

        private void UpdateAgeOver21(AgeOverPossibleValues value)
        {
            if (value == AgeOverPossibleValues.Not_Retrieved)
            {
                AgeOver21TextBlock.Visibility = Visibility.Collapsed;
                AgeOver21TextBlockBackground.Visibility = Visibility.Collapsed;
            }
            else
            {
                AgeOver21TextBlock.Visibility = Visibility.Visible;
                AgeOver21TextBlockBackground.Visibility = Visibility.Visible;
                if (value == AgeOverPossibleValues.True)
                {
                    AgeOver21TextBlockBackground.Background = new SolidColorBrush(Colors.DarkGreen);
                }
                else if (value == AgeOverPossibleValues.False)
                {
                    AgeOver21TextBlockBackground.Background = new SolidColorBrush(Colors.DarkRed);
                }
            }
        }

        private void AppendLogs(String toAppend)
        {
            if (toAppend != null)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    Debug.WriteLine(toAppend);
                    if (LogsValueTextBlock.Text.Length > 10000)
                    {
                        LogsValueTextBlock.Text =
                            LogsValueTextBlock.Text.Substring(LogsValueTextBlock.Text.Length - 500, LogsValueTextBlock.Text.Length)
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

        private String GetHumanReadableVerifyStateMessage(VerifyState verifyState)
        {
            String baseMessage = "Verification state change: ";
            switch (verifyState)
            {
                case VerifyState.DeviceEngagementStarted:
                    return baseMessage + "Device Engagement started";
                case VerifyState.DeviceEngagementEnded:
                    return baseMessage + "Device Engagement Ended";
                case VerifyState.DeviceConnectionStarted:
                    return baseMessage + "Device connection started";
                case VerifyState.DeviceConnectionEnded:
                    return baseMessage + "Connection ended";
                case VerifyState.UserConsentStarted:
                    return baseMessage + "Consent request sent";
                case VerifyState.UserConsentEnded:
                    return baseMessage + "Consent received";
                case VerifyState.DataTransferStarted:
                    return baseMessage + "Data transfer started";
                case VerifyState.DataTransferEnded:
                    return baseMessage + "Data transfer successful";
                case VerifyState.DataValidationStarted:
                    return baseMessage + "Data validation started";
                case VerifyState.DataValidationEnded:
                    return baseMessage + "Data validation validation successful";
                default: return baseMessage + "Unknwon";

            }
        }
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
