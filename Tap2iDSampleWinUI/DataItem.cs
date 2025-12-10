namespace Tap2iDSampleWinUI
{
    public class DisplayItem
    {
        public string Label { get; set; }
        public string Value { get; set; }

        public DisplayItem(string label, string value)
        {
            Label = label;
            Value = value;
        }
    }
}
