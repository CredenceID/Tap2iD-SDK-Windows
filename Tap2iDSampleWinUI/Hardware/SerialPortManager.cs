using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using System.Timers;

namespace Tap2iDSampleWinUI.Hardware
{
    /// <summary>
    /// Defines the settings for a single serial port.
    /// </summary>
    public class PortConfig
    {
        public string PortName { get; set; } = "COM4"; // Default port
        public int BaudRate { get; set; } = 9600;
    }

    /// <summary>
    /// Manages serial port communication for the barcode scanner.
    /// PDF417 payloads can be 600–1800 bytes; at 9600 baud that takes up to ~1.9 s.
    /// We buffer all incoming chunks and fire the callback only after the line goes
    /// idle for IDLE_TIMEOUT_MS, ensuring we always deliver a complete payload.
    /// </summary>
    public class SerialPortManager(PortConfig barcodeConfig) : IDisposable
    {
        private SerialPort _barcodeSerialPort;
        private Action<string> _onDataReceivedCallback;
        private readonly object _barcodeLock = new();

        // Buffer accumulates chunks; idle timer fires when transmission is complete.
        private readonly StringBuilder _receiveBuffer = new();
        private Timer _idleTimer;

        // Fire callback after this many ms with no new data — long enough for a full
        // PDF417 payload at 9600 baud (≈ 1900 ms) plus margin, short enough to feel snappy.
        private const int IDLE_TIMEOUT_MS = 300;

        /// <summary>
        /// Tries to open the serial port and start listening for data.
        /// </summary>
        public bool StartListening(Action<string> logAppender, Action<string> onDataReceived)
        {
            if (_barcodeSerialPort != null)
            {
                logAppender("[SerialPortManager] Already listening.");
                return true;
            }

            _onDataReceivedCallback = onDataReceived;

            _idleTimer = new Timer(IDLE_TIMEOUT_MS) { AutoReset = false };
            _idleTimer.Elapsed += OnIdleTimeout;

            lock (_barcodeLock)
            {
                try
                {
                    Debug.WriteLine($"[SerialPortManager] Opening port {barcodeConfig.PortName} for barcode scanner.");
                    _barcodeSerialPort = new SerialPort(barcodeConfig.PortName, barcodeConfig.BaudRate);
                    // SerialPort decodes as ASCII by default, which turns every byte >= 0x80 into
                    // '?' and corrupts raw AAMVA signature blocks (e.g. VA's ZVA). Latin1 maps
                    // all 256 byte values 1:1 (CIE-6730).
                    _barcodeSerialPort.Encoding = Encoding.Latin1;
                    _barcodeSerialPort.DataReceived += OnBarcodeDataReceived;
                    _barcodeSerialPort.Open();

                    logAppender($"[SerialPortManager] Barcode scanner open at port {barcodeConfig.PortName}");
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SerialPortManager] Failed to open serial port {barcodeConfig.PortName}: {ex.Message}");
                    logAppender($"No barcode scanner detected on {barcodeConfig.PortName}.");

                    _barcodeSerialPort?.Dispose();
                    _barcodeSerialPort = null;
                    return false;
                }
            }
        }

        private void OnBarcodeDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            if (sender is not SerialPort sp) return;

            try
            {
                string chunk = sp.ReadExisting();
                if (string.IsNullOrEmpty(chunk)) return;

                Debug.WriteLine($"[SerialPortManager] Chunk received: {chunk.Length} bytes");

                lock (_barcodeLock)
                    _receiveBuffer.Append(chunk);

                // Restart the idle timer on every chunk
                _idleTimer.Stop();
                _idleTimer.Start();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SerialPortManager] Error reading from barcode serial port: {ex.Message}");
            }
        }

        private void OnIdleTimeout(object sender, ElapsedEventArgs e)
        {
            string data;
            lock (_barcodeLock)
            {
                data = _receiveBuffer.ToString().Trim();
                _receiveBuffer.Clear();
            }

            if (string.IsNullOrEmpty(data)) return;

            // Length only: a barcode can carry personal data.
            Debug.WriteLine($"[SerialPortManager] Complete barcode received: length={data.Length}");
            _onDataReceivedCallback?.Invoke(data);
        }

        private void CleanupBarcodeReader()
        {
            lock (_barcodeLock)
            {
                _idleTimer?.Stop();
                _idleTimer?.Dispose();
                _idleTimer = null;

                if (_barcodeSerialPort != null)
                {
                    _barcodeSerialPort.DataReceived -= OnBarcodeDataReceived;
                    if (_barcodeSerialPort.IsOpen) _barcodeSerialPort.Close();
                    _barcodeSerialPort.Dispose();
                    _barcodeSerialPort = null;
                }
                _onDataReceivedCallback = null;
            }
        }

        public void Dispose()
        {
            CleanupBarcodeReader();
        }
    }
}
