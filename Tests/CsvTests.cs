namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.IO;
using Ecng.Serialization;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Algo.Import;
using StockSharp.CSV;
using StockSharp.Messages;

/// <summary>
/// Tests for the Csv connector.
/// </summary>
[TestClass]
public class CsvTests : HistoryTestBase
{
	private static bool OffersMarketData(CSVMessageAdapter adapter)
		=> adapter.PossibleSupportedMessages.Any(m => m.Type == MessageTypes.MarketData);

	/// <summary>
	/// What the adapter can be asked for follows the import settings it is given: with files to read
	/// it offers market data, with none it does not.
	/// </summary>
	[TestMethod]
	public void MarketDataIsOfferedOnlyWhileThereAreImportSettings()
	{
		var adapter = new CSVMessageAdapter(new IncrementalIdGenerator());

		adapter.Settings = [new ImportSettings { DataType = DataType.Ticks }];
		IsTrue(OffersMarketData(adapter));

		adapter.Settings = [];
		IsFalse(OffersMarketData(adapter));
	}

	private static ImportSettings CreateSettings(DataType dataType, string fileName)
	{
		var settings = new ImportSettings { DataType = dataType, FileName = fileName };
		return settings;
	}

	private static IMessageAdapter CreateCsvAdapter(MemoryFileSystem fs, params ImportSettings[] settings)
	{
		var adapter = CreateAdapter<CSVMessageAdapter>(a =>
		{
			a.FileSystem = fs;
			a.Settings = settings;
		});
		return adapter;
	}

	private static void WriteFile(MemoryFileSystem fs, string path, string content)
	{
		var dir = Path.GetDirectoryName(path);
		if (!dir.IsEmpty())
			fs.CreateDirectory(dir);

		fs.WriteAllText(path, content);
	}

	#region Securities

	[TestMethod]
	[Timeout(30000, CooperativeCancellation = true)]
	public async Task CsvSecurityLookup()
	{
		var fs = new MemoryFileSystem();
		const string file = @"c:\data\securities.csv";

		WriteFile(fs, file, "AAPL,NASDAQ\r\nGOOG,NASDAQ\r\nMSFT,NYSE\r\n");

		var settings = CreateSettings(DataType.Securities, file);
		var adapter = CreateCsvAdapter(fs, settings);

		var counter = 0;
		await foreach (var msg in DownloadSecurities(adapter, new()).WithCancellation(CancellationToken))
		{
			IsNotNull(msg.SecurityId.SecurityCode);
			IsNotNull(msg.SecurityId.BoardCode);
			counter++;
		}

		AreEqual(3, counter);
	}

	[TestMethod]
	[Timeout(30000, CooperativeCancellation = true)]
	public async Task CsvSecurityLookupFiltered()
	{
		var fs = new MemoryFileSystem();
		const string file = @"c:\data\securities.csv";

		WriteFile(fs, file, "AAPL,NASDAQ\r\nGOOG,NASDAQ\r\nMSFT,NYSE\r\n");

		var settings = CreateSettings(DataType.Securities, file);
		var adapter = CreateCsvAdapter(fs, settings);

		var counter = 0;
		await foreach (var msg in DownloadSecurities(adapter, new() { SecurityId = new() { SecurityCode = "AAPL" } }).WithCancellation(CancellationToken))
		{
			AreEqual("AAPL", msg.SecurityId.SecurityCode);
			counter++;
		}

		AreEqual(1, counter);
	}

	[TestMethod]
	[Timeout(30000, CooperativeCancellation = true)]
	public async Task CsvSecurityLookupEmpty()
	{
		var fs = new MemoryFileSystem();
		const string file = @"c:\data\securities.csv";

		WriteFile(fs, file, "");

		var settings = CreateSettings(DataType.Securities, file);
		var adapter = CreateCsvAdapter(fs, settings);

		var counter = 0;
		await foreach (var _ in DownloadSecurities(adapter, new()).WithCancellation(CancellationToken))
			counter++;

		AreEqual(0, counter);
	}

	#endregion

	#region Ticks

	[TestMethod]
	[Timeout(30000, CooperativeCancellation = true)]
	public async Task CsvTicks()
	{
		var fs = new MemoryFileSystem();
		const string file = @"c:\data\ticks.csv";

		WriteFile(fs, file,
			"AAPL,NASDAQ,20240101,150.5,100\r\n" +
			"AAPL,NASDAQ,20240102,151.0,200\r\n" +
			"AAPL,NASDAQ,20240103,149.5,150\r\n");

		var settings = CreateSettings(DataType.Ticks, file);
		var adapter = CreateCsvAdapter(fs, settings);

		var secId = new SecurityId { SecurityCode = "AAPL", BoardCode = "NASDAQ" };
		var from = new DateTime(2024, 1, 1);
		var to = new DateTime(2024, 1, 3);

		var lastTime = ResetTimeOrder();
		var counter = 0;
		await foreach (var msg in DownloadTicks(adapter, secId, from, to).WithCancellation(CancellationToken))
		{
			Validate(msg, secId, from, to, ref lastTime);
			counter++;
		}

		AreEqual(3, counter);
	}

	[TestMethod]
	[Timeout(30000, CooperativeCancellation = true)]
	public async Task CsvTicksDateFilter()
	{
		var fs = new MemoryFileSystem();
		const string file = @"c:\data\ticks.csv";

		WriteFile(fs, file,
			"AAPL,NASDAQ,20240101,150.5,100\r\n" +
			"AAPL,NASDAQ,20240102,151.0,200\r\n" +
			"AAPL,NASDAQ,20240103,149.5,150\r\n" +
			"AAPL,NASDAQ,20240104,152.0,300\r\n" +
			"AAPL,NASDAQ,20240105,153.0,250\r\n");

		var settings = CreateSettings(DataType.Ticks, file);
		var adapter = CreateCsvAdapter(fs, settings);

		var secId = new SecurityId { SecurityCode = "AAPL", BoardCode = "NASDAQ" };
		var from = new DateTime(2024, 1, 2);
		var to = new DateTime(2024, 1, 4);

		var lastTime = ResetTimeOrder();
		var counter = 0;
		await foreach (var msg in DownloadTicks(adapter, secId, from, to).WithCancellation(CancellationToken))
		{
			Validate(msg, secId, from, to, ref lastTime);
			counter++;
		}

		AreEqual(3, counter);
	}

	[TestMethod]
	[Timeout(30000, CooperativeCancellation = true)]
	public async Task CsvTicksSecurityFilter()
	{
		var fs = new MemoryFileSystem();
		const string file = @"c:\data\ticks.csv";

		WriteFile(fs, file,
			"AAPL,NASDAQ,20240101,150.5,100\r\n" +
			"GOOG,NASDAQ,20240101,140.0,50\r\n" +
			"AAPL,NASDAQ,20240102,151.0,200\r\n" +
			"GOOG,NASDAQ,20240102,141.0,60\r\n");

		var settings = CreateSettings(DataType.Ticks, file);
		var adapter = CreateCsvAdapter(fs, settings);

		var secId = new SecurityId { SecurityCode = "AAPL", BoardCode = "NASDAQ" };
		var from = new DateTime(2024, 1, 1);
		var to = new DateTime(2024, 1, 2);

		var counter = 0;
		await foreach (var msg in DownloadTicks(adapter, secId, from, to).WithCancellation(CancellationToken))
		{
			AreEqual(secId, msg.SecurityId);
			counter++;
		}

		AreEqual(2, counter);
	}

	[TestMethod]
	[Timeout(30000, CooperativeCancellation = true)]
	public async Task CsvTicksWithCount()
	{
		var fs = new MemoryFileSystem();
		const string file = @"c:\data\ticks.csv";

		WriteFile(fs, file,
			"AAPL,NASDAQ,20240101,150.5,100\r\n" +
			"AAPL,NASDAQ,20240102,151.0,200\r\n" +
			"AAPL,NASDAQ,20240103,149.5,150\r\n" +
			"AAPL,NASDAQ,20240104,152.0,300\r\n" +
			"AAPL,NASDAQ,20240105,153.0,250\r\n");

		var settings = CreateSettings(DataType.Ticks, file);
		var adapter = CreateCsvAdapter(fs, settings);

		var secId = new SecurityId { SecurityCode = "AAPL", BoardCode = "NASDAQ" };
		var from = new DateTime(2024, 1, 1);
		var to = new DateTime(2024, 1, 5);

		var counter = 0;
		await foreach (var msg in DownloadTicks(adapter, secId, from, to, maxCount: 2).WithCancellation(CancellationToken))
		{
			IsTrue(msg.TradePrice > 0);
			counter++;
		}

		AreEqual(2, counter);
	}

	#endregion

	#region Candles

	[TestMethod]
	[Timeout(30000, CooperativeCancellation = true)]
	public async Task CsvCandles()
	{
		var fs = new MemoryFileSystem();
		const string file = @"c:\data\candles.csv";

		WriteFile(fs, file,
			"AAPL,NASDAQ,20240101,150.0,155.0,148.0,153.0,1000\r\n" +
			"AAPL,NASDAQ,20240102,153.0,157.0,151.0,155.0,1200\r\n" +
			"AAPL,NASDAQ,20240103,155.0,158.0,152.0,156.0,900\r\n");

		var settings = CreateSettings(TimeSpan.FromDays(1).TimeFrame(), file);
		var adapter = CreateCsvAdapter(fs, settings);

		var secId = new SecurityId { SecurityCode = "AAPL", BoardCode = "NASDAQ" };
		var from = new DateTime(2024, 1, 1);
		var to = new DateTime(2024, 1, 3);

		var lastTime = ResetTimeOrder();
		var counter = 0;
		await foreach (var msg in DownloadCandles(adapter, secId, TimeSpan.FromDays(1), from, to).WithCancellation(CancellationToken))
		{
			Validate(msg, secId, from, to, ref lastTime);
			IsTrue(msg.HighPrice >= msg.LowPrice);
			IsTrue(msg.HighPrice >= msg.OpenPrice);
			IsTrue(msg.HighPrice >= msg.ClosePrice);
			IsTrue(msg.LowPrice <= msg.OpenPrice);
			IsTrue(msg.LowPrice <= msg.ClosePrice);
			IsTrue(msg.TotalVolume >= 0);
			counter++;
		}

		AreEqual(3, counter);
	}

	[TestMethod]
	[Timeout(30000, CooperativeCancellation = true)]
	public async Task CsvCandlesDateFilter()
	{
		var fs = new MemoryFileSystem();
		const string file = @"c:\data\candles.csv";

		WriteFile(fs, file,
			"AAPL,NASDAQ,20240101,150.0,155.0,148.0,153.0,1000\r\n" +
			"AAPL,NASDAQ,20240102,153.0,157.0,151.0,155.0,1200\r\n" +
			"AAPL,NASDAQ,20240103,155.0,158.0,152.0,156.0,900\r\n" +
			"AAPL,NASDAQ,20240104,156.0,160.0,154.0,159.0,1100\r\n");

		var settings = CreateSettings(TimeSpan.FromDays(1).TimeFrame(), file);
		var adapter = CreateCsvAdapter(fs, settings);

		var secId = new SecurityId { SecurityCode = "AAPL", BoardCode = "NASDAQ" };
		var from = new DateTime(2024, 1, 2);
		var to = new DateTime(2024, 1, 3);

		var lastTime = ResetTimeOrder();
		var counter = 0;
		await foreach (var msg in DownloadCandles(adapter, secId, TimeSpan.FromDays(1), from, to).WithCancellation(CancellationToken))
		{
			Validate(msg, secId, from, to, ref lastTime);
			counter++;
		}

		AreEqual(2, counter);
	}

	#endregion

	#region Level1

	[TestMethod]
	[Timeout(30000, CooperativeCancellation = true)]
	public async Task CsvLevel1()
	{
		var fs = new MemoryFileSystem();
		const string file = @"c:\data\level1.csv";

		// required: SecurityCode, BoardCode, Date
		// then Level1Fields in order of the enum
		// BestBidPrice is the first non-obsolete field
		WriteFile(fs, file,
			"AAPL,NASDAQ,20240101\r\n" +
			"AAPL,NASDAQ,20240102\r\n");

		var settings = CreateSettings(DataType.Level1, file);
		var adapter = CreateCsvAdapter(fs, settings);

		var secId = new SecurityId { SecurityCode = "AAPL", BoardCode = "NASDAQ" };
		var from = new DateTime(2024, 1, 1);
		var to = new DateTime(2024, 1, 2);

		var counter = 0;
		await foreach (var msg in DownloadLevel1(adapter, secId, from, to).WithCancellation(CancellationToken))
		{
			AreEqual(secId, msg.SecurityId);
			counter++;
		}

		AreEqual(2, counter);
	}

	#endregion

	#region Settings

	[TestMethod]
	public void CsvSettingsSecuritySupport()
	{
		var adapter = new CSVMessageAdapter(new IncrementalIdGenerator());

		adapter.Settings =
		[
			new ImportSettings { DataType = DataType.Securities, FileName = @"c:\test.csv" }
		];

		IsTrue(adapter.IsAllDownloadingSupported(DataType.Securities));
	}

	[TestMethod]
	public void CsvSettingsNoSecuritySupport()
	{
		var adapter = new CSVMessageAdapter(new IncrementalIdGenerator());

		adapter.Settings =
		[
			new ImportSettings { DataType = DataType.Ticks, FileName = @"c:\test.csv" }
		];

		IsFalse(adapter.IsAllDownloadingSupported(DataType.Securities));
	}

	/// <summary>
	/// A file of positions or of transactions holds every one there is. A connection asks for them on connect without
	/// naming a portfolio or an order, and a basket hands such a request only to an adapter that downloads them all.
	/// </summary>
	[TestMethod]
	public void EveryPositionAndTransactionIsDownloadedWhileThereIsAFileOfThem()
	{
		var adapter = new CSVMessageAdapter(new IncrementalIdGenerator());

		IsFalse(adapter.IsAllDownloadingSupported(DataType.PositionChanges));
		IsFalse(adapter.IsAllDownloadingSupported(DataType.Transactions));

		adapter.Settings = [new ImportSettings { DataType = DataType.PositionChanges }, new ImportSettings { DataType = DataType.Transactions }];

		IsTrue(adapter.IsAllDownloadingSupported(DataType.PositionChanges));
		IsTrue(adapter.IsAllDownloadingSupported(DataType.Transactions));
		IsFalse(adapter.IsAllDownloadingSupported(DataType.Securities));
	}

	[TestMethod]
	public async Task CsvSettingsSaveLoad()
	{
		var adapter = new CSVMessageAdapter(new IncrementalIdGenerator());

		adapter.Settings =
		[
			new ImportSettings { DataType = DataType.Securities, FileName = @"c:\sec.csv" },
			new ImportSettings { DataType = DataType.Ticks, FileName = @"c:\ticks.csv" },
		];

		var storage = new SettingsStorage();
		await adapter.SaveAsync(storage, CancellationToken);

		var adapter2 = new CSVMessageAdapter(new IncrementalIdGenerator());
		await adapter2.LoadAsync(storage, CancellationToken);

		AreEqual(2, adapter2.Settings.Count());
	}

	[TestMethod]
	public void CsvSettingsNull()
	{
		var adapter = new CSVMessageAdapter(new IncrementalIdGenerator());

		ThrowsExactly<ArgumentNullException>(() => adapter.Settings = null);
	}

	#endregion

	#region Transactions

	[TestMethod]
	[Timeout(30000, CooperativeCancellation = true)]
	public async Task CsvTransactions()
	{
		var fs = new MemoryFileSystem();
		const string file = @"c:\data\transactions.csv";

		// Transactions required fields (in order):
		// SecurityCode, BoardCode, Date, PortfolioName, OrderPrice, OrderVolume, Balance, OrderType, OrderState, TradeId, TradePrice, TradeVolume
		WriteFile(fs, file,
			"AAPL,NASDAQ,20240101,Portfolio1,150.5,10,0,Limit,Done,1001,150.5,10\r\n" +
			"AAPL,NASDAQ,20240102,Portfolio1,151.0,20,5,Limit,Active,1002,151.0,15\r\n" +
			"GOOG,NASDAQ,20240102,Portfolio1,140.0,30,0,Limit,Done,1003,140.0,30\r\n");

		var settings = CreateSettings(DataType.Transactions, file);
		var adapter = CreateCsvAdapter(fs, settings);

		var counter = 0;
		await foreach (var msg in Download<ExecutionMessage>(adapter, new OrderStatusMessage { IsSubscribe = true, To = DateTime.MaxValue }).WithCancellation(CancellationToken))
		{
			IsNotNull(msg.SecurityId.SecurityCode);
			counter++;
		}

		AreEqual(3, counter);
	}

	[TestMethod]
	[Timeout(30000, CooperativeCancellation = true)]
	public async Task CsvTransactionsSecurityFilter()
	{
		var fs = new MemoryFileSystem();
		const string file = @"c:\data\transactions.csv";

		WriteFile(fs, file,
			"AAPL,NASDAQ,20240101,Portfolio1,150.5,10,0,Limit,Done,1001,150.5,10\r\n" +
			"GOOG,NASDAQ,20240101,Portfolio1,140.0,30,0,Limit,Done,1002,140.0,30\r\n" +
			"AAPL,NASDAQ,20240102,Portfolio1,151.0,20,5,Limit,Active,1003,151.0,15\r\n");

		var settings = CreateSettings(DataType.Transactions, file);
		var adapter = CreateCsvAdapter(fs, settings);

		var secId = new SecurityId { SecurityCode = "AAPL", BoardCode = "NASDAQ" };

		var counter = 0;
		await foreach (var msg in Download<ExecutionMessage>(adapter, new OrderStatusMessage
		{
			IsSubscribe = true,
			SecurityId = secId,
			To = DateTime.MaxValue,
		}).WithCancellation(CancellationToken))
		{
			AreEqual(secId, msg.SecurityId);
			counter++;
		}

		AreEqual(2, counter);
	}

	#endregion

	#region Portfolios

	[TestMethod]
	[Timeout(30000, CooperativeCancellation = true)]
	public async Task CsvPortfolios()
	{
		var fs = new MemoryFileSystem();
		const string file = @"c:\data\positions.csv";

		// PositionChanges required fields: SecurityCode, BoardCode, PortfolioName, Date
		WriteFile(fs, file,
			"AAPL,NASDAQ,Portfolio1,20240101\r\n" +
			"GOOG,NASDAQ,Portfolio1,20240102\r\n" +
			"AAPL,NASDAQ,Portfolio2,20240102\r\n");

		var settings = CreateSettings(DataType.PositionChanges, file);
		var adapter = CreateCsvAdapter(fs, settings);

		var messages = new List<Message>();

		await adapter.ConnectAsync(CancellationToken);

		await foreach (var msg in adapter.SubscribeAsync<Message>(new PortfolioLookupMessage { IsSubscribe = true, To = DateTime.MaxValue }).WithCancellation(CancellationToken))
			messages.Add(msg);

		// should get PortfolioMessage for each unique portfolio + PositionChangeMessage for each row
		var portfolioMessages = messages.OfType<PortfolioMessage>().ToList();
		var positionMessages = messages.OfType<PositionChangeMessage>().ToList();

		AreEqual(2, portfolioMessages.Count); // Portfolio1, Portfolio2
		AreEqual(3, positionMessages.Count);  // 3 rows
	}

	[TestMethod]
	[Timeout(30000, CooperativeCancellation = true)]
	public async Task CsvPortfoliosDateFilter()
	{
		var fs = new MemoryFileSystem();
		const string file = @"c:\data\positions.csv";

		WriteFile(fs, file,
			"AAPL,NASDAQ,Portfolio1,20240101\r\n" +
			"AAPL,NASDAQ,Portfolio1,20240102\r\n" +
			"AAPL,NASDAQ,Portfolio1,20240103\r\n" +
			"AAPL,NASDAQ,Portfolio1,20240104\r\n");

		var settings = CreateSettings(DataType.PositionChanges, file);
		var adapter = CreateCsvAdapter(fs, settings);

		var messages = new List<Message>();

		await adapter.ConnectAsync(CancellationToken);

		await foreach (var msg in adapter.SubscribeAsync<Message>(new PortfolioLookupMessage
		{
			IsSubscribe = true,
			From = new DateTime(2024, 1, 2).UtcKind(),
			To = new DateTime(2024, 1, 3).UtcKind(),
		}).WithCancellation(CancellationToken))
			messages.Add(msg);

		var positionMessages = messages.OfType<PositionChangeMessage>().ToList();

		// should only get positions for 01/02 and 01/03
		AreEqual(2, positionMessages.Count);
	}

	#endregion

	#region Multiple files

	[TestMethod]
	[Timeout(30000, CooperativeCancellation = true)]
	public async Task CsvTicksFromDirectory()
	{
		var fs = new MemoryFileSystem();

		// the directory is listed by path segments, and a backslash separates none off Windows
		var dir = Path.Combine("data", "ticks");

		fs.CreateDirectory(dir);

		WriteFile(fs, Path.Combine(dir, "file1.csv"),
			"AAPL,NASDAQ,20240101,150.5,100\r\n" +
			"AAPL,NASDAQ,20240102,151.0,200\r\n");

		WriteFile(fs, Path.Combine(dir, "file2.csv"),
			"AAPL,NASDAQ,20240103,149.5,150\r\n");

		var settings = new ImportSettings
		{
			DataType = DataType.Ticks,
			Directory = dir,
			FileMask = "*.csv",
		};
		var adapter = CreateCsvAdapter(fs, settings);

		var secId = new SecurityId { SecurityCode = "AAPL", BoardCode = "NASDAQ" };
		var from = new DateTime(2024, 1, 1);
		var to = new DateTime(2024, 1, 3);

		var counter = 0;
		await foreach (var msg in DownloadTicks(adapter, secId, from, to).WithCancellation(CancellationToken))
		{
			IsTrue(msg.TradePrice > 0);
			counter++;
		}

		AreEqual(3, counter);
	}

	#endregion

	#region Separator

	[TestMethod]
	[Timeout(30000, CooperativeCancellation = true)]
	public async Task CsvTicksSemicolonSeparator()
	{
		var fs = new MemoryFileSystem();
		const string file = @"c:\data\ticks.csv";

		WriteFile(fs, file,
			"AAPL;NASDAQ;20240101;150.5;100\r\n" +
			"AAPL;NASDAQ;20240102;151.0;200\r\n");

		var settings = CreateSettings(DataType.Ticks, file);
		settings.ColumnSeparator = ";";
		var adapter = CreateCsvAdapter(fs, settings);

		var secId = new SecurityId { SecurityCode = "AAPL", BoardCode = "NASDAQ" };
		var from = new DateTime(2024, 1, 1);
		var to = new DateTime(2024, 1, 2);

		var lastTime = ResetTimeOrder();
		var counter = 0;
		await foreach (var msg in DownloadTicks(adapter, secId, from, to).WithCancellation(CancellationToken))
		{
			Validate(msg, secId, from, to, ref lastTime);
			counter++;
		}

		AreEqual(2, counter);
	}

	#endregion

	#region SkipHeader

	[TestMethod]
	[Timeout(30000, CooperativeCancellation = true)]
	public async Task CsvTicksSkipHeader()
	{
		var fs = new MemoryFileSystem();
		const string file = @"c:\data\ticks.csv";

		WriteFile(fs, file,
			"SecurityCode,BoardCode,Date,Price,Volume\r\n" +
			"AAPL,NASDAQ,20240101,150.5,100\r\n" +
			"AAPL,NASDAQ,20240102,151.0,200\r\n");

		var settings = CreateSettings(DataType.Ticks, file);
		settings.SkipFromHeader = 1;
		var adapter = CreateCsvAdapter(fs, settings);

		var secId = new SecurityId { SecurityCode = "AAPL", BoardCode = "NASDAQ" };
		var from = new DateTime(2024, 1, 1);
		var to = new DateTime(2024, 1, 2);

		var counter = 0;
		await foreach (var msg in DownloadTicks(adapter, secId, from, to).WithCancellation(CancellationToken))
		{
			IsTrue(msg.TradePrice > 0);
			counter++;
		}

		AreEqual(2, counter);
	}

	#endregion

	#region SupportedDataTypes

	[TestMethod]
	[Timeout(30000, CooperativeCancellation = true)]
	public async Task CsvGetSupportedDataTypes()
	{
		var adapter = new CSVMessageAdapter(new IncrementalIdGenerator());

		adapter.Settings =
		[
			new ImportSettings { DataType = DataType.Ticks, FileName = @"c:\test.csv" },
			new ImportSettings { DataType = DataType.Securities, FileName = @"c:\sec.csv" },
		];

		var types = new List<DataType>();
		await foreach (var dt in adapter.GetSupportedMarketDataTypesAsync(default, default, default).WithCancellation(CancellationToken))
			types.Add(dt);

		AreEqual(2, types.Count);
		IsTrue(types.Contains(DataType.Ticks));
		IsTrue(types.Contains(DataType.Securities));
	}

	#endregion
}
