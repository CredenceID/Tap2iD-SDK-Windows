using System;
using System.IO;

namespace Tap2iDSampleWinUI
{

    public class LicenseManager
    {
        private const string LicenseFolderName = "Tap2iD";
        private const string LicenseFileName = "license.txt";

        /// <summary>
        /// Gets the license string from the license file.
        /// </summary>
        /// <returns>The license string if the file exists and can be read; otherwise, an empty string.</returns>
        public string GetLicense()
        {
            // Construct the path to the target folder.
            string localPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), LicenseFolderName);
            string licenseFilePath = Path.Combine(localPath, LicenseFileName);

            try
            {
                // Check if the license file exists.
                if (File.Exists(licenseFilePath))
                {
                    return File.ReadAllText(licenseFilePath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error reading the license file: " + ex.Message);
            }

            // Return an empty string if the file doesn't exist or if an error occurred.
            return string.Empty;
        }
    }

}
