using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;

namespace OpenCoffee;

/// <summary>
/// Maintenance status percentages from @TG:C0 response.
/// Each value is a percentage (0-100%) representing remaining life of a maintenance item.
/// 1 byte per value, 0xFF means not applicable.
/// 
/// ProcessType position map (from MaintenanceStatisticsParser.java):
///   0 = Cleaning, 1 = FilterChange, 2 = Decalc, 3 = CappuRinse, 4 = CoffeeRinse, 5 = CappuClean
/// </summary>
public class MaintenanceStatus
{
    public int? CleaningPercent { get; set; }
    public int? FilterPercent { get; set; }
    public int? DecalcPercent { get; set; }
    public int? CappuRinsePercent { get; set; }
    public int? CoffeeRinsePercent { get; set; }
    public int? CappuCleanPercent { get; set; }

    /// <summary>
    /// Parse from the @TG:C0 response.
    /// Response format: @tg:C0{hexdata}\r\n
    /// The MaintenanceStatisticsParser strips the header (4 chars = "@tg:") plus 2 more for the command suffix,
    /// giving us just the hex values.
    /// Actually: preprocess first strips \r\n (last 2 chars), then strips HEADER_LENGTH(4) + 2 = 6 chars,
    /// so from "@tg:C0AABBCC" after stripping \r\n → "@tg:C0AABBCC", then substring(6) → "AABBCC"
    /// </summary>
    public static MaintenanceStatus Parse(string response)
    {
        // Strip the command prefix to get just hex data
        // Response: "@tg:C0{hexdata}"  (after \r\n already stripped)
        string data = StripHeader(response, "@tg:C0");
        
        var result = new MaintenanceStatus();
        if (data.Length >= 2) result.CleaningPercent = ParseByte(data, 0);
        if (data.Length >= 4) result.FilterPercent = ParseByte(data, 1);
        if (data.Length >= 6) result.DecalcPercent = ParseByte(data, 2);
        if (data.Length >= 8) result.CappuRinsePercent = ParseByte(data, 3);
        if (data.Length >= 10) result.CoffeeRinsePercent = ParseByte(data, 4);
        if (data.Length >= 12) result.CappuCleanPercent = ParseByte(data, 5);
        return result;
    }

    private static int? ParseByte(string hex, int position)
    {
        int offset = position * 2; // 1 byte = 2 hex chars
        if (offset + 2 > hex.Length) return null;
        int val = int.Parse(hex.Substring(offset, 2), NumberStyles.HexNumber);
        return val == 0xFF ? null : val; // 0xFF means not applicable
    }

    private static string StripHeader(string response, string prefix)
    {
        string trimmed = response.TrimEnd('\r', '\n');
        int idx = trimmed.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0) return trimmed.Substring(idx + prefix.Length);
        // Fallback: just skip the first 6 chars (header 4 + command suffix 2)
        return trimmed.Length > 6 ? trimmed.Substring(6) : "";
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        if (CleaningPercent.HasValue)    sb.AppendLine($"  Cleaning:     {CleaningPercent.Value}%");
        if (FilterPercent.HasValue)      sb.AppendLine($"  Filter:       {FilterPercent.Value}%");
        if (DecalcPercent.HasValue)      sb.AppendLine($"  Decalc:       {DecalcPercent.Value}%");
        if (CappuRinsePercent.HasValue)  sb.AppendLine($"  Cappu Rinse:  {CappuRinsePercent.Value}%");
        if (CoffeeRinsePercent.HasValue) sb.AppendLine($"  Coffee Rinse: {CoffeeRinsePercent.Value}%");
        if (CappuCleanPercent.HasValue)  sb.AppendLine($"  Cappu Clean:  {CappuCleanPercent.Value}%");
        if (sb.Length == 0) sb.AppendLine("  (no maintenance data available)");
        return sb.ToString();
    }
}

/// <summary>
/// Maintenance counters from @TG:43 response.
/// Each value is 2 bytes (a count of how many times a maintenance action was performed).
/// 
/// Same position map as MaintenanceStatus:
///   0 = Cleaning, 1 = FilterChange, 2 = Decalc, 3 = CappuRinse, 4 = CoffeeRinse, 5 = CappuClean
/// </summary>
public class MaintenanceCounters
{
    public int? CleaningCount { get; set; }
    public int? FilterChangeCount { get; set; }
    public int? DecalcCount { get; set; }
    public int? CappuRinseCount { get; set; }
    public int? CoffeeRinseCount { get; set; }
    public int? CappuCleanCount { get; set; }

    /// <summary>
    /// Parse from the @TG:43 response.
    /// MaintenanceCounterStatisticsParser: preprocess strips \r\n then substring(HEADER_LENGTH + 2) = substring(6)
    /// So from "@tg:43AABBCCDD..." → "AABBCCDD..."
    /// Each value is 2 bytes = 4 hex chars.
    /// </summary>
    public static MaintenanceCounters Parse(string response)
    {
        string data = StripHeader(response, "@tg:43");

        var result = new MaintenanceCounters();
        if (data.Length >= 4)  result.CleaningCount     = ParseUInt16(data, 0);
        if (data.Length >= 8)  result.FilterChangeCount = ParseUInt16(data, 1);
        if (data.Length >= 12) result.DecalcCount       = ParseUInt16(data, 2);
        if (data.Length >= 16) result.CappuRinseCount   = ParseUInt16(data, 3);
        if (data.Length >= 20) result.CoffeeRinseCount  = ParseUInt16(data, 4);
        if (data.Length >= 24) result.CappuCleanCount   = ParseUInt16(data, 5);
        return result;
    }

    private static int? ParseUInt16(string hex, int position)
    {
        int offset = position * 4; // 2 bytes = 4 hex chars
        if (offset + 4 > hex.Length) return null;
        int val = int.Parse(hex.Substring(offset, 4), NumberStyles.HexNumber);
        return val;
    }

    private static string StripHeader(string response, string prefix)
    {
        string trimmed = response.TrimEnd('\r', '\n');
        int idx = trimmed.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0) return trimmed.Substring(idx + prefix.Length);
        return trimmed.Length > 6 ? trimmed.Substring(6) : "";
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        if (CleaningCount.HasValue)     sb.AppendLine($"  Cleaning:      {CleaningCount.Value} times");
        if (FilterChangeCount.HasValue) sb.AppendLine($"  Filter Change: {FilterChangeCount.Value} times");
        if (DecalcCount.HasValue)       sb.AppendLine($"  Decalc:        {DecalcCount.Value} times");
        if (CappuRinseCount.HasValue)   sb.AppendLine($"  Cappu Rinse:   {CappuRinseCount.Value} times");
        if (CoffeeRinseCount.HasValue)  sb.AppendLine($"  Coffee Rinse:  {CoffeeRinseCount.Value} times");
        if (CappuCleanCount.HasValue)   sb.AppendLine($"  Cappu Clean:   {CappuCleanCount.Value} times");
        if (sb.Length == 0) sb.AppendLine("  (no maintenance counter data available)");
        return sb.ToString();
    }
}

/// <summary>
/// Product counter statistics from @TR:32 pages.
/// 
/// The machine stores product counters across 16 pages (0x00–0x0F).
/// Each page command: @TR:32,{pageHex}
/// Each page response: @tr:32,{pageHex},{pageCounter:3 hex chars}{data}
///   where data = 4 products × 2 bytes = 16 hex chars
/// 
/// All pages are concatenated to form one big hex string.
/// Position 0 (first 3 bytes, but only last 2 used) = total number of products.
/// Each product has a hex code that maps to its position in this array.
/// 
/// Known product codes for coffee machines (from machine XML config, common ones):
///   0x01 = Ristretto, 0x02 = Espresso, 0x03 = Coffee, 0x04 = Cappuccino,
///   0x05 = Latte Macchiato, 0x06 = Flat White, 0x07 = Café Barista,
///   0x08 = Long Coffee / Lungo Barista, 0x09 = Espresso Doppio,
///   0x0A = Macchiato, 0x0B = Hot Water, 0x0C = Hot Milk, 0x0D = Milk Foam,
///   0x0E = Special Coffee (varies by model), etc.
/// Position 0x00 is always "Total Products".
/// </summary>
public class ProductCounters
{
    /// <summary>Total number of all products made.</summary>
    public int TotalProducts { get; set; }

    /// <summary>Per-product counters. Key = product position (hex code), Value = count.</summary>
    [JsonIgnore]
    public Dictionary<int, int> Products { get; set; } = new();

    /// <summary>Per-product counters with human-readable names as keys. Used for JSON serialization.</summary>
    public Dictionary<string, int> NamedProducts =>
        Products.OrderBy(k => k.Key).ToDictionary(kvp => GetProductName(kvp.Key), kvp => kvp.Value);

    /// <summary>Raw concatenated hex data from all pages.</summary>
    [JsonIgnore]
    public string RawData { get; set; }

    // Common JURA product names by position code.
    // These may vary by machine model; these are the most common ones.
    private static readonly Dictionary<int, string> KnownProductNames = new()
    {
        [0x00] = "Total Products",
        [0x01] = "Ristretto",
        [0x02] = "Espresso",
        [0x03] = "Coffee",
        [0x04] = "Cappuccino",
        [0x05] = "Latte Macchiato",
        [0x06] = "Flat White",
        [0x07] = "Café Barista",
        [0x08] = "Lungo Barista",
        [0x09] = "Espresso Doppio",
        [0x0A] = "Macchiato",
        [0x0B] = "Hot Water",
        [0x0C] = "Hot Milk",
        [0x0D] = "Milk Foam",
        [0x0E] = "Jug Coffee",
        [0x0F] = "Special Coffee 1",
        [0x36] = "Double coffee",
    };

    public static string GetProductName(int code)
    {
        return KnownProductNames.TryGetValue(code, out var name) ? name : $"Product 0x{code:X2}";
    }

    /// <summary>
    /// Parse from concatenated page responses.
    /// Each page response: "@tr:32,{pageHex},{pageCounterByte}{productData}"
    /// 
    /// The WifiSinglePageStatisticsParser:
    ///   - preprocess: strips \r\n, then strips header(4)+3 chars → leaves "{pageCounterByte}{productData}"
    ///   - parseResponse: first 2 hex chars = page counter byte, skip 3 chars total (the page counter),
    ///     then read 4 products × bytesPerProduct(2) × 2 chars/byte = 16 hex chars
    /// 
    /// The WifiProductCounterStatisticsParser.get(data, pos):
    ///   bytesPerValue = 2, so each product = 4 hex chars
    ///   For 3-byte mode: skips first 2 hex chars of each 6-char block (reads last 4)
    ///   For 2-byte mode: reads all 4 hex chars directly
    /// 
    /// After collection, all page data is concatenated and parsed as one big hex string.
    /// Position 0 = total products, each product's position = its hex code.
    /// </summary>
    public static ProductCounters ParseFromPages(Dictionary<int, string> pageData)
    {
        // Sort pages and concatenate the product data
        var sb = new StringBuilder();
        for (int page = 0; page <= 15; page++)
        {
            if (pageData.TryGetValue(page, out string? data))
                sb.Append(data);
            else
                sb.Append("0000000000000000"); // DEFAULT padding (16 hex chars = 4 × 2 bytes)
        }

        string allData = sb.ToString();
        return ParseFromCombinedHex(allData, 2);
    }

    /// <summary>
    /// Parse the combined hex data.
    /// bytesPerProduct = 2: each product value is 4 hex chars
    /// </summary>
    public static ProductCounters ParseFromCombinedHex(string hex, int bytesPerProduct)
    {
        var result = new ProductCounters { RawData = hex };
        int charsPerProduct = bytesPerProduct * 2;

        // Position 0 = total products
        if (hex.Length >= charsPerProduct)
        {
            result.TotalProducts = ParseValue(hex, 0, bytesPerProduct);
        }

        // Scan positions 1 through however many fit in the data
        int maxPosition = hex.Length / charsPerProduct;
        for (int pos = 1; pos < maxPosition; pos++)
        {
            int value = ParseValue(hex, pos, bytesPerProduct);
            if (value != 0xFFFF) // 0xFFFF = product not available on this machine
            {
                result.Products[pos] = value;
            }
        }

        return result;
    }

    private static int ParseValue(string hex, int position, int bytesPerProduct)
    {
        int charsPerProduct = bytesPerProduct * 2;
        int offset = position * charsPerProduct;
        if (offset + charsPerProduct > hex.Length) return 0;
        return int.Parse(hex.Substring(offset, charsPerProduct), NumberStyles.HexNumber);
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"  Total Products: {TotalProducts}");
        foreach (var kvp in Products.OrderBy(k => k.Key))
        {
            string name = GetProductName(kvp.Key);
            sb.AppendLine($"  {name,-20} {kvp.Value}");
        }
        if (Products.Count == 0) sb.AppendLine("  (no individual product data)");
        return sb.ToString();
    }
}
