namespace StockSharp.RakutenRss.Native;

[SupportedOSPlatform("windows")]
sealed class RakutenRssExcelSession : IDisposable
{
	private sealed class SheetHandle
	{
		public object Sheet { get; set; }
		public int Columns { get; set; }
		public int Rows { get; set; }
		public bool IsDerivative { get; set; }
	}

	private sealed class Grid
	{
		private readonly object[,] _values;

		public Grid(object[,] values) => _values = values;
		public int Rows => _values.GetLength(0);
		public int Columns => _values.GetLength(1);
		public object this[int row, int column] => _values[row, column];
	}

	private readonly Dictionary<long, SheetHandle> _feeds = [];
	private object _application;
	private object _workbook;
	private object _commandSheet;
	private SheetHandle _orders;
	private SheetHandle _derivativeOrders;
	private SheetHandle _executions;
	private SheetHandle _derivativeExecutions;
	private SheetHandle _positions;
	private SheetHandle _marginPositions;
	private SheetHandle _derivativePositions;
	private SheetHandle _capacity;
	private SheetHandle _derivativeCapacity;
	private SheetHandle _orderIds;
	private int _maxRows;
	private int _commandRow;
	private long _feedId;
	private bool _disposed;

	public void Open(bool visible, int maxRows)
	{
		if (_application != null)
			throw new InvalidOperationException("The Excel RSS session is already open.");
		var type = Type.GetTypeFromProgID("Excel.Application", false)
			?? throw new InvalidOperationException(
				"Microsoft Excel is not installed. MARKETSPEED II RSS requires desktop Excel on Windows.");
		_application = Activator.CreateInstance(type)
			?? throw new InvalidOperationException("Cannot start Microsoft Excel.");
		Set(_application, "Visible", visible);
		Set(_application, "DisplayAlerts", false);
		Set(_application, "ScreenUpdating", visible);
		var workbooks = Get(_application, "Workbooks");
		try
		{
			_workbook = Invoke(workbooks, "Add");
		}
		finally
		{
			Release(workbooks);
		}
		_maxRows = Math.Clamp(maxRows, 100, 10_000);
		_commandSheet = AddSheet("Commands");
		_orders = CreateTable("Orders", "RssOrderList",
			["注文番号", "通常注文状況", "銘柄コード", "銘柄名称", "市場名称", "発注/受注日時",
			 "売買", "取引", "執行条件", "注文数量", "約定数量", "注文単価", "注文区分", "注文失効理由"],
			[0, 0, null, "A", 0, 0, 0, 0, 0]);
		_derivativeOrders = CreateTable("FopOrders", "RssFOPOrderList",
			["注文番号", "通常注文状況", "銘柄コード", "銘柄名称", "市場名称", "発注・受注日時",
			 "取引", "注文数量", "執行数量条件", "執行時間条件", "注文単価", "注文区分", "注文失効理由"],
			[0, 0, 0]);
		_executions = CreateTable("Executions", "RssExecutionList",
			["約定日", "銘柄コード", "銘柄名称", "市場名称", "売買", "約定数量", "約定単価", "約定代金"],
			[0, null, "A", 0, 0]);
		_derivativeExecutions = CreateTable("FopExecutions", "RssFOPExecutionList",
			["約定日", "銘柄コード", "銘柄名称", "市場名称", "取引", "約定数量", "約定単価", "約定代金"],
			[0, 0]);
		_positions = CreateTable("Positions", "RssPositionList",
			["銘柄コード", "銘柄名称", "保有数量", "発注数量", "平均取得価額", "時価", "評価損益額"],
			[null, "A"]);
		_marginPositions = CreateTable("MarginPositions", "RssMarginPositionList",
			["銘柄コード", "銘柄名称", "建市場", "売買", "建玉数量", "発注数量", "建値", "時価", "評価損益額"],
			[null, "A", 0, 0, null]);
		_derivativePositions = CreateTable("FopPositions", "RssFOPPositionList",
			["銘柄コード", "銘柄名称", "市場名称", "売買", "建玉数量", "発注数量", "建単価", "評価価格", "評価損益額"],
			[0, 0]);
		_capacity = CreateTable("Capacity", "RssCapacityList",
			["現物買付可能額", "信用口座_信用新規建余力", "信用口座_保証金率（新規建）"], []);
		_derivativeCapacity = CreateTable("FopCapacity", "RssFOPCapacityList",
			["証拠金維持率", "受入証拠金", "純資産"], []);
		_orderIds = CreateTable("OrderIds", "RssOrderIDList",
			["発注ID", "関数名", "発注日", "発注時刻", "注文番号", "発注結果"], []);
		Calculate();
		var probe = ExecuteScalar("RssOrderStatus", [int.MaxValue]);
		if (IsExcelError(probe) || ToInt(probe) != -1)
			throw new InvalidOperationException(
				"MARKETSPEED II RSS functions are unavailable. Log in to MARKETSPEED II and register its Excel add-in.");
	}

	public long CreateQuoteFeed(RakutenRssQuoteRequest request)
	{
		var sheet = AddSheet();
		var function = request.InstrumentKind == RakutenRssInstrumentKinds.Derivative
			? "RssFOPMarket" : "RssMarket";
		var bestAsk = request.InstrumentKind == RakutenRssInstrumentKinds.Derivative
			? "最良売気配値1" : "最良売気配値";
		var bestBid = request.InstrumentKind == RakutenRssInstrumentKinds.Derivative
			? "最良買気配値1" : "最良買気配値";
		var bestAskVolume = request.InstrumentKind == RakutenRssInstrumentKinds.Derivative
			? "最良売気配数量1" : "最良売気配数量";
		var bestBidVolume = request.InstrumentKind == RakutenRssInstrumentKinds.Derivative
			? "最良買気配数量1" : "最良買気配数量";
		var fields = new List<string>
		{
			"現在日付", "現在値詳細時刻", "現在値", "始値", "高値", "安値", "前日終値",
			"出来高", "売買代金", bestAsk, bestBid, bestAskVolume, bestBidVolume, "現在値フラグ",
		};
		for (var i = 1; i <= 10; i++) fields.Add($"最良売気配値{i}");
		for (var i = 1; i <= 10; i++) fields.Add($"最良売気配数量{i}");
		for (var i = 1; i <= 10; i++) fields.Add($"最良買気配値{i}");
		for (var i = 1; i <= 10; i++) fields.Add($"最良買気配数量{i}");
		for (var i = 0; i < fields.Count; i++)
			SetFormula(sheet, Address(1, i + 1), Formula(function,
				[request.SecurityCode, fields[i]]));
		var id = ++_feedId;
		_feeds.Add(id, new()
		{
			Sheet = sheet,
			Columns = fields.Count,
			Rows = 1,
			IsDerivative = request.InstrumentKind == RakutenRssInstrumentKinds.Derivative,
		});
		Calculate();
		return id;
	}

	public RakutenRssQuote ReadQuote(long feedId)
	{
		var handle = GetFeed(feedId);
		Calculate();
		var row = ReadGrid(handle.Sheet, $"A1:{Address(1, handle.Columns)}");
		var date = ToDate(row[0, 0]);
		var time = ToTime(row[0, 1]);
		var levels = new RakutenRssDepthLevel[10];
		for (var i = 0; i < levels.Length; i++)
		{
			var askPriceIndex = handle.IsDerivative ? 14 + i : i == 0 ? 9 : 13 + i;
			var askVolumeIndex = handle.IsDerivative ? 24 + i : i == 0 ? 11 : 23 + i;
			var bidPriceIndex = handle.IsDerivative ? 34 + i : i == 0 ? 10 : 33 + i;
			var bidVolumeIndex = handle.IsDerivative ? 44 + i : i == 0 ? 12 : 43 + i;
			levels[i] = new()
			{
				AskPrice = ToDecimal(row[0, askPriceIndex]),
				AskVolume = ToDecimal(row[0, askVolumeIndex]),
				BidPrice = ToDecimal(row[0, bidPriceIndex]),
				BidVolume = ToDecimal(row[0, bidVolumeIndex]),
			};
		}
		var lastPrice = ToDecimal(row[0, 2]);
		var bestAskPrice = ToDecimal(row[0, 9]);
		var bestBidPrice = ToDecimal(row[0, 10]);
		if (lastPrice == null && bestAskPrice == null && bestBidPrice == null)
			return null;
		return new()
		{
			Time = ToUtc(date, time),
			LastPrice = lastPrice,
			OpenPrice = ToDecimal(row[0, 3]),
			HighPrice = ToDecimal(row[0, 4]),
			LowPrice = ToDecimal(row[0, 5]),
			PreviousClose = ToDecimal(row[0, 6]),
			Volume = ToDecimal(row[0, 7]),
			Turnover = ToDecimal(row[0, 8]),
			BestAskPrice = bestAskPrice,
			BestBidPrice = bestBidPrice,
			BestAskVolume = ToDecimal(row[0, 11]),
			BestBidVolume = ToDecimal(row[0, 12]),
			State = ToText(row[0, 13]),
			Depth = levels,
		};
	}

	public long CreateTickFeed(RakutenRssTickRequest request)
	{
		var handle = CreateTable(null, "RssTickList", ["時刻", "出来高", "約定値"],
			[request.SecurityCode, Math.Clamp(request.Count, 1, 300)], request.Count);
		var id = ++_feedId;
		_feeds.Add(id, handle);
		Calculate();
		return id;
	}

	public RakutenRssTick[] ReadTicks(long feedId)
	{
		var handle = GetFeed(feedId);
		Calculate();
		var grid = ReadTable(handle);
		var result = new List<RakutenRssTick>();
		var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, JapanTimeZone).Date;
		for (var row = 0; row < grid.Rows; row++)
		{
			var time = ToTime(grid[row, 0]);
			var price = ToDecimal(grid[row, 2]);
			var volume = ToDecimal(grid[row, 1]);
			if (time == null || price is not > 0 || volume is not > 0)
				continue;
			result.Add(new()
			{
				Time = ToUtc(today, time),
				Price = price.Value,
				Volume = volume.Value,
			});
		}
		return result.ToArray();
	}

	public long CreateCandleFeed(RakutenRssCandleRequest request)
	{
		var isPast = request.From != null && request.TimeFrame is "D" or "W" or "M";
		var args = isPast
			? new object[] { request.SecurityCode, request.TimeFrame,
				request.From.Value.ToString("yyyyMMdd", CultureInfo.InvariantCulture), request.Count }
			: [request.SecurityCode, request.TimeFrame, request.Count];
		var handle = CreateTable(null, isPast ? "RssChartPast" : "RssChart",
			["日付", "時刻", "始値", "高値", "安値", "終値", "出来高"], args,
			Math.Clamp(request.Count, 1, 3000));
		var id = ++_feedId;
		_feeds.Add(id, handle);
		Calculate();
		return id;
	}

	public RakutenRssCandle[] ReadCandles(long feedId)
	{
		var handle = GetFeed(feedId);
		Calculate();
		var grid = ReadTable(handle);
		var result = new List<RakutenRssCandle>();
		for (var row = 0; row < grid.Rows; row++)
		{
			var date = ToDate(grid[row, 0]);
			var open = ToDecimal(grid[row, 2]);
			var high = ToDecimal(grid[row, 3]);
			var low = ToDecimal(grid[row, 4]);
			var close = ToDecimal(grid[row, 5]);
			if (date == null || open == null || high == null || low == null || close == null)
				continue;
			result.Add(new()
			{
				OpenTime = ToUtc(date, ToTime(grid[row, 1])),
				Open = open.Value,
				High = high.Value,
				Low = low.Value,
				Close = close.Value,
				Volume = ToDecimal(grid[row, 6]) ?? 0,
			});
		}
		return result.ToArray();
	}

	public RakutenRssSecurityInfo GetSecurity(string code, RakutenRssInstrumentKinds kind)
	{
		var function = kind == RakutenRssInstrumentKinds.Derivative ? "RssFOPMarket" : "RssMarket";
		var name = ExecuteScalar(function, [code, "銘柄名称"]);
		var market = ExecuteScalar(function, [code, "市場名称"]);
		var nativeCode = ExecuteScalar(function, [code, "銘柄コード"]);
		if (IsExcelError(name) || ToText(name).IsEmpty())
			return null;
		return new()
		{
			Code = ToText(nativeCode).IsEmpty(code.Split('.')[0]),
			Name = ToText(name),
			Market = ToText(market),
			InstrumentKind = kind,
		};
	}

	public RakutenRssOrderResult PlaceOrder(RakutenRssPlaceOrderRequest request)
	{
		var side = request.Side == Sides.Sell ? 1 : 3;
		var priceType = request.OrderType == OrderTypes.Market ? 0 : 1;
		object price = priceType == 0 ? null : request.Price;
		var valid = request.ValidTill?.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
		string function;
		object[] args;
		switch (request.Route)
		{
			case RakutenRssOrderRoutes.Cash:
				function = "RssStockOrder";
				args = [request.RequestId, 1, request.SecurityCode, side, 0, request.UseSor ? 1 : 0,
					request.Quantity, priceType, price, (int)request.Execution, valid,
					(int)request.AccountType, null, null, null, null, 0, null, null, null];
				break;
			case RakutenRssOrderRoutes.MarginOpen:
				function = "RssMarginOpenOrder";
				args = [request.RequestId, 1, request.SecurityCode, side, 0, request.UseSor ? 1 : 0,
					(int)request.MarginType, request.Quantity, priceType, price, (int)request.Execution,
					valid, (int)request.AccountType, null, null, null, null, 0, null, null, null, null];
				break;
			case RakutenRssOrderRoutes.MarginClose:
				if (request.OpenDate == null || request.OpenPrice is not > 0)
					throw new InvalidOperationException("Margin closing requires the opening date and price.");
				function = "RssMarginCloseOrder";
				args = [request.RequestId, 1, request.SecurityCode, side, 0, request.UseSor ? 1 : 0,
					(int)request.MarginType, request.Quantity, priceType, price, (int)request.Execution,
					valid, (int)request.AccountType,
					request.OpenDate.Value.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
					request.OpenPrice, ToMarketNumber(request.SecurityCode), null, null, null, null];
				break;
			case RakutenRssOrderRoutes.DerivativeOpen:
				function = "RssFOPOpenOrder";
				args = [request.RequestId, 1, request.SecurityCode, side, 0, request.Quantity,
					priceType, price, (int)request.FillCondition, (int)request.DerivativeTime, valid,
					null, null, null, null, request.UseSor ? 1 : 0];
				break;
			case RakutenRssOrderRoutes.DerivativeClose:
				if (request.OpenDate == null || request.OpenPrice is not > 0)
					throw new InvalidOperationException("Derivative closing requires the opening date and price.");
				function = "RssFOPCloseOrder";
				args = [request.RequestId, 1, request.SecurityCode, side, 0, request.Quantity,
					priceType, price, (int)request.FillCondition, (int)request.DerivativeTime, valid,
					request.OpenDate.Value.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
					request.OpenPrice, null, null, null, null, request.UseSor ? 1 : 0];
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(request.Route), request.Route, null);
		}
		return ExecuteOrder(request.RequestId, function, args);
	}

	public RakutenRssOrderResult ReplaceOrder(RakutenRssReplaceOrderRequest request)
	{
		var priceType = request.OrderType == OrderTypes.Market ? 0 : 1;
		var valid = request.ValidTill?.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
		return request.IsDerivative
			? ExecuteOrder(request.RequestId, "RssFOPModifyOrder",
				[request.RequestId, 1, request.OrderId, 0,
				 priceType == 0 ? null : request.Price, valid, null, null, null])
			: ExecuteOrder(request.RequestId, "RssModifyOrder",
				[request.RequestId, 1, request.OrderId, 0, request.Quantity, priceType,
				 priceType == 0 ? null : request.Price, (int)request.Execution, valid,
				 null, null, null, null, null, null, null, null]);
	}

	public RakutenRssOrderResult CancelOrder(RakutenRssCancelOrderRequest request)
		=> ExecuteOrder(request.RequestId,
			request.IsDerivative ? "RssFOPCancelOrder" : "RssCancelOrder",
			[request.RequestId, 1, request.OrderId]);

	public RakutenRssOrderIdRow[] ReadOrderIds()
	{
		Calculate();
		var grid = ReadTable(_orderIds);
		var result = new List<RakutenRssOrderIdRow>();
		for (var row = 0; row < grid.Rows; row++)
		{
			var requestId = ToInt(grid[row, 0]);
			if (requestId <= 0)
				continue;
			result.Add(new()
			{
				RequestId = requestId,
				Function = ToText(grid[row, 1]),
				Time = ToUtc(ToDate(grid[row, 2]), ToTime(grid[row, 3])),
				OrderId = ToText(grid[row, 4]),
				Result = ToText(grid[row, 5]),
			});
		}
		return result.ToArray();
	}

	public RakutenRssOrderRow[] ReadOrders()
		=> ParseOrders(_orders, false).Concat(ParseOrders(_derivativeOrders, true)).ToArray();

	public RakutenRssExecutionRow[] ReadExecutions()
		=> ParseExecutions(_executions, false).Concat(ParseExecutions(_derivativeExecutions, true)).ToArray();

	public RakutenRssPortfolioInfo ReadPortfolio()
	{
		Calculate();
		var capacity = ReadTable(_capacity);
		var derivativeCapacity = ReadTable(_derivativeCapacity);
		return new()
		{
			BuyingPower = capacity.Rows > 0 ? ToDecimal(capacity[0, 0]) : null,
			MarginAvailable = capacity.Rows > 0 ? ToDecimal(capacity[0, 1]) : null,
			MarginRatio = capacity.Rows > 0 ? ToDecimal(capacity[0, 2]) : null,
			DerivativeMargin = derivativeCapacity.Rows > 0 ? ToDecimal(derivativeCapacity[0, 1]) : null,
			DerivativeNetAsset = derivativeCapacity.Rows > 0 ? ToDecimal(derivativeCapacity[0, 2]) : null,
			Positions = ParsePositions().ToArray(),
		};
	}

	public void RemoveFeed(long feedId)
	{
		if (!_feeds.Remove(feedId, out var handle))
			return;
		try { Invoke(handle.Sheet, "Delete"); }
		finally { Release(handle.Sheet); }
	}

	private IEnumerable<RakutenRssOrderRow> ParseOrders(SheetHandle handle, bool derivative)
	{
		Calculate();
		var grid = ReadTable(handle);
		for (var row = 0; row < grid.Rows; row++)
		{
			var id = ToText(grid[row, 0]);
			if (id.IsEmpty())
				continue;
			if (derivative)
			{
				yield return new()
				{
					OrderId = id, Status = ToText(grid[row, 1]), Code = ToText(grid[row, 2]),
					Name = ToText(grid[row, 3]), Market = ToText(grid[row, 4]),
					Time = ToUtc(ToDateTime(grid[row, 5])), Side = ToText(grid[row, 6]),
					TradeType = ToText(grid[row, 6]), Quantity = ToDecimal(grid[row, 7]) ?? 0,
					ExecutionCondition = ToText(grid[row, 9]), Price = ToDecimal(grid[row, 10]) ?? 0,
					OrderType = ToText(grid[row, 11]), Error = ToText(grid[row, 12]), IsDerivative = true,
				};
			}
			else
			{
				yield return new()
				{
					OrderId = id, Status = ToText(grid[row, 1]), Code = ToText(grid[row, 2]),
					Name = ToText(grid[row, 3]), Market = ToText(grid[row, 4]),
					Time = ToUtc(ToDateTime(grid[row, 5])), Side = ToText(grid[row, 6]),
					TradeType = ToText(grid[row, 7]), ExecutionCondition = ToText(grid[row, 8]),
					Quantity = ToDecimal(grid[row, 9]) ?? 0, FilledQuantity = ToDecimal(grid[row, 10]) ?? 0,
					Price = ToDecimal(grid[row, 11]) ?? 0, OrderType = ToText(grid[row, 12]),
					Error = ToText(grid[row, 13]),
				};
			}
		}
	}

	private IEnumerable<RakutenRssExecutionRow> ParseExecutions(SheetHandle handle, bool derivative)
	{
		Calculate();
		var grid = ReadTable(handle);
		for (var row = 0; row < grid.Rows; row++)
		{
			var time = ToDateTime(grid[row, 0]);
			var code = ToText(grid[row, 1]);
			var quantity = ToDecimal(grid[row, 5]);
			var price = ToDecimal(grid[row, 6]);
			if (time == null || code.IsEmpty() || quantity is not > 0 || price is not > 0)
				continue;
			yield return new()
			{
				Time = ToUtc(time), Code = code, Name = ToText(grid[row, 2]),
				Market = ToText(grid[row, 3]), Side = ToText(grid[row, 4]),
				Quantity = quantity.Value, Price = price.Value,
				Amount = ToDecimal(grid[row, 7]) ?? quantity.Value * price.Value,
				IsDerivative = derivative,
			};
		}
	}

	private IEnumerable<RakutenRssPositionRow> ParsePositions()
	{
		foreach (var item in ParsePositions(_positions, false, false)) yield return item;
		foreach (var item in ParsePositions(_marginPositions, false, true)) yield return item;
		foreach (var item in ParsePositions(_derivativePositions, true, false)) yield return item;
	}

	private IEnumerable<RakutenRssPositionRow> ParsePositions(SheetHandle handle,
		bool derivative, bool margin)
	{
		var grid = ReadTable(handle);
		for (var row = 0; row < grid.Rows; row++)
		{
			var code = ToText(grid[row, 0]);
			if (code.IsEmpty())
				continue;
			if (margin)
			{
				yield return new()
				{
					Code = code, Name = ToText(grid[row, 1]), Market = ToText(grid[row, 2]),
					Side = ToText(grid[row, 3]), Quantity = ToDecimal(grid[row, 4]) ?? 0,
					BlockedQuantity = ToDecimal(grid[row, 5]) ?? 0,
					AveragePrice = ToDecimal(grid[row, 6]) ?? 0,
					CurrentPrice = ToDecimal(grid[row, 7]) ?? 0,
					UnrealizedPnL = ToDecimal(grid[row, 8]) ?? 0,
				};
			}
			else if (derivative)
			{
				yield return new()
				{
					Code = code, Name = ToText(grid[row, 1]), Market = ToText(grid[row, 2]),
					Side = ToText(grid[row, 3]), Quantity = ToDecimal(grid[row, 4]) ?? 0,
					BlockedQuantity = ToDecimal(grid[row, 5]) ?? 0,
					AveragePrice = ToDecimal(grid[row, 6]) ?? 0,
					CurrentPrice = ToDecimal(grid[row, 7]) ?? 0,
					UnrealizedPnL = ToDecimal(grid[row, 8]) ?? 0, IsDerivative = true,
				};
			}
			else
			{
				yield return new()
				{
					Code = code, Name = ToText(grid[row, 1]), Market = "東証",
					Quantity = ToDecimal(grid[row, 2]) ?? 0,
					BlockedQuantity = ToDecimal(grid[row, 3]) ?? 0,
					AveragePrice = ToDecimal(grid[row, 4]) ?? 0,
					CurrentPrice = ToDecimal(grid[row, 5]) ?? 0,
					UnrealizedPnL = ToDecimal(grid[row, 6]) ?? 0,
				};
			}
		}
	}

	private RakutenRssOrderResult ExecuteOrder(int requestId, string function, object[] args)
	{
		var status = ExecuteCommand(function, args);
		if (status.Contains("入力エラー", StringComparison.Ordinal) ||
			status.Contains("エラー", StringComparison.Ordinal) ||
			status.Contains("キャンセル", StringComparison.Ordinal) ||
			status.Contains("発注ロック", StringComparison.Ordinal))
			throw new InvalidOperationException($"MARKETSPEED II RSS {function}: {status}");
		return new() { RequestId = requestId, Status = status };
	}

	private object ExecuteScalar(string function, object[] args)
	{
		var row = ++_commandRow;
		SetFormula(_commandSheet, Address(row, 1), Formula(function, args));
		Calculate();
		return GetValue(_commandSheet, Address(row, 1));
	}

	private string ExecuteCommand(string function, object[] args)
	{
		var row = ++_commandRow;
		var address = Address(row, 1);
		SetFormula(_commandSheet, address, Formula(function, args));
		for (var attempt = 0; attempt < 150; attempt++)
		{
			Calculate();
			var status = ToText(GetValue(_commandSheet, address));
			if (!status.IsEmpty() && !status.Contains("待機中", StringComparison.Ordinal) &&
				!status.Contains("応答待ち", StringComparison.Ordinal) &&
				!status.Contains("接続待ち", StringComparison.Ordinal))
				return status;
			Thread.Sleep(100);
		}
		throw new TimeoutException($"MARKETSPEED II RSS {function} did not complete within 15 seconds.");
	}

	private SheetHandle CreateTable(string name, string function, string[] headers,
		object[] args, int? rows = null)
	{
		var sheet = AddSheet(name);
		SetValues(sheet, $"A2:{Address(2, headers.Length)}", headers);
		var reference = $"A2:{Address(2, headers.Length)}";
		SetFormula(sheet, "A1", Formula(function, [new CellReference(reference), .. args]));
		return new() { Sheet = sheet, Columns = headers.Length, Rows = rows ?? _maxRows };
	}

	private sealed class CellReference(string value)
	{
		public string Value { get; } = value;
	}

	private Grid ReadTable(SheetHandle handle)
		=> ReadGrid(handle.Sheet, $"A3:{Address(handle.Rows + 2, handle.Columns)}");

	private Grid ReadGrid(object sheet, string address)
	{
		var range = Get(sheet, "Range", address);
		try
		{
			var value = Get(range, "Value2");
			if (value is object[,] values)
			{
				var rows = values.GetLength(0);
				var columns = values.GetLength(1);
				var normalized = new object[rows, columns];
				var rowStart = values.GetLowerBound(0);
				var columnStart = values.GetLowerBound(1);
				for (var row = 0; row < rows; row++)
					for (var column = 0; column < columns; column++)
						normalized[row, column] = values[row + rowStart, column + columnStart];
				return new(normalized);
			}
			var scalar = new object[1, 1];
			scalar[0, 0] = value;
			return new(scalar);
		}
		finally
		{
			Release(range);
		}
	}

	private object AddSheet(string name = null)
	{
		var sheets = Get(_workbook, "Worksheets");
		try
		{
			var sheet = Invoke(sheets, "Add");
			Set(sheet, "Name", name.IsEmpty($"Feed{_feedId + 1}"));
			return sheet;
		}
		finally
		{
			Release(sheets);
		}
	}

	private void SetValues(object sheet, string address, string[] values)
	{
		var range = Get(sheet, "Range", address);
		try
		{
			var matrix = new object[1, values.Length];
			for (var i = 0; i < values.Length; i++) matrix[0, i] = values[i];
			Set(range, "Value2", matrix);
		}
		finally { Release(range); }
	}

	private static void SetFormula(object sheet, string address, string formula)
	{
		var range = Get(sheet, "Range", address);
		try { Set(range, "Formula", formula); }
		finally { Release(range); }
	}

	private static object GetValue(object sheet, string address)
	{
		var range = Get(sheet, "Range", address);
		try { return Get(range, "Value2"); }
		finally { Release(range); }
	}

	private SheetHandle GetFeed(long id)
		=> _feeds.TryGetValue(id, out var handle) ? handle
			: throw new InvalidOperationException($"Unknown MARKETSPEED II RSS feed {id}.");

	private void Calculate() => Invoke(_application, "Calculate");

	private static string Formula(string function, object[] args)
		=> $"={function}({string.Join(",", args.Select(FormatArgument))})";

	private static string FormatArgument(object value)
		=> value switch
		{
			null => string.Empty,
			CellReference reference => reference.Value,
			string text => $"\"{text.Replace("\"", "\"\"")}\"",
			bool flag => flag ? "TRUE" : "FALSE",
			IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
			_ => $"\"{value.ToString().Replace("\"", "\"\"")}\"",
		};

	private static string Address(int row, int column)
	{
		var letters = string.Empty;
		for (var value = column; value > 0; value = (value - 1) / 26)
			letters = (char)('A' + (value - 1) % 26) + letters;
		return letters + row.ToString(CultureInfo.InvariantCulture);
	}

	private static int ToMarketNumber(string securityCode)
		=> securityCode.EndsWith(".JNX", StringComparison.OrdinalIgnoreCase) ? 4
			: securityCode.EndsWith(".JAX", StringComparison.OrdinalIgnoreCase) ? 5 : 1;

	private static string ToText(object value)
		=> value == null || value is DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();

	private static int ToInt(object value)
		=> value == null || value is DBNull ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);

	private static decimal? ToDecimal(object value)
	{
		if (value == null || value is DBNull)
			return null;
		if (value is double number)
			return (decimal)number;
		var text = ToText(value)?.Replace(",", string.Empty).Replace("－", string.Empty);
		return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture,
			out var result) ? result : null;
	}

	private static DateTime? ToDate(object value)
	{
		if (value is DateTime date)
			return date.Date;
		if (value is double number)
			return DateTime.FromOADate(number).Date;
		var text = ToText(value);
		return DateTime.TryParse(text, CultureInfo.GetCultureInfo("ja-JP"),
			DateTimeStyles.AllowWhiteSpaces, out date) ? date.Date : null;
	}

	private static TimeSpan? ToTime(object value)
	{
		if (value is DateTime date)
			return date.TimeOfDay;
		if (value is double number)
			return DateTime.FromOADate(number).TimeOfDay;
		var text = ToText(value);
		return TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var result) ? result : null;
	}

	private static DateTime? ToDateTime(object value)
	{
		if (value is DateTime date)
			return date;
		if (value is double number)
			return DateTime.FromOADate(number);
		var text = ToText(value);
		return DateTime.TryParse(text, CultureInfo.GetCultureInfo("ja-JP"),
			DateTimeStyles.AllowWhiteSpaces, out date) ? date : null;
	}

	private static readonly TimeZoneInfo JapanTimeZone =
		TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");

	private static DateTime ToUtc(DateTime? date, TimeSpan? time)
	{
		var local = (date ?? TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, JapanTimeZone).Date)
			.Date + (time ?? TimeSpan.Zero);
		return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local,
			DateTimeKind.Unspecified), JapanTimeZone);
	}

	private static DateTime ToUtc(DateTime? value)
		=> value == null ? DateTime.UtcNow : TimeZoneInfo.ConvertTimeToUtc(
			DateTime.SpecifyKind(value.Value, DateTimeKind.Unspecified), JapanTimeZone);

	private static bool IsExcelError(object value)
		=> ToText(value)?.StartsWith("#", StringComparison.Ordinal) == true;

	private static object Invoke(object target, string name, params object[] args)
		=> InvokeMember(target, name, BindingFlags.InvokeMethod, args);

	private static object Get(object target, string name, params object[] args)
		=> InvokeMember(target, name, BindingFlags.GetProperty, args);

	private static void Set(object target, string name, object value)
		=> InvokeMember(target, name, BindingFlags.SetProperty, [value]);

	private static object InvokeMember(object target, string name, BindingFlags flags,
		object[] args)
	{
		try
		{
			return target.GetType().InvokeMember(name,
				BindingFlags.Instance | BindingFlags.Public | flags, null, target, args,
				CultureInfo.InvariantCulture);
		}
		catch (TargetInvocationException error)
		{
			throw error.InnerException ?? error;
		}
	}

	private static void Release(object value)
	{
		if (value != null && Marshal.IsComObject(value))
			Marshal.FinalReleaseComObject(value);
	}

	public void Dispose()
	{
		if (_disposed)
			return;
		_disposed = true;
		foreach (var handle in _feeds.Values)
			Release(handle.Sheet);
		_feeds.Clear();
		Release(_orders?.Sheet);
		Release(_derivativeOrders?.Sheet);
		Release(_executions?.Sheet);
		Release(_derivativeExecutions?.Sheet);
		Release(_positions?.Sheet);
		Release(_marginPositions?.Sheet);
		Release(_derivativePositions?.Sheet);
		Release(_capacity?.Sheet);
		Release(_derivativeCapacity?.Sheet);
		Release(_orderIds?.Sheet);
		Release(_commandSheet);
		if (_workbook != null)
		{
			try { Invoke(_workbook, "Close", false); }
			catch (COMException) { }
			Release(_workbook);
			_workbook = null;
		}
		if (_application != null)
		{
			try { Invoke(_application, "Quit"); }
			catch (COMException) { }
			Release(_application);
			_application = null;
		}
	}
}
