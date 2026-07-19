namespace StockSharp.BTCMarkets.Native;

static class BTCMarketsChecksum
{
    private static readonly uint[] _table = CreateTable();

    public static uint Calculate(IEnumerable<BTCMarketsBookLevel> bids,
        IEnumerable<BTCMarketsBookLevel> asks)
    {
        var value = string.Concat((bids ?? []).Take(10)
            .Concat((asks ?? []).Take(10))
            .Select(static level => Trim(level.PriceText) +
                Trim(level.VolumeText)));
        var checksum = uint.MaxValue;
        foreach (var item in Encoding.UTF8.GetBytes(value))
            checksum = _table[(checksum ^ item) & 0xff] ^ checksum >> 8;
        return checksum ^ uint.MaxValue;
    }

    private static string Trim(string value)
    {
        value = value.ThrowIfEmpty(nameof(value)).Replace(".", string.Empty,
            StringComparison.Ordinal).TrimStart('0');
        return value.IsEmpty() ? "0" : value;
    }

    private static uint[] CreateTable()
    {
        var result = new uint[256];
        for (uint index = 0; index < result.Length; index++)
        {
            var value = index;
            for (var bit = 0; bit < 8; bit++)
                value = (value & 1) != 0
                    ? 0xedb88320U ^ value >> 1
                    : value >> 1;
            result[index] = value;
        }
        return result;
    }
}
