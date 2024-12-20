////////////////////////////////////////////////////////////////////////////////
// Module       : ViewModel
//
// Description  : Partial class for defining the videoOCR DLL function signatures
//                and data.
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


        // Location of the DLL required
        const String DLL_LOCATION = "videoocr.dll";

        // structure required for MRZ data
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct DLL_MRZDATA
        {
            [MarshalAs(UnmanagedType.BStr)]
            public String RawMRZ;
            [MarshalAs(UnmanagedType.BStr)]
            public String DocumentNumber;
            [MarshalAs(UnmanagedType.BStr)]
            public String DOB;
            [MarshalAs(UnmanagedType.BStr)]
            public String Expiry;
            [MarshalAs(UnmanagedType.BStr)]
            public String Issuer;
            [MarshalAs(UnmanagedType.BStr)]
            public String Nationality;
            [MarshalAs(UnmanagedType.BStr)]
            public String LastNames;
            [MarshalAs(UnmanagedType.BStr)]
            public String FirstNames;
            [MarshalAs(UnmanagedType.BStr)]
            public String Type;
            [MarshalAs(UnmanagedType.BStr)]
            public String Discretionary1;
            [MarshalAs(UnmanagedType.BStr)]
            public String Discretionary2;
            [MarshalAs(UnmanagedType.BStr)]
            public String Gender; // added 130810

        }

        // structure required for scanner status updates
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct DLL_STATUS
        {
            [MarshalAs(UnmanagedType.Bool)]
            public Boolean CameraPresent;
            [MarshalAs(UnmanagedType.Bool)]
            public Boolean PassportPresent;
            [MarshalAs(UnmanagedType.Bool)]
            public Boolean RFIDPresent;
            [MarshalAs(UnmanagedType.Bool)]
            public Boolean Active;
            [MarshalAs(UnmanagedType.Bool)]
            public Boolean Busy;
            [MarshalAs(UnmanagedType.Bool)]
            public Boolean MRZDecoded;
            [MarshalAs(UnmanagedType.Bool)]
            public Boolean RFIDDecoded;
            [MarshalAs(UnmanagedType.Bool)]
            public Boolean UVEnabled;
            [MarshalAs(UnmanagedType.Bool)]
            public Boolean RFIDEnabled;
            [MarshalAs(UnmanagedType.Bool)]
            public Boolean ColourEnabled;
            [MarshalAs(UnmanagedType.Bool)]
            public Boolean AutoStopEnabled;
            [MarshalAs(UnmanagedType.Bool)]
            public Boolean InfraredEnabled;
            [MarshalAs(UnmanagedType.Bool)]
            public Boolean BadDecode; // added 110810
            [MarshalAs(UnmanagedType.Bool)]
            public Boolean DocumentPresent; // added 110810
        }

        // DLL function signatures 
        [DllImport(DLL_LOCATION)]
        private static extern Boolean voInitialiseReader(Boolean InfraRed, Boolean Colour, Boolean UV, Boolean RFID, Boolean AutoStop);
        [DllImport(DLL_LOCATION)]
        private static extern Boolean voStartRead();
        [DllImport(DLL_LOCATION)]
        private static extern Boolean voStopRead();
        [DllImport(DLL_LOCATION)]
        public static extern Boolean voQueryReaderState(ref DLL_STATUS Status);
        [DllImport(DLL_LOCATION)]

        private static extern Boolean voTerminate();
        [DllImport(DLL_LOCATION)]
        private static extern Boolean voGetImage(int ImageType, Boolean FullSize);

        [DllImport(DLL_LOCATION, CharSet = CharSet.Ansi)]
        private static extern Boolean voRegisterBarcodeCallback(BarcodeDelegate Callback, ref UInt32 Parameter);
        [DllImport(DLL_LOCATION)]
        private static extern void voSetBarcodeStateMachine(int Parameter);
        [DllImport(DLL_LOCATION)]
        private static extern void voSetDecodeOrder(int Parameter);


        [DllImport(DLL_LOCATION)]
        private static extern int voSetCaptureResolution(int resolution);

        [DllImport(DLL_LOCATION)]
        private static extern int voGetHardwareType();



        //     [DllImport(DLL_LOCATION, CharSet = CharSet.Ansi)]
        // private static extern int voGetSerialNumber([MarshalAs(UnmanagedType.LPStr)] StringBuilder serialNumber, ref UInt32 size);

        [DllImport(DLL_LOCATION)]
        private static extern int voGetSerialNumber([MarshalAs(UnmanagedType.LPStr)] StringBuilder serialNumber, ref UInt32 size);

        [DllImport(DLL_LOCATION)]
        private static extern Boolean voSetDataGroupsRequired(int groups);


        [DllImport(DLL_LOCATION)]
        private static extern void voEnablePassiveAuthentication(Boolean status);

        [DllImport(DLL_LOCATION)]
        private static extern void voEnableActiveAuthentication(Boolean status);

        [DllImport(DLL_LOCATION)]
        private static extern int voCheckPassiveAuthenticationStatus(int dataGroup);


        [DllImport(DLL_LOCATION)]
        private static extern int voCheckActiveAuthenticationStatus();

        [DllImport(DLL_LOCATION)]
        private static extern void voEnableTerminalAuthentication(Boolean status);

        [DllImport(DLL_LOCATION)]
        private static extern int voCheckChipAuthenticationStatus();

        [DllImport(DLL_LOCATION)]
        private static extern void voSethWnd(IntPtr windowHandle);

        [DllImport(DLL_LOCATION)]
        private static extern int voGetMrzValidity();


        [DllImport(DLL_LOCATION)]
        private static extern void voSetMRZValidation(Boolean status);

        [DllImport(DLL_LOCATION)]
        private static extern void voSetFaceDetectionBorder(float xPercentage, float yPercentage);

        [DllImport(DLL_LOCATION)]
        private static extern void voRegisterValidationCallback(ValidationDelegate Callback, ref UInt32 Parameter);


        [DllImport(DLL_LOCATION)]
        private static extern void voRegisterValidationFlipCallback(ValidationFlipDelegate Callback, ref UInt32 Parameter);

        [DllImport(DLL_LOCATION)]
        private static extern Boolean voGetDocumentInformation(int informationType, [In, Out] char[] documentInformation);
        [DllImport(DLL_LOCATION)]
        private static extern int voGetDocumentInformationSize(int informationType);
        [DllImport(DLL_LOCATION)]
        private static extern int voGetNumericDocumentResult();
        [DllImport(DLL_LOCATION)]
        private static extern Boolean voCloseValidationService();
        [DllImport(DLL_LOCATION)]
        private static extern Boolean voCancelDocumentFlip();

        [DllImport(DLL_LOCATION)]
        private static extern int voGetBarcodeSize();

        [DllImport(DLL_LOCATION)]
        public static extern IntPtr voGetMrz(int mrzType);

        [DllImport(DLL_LOCATION)]
        public static extern Boolean voCheckMrzExpiryDate(int mrzType);

        [DllImport(DLL_LOCATION)]
        public static extern Boolean voCheckB900();

        [DllImport(DLL_LOCATION)]
        public static extern Boolean voCheckUVDull();

        [DllImport(DLL_LOCATION)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern Boolean voBarcodeParseDataAvailable();

        [DllImport(DLL_LOCATION)]
        private static extern int voGetBarcodeInformationSize(int informationType);

        [DllImport(DLL_LOCATION)]
        private static extern void voGetBarcodeParserInformation(int informationType, [In, Out] char[] information);



    }
}
