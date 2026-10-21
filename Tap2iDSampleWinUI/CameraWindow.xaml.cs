using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Tap2iDSampleWinUI.ViewModels;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Playback;
using ZXing;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Tap2iDSampleWinUI
{
    /// <summary>
    /// Scans a QR / PDF417 code with a webcam. Camera selection follows the Windows App
    /// (WebcamService / WebcamPolicy): document-reader imaging heads are left out, the back
    /// camera is preferred, and a flip button cycles through the others (CIE-8234).
    /// </summary>
    public sealed partial class CameraWindow : Window
    {
        /// <summary>
        /// USB vendor ids whose cameras are document-reader imaging heads, not webcams. The
        /// Access-IS ATOM / ATOM3K exposes its imaging head as a video-capture device ("ACCESS-IS
        /// READER", VID 0DB5); Tap2iDSdk.HidAtom already drives it, so opening it here shows nothing.
        /// </summary>
        private static readonly string[] DocumentReaderVendorIds = { "0DB5" }; // Access-IS

        private readonly SoftwareBitmapBarcodeReader _zxingReader;
        private readonly Action<string> _log;

        private MediaCapture _capture;
        private MediaFrameReader _frameReader;
        private MediaSource _mediaSource;
        private List<DeviceInformation> _cameras = new();
        private int _currentCameraIndex;
        private bool _isSwitchingCamera;
        private bool _isDisposed = false;

        public delegate void OnQrCodeReadByCameraEventHandler(object sender, QrCodeReadByCameraEventArgs qrCodeReadByCameraEventArgs);
        public event OnQrCodeReadByCameraEventHandler qrCodeReadEvent;

        /// <param name="log">Writes a line to the main window's logs.</param>
        public CameraWindow(Action<string> log)
        {
            this.InitializeComponent();

            _log = log ?? (s => Debug.WriteLine(s));
            this.Closed += CameraWindow_Closed;

            _zxingReader = new SoftwareBitmapBarcodeReader();
            _zxingReader.Options.PossibleFormats = new[] { BarcodeFormat.QR_CODE, BarcodeFormat.PDF_417 };
            _zxingReader.Options.TryHarder = true;
            _zxingReader.Options.TryInverted = true;

            ConfigureWindow();
            _ = LoadCameraDevicesAsync();
        }

        private void ConfigureWindow()
        {
            this.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(200, 100, 900, 800));
            var titleBar = this.AppWindow.TitleBar;
            titleBar.ForegroundColor = MainWindow.UI_DARK_GREEN;
            titleBar.BackgroundColor = MainWindow.UI_DARK_GREEN;
            titleBar.ButtonForegroundColor = MainWindow.UI_DARK_GREEN;
            titleBar.ButtonBackgroundColor = MainWindow.UI_WHITE_GREEN;
            titleBar.ButtonHoverForegroundColor = MainWindow.UI_DARK_GREEN;
            titleBar.ButtonHoverBackgroundColor = MainWindow.UI_DARK_GREEN;
            titleBar.ButtonPressedForegroundColor = MainWindow.UI_DARK_GREEN;
            titleBar.ButtonPressedBackgroundColor = MainWindow.UI_DARK_GREEN;
            titleBar.IconShowOptions = Microsoft.UI.Windowing.IconShowOptions.HideIconAndSystemMenu;
        }

        private async void CameraWindow_Closed(object sender, WindowEventArgs args)
        {
            await CleanupCameraAsync();
        }

        private void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
        {
            using (var frame = sender.TryAcquireLatestFrame())
            {
                var bitmap = frame?.VideoMediaFrame?.SoftwareBitmap;
                if (bitmap == null) return;

                try
                {
                    var zxingResult = _zxingReader.Decode(new SoftwareBitmapLuminanceSource(bitmap));
                    var decoded = zxingResult?.Text;

                    if (decoded != null)
                    {
                        var value = decoded;
                        DispatcherQueue.TryEnqueue(async () =>
                        {
                            if (_isDisposed) return;

                            Debug.WriteLine($"[Camera] Barcode found: {value.Substring(0, Math.Min(60, value.Length)).Replace("\r", "\\r").Replace("\n", "\\n")}");
                            qrCodeReadEvent?.Invoke(this, new QrCodeReadByCameraEventArgs(value));

                            await CleanupCameraAsync();
                            this.Close();
                        });
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Camera] Decode error: {ex.Message}");
                }
            }
        }

        // ── Camera discovery ─────────────────────────────────────────────────

        /// <summary>
        /// True when the device is a document reader's camera and must not be used as a webcam.
        /// The id carries the USB vendor id (VID_xxxx); the name is a second signal.
        /// </summary>
        private static bool IsDocumentReaderCamera(DeviceInformation device)
        {
            var id = device.Id ?? string.Empty;
            if (DocumentReaderVendorIds.Any(vid => id.Contains($"VID_{vid}", StringComparison.OrdinalIgnoreCase)))
                return true;
            return (device.Name ?? string.Empty).Contains("ACCESS-IS", StringComparison.OrdinalIgnoreCase);
        }

        private static string Describe(DeviceInformation device) =>
            $"'{device.Name}' (panel {device.EnclosureLocation?.Panel.ToString() ?? "unknown"})";

        private async Task LoadCameraDevicesAsync()
        {
            try
            {
                var all = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
                foreach (var device in all)
                    _log($"Camera found: {Describe(device)}{(IsDocumentReaderCamera(device) ? " - document reader, skipped" : "")}");
                _cameras = all.Where(d => !IsDocumentReaderCamera(d)).ToList();
            }
            catch (Exception ex)
            {
                _log($"Camera: enumeration failed - {ex.GetType().Name} 0x{ex.HResult:X8}: {ex.Message}");
                _cameras = new List<DeviceInformation>();
            }

            if (_cameras.Count == 0)
            {
                _log("Camera: no webcam available.");
                ShowStatus("No camera available");
                return;
            }

            // Prefer the back (world-facing) camera
            _currentCameraIndex = Math.Max(0, _cameras.FindIndex(d => d.EnclosureLocation?.Panel == Panel.Back));

            FlipCameraButton.Visibility = _cameras.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

            await StartCameraAsync();
        }

        // ── Start / flip ─────────────────────────────────────────────────────

        private async Task StartCameraAsync()
        {
            if (_capture != null || _isDisposed) return;

            var camera = _cameras[_currentCameraIndex];
            _log($"Camera: opening {Describe(camera)}.");
            try
            {
                StatusText.Visibility = Visibility.Collapsed;
                await InitializeCaptureAsync(camera);
            }
            catch (Exception ex)
            {
                _log($"Camera: failed to open {Describe(camera)} - {ex.GetType().Name} 0x{ex.HResult:X8}: {ex.Message}");
                ShowStatus("Camera could not be opened");
                await StopCaptureAsync();
            }
        }

        private async void FlipCameraButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isSwitchingCamera || _cameras.Count < 2)
                return;

            _isSwitchingCamera = true;
            FlipCameraButton.IsEnabled = false;
            try
            {
                await StopCaptureAsync();
                _currentCameraIndex = (_currentCameraIndex + 1) % _cameras.Count;
                await StartCameraAsync();
            }
            finally
            {
                _isSwitchingCamera = false;
                FlipCameraButton.IsEnabled = true;
            }
        }

        private void ShowStatus(string text)
        {
            StatusText.Text = text;
            StatusText.Visibility = Visibility.Visible;
        }

        // ── Capture lifecycle ─────────────────────────────────────────────────

        private async Task InitializeCaptureAsync(DeviceInformation device)
        {
            // Prefer exclusive control: it lets us pick the capture format, and makes this window
            // the producer of the stream. If another app owns the camera the open fails, and we
            // retry shared read-only, which only sees what the current owner streams.
            _capture = new MediaCapture();
            var mode = MediaCaptureSharingMode.ExclusiveControl;
            try
            {
                await _capture.InitializeAsync(NewSettings(device.Id, mode));
            }
            catch (Exception ex)
            {
                _log($"Camera: exclusive open failed - {ex.GetType().Name} 0x{ex.HResult:X8}: {ex.Message}; retrying shared read-only.");
                _capture.Dispose();
                _capture = new MediaCapture();
                mode = MediaCaptureSharingMode.SharedReadOnly;
                await _capture.InitializeAsync(NewSettings(device.Id, mode));
            }
            _log($"Camera: {Describe(device)} opened with {mode}.");

            var source = _capture.FrameSources.Values
                .FirstOrDefault(s => s.Info.SourceKind == MediaFrameSourceKind.Color);
            if (source == null)
            {
                _log($"Camera: {Describe(device)} exposes no color frame source.");
                ShowStatus("Camera has no color stream");
                return;
            }

            // Pick the highest-resolution format for better barcode decoding. Changing the format
            // needs exclusive control: in shared mode SetFormatAsync fails, so keep the current one.
            if (mode == MediaCaptureSharingMode.ExclusiveControl)
            {
                var bestFormat = source.SupportedFormats
                    .Where(f => f.VideoFormat != null)
                    .OrderByDescending(f => f.VideoFormat.Width * f.VideoFormat.Height)
                    .FirstOrDefault();
                if (bestFormat != null)
                {
                    try
                    {
                        await source.SetFormatAsync(bestFormat);
                    }
                    catch (Exception ex)
                    {
                        _log($"Camera: could not set {bestFormat.VideoFormat.Width}x{bestFormat.VideoFormat.Height} - {ex.GetType().Name} 0x{ex.HResult:X8}: {ex.Message}; keeping the current format.");
                    }
                }
            }
            var format = source.CurrentFormat?.VideoFormat;
            _log($"Camera: streaming {format?.Width}x{format?.Height} {source.CurrentFormat?.Subtype}.");

            _frameReader = await _capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8);
            _frameReader.FrameArrived += OnFrameArrived;
            var status = await _frameReader.StartAsync();
            if (status != MediaFrameReaderStartStatus.Success)
            {
                _log($"Camera: frame reader did not start - {status}.");
                ShowStatus("Camera could not be started");
            }

            _mediaSource = MediaSource.CreateFromMediaFrameSource(source);
            player.Source = new MediaPlaybackItem(_mediaSource);
        }

        private static MediaCaptureInitializationSettings NewSettings(string deviceId, MediaCaptureSharingMode mode) => new()
        {
            VideoDeviceId = deviceId,
            SharingMode = mode,
            MemoryPreference = MediaCaptureMemoryPreference.Cpu,
            StreamingCaptureMode = StreamingCaptureMode.Video,
        };

        /// <summary>Releases the current camera; the window can open another one afterwards (flip).</summary>
        private async Task StopCaptureAsync()
        {
            try
            {
                if (player != null)
                    player.Source = null;

                if (_frameReader != null)
                {
                    _frameReader.FrameArrived -= OnFrameArrived;
                    await _frameReader.StopAsync();
                    _frameReader.Dispose();
                    _frameReader = null;
                }

                _mediaSource?.Dispose();
                _mediaSource = null;

                _capture?.Dispose();
                _capture = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during camera cleanup: {ex.Message}");
            }
        }

        private async Task CleanupCameraAsync()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            await StopCaptureAsync();
        }
    }
}


// this is the thin layer that allows you to use ZXing over WinRT's SoftwareBitmap
public class SoftwareBitmapBarcodeReader : BarcodeReader<SoftwareBitmap>
{
    public SoftwareBitmapBarcodeReader()
        : base(bmp => new SoftwareBitmapLuminanceSource(bmp))
    {
        AutoRotate = true;
    }
}

// from https://github.com/micjahn/ZXing.Net/blob/master/Source/lib/BitmapLuminanceSource.SoftwareBitmap.cs
public class SoftwareBitmapLuminanceSource : BaseLuminanceSource
{
    protected SoftwareBitmapLuminanceSource(int width, int height)
        : base(width, height)
    {
    }

    public SoftwareBitmapLuminanceSource(SoftwareBitmap softwareBitmap)
        : base(softwareBitmap.PixelWidth, softwareBitmap.PixelHeight)
    {
        if (softwareBitmap.BitmapPixelFormat != BitmapPixelFormat.Gray8)
        {
            using (SoftwareBitmap convertedSoftwareBitmap = SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Gray8))
            {
                convertedSoftwareBitmap.CopyToBuffer(luminances.AsBuffer());
            }
        }
        else
        {
            softwareBitmap.CopyToBuffer(luminances.AsBuffer());
        }
    }

    protected override LuminanceSource CreateLuminanceSource(byte[] newLuminances, int width, int height)
        => new SoftwareBitmapLuminanceSource(width, height) { luminances = newLuminances };
}
