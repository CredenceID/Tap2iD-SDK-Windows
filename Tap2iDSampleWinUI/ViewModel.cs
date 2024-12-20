////////////////////////////////////////////////////////////////////////////////
// Module       : ViewModel
//
// Description  : Partial class for interfacing to the videoOCR DLL
//				  
//
// Version      : 1.0
//
////////////////////////////////////////////////////////////////////////////////
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Tap2iDSampleWinUI
{
    public partial class ViewModel
    {

        public Boolean validationFlipEvent = false;
        private delegate void BarcodeDelegate(ref UInt32 Parameter, int barcodeType, IntPtr bc);
        private delegate void updateBarcodeTextBox(string barcode);

        private delegate void ValidationDelegate(ref UInt32 Parameter);
        private delegate void ValidationFlipDelegate(ref UInt32 Parameter);

        // Validation 
        private delegate void updateValidationTextBox(string text, int index);

        private BarcodeDelegate bd;

        private MainWindow frontend;

        ////////////////////////////////////////////////////////////////////////
        // Function name   : ViewModel
        // Description     : Constructor
        //                 : 
        // Return type     : 
        // Argument        : Frontend - Form Class
        ////////////////////////////////////////////////////////////////////////
        public ViewModel(MainWindow fe)
        {

            // Get Form class as we need to update when data arrives
            frontend = fe;

            // create the delegates for the callback functions
            bd = new BarcodeDelegate(BarcodeCallback);
        }

        public async void StartBarcodeServer(Action<string> onBarcodeReceived)
        {
            await BarcodeServer.BarcodeServer.StartServerAsync(onBarcodeReceived);
        }


        ////////////////////////////////////////////////////////////////////////
        // Function name   : initialiseReader
        // Description     : Intialise scanner for reading. 
        //					
        //                 : 
        // Return type     : void 
        // Argument        : 
        //////////////////////////////////////////////////////////////////////// 
        public void initialiseReader()
        {
            // Start up the reader
            // Set up capture of all illumination types and RFID
            Boolean Temp = voInitialiseReader(true, true, true, true, false);

            UInt32 Val = 0;

            voRegisterBarcodeCallback(bd, ref Val);

            System.Threading.Thread.Sleep(1000);
        }


        ////////////////////////////////////////////////////////////////////////
        // Function name   : reInitialiseReader
        // Description     : Re - intialise scanner for reading. 
        //					
        //                 : 
        // Return type     : void 
        // Argument        : 
        //////////////////////////////////////////////////////////////////////// 
        public void reInitialiseReader()
        {
            // Terminates all the process threads and goes into an ide state
            voTerminate();

            // Set up capture of all illumination types and RFID
            voInitialiseReader(true, true, true, true, false);
        }

        ////////////////////////////////////////////////////////////////////////
        // Function name   : startReader
        // Description     : enables scanner for reading. 
        //					
        //                 : 
        // Return type     : void 
        // Argument        : 
        //////////////////////////////////////////////////////////////////////// 
        public void startReader()
        {
            //Initialise the RFID status beore starting
            voSetDataGroupsRequired(0x0018ffff);
            voEnablePassiveAuthentication(true);
            voEnableActiveAuthentication(true);

            // Start the capture system of the scanner
            Boolean status = voStartRead();
        }


        ////////////////////////////////////////////////////////////////////////
        // Function name   : stop Reader
        // Description     : disable scanner. 
        //					
        //                 : 
        // Return type     : void 
        // Argument        : 
        //////////////////////////////////////////////////////////////////////// 
        public void stopReader()
        {

            // Start the capture system of the scanner
            Boolean status = voStopRead();
        }

        ////////////////////////////////////////////////////////////////////////
        // Function name   : terminateReader
        // Description     : Terminate Reader
        //					
        //                 : 
        // Return type     : void 
        // Argument        : 
        //////////////////////////////////////////////////////////////////////// 
        public void terminateReader()
        {
            voCloseValidationService();
            voTerminate();
        }


        ////////////////////////////////////////////////////////////////////////
        // Function name   : getColourImage
        // Description     : Grabs a colour image.
        //					
        //                 : 
        // Return type     : void 
        // Argument        : 
        //////////////////////////////////////////////////////////////////////// 
        public void getColourImage()
        {
            // Takes a colour image of whats on scanner plate
            voGetImage(1, true);
        }

        ////////////////////////////////////////////////////////////////////////
        // Function name   : updateBarcodeData
        // Description     : Ths functions is run in the same thread as the UI.
        //                   Allows us to update the Text box from the callback function.
        //					
        //                 : 
        // Return type     : void 
        // Argument        : string         - barcode string from the scanner 
        //////////////////////////////////////////////////////////////////////// 
        private void updateBarcodeData(string barcode)
        {
            // frontend.Text = barcode;
        }


        public int getUnitType()
        {
            int returnValue = voGetHardwareType();

            return returnValue;
        }

        public string getUnitSerialNumber()
        {
            uint bufferSize = 128;
            var sb = new StringBuilder((int)bufferSize);

            voGetSerialNumber(sb, ref bufferSize);

            return sb.ToString();
        }

        #region Event Handler

        ////////////////////////////////////////////////////////////////////////
        // Function name   : selectValidationResolution
        // Description     : Select the required resolution for document validation
        //                   Note:-
        //                          Curently this should be set to index 1 (2048x1536)
        //					
        //                 : 
        // Return type     : void 
        // Argument        : int     - resolution index
        //////////////////////////////////////////////////////////////////////// 
        public void selectValidationResolution(int resolution)
        {
            voSetCaptureResolution(resolution);
        }

        #endregion
        //////////////////////////////////////////////////////////////////////////////////
        //
        //                  Define callback functions
        //                    
        //////////////////////////////////////////////////////////////////////////////////     

        public const int FullName = 22;
        ////////////////////////////////////////////////////////////////////////
        // Function name   : BarcodeCallback
        // Description     : Ths functions is registered as a callback. It is run by the
        //                   videoOCR dll when varcode data is available.
        //					
        //                 : 
        // Return type     : void 
        // Argument        : ref UInt32     - paramter registered by user.
        //                 : int            - Barcode type .
        //                 : IntPtr         - Barcode data.
        //////////////////////////////////////////////////////////////////////// 
        //private void BarcodeCallback(ref UInt32 Parameter, int barcodeType, [MarshalAs(UnmanagedType.LPStr)] String bc)
        private void BarcodeCallback(ref UInt32 Parameter, int barcodeType, IntPtr bc)
        {
            int size = voGetBarcodeSize();

            byte[] rawData = new byte[size];
            Marshal.Copy(bc, rawData, 0, size);


            string barcodeData = Encoding.ASCII.GetString(rawData);

            frontend.QRCodeRead(barcodeData);

            if (voBarcodeParseDataAvailable() == true)
            {
                // This gets the full name of the DL holder
                int bcSize = voGetBarcodeInformationSize(ViewModel.FullName);
                char[] data = new char[bcSize];
                voGetBarcodeParserInformation(ViewModel.FullName, data);

            }
        }
    }
}
