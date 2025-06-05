using ImageMagick;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Tap2iDSdk.Model;
using Windows.Storage.Streams;

namespace Tap2iDSampleWinUI
{
    internal static class Utils
    {
        public static String GetHumanReadableVerifyStateMessage(VerifyState verifyState)
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
                    return baseMessage + "Data validation successful";
                default: return baseMessage + "Unknwon";

            }
        }

        public static byte[] ConvertJP2ToPng(byte[] jp2Bytes)
        {
            using (MagickImage image = new MagickImage(jp2Bytes))
            {
                return image.ToByteArray(MagickFormat.Png);
            }
        }

        public static async Task SetImageAsync(Image imageControl, byte[] imageBytes)
        {
            try
            {
                if (imageBytes == null || imageBytes.Length == 0)
                    throw new ArgumentException("Image data is empty or null.");

                // First attempt: Try standard decoding
                if (await TrySetStandardImageAsync(imageControl, imageBytes))
                    return; // Success, exit method

                Debug.WriteLine("Standard decoding failed, attempting JPEG 2000 conversion...");

                // Convert JPEG 2000 to PNG
                imageBytes = ConvertJP2ToPng(imageBytes);

                // Attempt to display the converted PNG
                await TrySetStandardImageAsync(imageControl, imageBytes);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading image: {ex.Message}");
                Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
            }
        }

        public static async Task<bool> TrySetStandardImageAsync(Image imageControl, byte[] imageBytes)
        {
            try
            {
                using (var stream = new InMemoryRandomAccessStream())
                {
                    await stream.WriteAsync(imageBytes.AsBuffer());
                    stream.Seek(0);

                    BitmapImage bitmapImage = new BitmapImage();
                    await bitmapImage.SetSourceAsync(stream);

                    imageControl.Source = bitmapImage;
                    return true;
                }
            }
            catch
            {
                return false; // Standard decoding failed
            }
        }
    }
}
