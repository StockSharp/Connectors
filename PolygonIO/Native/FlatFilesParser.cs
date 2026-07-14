namespace StockSharp.PolygonIO.Native;

using System.Runtime.CompilerServices;
using System.Text;

using Ecng.IO;

static class FlatFilesParser
{
	private static async ValueTask<FastCsvReader> CreateCsv(Stream stream, CancellationToken cancellationToken)
	{
		var sr = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1 << 16, leaveOpen: true);
		var reader = new FastCsvReader(sr, StringHelper.N) { ColumnSeparator = ',' };
		
		if (!await reader.NextLineAsync(cancellationToken))
			return null;

		return reader;
	}

	// Minute aggregates
	public static async IAsyncEnumerable<TimeFrameCandleMessage> ParseStocksMinuteAggregates(this Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var csv = await CreateCsv(stream, cancellationToken);
		if (csv is null) yield break;

		while (await csv.NextLineAsync(cancellationToken))
		{
			var code = csv.ReadString();
			var secId = new SecurityId { SecurityCode = code, BoardCode = BoardCodes.StockSharp };

			var vol = csv.ReadDecimal();
			var open = csv.ReadDecimal();
			var close = csv.ReadDecimal();
			var high = csv.ReadDecimal();
			var low = csv.ReadDecimal();
			var openTime = TimeHelper.GregorianStart.AddNanoseconds(csv.ReadLong());
			var trans = csv.ReadInt();

			yield return new TimeFrameCandleMessage
			{
				SecurityId = secId,
				DataType = DataType.CandleTimeFrame,
				OpenTime = openTime,
				OpenPrice = open,
				HighPrice = high,
				LowPrice = low,
				ClosePrice = close,
				TotalVolume = vol,
				TotalTicks = trans,
				State = CandleStates.Finished,
			};
		}
	}

	public static async IAsyncEnumerable<TimeFrameCandleMessage> ParseIndicesMinuteAggregates(this Stream stream, [EnumeratorCancellation]CancellationToken cancellationToken)
	{
		var csv = await CreateCsv(stream, cancellationToken);
		if (csv is null) yield break;

		while (await csv.NextLineAsync(cancellationToken))
		{
			var code = csv.ReadString();
			var secId = new SecurityId { SecurityCode = code, BoardCode = BoardCodes.StockSharp };

			var open = csv.ReadDecimal();
			var close = csv.ReadDecimal();
			var high = csv.ReadDecimal();
			var low = csv.ReadDecimal();
			var openTime = TimeHelper.GregorianStart.AddNanoseconds(csv.ReadLong());

			yield return new TimeFrameCandleMessage
			{
				SecurityId = secId,
				DataType = DataType.CandleTimeFrame,
				OpenTime = openTime,
				OpenPrice = open,
				HighPrice = high,
				LowPrice = low,
				ClosePrice = close,
				State = CandleStates.Finished,
			};
		}
	}

	public static IAsyncEnumerable<TimeFrameCandleMessage> ParseForexMinuteAggregates(this Stream stream, CancellationToken cancellationToken)
		=> ParseStocksMinuteAggregates(stream, cancellationToken);

	public static IAsyncEnumerable<TimeFrameCandleMessage> ParseCryptoMinuteAggregates(this Stream stream, CancellationToken cancellationToken)
		=> ParseStocksMinuteAggregates(stream, cancellationToken);

	// Daily aggregates (1 Day)
	public static IAsyncEnumerable<TimeFrameCandleMessage> ParseStocksDailyAggregates(this Stream stream, CancellationToken cancellationToken)
		=> ParseStocksMinuteAggregates(stream, cancellationToken);

	public static IAsyncEnumerable<TimeFrameCandleMessage> ParseIndicesDailyAggregates(this Stream stream, CancellationToken cancellationToken)
		=> ParseIndicesMinuteAggregates(stream, cancellationToken);

	public static IAsyncEnumerable<TimeFrameCandleMessage> ParseForexDailyAggregates(this Stream stream, CancellationToken cancellationToken)
		=> ParseStocksMinuteAggregates(stream, cancellationToken);

	public static IAsyncEnumerable<TimeFrameCandleMessage> ParseCryptoDailyAggregates(this Stream stream, CancellationToken cancellationToken)
		=> ParseStocksMinuteAggregates(stream, cancellationToken);

	// Ticks / values
	public static async IAsyncEnumerable<ExecutionMessage> ParseStocksTrades(this Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var csv = await CreateCsv(stream, cancellationToken);
		if (csv is null) yield break;

		while (await csv.NextLineAsync(cancellationToken))
		{
			var code = csv.ReadString();
			var secId = new SecurityId { SecurityCode = code, BoardCode = BoardCodes.StockSharp };

			csv.Skip(3); // conditions, correction, exchange
			var tradeId = csv.ReadLong();
			var partTs = TimeHelper.GregorianStart.AddNanoseconds(csv.ReadLong());
			var price = csv.ReadDecimal();
			var seq = csv.ReadLong();
			csv.Skip(); // sip_timestamp
			var size = csv.ReadDecimal();

			yield return new ExecutionMessage
			{
				SecurityId = secId,
				DataTypeEx = DataType.Ticks,
				TradeId = tradeId,
				ServerTime = partTs,
				TradePrice = price,
				TradeVolume = size,
				SeqNum = seq,
			};
		}
	}

	public static IAsyncEnumerable<ExecutionMessage> ParseCryptoTrades(this Stream stream, CancellationToken cancellationToken)
		=> ParseStocksTrades(stream, cancellationToken);

	public static async IAsyncEnumerable<ExecutionMessage> ParseIndicesValues(this Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var csv = await CreateCsv(stream, cancellationToken);
		if (csv is null) yield break;

		while (await csv.NextLineAsync(cancellationToken))
		{
			var code = csv.ReadString();
			var secId = new SecurityId { SecurityCode = code, BoardCode = BoardCodes.StockSharp };

			var price = csv.ReadDecimal();
			var ts = TimeHelper.GregorianStart.AddNanoseconds(csv.ReadLong());

			yield return new ExecutionMessage
			{
				SecurityId = secId,
				DataTypeEx = DataType.Ticks,
				TradePrice = price,
				ServerTime = ts,
			};
		}
	}

	// Quotes (Level1)
	public static async IAsyncEnumerable<Level1ChangeMessage> ParseStocksQuotes(this Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var csv = await CreateCsv(stream, cancellationToken);
		if (csv is null) yield break;

		while (await csv.NextLineAsync(cancellationToken))
		{
			var code = csv.ReadString();
			var secId = new SecurityId { SecurityCode = code, BoardCode = BoardCodes.StockSharp };

			var bidPrice = csv.ReadDecimal().DefaultAsNull();
			var bidSize = csv.ReadDecimal().DefaultAsNull();
			var askPrice = csv.ReadDecimal().DefaultAsNull();
			var askSize = csv.ReadDecimal().DefaultAsNull();
			var ts = TimeHelper.GregorianStart.AddNanoseconds(csv.ReadLong());
			var seq = csv.ReadLong();

			var msg = new Level1ChangeMessage
			{
				SecurityId = secId,
				ServerTime = ts,
				SeqNum = seq,
			};

			msg
				.TryAdd(Level1Fields.BestBidPrice, bidPrice)
				.TryAdd(Level1Fields.BestBidVolume, bidSize)
				.TryAdd(Level1Fields.BestAskPrice, askPrice)
				.TryAdd(Level1Fields.BestAskVolume, askSize);

			yield return msg;
		}
	}

	public static IAsyncEnumerable<Level1ChangeMessage> ParseForexQuotes(this Stream stream, CancellationToken cancellationToken)
		=> ParseStocksQuotes(stream, cancellationToken);

	public static IAsyncEnumerable<Level1ChangeMessage> ParseCryptoQuotes(this Stream stream, CancellationToken cancellationToken)
		=> ParseStocksQuotes(stream, cancellationToken);
}