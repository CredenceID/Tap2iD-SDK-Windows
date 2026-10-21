using Microsoft.UI.Xaml;
using System;

namespace Tap2iDSampleWinUI.ViewModels
{
    class CameraScanViewModel
    {

        private readonly MainWindow _mainWindow;
        private CameraWindow? _currentCameraWindow;

        ////////////////////////////////////////////////////////////////////////
        // Function name   : CameraScanViewModel
        // Description     : Constructor
        //                 : 
        // Return type     : 
        // Argument        : MainWindow - Form Class
        ////////////////////////////////////////////////////////////////////////
        public CameraScanViewModel(MainWindow mainWindow)
        {
            // Get Form class as we need to update when data arrives
            _mainWindow = mainWindow;
        }

        public void ScanQrCodeWithCamera()
        {
            if (_currentCameraWindow != null)
            {
                _currentCameraWindow.Activate(); // Bring to front
                return;
            }

            _currentCameraWindow = new CameraWindow(_mainWindow.AppendLogs);
            _currentCameraWindow.qrCodeReadEvent += QrCodeReadByCameraHandler;
            _currentCameraWindow.Closed += OnCameraWindowClosed;
            _currentCameraWindow.Activate();
        }

        private void QrCodeReadByCameraHandler(object? sender, QrCodeReadByCameraEventArgs args)
        {
            _mainWindow.QRCodeRead(args.QrCodeValue);

            // Close the camera window automatically on success
            if (sender is CameraWindow window)
            {
                window.Close();
            }
        }

        private void OnCameraWindowClosed(object sender, WindowEventArgs args)
        {
            // CLEANUP: Unsubscribe events to prevent memory leaks
            if (sender is CameraWindow window)
            {
                window.qrCodeReadEvent -= QrCodeReadByCameraHandler;
                window.Closed -= OnCameraWindowClosed;
                _currentCameraWindow = null; // Allow opening a new one later
            }
        }
    }

    public class QrCodeReadByCameraEventArgs(string qrCodeValue) : EventArgs
    {
        public string QrCodeValue { get; set; } = qrCodeValue;
    }
}
