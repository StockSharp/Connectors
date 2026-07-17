namespace StockSharp.Yuanta.Native;

sealed class YuantaSdkBridge : IDisposable
{
	private sealed class EventRegistration
	{
		public object Target { get; init; }
		public EventInfo Event { get; init; }
		public Delegate Handler { get; init; }
	}

	private static readonly object _resolverSync = new();
	private static readonly HashSet<string> _managedRoots = new(StringComparer.OrdinalIgnoreCase);
	private static readonly Dictionary<string, IntPtr> _nativeHandles = new(StringComparer.OrdinalIgnoreCase);
	private static bool _managedResolverRegistered;

	private readonly Assembly _assembly;
	private readonly string _sdkRoot;
	private readonly List<EventRegistration> _events = [];
	private object _sdk;

	public YuantaSdkBridge(string sdkPath, string logPath)
	{
		(_assembly, _sdkRoot) = LoadAssembly(sdkPath);
		RegisterManagedResolver(_sdkRoot);
		LoadNativeLibraries(_sdkRoot);
		var sdkType = GetType("YuantaOneAPI.YuantaSparkAPITrader");
		_sdk = Create(sdkType, logPath ?? string.Empty);
		AttachEvent(_sdk, "OnResponse", values => Response?.Invoke(
			Convert.ToInt32(values[0], CultureInfo.InvariantCulture),
			Convert.ToUInt32(values[1], CultureInfo.InvariantCulture),
			values[2]?.ToString(), values[3], values[4]));
	}

	public event Action<int, uint, string, object, object> Response;

	public string Version => _assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ??
		_assembly.GetName().Version?.ToString() ?? "unknown";

	public void Open(YuantaEnvironments environment)
	{
		Invoke(_sdk, "SetLogType", EnumValue("YuantaOneAPI.enumLogType", "COMMON"));
		Invoke(_sdk, "Open", EnumValue("YuantaOneAPI.enumEnvironmentMode",
			environment == YuantaEnvironments.Uat ? "UAT" : "PROD"));
	}

	public bool Login(string account, string password, string certificatePath, string certificatePassword)
	{
		var result = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
			? Invoke(_sdk, "Login", account, password)
			: Invoke(_sdk, "Login", certificatePath, certificatePassword, account, password);
		return Convert.ToBoolean(result, CultureInfo.InvariantCulture);
	}

	public void Logout()
	{
		if (_sdk != null)
			Invoke(_sdk, "LogOut");
	}

	public void Close()
	{
		if (_sdk != null)
			Invoke(_sdk, "Close");
	}

	public object GetQuotes(string account, IEnumerable<YuantaSecurityInfo> securities)
	{
		var list = CreateList("YuantaOneAPI.Quote", securities.Select(CreateQuoteObject));
		return InvokeResponse("GetWatchListAllSync", "instrument query",
			account, list, Utf8Language, false);
	}

	public object GetCandles(string account, int market, string symbol, int kLineType, DateTime from, DateTime to)
		=> InvokeResponse("GetKLineSync", "candle query", account,
			EnumValue("YuantaOneAPI.KLineType", kLineType),
			EnumValue("YuantaOneAPI.enumMarketType", market), symbol,
			from.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture),
			to.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture), Utf8Language);

	public object GetTicks(string account, int market, string symbol, string from, string to, int count)
		=> InvokeResponse("GetStkTickDetailSync", "tick query", account,
			EnumValue("YuantaOneAPI.enumMarketType", market), symbol,
			EnumValue("YuantaOneAPI.enumStkTickSelectType", 0), from, to, count, Utf8Language);

	public object GetOrders(string account)
		=> InvokeResponse("GetOrderTradeReportSync", "order and trade query", false, account, Utf8Language);

	public object GetStockPositions(string account)
		=> InvokeResponse("GetStoreSummarySync", "stock position query", account, Utf8Language);

	public object GetFuturesPositions(string account)
		=> InvokeResponse("GetFutStoreSummarySync", "futures position query", account, Utf8Language);

	public object GetFuturesEquity(string account)
		=> InvokeResponse("GetFutInterestStoreSync", "futures equity query",
			account, "1", "TWD", Utf8Language);

	public object GetBankBalance(string account)
		=> InvokeResponse("GetBankBalanceSync", "bank balance query", account, Utf8Language);

	public object GetSubscriptions(string account)
		=> InvokeResponse("GetQuoteListSync", "subscription heartbeat", account);

	public void Subscribe(string account, YuantaSubscription subscription)
	{
		var (method, typeName) = subscription.Kind switch
		{
			YuantaMarketDataKinds.Level1 => ("SubscribeWatchlistAll", "YuantaOneAPI.WatchlistAll"),
			YuantaMarketDataKinds.Trades => ("SubscribeStockTick", "YuantaOneAPI.StockTick"),
			YuantaMarketDataKinds.MarketDepth => ("SubscribeFiveTickA", "YuantaOneAPI.FiveTickA"),
			_ => throw new ArgumentOutOfRangeException(nameof(subscription.Kind), subscription.Kind, null),
		};
		var item = CreateQuoteObject(typeName, subscription.Market, subscription.Symbol);
		Invoke(_sdk, method, account, CreateList(typeName, [item]), Utf8Language);
	}

	public void Unsubscribe(string account, YuantaSubscription subscription)
	{
		var (method, typeName) = subscription.Kind switch
		{
			YuantaMarketDataKinds.Level1 => ("UnSubscribeWatchlistAll", "YuantaOneAPI.WatchlistAll"),
			YuantaMarketDataKinds.Trades => ("UnSubscribeStockTick", "YuantaOneAPI.StockTick"),
			YuantaMarketDataKinds.MarketDepth => ("UnSubscribeFiveTickA", "YuantaOneAPI.FiveTickA"),
			_ => throw new ArgumentOutOfRangeException(nameof(subscription.Kind), subscription.Kind, null),
		};
		var item = CreateQuoteObject(typeName, subscription.Market, subscription.Symbol);
		Invoke(_sdk, method, account, CreateList(typeName, [item]), Utf8Language);
	}

	public object SendOrder(YuantaOrderRequest request, int functionCode)
		=> request.SecurityType is SecurityTypes.Future or SecurityTypes.Option
			? SendFuturesOrder(request, functionCode)
			: SendStockOrder(request, functionCode);

	public object Member(object target, string name)
		=> GetMember(target, name);

	public T Value<T>(object target, string name, T defaultValue = default)
	{
		var value = GetMember(target, name);
		if (value == null)
			return defaultValue;
		var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
		if (targetType.IsEnum)
			return (T)Enum.ToObject(targetType, Convert.ToInt32(value, CultureInfo.InvariantCulture));
		return (T)Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
	}

	public object[] Items(object target, string name)
	{
		var value = GetMember(target, name);
		return value is IEnumerable items ? items.Cast<object>().ToArray() : [];
	}

	private object Utf8Language => EnumValue("YuantaOneAPI.enumLangType", "UTF8");

	private object SendStockOrder(YuantaOrderRequest request, int tradeKind)
	{
		var order = Create(GetType("YuantaOneAPI.StockOrder"));
		SetMember(order, "Identify", request.NativeId);
		SetMember(order, "Account", request.Account);
		SetMember(order, "OrderNo", request.OrderId ?? string.Empty);
		SetMember(order, "TradeDate", GetTradeDate(request));
		SetMember(order, "APCode", request.StockMarketType switch
		{
			YuantaStockMarketTypes.Regular => 0,
			YuantaStockMarketTypes.OddLot => 2,
			YuantaStockMarketTypes.IntradayOddLot => 4,
			YuantaStockMarketTypes.AfterHours => 7,
			_ => 0,
		});
		SetMember(order, "TradeKind", tradeKind);
		SetMember(order, "OrderType", request.StockOrderType switch
		{
			YuantaStockOrderTypes.Cash => "0",
			YuantaStockOrderTypes.Margin => "3",
			YuantaStockOrderTypes.Short => "4",
			YuantaStockOrderTypes.StrategyBorrowed => "5",
			YuantaStockOrderTypes.HedgeBorrowed => "6",
			YuantaStockOrderTypes.DayTrade => "9",
			_ => "0",
		});
		SetMember(order, "StkCode", request.OrderSymbol.IsEmpty(request.Symbol));
		SetMember(order, "BuySell", request.Side == Sides.Buy ? "B" : "S");
		SetMember(order, "PriceFlag", request.OrderType == OrderTypes.Market ? "M" : " ");
		SetMember(order, "Price", request.OrderType == OrderTypes.Market ? 0d : (double)request.Price);
		SetMember(order, "BasketNo", request.UserTag ?? string.Empty);
		SetMember(order, "OrderQty", request.Volume);
		SetMember(order, "Time_in_force", request.TimeInForce switch
		{
			TimeInForce.CancelBalance => "3",
			TimeInForce.MatchOrCancel => "4",
			_ => "0",
		});
		return InvokeResponse("SendStockOrderSync", "stock order",
			request.Account, CreateList("YuantaOneAPI.StockOrder", [order]), Utf8Language);
	}

	private object SendFuturesOrder(YuantaOrderRequest request, int functionCode)
	{
		var order = Create(GetType("YuantaOneAPI.FutureOrder"));
		SetMember(order, "Identify", request.NativeId);
		SetMember(order, "Account", request.Account);
		SetMember(order, "OrderNo", request.OrderId ?? string.Empty);
		SetMember(order, "TradeDate", GetTradeDate(request));
		SetMember(order, "FunctionCode", functionCode);
		SetMember(order, "CommodityID1", request.OrderSymbol.IsEmpty(request.Symbol));
		SetMember(order, "CallPut1", request.OptionType switch
		{
			OptionTypes.Call => "C",
			OptionTypes.Put => "P",
			_ => string.Empty,
		});
		SetMember(order, "SettlementMonth1", request.SettlementMonth);
		SetMember(order, "Price", request.OrderType == OrderTypes.Market ? 0d : (double)request.Price);
		SetMember(order, "StrikePrice1", (double)request.StrikePrice);
		SetMember(order, "OrderQty1", checked((short)request.Volume));
		SetMember(order, "BuySell1", request.Side == Sides.Buy ? "B" : "S");
		SetMember(order, "CommodityID2", string.Empty);
		SetMember(order, "CallPut2", string.Empty);
		SetMember(order, "SettlementMonth2", 0);
		SetMember(order, "StrikePrice2", 0d);
		SetMember(order, "OrderQty2", (short)0);
		SetMember(order, "BuySell2", string.Empty);
		SetMember(order, "OpenOffsetKind", request.PositionEffect switch
		{
			YuantaFuturesPositionEffects.Open => "0",
			YuantaFuturesPositionEffects.Close => "1",
			_ => "2",
		});
		SetMember(order, "DayTradeID", request.IsDayTrade ? "Y" : " ");
		var nativePriceType = request.FuturesPriceType == YuantaFuturesPriceTypes.Auto
			? request.OrderType == OrderTypes.Market ? YuantaFuturesPriceTypes.Market : YuantaFuturesPriceTypes.Limit
			: request.FuturesPriceType;
		SetMember(order, "OrderType", nativePriceType switch
		{
			YuantaFuturesPriceTypes.Market => "1",
			YuantaFuturesPriceTypes.RangeMarket => "3",
			_ => "2",
		});
		SetMember(order, "OrderCond", request.TimeInForce switch
		{
			TimeInForce.CancelBalance => "2",
			TimeInForce.MatchOrCancel => "I",
			_ => " ",
		});
		SetMember(order, "SellerNo", request.SellerNo);
		SetMember(order, "BasketNo", request.UserTag ?? string.Empty);
		SetMember(order, "Session", request.IsPreOrder ? "1" : " ");
		return InvokeResponse("SendFutureOrderSync", "futures order",
			request.Account, CreateList("YuantaOneAPI.FutureOrder", [order]), Utf8Language);
	}

	private static string GetTradeDate(YuantaOrderRequest request)
		=> (request.TradeDate == default ? DateTime.UtcNow.ToTaipeiTime() : request.TradeDate.ToTaipeiTime())
			.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

	private object CreateQuoteObject(YuantaSecurityInfo security)
		=> CreateQuoteObject("YuantaOneAPI.Quote", security.Market, security.Symbol);

	private object CreateQuoteObject(string typeName, int market, string symbol)
	{
		var item = Create(GetType(typeName));
		SetMember(item, "MarketType", EnumValue("YuantaOneAPI.enumMarketType", market));
		SetMember(item, "StockCode", symbol);
		return item;
	}

	private object CreateList(string elementTypeName, IEnumerable<object> values)
	{
		var list = (IList)Create(typeof(List<>).MakeGenericType(GetType(elementTypeName)));
		foreach (var value in values)
			list.Add(value);
		return list;
	}

	private object InvokeResponse(string method, string operation, params object[] args)
	{
		var response = Invoke(_sdk, method, args)
			?? throw new InvalidOperationException($"Yuanta {operation} returned no response.");
		var success = GetMember(response, "Success");
		if (success != null && !Convert.ToBoolean(success, CultureInfo.InvariantCulture))
		{
			var message = GetMember(response, "ErrorMessage")?.ToString();
			throw new InvalidOperationException(message.IsEmpty($"Yuanta {operation} failed."));
		}
		return GetMember(response, "objValue")
			?? throw new InvalidDataException($"Yuanta {operation} returned an empty payload.");
	}

	private Type GetType(string name)
		=> _assembly.GetType(name, true, false);

	private object EnumValue(string typeName, object value)
	{
		var type = GetType(typeName);
		return value is string text ? Enum.Parse(type, text, true) : Enum.ToObject(type, value);
	}

	private void AttachEvent(object target, string name, Action<object[]> callback)
	{
		var eventInfo = target.GetType().GetEvent(name,
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)
			?? throw new MissingMemberException(target.GetType().FullName, name);
		var handler = CreateDelegate(eventInfo.EventHandlerType, callback);
		eventInfo.AddEventHandler(target, handler);
		_events.Add(new() { Target = target, Event = eventInfo, Handler = handler });
	}

	private static Delegate CreateDelegate(Type delegateType, Action<object[]> callback)
	{
		var invoke = delegateType.GetMethod("Invoke");
		var parameters = invoke.GetParameters().Select(parameter =>
			Expression.Parameter(parameter.ParameterType, parameter.Name)).ToArray();
		var values = Expression.NewArrayInit(typeof(object),
			parameters.Select(parameter => Expression.Convert(parameter, typeof(object))));
		var body = Expression.Invoke(Expression.Constant(callback), values);
		return Expression.Lambda(delegateType, body, parameters).Compile();
	}

	private static object Invoke(object target, string name, params object[] args)
	{
		var methods = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
			.Where(method => method.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
				method.GetParameters().Length == args.Length);
		foreach (var method in methods)
		{
			var parameters = method.GetParameters();
			if (!parameters.Select((parameter, index) => CanConvert(args[index], parameter.ParameterType)).All(value => value))
				continue;
			try
			{
				return method.Invoke(target, parameters.Select((parameter, index) =>
					ConvertArgument(args[index], parameter.ParameterType)).ToArray());
			}
			catch (TargetInvocationException error)
			{
				throw error.InnerException ?? error;
			}
		}
		throw new MissingMethodException(target.GetType().FullName, name);
	}

	private static object Create(Type type, params object[] args)
	{
		try
		{
			return Activator.CreateInstance(type, args);
		}
		catch (TargetInvocationException error)
		{
			throw error.InnerException ?? error;
		}
	}

	private static object GetMember(object target, string name)
	{
		if (target == null)
			return null;
		var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase;
		return target.GetType().GetField(name, flags)?.GetValue(target) ??
			target.GetType().GetProperty(name, flags)?.GetValue(target);
	}

	private static void SetMember(object target, string name, object value)
	{
		var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase;
		if (target.GetType().GetField(name, flags) is FieldInfo field)
		{
			field.SetValue(target, ConvertArgument(value, field.FieldType));
			return;
		}
		if (target.GetType().GetProperty(name, flags) is PropertyInfo property)
		{
			property.SetValue(target, ConvertArgument(value, property.PropertyType));
			return;
		}
		throw new MissingMemberException(target.GetType().FullName, name);
	}

	private static bool CanConvert(object value, Type targetType)
	{
		try
		{
			_ = ConvertArgument(value, targetType);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static object ConvertArgument(object value, Type targetType)
	{
		if (value == null)
			return null;
		var actualType = Nullable.GetUnderlyingType(targetType) ?? targetType;
		if (actualType.IsInstanceOfType(value))
			return value;
		if (actualType.IsEnum)
			return value is string text ? Enum.Parse(actualType, text, true) : Enum.ToObject(actualType, value);
		return Convert.ChangeType(value, actualType, CultureInfo.InvariantCulture);
	}

	private static (Assembly Assembly, string Root) LoadAssembly(string sdkPath)
	{
		if (sdkPath.IsEmpty())
		{
			try
			{
				var assembly = Assembly.Load("YuantaSparkAPI");
				return (assembly, Path.GetDirectoryName(assembly.Location));
			}
			catch (Exception error)
			{
				throw new FileNotFoundException(
					"Install the official Yuanta SPARK C# SDK and specify SdkPath.",
					"YuantaSparkAPI.dll", error);
			}
		}

		var input = Path.GetFullPath(sdkPath);
		var assemblyPath = File.Exists(input) ? input : FindAssembly(input);
		if (assemblyPath == null)
			throw new FileNotFoundException("YuantaSparkAPI.dll was not found in the specified SDK path.", sdkPath);
		var root = Path.GetDirectoryName(assemblyPath);
		RegisterManagedResolver(root);
		return (Assembly.LoadFrom(assemblyPath), root);
	}

	private static string FindAssembly(string directory)
	{
		if (!Directory.Exists(directory))
			return null;
		var candidates = new[]
		{
			Path.Combine(directory, "YuantaSparkAPI.dll"),
			Path.Combine(directory, "lib", "net10.0", "YuantaSparkAPI.dll"),
			Path.Combine(directory, "lib", "net8.0", "YuantaSparkAPI.dll"),
			Path.Combine(directory, "lib", "net6.0", "YuantaSparkAPI.dll"),
		};
		return candidates.FirstOrDefault(File.Exists) ??
			Directory.EnumerateFiles(directory, "YuantaSparkAPI.dll", SearchOption.AllDirectories).FirstOrDefault();
	}

	private static void RegisterManagedResolver(string root)
	{
		if (root.IsEmpty())
			return;
		lock (_resolverSync)
		{
			_managedRoots.Add(root);
			if (_managedResolverRegistered)
				return;
			AssemblyLoadContext.Default.Resolving += ResolveManagedAssembly;
			_managedResolverRegistered = true;
		}
	}

	private static Assembly ResolveManagedAssembly(AssemblyLoadContext context, AssemblyName name)
	{
		string[] roots;
		lock (_resolverSync)
			roots = _managedRoots.ToArray();
		foreach (var root in roots)
		{
			var path = Path.Combine(root, $"{name.Name}.dll");
			if (File.Exists(path))
				return context.LoadFromAssemblyPath(path);
		}
		return null;
	}

	private static void LoadNativeLibraries(string root)
	{
		if (root.IsEmpty() || !Directory.Exists(root))
			return;
		var architecture = RuntimeInformation.ProcessArchitecture switch
		{
			Architecture.X86 => "x86",
			Architecture.X64 => "x64",
			Architecture.Arm64 => "arm64",
			_ => string.Empty,
		};
		var names = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
			? new[] { $"grpc_csharp_ext.{architecture}.dll", "YuantaCAPIDLL64.dll", "YuantaCAPIDLL.dll", "libCGCrypt.dll" }
			: RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
				? new[] { $"libgrpc_csharp_ext.{architecture}.dylib", "libYuantaCGCrypt.dylib" }
				: new[] { $"libgrpc_csharp_ext.{architecture}.so", "libCGCGCrypt.so", "libCGCrypt.so" };
		foreach (var name in names)
		{
			var path = Directory.EnumerateFiles(root, name, SearchOption.AllDirectories).FirstOrDefault();
			if (path == null)
				continue;
			lock (_resolverSync)
			{
				if (_nativeHandles.ContainsKey(path))
					continue;
				try
				{
					_nativeHandles[path] = NativeLibrary.Load(path);
				}
				catch
				{
					// Some SDK libraries load lazily after their own dependencies are present.
				}
			}
		}
	}

	public void Dispose()
	{
		foreach (var registration in _events)
			registration.Event.RemoveEventHandler(registration.Target, registration.Handler);
		_events.Clear();
		if (_sdk is IDisposable disposable)
			disposable.Dispose();
		_sdk = null;
	}
}
