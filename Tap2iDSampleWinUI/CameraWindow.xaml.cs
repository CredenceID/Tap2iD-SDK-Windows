using Microsoft.UI.Xaml;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
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
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class CameraWindow : Window
    {
        private readonly SoftwareBitmapBarcodeReader _reader;
        private MediaCapture _capture;
        private MediaFrameReader _frameReader;
        private MediaSource _mediaSource;
        private ObservableCollection<MediaFrameSourceGroup> _cameraDevices;
        public delegate void OnQrCodeReadByCameraEventHandler(object sender, QrCodeReadByCameraEventArgs qrCodeReadByCameraEventArgs);
        public event OnQrCodeReadByCameraEventHandler qrCodeReadEvent;

        public CameraWindow()
        {
            this.InitializeComponent();

            // Load available cameras
            _cameraDevices = new ObservableCollection<MediaFrameSourceGroup>();
            _ = LoadCameraDevicesAsync();
            _reader = new SoftwareBitmapBarcodeReader();
            {
                //AutoRotate = true;
            };
            _reader.Options.PossibleFormats = new[] { BarcodeFormat.QR_CODE };
            _reader.Options.TryHarder = true;

            this.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(300, 300, 600, 600));
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


        private async void StartCamera()
        {
            if (_capture == null)
            {
                try
                {
                    var camera = _cameraDevices.FirstOrDefault();
                    await InitializeCaptureAsync(camera);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Fail to Initialize Camera");
                    Debug.WriteLine(ex.Message);
                }
                return;
            }
            _ = Task.Run(() => TerminateCaptureAsync());
        }

        private void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
        {
            using (var frame = sender.TryAcquireLatestFrame())
            {
                var bitmap = frame?.VideoMediaFrame?.SoftwareBitmap;
                if (bitmap == null)
                    return;

                // Process the frame asynchronously
                try
                {
                    var luminanceSource = new SoftwareBitmapLuminanceSource(bitmap);
                    var result = _reader.Decode(luminanceSource);
                    if (result != null)
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            TerminateCaptureAsync();
                        });
                        Thread.Sleep(250);
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            Debug.WriteLine(result.Text);
                            qrCodeReadEvent?.Invoke(this, new QrCodeReadByCameraEventArgs(result.Text));
                            this.Close();
                        });
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
        }

        private async Task LoadCameraDevicesAsync()
        {
            var allGroups = await MediaFrameSourceGroup.FindAllAsync();

            var videoCameras = allGroups.Where(camera =>
            camera.SourceInfos.Any(sourceInfo =>
                sourceInfo.SourceKind == MediaFrameSourceKind.Color // Include all color cameras
            )).ToList();

            _cameraDevices.Clear(); // Clear the existing list

            foreach (var camera in videoCameras)
            {
                _cameraDevices.Add(camera); // Add filtered cameras
            }

            if (_cameraDevices.Count > 0)
            {
                StartCamera();
            }
        }

        private async Task InitializeCaptureAsync(MediaFrameSourceGroup sourceGroup)
        {
            if (sourceGroup == null)
                return; // not found!

            // init capture & initialize
            _capture = new MediaCapture();
            await _capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                SourceGroup = sourceGroup,
                SharingMode = MediaCaptureSharingMode.SharedReadOnly,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu, // to ensure we get SoftwareBitmaps
            });

            // initialize source
            var source = _capture.FrameSources[sourceGroup.SourceInfos[0].Id];

            // create reader to get frames & pass reader to player to visualize the webcam
            _frameReader = await _capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8);
            _frameReader.FrameArrived += OnFrameArrived;
            await _frameReader.StartAsync();

            _mediaSource = MediaSource.CreateFromMediaFrameSource(source);
            var mediaPlaybackItem = new MediaPlaybackItem(_mediaSource);

            // Set the media playback item to the player
            player.Source = mediaPlaybackItem;
        }

        private async void TerminateCaptureAsync()
        {
            _mediaSource?.Dispose();
            _mediaSource = null;

            if (_frameReader != null)
            {
                _frameReader.FrameArrived -= OnFrameArrived;
                await _frameReader.StopAsync();
                _frameReader?.Dispose();
                _frameReader = null;
            }

            _capture?.Dispose();
            _capture = null;

            DispatcherQueue.TryEnqueue(() =>
            {
                CameraPanel.Visibility = Visibility.Collapsed;
            });
            try
            {
                player.Source = null;
            }
            catch (Exception)
            {
                //ignore this exeption
            }
        }
    }
}


// this is the thin layer that allows you to use XZing over WinRT's SoftwareBitmap
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
