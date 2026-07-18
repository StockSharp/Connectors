namespace StockSharp.NasdaqDataLink.Native.Model;

abstract class NasdaqDataLinkQuery
{
	private readonly List<KeyValuePair<string, string>> _values = [];

	protected void Reset()
		=> _values.Clear();

	protected void Add(string name, string value)
	{
		if (!value.IsEmpty())
			_values.Add(new(name, value));
	}

	protected void Add(string name, int? value)
	{
		if (value != null)
			Add(name, value.Value.ToString(CultureInfo.InvariantCulture));
	}

	protected void Add(string name, DateTime? value)
	{
		if (value != null)
			Add(name, value.Value.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
	}

	public string ToQueryString()
		=> _values.Select(pair =>
			$"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}").Join("&");
}

sealed class NasdaqDataLinkSearchQuery : NasdaqDataLinkQuery
{
	public string Query { get; set; }
	public string DatabaseCode { get; set; }
	public int Page { get; set; } = 1;
	public int PerPage { get; set; } = 100;

	public new string ToQueryString()
	{
		Reset();
		Add("query", Query);
		Add("database_code", DatabaseCode);
		Add("page", Page);
		Add("per_page", PerPage);
		return base.ToQueryString();
	}
}

sealed class NasdaqDataLinkDataQuery : NasdaqDataLinkQuery
{
	public DateTime? StartDate { get; set; }
	public DateTime? EndDate { get; set; }
	public int? Limit { get; set; }
	public NasdaqDataLinkOrders Order { get; set; } = NasdaqDataLinkOrders.Ascending;

	public new string ToQueryString()
	{
		Reset();
		Add("start_date", StartDate);
		Add("end_date", EndDate);
		Add("limit", Limit);
		Add("order", Order == NasdaqDataLinkOrders.Ascending ? "asc" : "desc");
		return base.ToQueryString();
	}
}
