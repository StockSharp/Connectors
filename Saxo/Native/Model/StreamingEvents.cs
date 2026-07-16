namespace StockSharp.Saxo.Native.Model;

sealed class SaxoPriceEvent(long transactionId, SecurityId securityId, DataType dataType, SaxoInstrument instrument,
	SaxoInfoPrice data, DateTime? timestamp)
{
	public long TransactionId { get; } = transactionId;
	public SecurityId SecurityId { get; } = securityId;
	public DataType DataType { get; } = dataType;
	public SaxoInstrument Instrument { get; } = instrument;
	public SaxoInfoPrice Data { get; } = data;
	public DateTime? Timestamp { get; } = timestamp;
}

sealed class SaxoCandleEvent(long transactionId, SecurityId securityId, TimeSpan timeFrame, SaxoChartSample data,
	CandleStates state)
{
	public long TransactionId { get; } = transactionId;
	public SecurityId SecurityId { get; } = securityId;
	public TimeSpan TimeFrame { get; } = timeFrame;
	public SaxoChartSample Data { get; } = data;
	public CandleStates State { get; } = state;
}

sealed class SaxoPriceRegistration
{
	public long TransactionId { get; set; }
	public SecurityId SecurityId { get; set; }
	public DataType DataType { get; set; }
	public SaxoInstrument Instrument { get; set; }
	public string ReferenceId { get; set; }
	public SaxoInfoPrice LastPrice { get; set; }
}

sealed class SaxoCandleRegistration
{
	public long TransactionId { get; set; }
	public SecurityId SecurityId { get; set; }
	public TimeSpan TimeFrame { get; set; }
	public SaxoInstrument Instrument { get; set; }
	public string ReferenceId { get; set; }
	public SortedDictionary<DateTime, SaxoChartSample> Samples { get; } = [];
}
