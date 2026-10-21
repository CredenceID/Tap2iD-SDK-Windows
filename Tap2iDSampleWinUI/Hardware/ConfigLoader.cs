using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tap2iDSampleWinUI.Hardware
{
    /// <summary>
    /// A helper class to load configuration from appsettings.json
    /// </summary>
    public static class ConfigLoader
    {
        private static readonly string _appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Tap2iD");
        private static readonly string _configPath = Path.Combine(_appDataFolder, "appsettings.json");
        /// <summary>
        /// This class maps to the root of the appsettings.json
        /// </summary>
        private class AppSettings
        {
            [JsonPropertyName("SerialPortSettings")]
            public SerialPortSettings SerialPortSettings { get; set; }
        }

        /// <summary>
        /// This class maps to the "SerialPortSettings" section
        /// </summary>
        private class SerialPortSettings
        {
            [JsonPropertyName("BarcodeScanner")]
            public PortConfig BarcodeScanner { get; set; }
        }


        /// <summary>
        /// The single authoritative config file read at runtime. There is also a copy of
        /// appsettings.json next to the app executable — that copy is only the seed template
        /// used to populate this file on first run (see <see cref="EnsureUserConfigExists"/>)
        /// and is never re-read afterwards. Edit THIS path, not the one in the install directory
        /// (CIE-6244 — QA was editing the install-dir copy, which had no effect after first run).
        /// </summary>
        public static string ConfigPath => _configPath;

        /// <summary>
        /// Loads the BarcodeScanner configuration from appsettings.json.
        /// </summary>
        /// <param name="logAppender">
        /// Optional sink (e.g. the app's on-screen Logs panel) for the resolved config path and
        /// load outcome. Without this, a failed/missing config silently falls back to the
        /// default COM4 with no visible signal (CIE-6244) — every branch below logs through it
        /// so "using COM4" and "configured for COM4" are never indistinguishable.
        /// </param>
        /// <returns>The PortConfig for the BarcodeScanner</returns>
        public static PortConfig LoadBarcodeConfig(Action<string> logAppender = null)
        {
            void Log(string message)
            {
                Debug.WriteLine(message);
                logAppender?.Invoke(message);
            }

            Log($"[ConfigLoader] Config path: {_configPath}");

            try
            {
                EnsureUserConfigExists(Log);

                if (!File.Exists(_configPath))
                {
                    throw new FileNotFoundException($"Configuration file not found. Make sure 'appsettings.json' exists and is set to 'Copy to Output Directory'. Path checked: {_configPath}");
                }

                string json = File.ReadAllText(_configPath);
                var appSettings = JsonSerializer.Deserialize<AppSettings>(json);

                if (appSettings?.SerialPortSettings?.BarcodeScanner == null)
                {
                    throw new InvalidOperationException("'SerialPortSettings.BarcodeScanner' not found in appsettings.json.");
                }

                var config = appSettings.SerialPortSettings.BarcodeScanner;
                Log($"[ConfigLoader] Loaded BarcodeScanner config from {_configPath}: PortName={config.PortName}, BaudRate={config.BaudRate}");
                return config;
            }
            catch (Exception ex)
            {
                // Fallback to defaults if loading fails — logged loudly (not silently) so a bad
                // or missing config never looks identical to "COM4 was actually configured".
                var fallback = new PortConfig();
                Log($"[ConfigLoader] FAILED to load {_configPath}: {ex.Message} — falling back to default PortName={fallback.PortName}.");
                return fallback;
            }
        }

        /// <summary>
        /// Checks if a user-editable config exists in AppData.
        /// If not, it copies the default config from the app's read-only install directory.
        /// </summary>
        private static void EnsureUserConfigExists(Action<string> log)
        {
            try
            {
                // Ensure the AppData directory exists
                Directory.CreateDirectory(_appDataFolder);

                // If the user config file already exists, we're done.
                if (File.Exists(_configPath))
                {
                    log($"[ConfigLoader] Using existing config at {_configPath}.");
                    return;
                }

                // --- Copy the default config from the install package ---
                log($"[ConfigLoader] No config found at {_configPath} — seeding it from the app's default.");

                string exePath = Assembly.GetExecutingAssembly().Location;
                string appDir = Path.GetDirectoryName(exePath);
                string defaultConfigPath = Path.Combine(appDir, "appsettings.json");

                if (File.Exists(defaultConfigPath))
                {
                    File.Copy(defaultConfigPath, _configPath);
                    log($"[ConfigLoader] Seeded {_configPath} from {defaultConfigPath}. " +
                        "Edit the copy in %ProgramData%\\Tap2iD from now on — the one next to the " +
                        "app is only the seed template and is not re-read after this first run.");
                }
                else
                {
                    // This should not happen if appsettings.json is set to "Copy if newer"
                    throw new FileNotFoundException($"Default configuration file not found in app install directory. Path checked: {defaultConfigPath}");
                }
            }
            catch (Exception ex)
            {
                // If we can't even create the default, log it and let the main loader fall back.
                log($"[ConfigLoader] FAILED to create config file at {_configPath}: {ex.Message}");
                throw; // Re-throw so LoadBarcodeConfig can catch it and use defaults
            }
        }
    }
}
