using System.Collections.Frozen;

namespace ChatAgent.Api.Barcode;

/// <summary>
/// Symbologies the agent may choose for beverage labels. A curated subset of section 3 of the
/// TEC-IT API reference: retail, logistics and GS1 2D codes. Only types whose data format the
/// validator knows how to build are included; extend deliberately.
/// </summary>
public static class BarcodeTypes
{
    public static readonly FrozenSet<string> Allowed = new[]
    {
        // Retail (consumer units)
        "EAN13", "EAN8", "UPCA",
        // Logistics (cases, pallets)
        "EAN14", "GS1-128", "Code128", "Code39",
        // 2D
        "QRCode", "DataMatrix", "GS1QRCode", "GS1DataMatrix",
        "GS1DigitalLink_QRCode", "GS1DigitalLink_DataMatrix",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
}
