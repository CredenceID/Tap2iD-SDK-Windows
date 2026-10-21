using System;
using System.Threading.Tasks;
using Tap2iDBluetoothCommon;
using Tap2iDSdk;
using Tap2iDSdk.Extension;
using Tap2iDSdk.Model;

namespace Tap2iDSampleWinUI.Services
{
    /// <summary>
    /// Default <see cref="IPeripheralService"/> implementation backed by the SDK verifier.
    /// The verifier is injected (single shared instance) rather than created here, so the
    /// same <see cref="IVerifyMdoc"/> is used across the app.
    /// </summary>
    public sealed class PeripheralService : IPeripheralService
    {
        private readonly IVerifyMdoc _verifier;

        public PeripheralService(IVerifyMdoc verifier)
        {
            _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        }

        public Task<AllPeripheralStatus> GetStatusAsync()
            => _verifier.CheckAllPeripheralStatusAsync();
    }
}
