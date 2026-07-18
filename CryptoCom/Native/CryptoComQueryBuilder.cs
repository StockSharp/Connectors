namespace StockSharp.CryptoCom.Native;

interface ICryptoComQueryParams
{
	void Write(CryptoComQueryBuilder query);
}

sealed class CryptoComQueryBuilder
{
	private readonly StringBuilder _builder = new();

	public void Add(string name, string value)
	{
		if (name.IsEmpty() || value is null)
			return;

		if (_builder.Length > 0)
			_builder.Append('&');

		_builder
			.Append(Uri.EscapeDataString(name))
			.Append('=')
			.Append(Uri.EscapeDataString(value));
	}

	public void Add(string name, int? value)
	{
		if (value is int number)
			Add(name, number.ToString(CultureInfo.InvariantCulture));
	}

	public void Add(string name, long? value)
	{
		if (value is long number)
			Add(name, number.ToString(CultureInfo.InvariantCulture));
	}

	public override string ToString() => _builder.ToString();
}

sealed class CryptoComEmptyParams : ICryptoComQueryParams, ICryptoComPrivateParams
{
	public static CryptoComEmptyParams Instance { get; } = new();
	private CryptoComEmptyParams() { }
	public void Write(CryptoComQueryBuilder query) { }
	public void WriteSignature(CryptoComSignatureBuilder builder) { }
}

sealed class CryptoComInstrumentQuery : ICryptoComQueryParams
{
	public string InstrumentName { get; init; }
	public void Write(CryptoComQueryBuilder query) => query.Add("instrument_name", InstrumentName);
}

sealed class CryptoComBookQuery : ICryptoComQueryParams
{
	public string InstrumentName { get; init; }
	public int Depth { get; init; }

	public void Write(CryptoComQueryBuilder query)
	{
		query.Add("instrument_name", InstrumentName);
		query.Add("depth", Depth);
	}
}

sealed class CryptoComTradesQuery : ICryptoComQueryParams
{
	public string InstrumentName { get; init; }
	public int? Count { get; init; }
	public long? StartTime { get; init; }
	public long? EndTime { get; init; }

	public void Write(CryptoComQueryBuilder query)
	{
		query.Add("instrument_name", InstrumentName);
		query.Add("count", Count);
		query.Add("start_ts", StartTime);
		query.Add("end_ts", EndTime);
	}
}

sealed class CryptoComCandlesQuery : ICryptoComQueryParams
{
	public string InstrumentName { get; init; }
	public string TimeFrame { get; init; }
	public int? Count { get; init; }
	public long? StartTime { get; init; }
	public long? EndTime { get; init; }

	public void Write(CryptoComQueryBuilder query)
	{
		query.Add("instrument_name", InstrumentName);
		query.Add("timeframe", TimeFrame);
		query.Add("count", Count);
		query.Add("start_ts", StartTime);
		query.Add("end_ts", EndTime);
	}
}
