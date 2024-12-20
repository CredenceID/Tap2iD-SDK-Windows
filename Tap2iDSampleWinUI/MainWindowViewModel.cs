using System;

namespace Tap2iDSampleWinUI
{
    class MainWindowViewModel
    {

        private MainWindow _MainWindow;

        ////////////////////////////////////////////////////////////////////////
        // Function name   : MainWindowViewModel
        // Description     : Constructor
        //                 : 
        // Return type     : 
        // Argument        : MainWindow - Form Class
        ////////////////////////////////////////////////////////////////////////
        public MainWindowViewModel(MainWindow mainWindow)
        {
            // Get Form class as we need to update when data arrives
            _MainWindow = mainWindow;
        }

        void QrCodeReadByCameraHandler(object sender, QrCodeReadByCameraEventArgs args)
        {
            _MainWindow.QRCodeRead(args.QrCodeValue);
        }

        public void ScanQrCodeWithCamera()
        {
            var cameraWindow = new CameraWindow();
            cameraWindow.qrCodeReadEvent += QrCodeReadByCameraHandler;
            cameraWindow.Activate();
        }
    }

    public class QrCodeReadByCameraEventArgs(string qrCodeValue) : EventArgs
    {
        public string QrCodeValue { get; set; } = qrCodeValue;
    }
}
