namespace ChatAgent.Api.Barcode;

/// <summary>
/// Symbologies the agent may choose for beverage labels. A curated subset of section 3 of the
/// TEC-IT API reference: retail, logistics and GS1 2D codes. Extend as needed.
/// </summary>
public static class BarcodeTypes
{
    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Retail (consumer units)
        "EAN13", "EAN8", "UPCA", "UPCE",
        // Logistics (cases, pallets)
        "EAN14", "GS1-128", "Code128", "Code39",
        // GS1 DataBar
        "GS1DataBar", "GS1DataBarLimited", "GS1DataBarExpanded",
        // 2D
        "QRCode", "DataMatrix", "GS1QRCode", "GS1DataMatrix",
        "GS1DigitalLink_QRCode", "GS1DigitalLink_DataMatrix",
    };
}
