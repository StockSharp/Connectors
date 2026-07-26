namespace StockSharp.Bcs.Native;

static class BcsExtensions
{
	private static readonly PairSet<TimeSpan, string> _timeFrames = new()
	{
		{ TimeSpan.FromMinutes(1), "M1" },
		{ TimeSpan.FromMinutes(5), "M5" },
		{ TimeSpan.FromMinutes(15), "M15" },
		{ TimeSpan.FromMinutes(30), "M30" },
		{ TimeSpan.FromHours(1), "H1" },
		{ TimeSpan.FromHours(4), "H4" },
		{ TimeSpan.FromDays(1), "D" },
		{ TimeSpan.FromDays(7), "W" },
		{ TimeSpan.FromDays(30), "MN" },
	};

	public static IEnumerable<TimeSpan> TimeFrames => _timeFrames.Keys;

	public static string ToNative(this TimeSpan timeFrame)
		=> _timeFrames.TryGetValue(timeFrame, out var value)
			? value
			: throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
				LocalizedStrings.InvalidValue);

	public static SecurityTypes ToSecurityType(this string type)
		=> type?.ToUpperInvariant() switch
		{
			"STOCK" or "FOREIGN_STOCK" => SecurityTypes.Stock,
			"DEPOSITARY_RECEIPTS" => SecurityTypes.Adr,
			"BONDS" or "EURO_BONDS" or "NOTES" => SecurityTypes.Bond,
			"MUTUAL_FUNDS" => SecurityTypes.Fund,
			"ETF" => SecurityTypes.Etf,
			"FUTURES" => SecurityTypes.Future,
			"OPTIONS" => SecurityTypes.Option,
			"CURRENCY" => SecurityTypes.Currency,
			"GOODS" => SecurityTypes.Commodity,
			"INDICES" => SecurityTypes.Index,
			_ => SecurityTypes.Stock,
		};

	public static string[] ToNative(this IEnumerable<SecurityTypes> types)
	{
		var result = new List<string>();
		foreach (var type in types ?? [])
		{
			switch (type)
			{
				case SecurityTypes.Stock:
					result.AddRange(["STOCK", "FOREIGN_STOCK"]);
					break;
				case SecurityTypes.Adr:
				case SecurityTypes.Gdr:
					result.Add("DEPOSITARY_RECEIPTS");
					break;
				case SecurityTypes.Bond:
					result.AddRange(["BONDS", "EURO_BONDS", "NOTES"]);
					break;
				case SecurityTypes.Fund:
					result.Add("MUTUAL_FUNDS");
					break;
				case SecurityTypes.Etf:
					result.Add("ETF");
					break;
				case SecurityTypes.Future:
					result.Add("FUTURES");
					break;
				case SecurityTypes.Option:
					result.Add("OPTIONS");
					break;
				case SecurityTypes.Currency:
					result.Add("CURRENCY");
					break;
				case SecurityTypes.Commodity:
					result.Add("GOODS");
					break;
				case SecurityTypes.Index:
					result.Add("INDICES");
					break;
			}
		}

		return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
	}

	public static string ToNative(this Sides side)
		=> side == Sides.Buy ? "1" : "2";

	public static Sides ToSide(this string side)
		=> side?.ToUpperInvariant() is "1" or "BUY" ? Sides.Buy : Sides.Sell;

	public static string ToNative(this OrderTypes? type)
		=> type == OrderTypes.Market ? "1" : "2";

	public static OrderTypes ToOrderType(this string type)
		=> type == "1" ? OrderTypes.Market : OrderTypes.Limit;

	public static OrderTypes ToOrderType(this int type)
		=> type == 1 ? OrderTypes.Market :
			type == 2 ? OrderTypes.Limit : OrderTypes.Conditional;

	public static OrderStates ToOrderState(this string status)
		=> status switch
		{
			"0" or "1" => OrderStates.Active,
			"2" or "4" or "5" => OrderStates.Done,
			"8" => OrderStates.Failed,
			_ => OrderStates.Pending,
		};

	public static OrderStates ToOrderState(this int status, string rejectReason)
		=> !rejectReason.IsEmpty() ? OrderStates.Failed :
			status == 3 ? OrderStates.Active :
			status is 1 or 2 ? OrderStates.Done : OrderStates.Pending;

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency : null;

	public static SecurityId ToSecurityId(this string ticker, string classCode)
		=> new()
		{
			SecurityCode = ticker,
			BoardCode = classCode.IsEmpty("MOEX"),
		};
}
