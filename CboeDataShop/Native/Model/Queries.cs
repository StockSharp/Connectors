namespace StockSharp.CboeDataShop.Native.Model;

abstract class CboeQuery
{
	protected readonly List<KeyValuePair<string, string>> Values = [];

	protected void Reset()
		=> Values.Clear();

	protected void Add(string name, string value)
	{
		if (!value.IsEmpty())
			Values.Add(new(name, value));
	}

	protected void Add(string name, DateTime? value)
	{
		if (value != null)
			Add(name, value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
	}

	protected void Add(string name, decimal? value)
	{
		if (value != null)
			Add(name, value.Value.ToString(CultureInfo.InvariantCulture));
	}

	protected void Add(string name, long? value)
	{
		if (value != null)
			Add(name, value.Value.ToString(CultureInfo.InvariantCulture));
	}

	protected void Add(string name, int value)
		=> Add(name, (long)value);

	public string ToQueryString()
		=> Values.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}").Join("&");
}

sealed class CboeUnderlyingQuoteQuery : CboeQuery
{
	public string Symbols { get; set; }
	public DateTime Date { get; set; }
	public long? SequenceNumber { get; set; }

	public new string ToQueryString()
	{
		Reset();
		Add("symbols", Symbols.ThrowIfEmpty(nameof(Symbols)));
		Add("date", Date);
		Add("seq_no", SequenceNumber);
		return base.ToQueryString();
	}
}

sealed class CboeOptionQuoteQuery : CboeQuery
{
	public string Symbol { get; set; }
	public string Root { get; set; }
	public CboeOptionTypes? OptionType { get; set; }
	public DateTime Date { get; set; }
	public DateTime? MinimumExpiry { get; set; }
	public DateTime? MaximumExpiry { get; set; }
	public decimal? MinimumStrike { get; set; }
	public decimal? MaximumStrike { get; set; }

	public new string ToQueryString()
	{
		Reset();
		Add("root", Root);
		if (OptionType != null)
			Add("option_type", OptionType.Value.ToApi());
		Add("date", Date);
		Add("min_expiry", MinimumExpiry);
		Add("max_expiry", MaximumExpiry);
		Add("min_strike", MinimumStrike);
		Add("max_strike", MaximumStrike);
		Add("symbol", Symbol.ThrowIfEmpty(nameof(Symbol)));
		return base.ToQueryString();
	}
}

sealed class CboeTradingDaysQuery : CboeQuery
{
	public DateTime StartDate { get; set; }
	public DateTime EndDate { get; set; }

	public new string ToQueryString()
	{
		Reset();
		Add("start_date", StartDate);
		Add("end_date", EndDate);
		return base.ToQueryString();
	}
}

sealed class CboeTradeQuery : CboeQuery
{
	public string Symbol { get; set; }
	public string Root { get; set; }
	public DateTime? Expiry { get; set; }
	public decimal? Strike { get; set; }
	public CboeOptionTypes? OptionType { get; set; }
	public string MinimumTime { get; set; }
	public string MaximumTime { get; set; }
	public long? SequenceNumber { get; set; }
	public int Limit { get; set; } = 10000;
	public DateTime Date { get; set; }

	public new string ToQueryString()
	{
		Reset();
		Add("symbol", Symbol.ThrowIfEmpty(nameof(Symbol)));
		Add("root", Root);
		Add("expiry", Expiry);
		Add("strike", Strike);
		if (OptionType != null)
			Add("option_type", OptionType.Value.ToApi());
		Add("min_time", MinimumTime);
		Add("max_time", MaximumTime);
		Add("seq_no", SequenceNumber);
		Add("limit", Limit);
		Add("date", Date);
		return base.ToQueryString();
	}
}
