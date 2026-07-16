namespace StockSharp.Groww.Native.Model;

internal sealed class GrowwInstrument
{
	public string Exchange { get; set; }
	public string ExchangeToken { get; set; }
	public string TradingSymbol { get; set; }
	public string GrowwSymbol { get; set; }
	public string Name { get; set; }
	public string InstrumentType { get; set; }
	public string Segment { get; set; }
	public string Series { get; set; }
	public string Isin { get; set; }
	public string UnderlyingSymbol { get; set; }
	public string UnderlyingExchangeToken { get; set; }
	public DateTime? ExpiryDate { get; set; }
	public decimal? StrikePrice { get; set; }
	public decimal? LotSize { get; set; }
	public decimal? TickSize { get; set; }
	public decimal? FreezeQuantity { get; set; }
	public bool IsReserved { get; set; }
	public bool IsBuyAllowed { get; set; }
	public bool IsSellAllowed { get; set; }
}

internal sealed class GrowwSecurityInfo
{
	private const string _prefix = "GROWW";

	public string Exchange { get; init; }
	public string Segment { get; init; }
	public string ExchangeToken { get; init; }
	public string TradingSymbol { get; init; }
	public string GrowwSymbol { get; init; }
	public string InstrumentType { get; init; }
	public string Isin { get; init; }

	public string ToNative()
		=> string.Join('|', _prefix, Encode(Exchange), Encode(Segment), Encode(ExchangeToken),
			Encode(TradingSymbol), Encode(GrowwSymbol), Encode(InstrumentType), Encode(Isin));

	public static GrowwSecurityInfo FromInstrument(GrowwInstrument instrument)
		=> new()
		{
			Exchange = instrument.Exchange,
			Segment = instrument.Segment,
			ExchangeToken = instrument.ExchangeToken,
			TradingSymbol = instrument.TradingSymbol,
			GrowwSymbol = instrument.GrowwSymbol,
			InstrumentType = instrument.InstrumentType,
			Isin = instrument.Isin,
		};

	public static bool TryParse(object native, out GrowwSecurityInfo info)
	{
		info = null;
		if (native is not string value)
			return false;

		var parts = value.Split('|');
		if (parts.Length != 8 || !parts[0].Equals(_prefix, StringComparison.OrdinalIgnoreCase))
			return false;

		info = new()
		{
			Exchange = Decode(parts[1]),
			Segment = Decode(parts[2]),
			ExchangeToken = Decode(parts[3]),
			TradingSymbol = Decode(parts[4]),
			GrowwSymbol = Decode(parts[5]),
			InstrumentType = Decode(parts[6]),
			Isin = Decode(parts[7]),
		};
		return true;
	}

	private static string Encode(string value) => Uri.EscapeDataString(value ?? string.Empty);
	private static string Decode(string value) => Uri.UnescapeDataString(value ?? string.Empty);
}
