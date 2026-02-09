using com.credenceid.identity.iso18013.drivingprivilege;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Tap2iDSdk.Domain.Model;
using Tap2iDSdk.Model;
using Windows.Storage.Streams;

namespace Tap2iDSampleWinUI.ViewModels
{
    internal class DocumentViewModel
    {
        public string Title { get; set; }
        public ImageSource ProfileImage { get; set; }
        public bool HasProfileImage => ProfileImage != null;
        public ImageSource SignatureImage { get; set; }
        public bool HasSignatureImage => SignatureImage != null;
        public bool? IsAgeOver21 { get; set; }
        public SolidColorBrush AgeBadgeColor { get; set; } = new SolidColorBrush(Microsoft.UI.Colors.Gray);
        public List<DisplayItem> Attributes { get; set; } = new List<DisplayItem>();

        // Validation Flags
        public bool MsoValid { get; set; }
        public bool DeviceSigned { get; set; }
        public bool IssuerSigned { get; set; }
        public bool IssuerTrusted { get; set; }
        public string IssuerName { get; set; }

        public static async Task<DocumentViewModel> FromVerifiedDocumentAsync(VerifiedDocument doc)
        {
            var vm = new DocumentViewModel();
            vm.Title = PrettifyDocType(doc.DocType);

            // 1. Validation Status
            vm.MsoValid = doc.Authentication.MsoValidity.Status == MsoValidityStatus.Valid;
            vm.DeviceSigned = doc.Authentication.SecurityChecks.IsDeviceSignedValid;
            vm.IssuerSigned = doc.Authentication.SecurityChecks.IsIssuerSignedValid;
            vm.IssuerTrusted = doc.Authentication.TrustAttributes.IsIssuerTrusted;
            vm.IssuerName = doc.Authentication.TrustAttributes.IssuerDistinguishedName; // Or parsed field

            // 2. Parse Attributes & Find Image
            var displayItems = new List<DisplayItem>();

            foreach (var ns in doc.Namespaces)
            {
                foreach (var item in ns.Items)
                {
                    // Check for Portrait (Raw Bytes)
                    if (item.Key.Equals("portrait", StringComparison.OrdinalIgnoreCase) && item.Value is byte[] bytes)
                    {
                        vm.ProfileImage = await LoadImageAsync(bytes);
                        continue; // Don't add to text list
                    }

                    if (item.Key.Equals("signature_usual_mark", StringComparison.OrdinalIgnoreCase) && item.Value is byte[] sigBytes)
                    {
                        vm.SignatureImage = await LoadImageAsync(sigBytes);
                        continue;
                    }

                    // Check for Age Over 21
                    if (item.Key.Equals("age_over_21", StringComparison.OrdinalIgnoreCase) && item.Value is bool isOver21)
                    {
                        vm.IsAgeOver21 = isOver21;
                        vm.AgeBadgeColor = new SolidColorBrush(isOver21 ? Microsoft.UI.Colors.DarkGreen : Microsoft.UI.Colors.DarkRed);
                    }

                    if (item.Key.Equals("sex", StringComparison.OrdinalIgnoreCase))
                    {
                        string sexStr = FormatSex(item.Value);
                        if (!string.IsNullOrWhiteSpace(sexStr))
                        {
                            displayItems.Add(new DisplayItem(PrettifyLabel(item.Key) + ":", sexStr));
                        }
                        continue;
                    }
                    // Format other items
                    string displayVal = FormatValue(item.Value);
                    if (!string.IsNullOrWhiteSpace(displayVal))
                    {
                        displayItems.Add(new DisplayItem(PrettifyLabel(item.Key) + ":", displayVal));
                    }
                }
            }
            vm.Attributes = displayItems;

            return vm;
        }

        private static string FormatValue(object val)
        {
            if (val == null) return "";
            if (val is byte[]) return "[Binary Data]";
            if (val is List<DrivingPrivilege> privileges)
            {
                if (privileges.Count == 0) return "None";

                var formattedList = privileges.Select(p =>
                {
                    string category = p.getVehicleCategory() ?? "?";
                    string expiry = p.getExpiryDate()?.toString() ?? "N/A";
                    return $"{category} (Exp: {expiry})";
                });

                return string.Join(", ", formattedList);
            }
            if (val is System.Collections.IEnumerable list && val is not string)
            {
                // Quick format for lists
                return "List/Complex Object";
            }
            return val.ToString();
        }

        private static string FormatSex(object val)
        {
            if (val == null) return "";
            string s = val.ToString().ToUpper();

            // Handle ISO 5218 codes (1=Male, 2=Female) or strings
            return s switch
            {
                "1" or "MALE" => "Male",
                "2" or "FEMALE" => "Female",
                "0" or "UNKNOWN" => "Unknown",
                "9" or "NOT_APPLICABLE" => "Not Applicable",
                _ => s // Return original if not matched (e.g. "3")
            };
        }

        private static string PrettifyDocType(string docType)
        {
            if (docType.Contains("mDL")) return "Mobile Driving License";
            if (docType.Contains("pid")) return "Personal ID (EU PID)";
            return docType;
        }

        private static string PrettifyLabel(string propertyName)
        {
            if (propertyName.StartsWith("dhs", StringComparison.OrdinalIgnoreCase))
                return "DHS " + propertyName.Substring(3);

            var withSpaces = Regex.Replace(propertyName, "(\\B[A-Z])", " $1"); // Handle CamelCase
            withSpaces = withSpaces.Replace("_", " "); // Handle snake_case

            if (withSpaces.Length > 0)
                withSpaces = char.ToUpper(withSpaces[0]) + withSpaces.Substring(1);

            return withSpaces;
        }

        // Helper to convert byte[] to BitmapImage for UI
        private static async Task<BitmapImage> LoadImageAsync(byte[] imageData)
        {
            if (imageData == null || imageData.Length == 0) return null;
            using (InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream())
            {
                using (DataWriter writer = new DataWriter(stream.GetOutputStreamAt(0)))
                {
                    writer.WriteBytes(imageData);
                    await writer.StoreAsync();
                }
                var image = new BitmapImage();
                await image.SetSourceAsync(stream);
                return image;
            }
        }
    }
}
