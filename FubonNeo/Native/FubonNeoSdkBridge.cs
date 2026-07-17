namespace StockSharp.FubonNeo.Native;

sealed class FubonNeoSdkBridge : IDisposable
{
	private sealed class CallbackRegistration
	{
		public object Target { get; init; }
		public FieldInfo Field { get; init; }
		public object Previous { get; init; }
	}

	private static readonly object _resolverSync = new();
	private static readonly HashSet<Assembly> _resolverAssemblies = [];
	private static readonly Dictionary<Assembly, string> _nativePaths = [];
	private static readonly Dictionary<Assembly, IntPtr> _nativeHandles = [];

	private readonly Assembly _assembly;
	private readonly List<CallbackRegistration> _callbacks = [];
	private object _sdk;
	private object _stockSocket;
	private object _futuresSocket;

	public FubonNeoSdkBridge(string sdkPath, string environmentUrl)
	{
		(_assembly, var nativePath) = LoadAssembly(sdkPath);
		RegisterNativeResolver(_assembly, nativePath);

		var sdkType = GetType("FubonNeo.Sdk.FubonSDK");
		_sdk = environmentUrl.IsEmpty()
			? Create(sdkType)
			: Create(sdkType, environmentUrl);

		SetCallback(_sdk, "OnOrder", values => Order?.Invoke(values[0]?.ToString(), values[1], false));
		SetCallback(_sdk, "OnOrderChanged", values => Order?.Invoke(values[0]?.ToString(), values[1], false));
		SetCallback(_sdk, "OnFilled", values => Filled?.Invoke(values[0]?.ToString(), values[1], false));
		SetCallback(_sdk, "OnFutoptOrder", values => Order?.Invoke(values[0]?.ToString(), values[1], true));
		SetCallback(_sdk, "OnFutoptOrderChanged", values => Order?.Invoke(values[0]?.ToString(), values[1], true));
		SetCallback(_sdk, "OnFutoptFilled", values => Filled?.Invoke(values[0]?.ToString(), values[1], true));
		SetCallback(_sdk, "OnEvent", values => Event?.Invoke(values[0]?.ToString(), values[1]?.ToString()));
	}

	public event Action<string, object, bool> Order;
	public event Action<string, object, bool> Filled;
	public event Action<string, string> Event;
	public event Action<FubonNeoAssetKinds, string> MarketMessage;
	public event Action<FubonNeoAssetKinds, Exception> SocketException;
	public event Action<FubonNeoAssetKinds, string> SocketDisconnected;

	public string Version => _assembly.GetName().Version?.ToString() ?? "unknown";

	public object Stock => GetProperty(_sdk, "Stock");
	public object Accounting => GetProperty(_sdk, "Accounting");
	public object FutOpt => GetProperty(_sdk, "FutOpt");
	public object FutOptAccounting => GetProperty(_sdk, "FutOptAccounting");

	public object Login(string personalId, string credential, string certificatePath,
		string certificatePassword, bool isApiKey)
		=> Invoke(_sdk, isApiKey ? "ApikeyLogin" : "Login",
			personalId, credential, certificatePath, certificatePassword.IsEmpty() ? null : certificatePassword);

	public void InitializeRealtime(FubonNeoRealtimeModes mode)
	{
		Invoke(_sdk, "InitRealtime", EnumValue("FugleMarketData.WebsocketModels.Mode", mode.ToString()));
		var marketData = GetField(_sdk, "MarketData")
			?? throw new InvalidOperationException("The Fubon SDK did not initialize market data.");
		var socketFactory = GetProperty(marketData, "WebSocketClient");
		_stockSocket = GetProperty(socketFactory, "Stock");
		_futuresSocket = GetProperty(socketFactory, "FutureOption");
		AttachSocketCallbacks(_stockSocket, FubonNeoAssetKinds.Stock);
		AttachSocketCallbacks(_futuresSocket, FubonNeoAssetKinds.FuturesOptions);
	}

	public object RecoverEventData()
		=> Invoke(_sdk, "RecoverEventData");

	public Task ConnectSocketAsync(FubonNeoAssetKinds kind)
		=> AwaitVoidAsync(Invoke(GetSocket(kind), "Connect", 10000, true));

	public Task DisconnectSocketAsync(FubonNeoAssetKinds kind)
		=> AwaitVoidAsync(Invoke(GetSocket(kind), "Disconnect", "StockSharp disconnect"));

	public Task PingSocketAsync(FubonNeoAssetKinds kind)
		=> AwaitVoidAsync(Invoke(GetSocket(kind), "Ping", "StockSharp"));

	public Task SubscribeAsync(FubonNeoSubscription subscription)
	{
		var socket = GetSocket(subscription.Kind);
		var enumType = subscription.Kind == FubonNeoAssetKinds.Stock
			? "FugleMarketData.WebsocketModels.StockChannel"
			: "FugleMarketData.WebsocketModels.FutureOptionChannel";
		var channel = EnumValue(enumType, subscription.Channel);

		if (subscription.Kind == FubonNeoAssetKinds.FuturesOptions && subscription.IsAfterHours)
		{
			var parameters = Create(GetType("FugleMarketData.WebsocketModels.FutureOptionParams"));
			SetProperty(parameters, "Symbol", subscription.Symbol);
			SetProperty(parameters, "AfterHours", true);
			return AwaitVoidAsync(Invoke(socket, "Subscribe", channel, parameters));
		}

		return AwaitVoidAsync(Invoke(socket, "Subscribe", channel, subscription.Symbol));
	}

	public Task UnsubscribeAsync(FubonNeoAssetKinds kind, string serverId)
		=> AwaitVoidAsync(Invoke(GetSocket(kind), "Unsubscribe", serverId));

	public async Task<string> GetTickerListAsync(FubonNeoAssetKinds kind, string type, string session = null)
	{
		var intraday = GetIntraday(kind);
		var enumType = kind == FubonNeoAssetKinds.Stock
			? "FugleMarketData.QueryModels.Stock.Intraday.TickersType"
			: "FugleMarketData.QueryModels.FuOpt.FutOptType";
		object response;
		if (kind == FubonNeoAssetKinds.FuturesOptions && !session.IsEmpty())
		{
			var request = Create(GetType("FugleMarketData.QueryModels.FuOpt.Intraday.TickersRequest"));
			SetProperty(request, "Session", EnumValue("FugleMarketData.QueryModels.FuOpt.SessionType", session));
			response = await AwaitResultAsync(Invoke(intraday, "Tickers", EnumValue(enumType, type), request));
		}
		else
			response = await AwaitResultAsync(Invoke(intraday, "Tickers", EnumValue(enumType, type)));
		return await ReadHttpResponseAsync(response, $"ticker list {type}");
	}

	public async Task<string> GetCandlesAsync(FubonNeoSecurityInfo security, TimeSpan timeFrame,
		DateTime? from, DateTime? to)
	{
		object response;
		if (security.Kind == FubonNeoAssetKinds.Stock)
		{
			var marketData = GetField(_sdk, "MarketData");
			var restFactory = GetProperty(marketData, "RestClient");
			var stock = GetProperty(restFactory, "Stock");
			var history = GetField(stock, "History");
			var request = Create(GetType("FugleMarketData.QueryModels.Stock.History.HistoryCandlesRequest"));
			if (from != null)
				SetProperty(request, "From", ToTaipeiDate(from.Value));
			if (to != null)
				SetProperty(request, "To", ToTaipeiDate(to.Value));
			SetProperty(request, "TimeFrame", EnumValue(
				"FugleMarketData.QueryModels.Stock.History.HistoryTimeFrame",
				timeFrame.ToFubonTimeFrame(security.Kind)));
			SetProperty(request, "Sort", EnumValue("FugleMarketData.QueryModels.Stock.History.SortType", "Asc"));
			response = await AwaitResultAsync(Invoke(history, "Candles", security.Symbol, request));
		}
		else
		{
			var request = Create(GetType("FugleMarketData.QueryModels.FuOpt.Intraday.CandlesRequest"));
			SetProperty(request, "TimeFrame", EnumValue(
				"FugleMarketData.QueryModels.FuOpt.Intraday.CandlesTimeFrame",
				timeFrame.ToFubonTimeFrame(security.Kind)));
			if (security.IsAfterHours)
				SetProperty(request, "Session", EnumValue("FugleMarketData.QueryModels.FuOpt.TradeSession", "AfterHours"));
			response = await AwaitResultAsync(Invoke(GetIntraday(security.Kind), "Candles", security.Symbol, request));
		}
		return await ReadHttpResponseAsync(response, $"candles for {security.Symbol}");
	}

	public object CreateSdkObject(string typeName, params object[] args)
		=> Create(GetType(typeName), args);

	public object EnumValue(string typeName, string name)
		=> Enum.Parse(GetType(typeName), name, true);

	public object Call(object target, string name, params object[] args)
		=> Invoke(target, name, args);

	public object Property(object target, string name)
		=> GetProperty(target, name);

	public T Value<T>(object target, string name, T defaultValue = default)
	{
		var value = GetProperty(target, name);
		if (value == null)
			return defaultValue;
		var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
		if (targetType.IsEnum)
			return (T)Enum.Parse(targetType, value.ToString(), true);
		return (T)Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
	}

	public object ReadData(object response, string operation)
	{
		EnsureSuccess(response, operation);
		return GetProperty(response, "data");
	}

	public object[] ReadList(object response, string operation)
	{
		var data = ReadData(response, operation);
		if (data is not IEnumerable items)
			return [];
		return [.. items.Cast<object>()];
	}

	public void EnsureSuccess(object response, string operation)
	{
		if (response == null)
			throw new InvalidDataException($"Fubon returned no response for {operation}.");
		if (Value(response, "isSuccess", false))
			return;
		var message = Value<string>(response, "message");
		throw new InvalidOperationException($"Fubon {operation} failed: {message.IsEmpty("unknown error")}.");
	}

	public void Dispose()
	{
		var sdk = Interlocked.Exchange(ref _sdk, null);
		if (sdk == null)
			return;
		for (var index = _callbacks.Count - 1; index >= 0; index--)
		{
			var callback = _callbacks[index];
			try
			{
				callback.Field.SetValue(callback.Target, callback.Previous);
			}
			catch
			{
			}
		}
		_callbacks.Clear();
		try
		{
			Invoke(sdk, "Logout");
		}
		catch
		{
		}
		if (sdk is IDisposable disposable)
			disposable.Dispose();
		_stockSocket = null;
		_futuresSocket = null;
	}

	private object GetIntraday(FubonNeoAssetKinds kind)
	{
		var marketData = GetField(_sdk, "MarketData")
			?? throw new InvalidOperationException("Fubon market data is not initialized.");
		var restFactory = GetProperty(marketData, "RestClient");
		var market = GetProperty(restFactory, kind == FubonNeoAssetKinds.Stock ? "Stock" : "FutureOption");
		return GetField(market, "Intraday");
	}

	private object GetSocket(FubonNeoAssetKinds kind)
		=> (kind == FubonNeoAssetKinds.Stock ? _stockSocket : _futuresSocket)
			?? throw new InvalidOperationException("Fubon realtime market data is not initialized.");

	private void AttachSocketCallbacks(object socket, FubonNeoAssetKinds kind)
	{
		SetCallback(socket, "OnMessage", values => MarketMessage?.Invoke(kind, values[0]?.ToString()));
		SetCallback(socket, "OnException", values =>
			SocketException?.Invoke(kind, values[0] as Exception ?? new InvalidOperationException(values[0]?.ToString())));
		SetCallback(socket, "OnError", values =>
			SocketException?.Invoke(kind, new InvalidOperationException(values[0]?.ToString().IsEmpty("Fubon WebSocket error."))));
		SetCallback(socket, "OnDisconnected", values => SocketDisconnected?.Invoke(kind, values[0]?.ToString()));
		SetCallback(socket, "OnClose", values => SocketDisconnected?.Invoke(kind, values[0]?.ToString()));
	}

	private void SetCallback(object target, string fieldName, Action<object[]> handler)
	{
		var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)
			?? throw new MissingFieldException(target.GetType().FullName, fieldName);
		var previous = field.GetValue(target);
		var parameters = field.FieldType.GetMethod("Invoke")?.GetParameters()
			?? throw new InvalidOperationException($"Fubon callback field {fieldName} is not a delegate.");
		var expressions = parameters.Select(parameter => Expression.Parameter(parameter.ParameterType, parameter.Name)).ToArray();
		var values = Expression.NewArrayInit(typeof(object), expressions.Select(expression => Expression.Convert(expression, typeof(object))));
		var invoke = Expression.Call(Expression.Constant(handler), handler.GetType().GetMethod("Invoke"), values);
		var callback = Expression.Lambda(field.FieldType, invoke, expressions).Compile();
		field.SetValue(target, callback);
		_callbacks.Add(new() { Target = target, Field = field, Previous = previous });
	}

	private Type GetType(string name)
		=> _assembly.GetType(name, true, false);

	private static async Task<string> ReadHttpResponseAsync(object value, string operation)
	{
		if (value is not HttpResponseMessage response)
			throw new InvalidDataException($"Fubon returned an invalid HTTP response for {operation}.");
		using (response)
		{
			var content = await response.Content.ReadAsStringAsync();
			if (!response.IsSuccessStatusCode)
				throw new HttpRequestException($"Fubon {operation} returned HTTP {(int)response.StatusCode}: " +
					content.IsEmpty(response.ReasonPhrase));
			if (content.IsEmpty())
				throw new InvalidDataException($"Fubon returned an empty response for {operation}.");
			return content;
		}
	}

	private static async Task<object> AwaitResultAsync(object value)
	{
		if (value is Task task)
		{
			await task;
			return task.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.Public)?.GetValue(task);
		}
		if (value is ValueTask valueTask)
		{
			await valueTask;
			return null;
		}
		if (value?.GetType().IsValueType == true &&
			value.GetType().IsGenericType && value.GetType().GetGenericTypeDefinition() == typeof(ValueTask<>))
		{
			var taskValue = (Task)value.GetType().GetMethod("AsTask").Invoke(value, null);
			await taskValue;
			return taskValue.GetType().GetProperty("Result")?.GetValue(taskValue);
		}
		return value;
	}

	private static async Task AwaitVoidAsync(object value)
		=> _ = await AwaitResultAsync(value);

	private static object Create(Type type, params object[] args)
	{
		var constructors = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public)
			.Select(constructor => (Constructor: constructor, Score: GetScore(constructor.GetParameters(), args)))
			.Where(candidate => candidate.Score >= 0)
			.OrderByDescending(candidate => candidate.Score)
			.ToArray();
		if (constructors.Length == 0)
			throw new MissingMethodException(type.FullName, ".ctor");
		var selected = constructors[0].Constructor;
		return selected.Invoke(ConvertArguments(selected.GetParameters(), args));
	}

	private static object Invoke(object target, string name, params object[] args)
	{
		if (target == null)
			throw new ArgumentNullException(nameof(target));
		var methods = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
			.Where(method => method.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && !method.ContainsGenericParameters)
			.Select(method => (Method: method, Score: GetScore(method.GetParameters(), args)))
			.Where(candidate => candidate.Score >= 0)
			.OrderByDescending(candidate => candidate.Score)
			.ToArray();
		if (methods.Length == 0)
			throw new MissingMethodException(target.GetType().FullName, name);
		var selected = methods[0].Method;
		try
		{
			return selected.Invoke(target, ConvertArguments(selected.GetParameters(), args));
		}
		catch (TargetInvocationException error) when (error.InnerException != null)
		{
			throw error.InnerException;
		}
	}

	private static int GetScore(ParameterInfo[] parameters, object[] args)
	{
		if (args.Length > parameters.Length || parameters.Skip(args.Length).Any(parameter => !parameter.HasDefaultValue))
			return -1;
		var score = parameters.Length == args.Length ? 2 : 0;
		for (var index = 0; index < args.Length; index++)
		{
			var argument = args[index];
			var parameterType = parameters[index].ParameterType;
			if (argument == null)
			{
				if (parameterType.IsValueType && Nullable.GetUnderlyingType(parameterType) == null)
					return -1;
				continue;
			}
			var actualType = argument.GetType();
			if (actualType == parameterType)
				score += 6;
			else if (parameterType.IsAssignableFrom(actualType))
				score += 4;
			else if (CanConvert(argument, parameterType))
				score++;
			else
				return -1;
		}
		return score;
	}

	private static object[] ConvertArguments(ParameterInfo[] parameters, object[] args)
	{
		var converted = new object[parameters.Length];
		for (var index = 0; index < parameters.Length; index++)
			converted[index] = index < args.Length
				? ConvertArgument(args[index], parameters[index].ParameterType)
				: parameters[index].DefaultValue;
		return converted;
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

	private static object GetProperty(object target, string name)
		=> target?.GetType().GetProperty(name,
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)?.GetValue(target);

	private static object GetField(object target, string name)
		=> target?.GetType().GetField(name,
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)?.GetValue(target);

	private static void SetProperty(object target, string name, object value)
	{
		var property = target.GetType().GetProperty(name,
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)
			?? throw new MissingMemberException(target.GetType().FullName, name);
		property.SetValue(target, ConvertArgument(value, property.PropertyType));
	}

	private static (Assembly Assembly, string NativePath) LoadAssembly(string sdkPath)
	{
		if (sdkPath.IsEmpty())
		{
			try
			{
				return (Assembly.Load("FubonNeo"), null);
			}
			catch (Exception error)
			{
				throw new FileNotFoundException(
					"Install the official Fubon Neo C# SDK and specify SdkPath.", "FubonNeo.dll", error);
			}
		}

		var input = Path.GetFullPath(sdkPath);
		var assemblyPath = File.Exists(input) ? input : FindAssembly(input);
		if (assemblyPath == null)
			throw new FileNotFoundException("FubonNeo.dll was not found in the specified SDK path.", sdkPath);
		var assembly = Assembly.LoadFrom(assemblyPath);
		return (assembly, FindNativeLibrary(input, assemblyPath));
	}

	private static string FindAssembly(string directory)
	{
		if (!Directory.Exists(directory))
			return null;
		var candidates = new[]
		{
			Path.Combine(directory, "FubonNeo.dll"),
			Path.Combine(directory, "lib", "net10.0", "FubonNeo.dll"),
			Path.Combine(directory, "lib", "net8.0", "FubonNeo.dll"),
			Path.Combine(directory, "lib", "net6.0", "FubonNeo.dll"),
			Path.Combine(directory, "lib", "netstandard2.0", "FubonNeo.dll"),
		};
		return candidates.FirstOrDefault(File.Exists);
	}

	private static string FindNativeLibrary(string input, string assemblyPath)
	{
		var fileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "fubon.dll" :
			RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "libfubon.dylib" : "libfubon.so";
		var rid = GetRuntimeId();
		var assemblyDirectory = Path.GetDirectoryName(assemblyPath);
		var roots = new List<string>();
		if (Directory.Exists(input))
			roots.Add(input);
		if (!assemblyDirectory.IsEmpty())
		{
			roots.Add(assemblyDirectory);
			var parent = Directory.GetParent(assemblyDirectory)?.Parent?.FullName;
			if (!parent.IsEmpty())
				roots.Add(parent);
		}

		foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
		{
			var candidates = new[]
			{
				Path.Combine(root, fileName),
				Path.Combine(root, "native", fileName),
				Path.Combine(root, "runtimes", rid, "native", fileName),
			};
			var path = candidates.FirstOrDefault(File.Exists);
			if (path != null)
				return path;
		}
		return null;
	}

	private static string GetRuntimeId()
	{
		var os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win" :
			RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx" : "linux";
		var architecture = RuntimeInformation.ProcessArchitecture switch
		{
			Architecture.X86 => "x86",
			Architecture.X64 => "x64",
			Architecture.Arm64 => "arm64",
			_ => throw new PlatformNotSupportedException(
				$"The Fubon Neo SDK does not provide a runtime for {RuntimeInformation.ProcessArchitecture}."),
		};
		return $"{os}-{architecture}";
	}

	private static void RegisterNativeResolver(Assembly assembly, string nativePath)
	{
		if (nativePath.IsEmpty())
			return;
		lock (_resolverSync)
		{
			_nativePaths[assembly] = nativePath;
			if (!_resolverAssemblies.Add(assembly))
				return;
			NativeLibrary.SetDllImportResolver(assembly, (libraryName, target, searchPath) =>
			{
				if (!libraryName.Contains("fubon", StringComparison.OrdinalIgnoreCase))
					return IntPtr.Zero;
				lock (_resolverSync)
				{
					if (_nativeHandles.TryGetValue(target, out var handle))
						return handle;
					if (!_nativePaths.TryGetValue(target, out var path))
						return IntPtr.Zero;
					handle = NativeLibrary.Load(path);
					_nativeHandles[target] = handle;
					return handle;
				}
			});
		}
	}

	private static DateTime NormalizeUtc(DateTime value)
		=> value.Kind == DateTimeKind.Unspecified
			? DateTime.SpecifyKind(value, DateTimeKind.Utc)
			: value.ToUniversalTime();

	private static DateTime ToTaipeiDate(DateTime value)
		=> DateTime.SpecifyKind(NormalizeUtc(value).AddHours(8).Date, DateTimeKind.Unspecified);
}
