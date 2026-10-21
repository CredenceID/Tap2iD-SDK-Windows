using System.Threading.Tasks;
using Tap2iDBluetoothCommon;
using Tap2iDSdk;
using Tap2iDSdk.Extension;
using Tap2iDSdk.Model;

namespace Tap2iDSampleWinUI.Services
{
    /// <summary>
    /// Abstraction over the Tap2iD SDK peripheral API. Keeps the SDK's
    /// <c>IVerifyMdoc.CheckAllPeripheralStatusAsync</c> behind a testable, injectable
    /// boundary so view models never touch the SDK directly.
    /// </summary>
    public interface IPeripheralService
    {
        /// <summary>
        /// Queries the current per-capability peripheral status (QR / NFC / BLE) and the
        /// list of available readers, as reported by the SDK.
        /// </summary>
        Task<AllPeripheralStatus> GetStatusAsync();
    }
}
