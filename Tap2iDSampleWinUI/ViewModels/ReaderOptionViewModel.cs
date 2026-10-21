using Tap2iDBluetoothCommon;
using Tap2iDSdk.Extension;
using Tap2iDSdk.Model;

namespace Tap2iDSampleWinUI.ViewModels
{
    /// <summary>
    /// One selectable reader in a per-capability dropdown. A <see cref="Model"/> of
    /// <c>null</c> represents the SDK default (i.e. <c>MdocConfig.*Peripheral = null</c>).
    /// </summary>
    public sealed class ReaderOptionViewModel
    {
        public string Label { get; }
        public PeripheralModel? Model { get; }

        public ReaderOptionViewModel(string label, PeripheralModel? model)
        {
            Label = label;
            Model = model;
        }

        public override string ToString() => Label;
    }
}
