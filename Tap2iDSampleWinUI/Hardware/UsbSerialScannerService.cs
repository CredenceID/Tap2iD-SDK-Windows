using System;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.SerialCommunication;

namespace Tap2iDSampleWinUI.Hardware
{
    /// <summary>
    /// Reads barcodes from a scanner that enumerates as a USB CDC virtual COM port rather than a
    /// keyboard: the Credence P35's built-in scanner (VID_E851&amp;PID_1002, CIE-7857) and the HID
    /// TripTick Halo 210's scanner (VID_0DB5&amp;PID_0142, CIE-6244). A decoded barcode arrives as
    /// serial bytes and is never typed into the focused text box. This service finds the port by its
    /// USB id, since Windows numbers the COM port differently per PC and can renumber it after a
    /// reboot, opens it at 115200 8N1 with DTR asserted (the CDC port stays silent without DTR), and
    /// raises <see cref="BarcodeScanned"/> once per scan.
    ///
    /// Framing is by idle gap: bytes are buffered and flushed as one payload after
    /// <see cref="IdleFlushMs"/> of quiet. Never split on CR/LF: an AAMVA PDF417 carries CR/LF/RS
    /// separators internally, and its DMV signature is checked over the exact bytes.
    ///
    /// A device watcher follows the scanner across USB plug and unplug, so a scanner connected after
    /// start-up is read too, and a stale handle is dropped on unplug. Opening fails soft (logged,
    /// never thrown): another process, or the SDK's P35 transport, can hold this exclusive port.
    ///
    /// Same approach as the Tap2iD Windows app (C-mDocWindowsSampleApp, P35ScannerService.cs).
    /// </summary>
    internal sealed class UsbSerialScannerService : IDisposable
    {
        private const string PortNameProperty = "System.DeviceInterface.Serial.PortName";

        /// <summary>
        /// Quiet gap (ms) after the last byte before the buffered scan is flushed. A PDF417 transmits
        /// well within it at 115200 baud, while two deliberate scans stay separate.
        /// </summary>
        private const int IdleFlushMs = 150;

        private readonly string _name;
        private readonly string _selector;
        private readonly bool _rtsEnable;
        private readonly Action<string> _log;
        private readonly object _gate = new();
        private readonly StringBuilder _rx = new();
        private DeviceWatcher _watcher;
        private SerialPort _port;
        private string _deviceId;
        private Timer _idleTimer;
        private bool _disposed;

        /// <summary>Raised on a background thread with each complete scan, internal separators kept.</summary>
        public event Action<string> BarcodeScanned;

        /// <param name="name">Used in log lines, e.g. "P35 scanner".</param>
        /// <param name="vid">USB vendor id of the scanner's serial interface.</param>
        /// <param name="pid">USB product id of the scanner's serial interface. Only devices with this
        /// id match, so another serial port of the same vendor (the Halo's NFC unit, 0DB5:0184, has
        /// one that carries no barcode data) is never picked.</param>
        /// <param name="rtsEnable">Also assert RTS when opening the port.</param>
        public UsbSerialScannerService(string name, ushort vid, ushort pid, Action<string> log, bool rtsEnable = false)
        {
            _name = name;
            _selector = SerialDevice.GetDeviceSelectorFromUsbVidPid(vid, pid);
            _rtsEnable = rtsEnable;
            _log = log;
        }

        /// <summary>The COM port of the connected scanner, or null. Does not open the port.</summary>
        public async Task<string> FindPortAsync()
        {
            try
            {
                var devices = await DeviceInformation.FindAllAsync(_selector, new[] { PortNameProperty });
                return devices.Select(d => d.Properties.TryGetValue(PortNameProperty, out var p) ? p as string : null)
                              .FirstOrDefault(p => !string.IsNullOrEmpty(p));
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Starts watching for the scanner. It is opened as soon as it is found, now or on a later
        /// plug-in. Idempotent.
        /// </summary>
        public void Start()
        {
            lock (_gate)
            {
                if (_disposed || _watcher != null) return;
                try
                {
                    var watcher = DeviceInformation.CreateWatcher(_selector, new[] { PortNameProperty });
                    watcher.Added += OnDeviceAdded;
                    watcher.Removed += OnDeviceRemoved;
                    // Added/Removed are only raised reliably when Updated has a handler too.
                    watcher.Updated += (_, _) => { };
                    watcher.EnumerationCompleted += (_, _) =>
                    {
                        lock (_gate)
                        {
                            if (_port == null) _log?.Invoke($"{_name} not connected; it will be used as soon as it is plugged in.");
                        }
                    };
                    _watcher = watcher;
                    watcher.Start();
                }
                catch (Exception ex)
                {
                    _watcher = null;
                    _log?.Invoke($"{_name}: could not watch for the device ({ex.Message}).");
                }
            }
        }

        private void OnDeviceAdded(DeviceWatcher sender, DeviceInformation info)
        {
            var portName = info.Properties.TryGetValue(PortNameProperty, out var p) ? p as string : null;
            if (string.IsNullOrEmpty(portName))
            {
                _log?.Invoke($"{_name} found, but its COM port could not be resolved.");
                return;
            }

            lock (_gate)
            {
                if (_disposed) return;
                // A re-plug can leave the old handle "open" but silent, so always reopen.
                ClosePort();
                try
                {
                    var port = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One)
                    {
                        ReadTimeout = 500,
                        WriteTimeout = 500,
                        DtrEnable = true, // the CDC port streams only with DTR asserted
                        RtsEnable = _rtsEnable,
                        // SerialPort decodes as ASCII by default, which turns every byte >= 0x80
                        // into '?' and corrupts raw AAMVA signature blocks (e.g. VA's ZVA).
                        // Latin1 maps all 256 byte values 1:1 (CIE-6730).
                        Encoding = Encoding.Latin1,
                    };
                    port.DataReceived += OnDataReceived;
                    port.Open();
                    _port = port;
                    _deviceId = info.Id;
                    _idleTimer = new Timer(OnIdleFlush, null, Timeout.Infinite, Timeout.Infinite);
                    _log?.Invoke($"{_name} listening on {portName}.");
                }
                catch (Exception ex) // UnauthorizedAccessException when the port is in use, IOException, ...
                {
                    _log?.Invoke($"{_name}: could not open {portName} ({ex.Message}).");
                    ClosePort();
                }
            }
        }

        private void OnDeviceRemoved(DeviceWatcher sender, DeviceInformationUpdate update)
        {
            lock (_gate)
            {
                if (_port == null || !string.Equals(update.Id, _deviceId, StringComparison.OrdinalIgnoreCase)) return;
                ClosePort();
                _log?.Invoke($"{_name} disconnected.");
            }
        }

        private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                lock (_gate)
                {
                    if (_port == null || !_port.IsOpen) return;
                    _rx.Append(_port.ReadExisting());
                    // Each chunk pushes the flush deadline out; the scan is complete once it goes quiet.
                    _idleTimer?.Change(IdleFlushMs, Timeout.Infinite);
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"{_name} read error ({ex.Message}).");
            }
        }

        private void OnIdleFlush(object state)
        {
            string scan;
            lock (_gate)
            {
                // Only the ends are trimmed (the scanner's suffix terminator); internal separators stay.
                scan = _rx.ToString().Trim();
                _rx.Clear();
            }
            if (scan.Length == 0) return;

            // Never log scan content (PII), only its length.
            _log?.Invoke($"{_name}: barcode read ({scan.Length} chars).");
            try { BarcodeScanned?.Invoke(scan); }
            catch (Exception ex) { _log?.Invoke($"{_name}: scan handler failed ({ex.Message})."); }
        }

        /// <summary>
        /// Drops the current port. Caller holds _gate. The port is closed on a worker thread, not
        /// under the lock: SerialPort.Close waits for a running DataReceived handler, which may itself
        /// be waiting for _gate.
        /// </summary>
        private void ClosePort()
        {
            _idleTimer?.Dispose();
            _idleTimer = null;
            _rx.Clear();
            _deviceId = null;
            var port = _port;
            _port = null;
            if (port == null) return;

            port.DataReceived -= OnDataReceived;
            Task.Run(() =>
            {
                try
                {
                    if (port.IsOpen) port.Close();
                }
                catch
                {
                    // Closing a port whose device was unplugged can throw; it is being dropped anyway.
                }
                finally
                {
                    port.Dispose();
                }
            });
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                if (_watcher != null)
                {
                    _watcher.Added -= OnDeviceAdded;
                    _watcher.Removed -= OnDeviceRemoved;
                    try
                    {
                        if (_watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                            _watcher.Stop();
                    }
                    catch
                    {
                        // Best effort on shutdown.
                    }
                    _watcher = null;
                }
                ClosePort();
            }
        }
    }
}
