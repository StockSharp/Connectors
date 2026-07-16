namespace StockSharp.Bloomberg.Native;

using System.Collections;
using System.Reflection;

internal sealed class BloombergSdkBridge : IDisposable
{
	private readonly Assembly _assembly;
	private readonly Type _sessionOptionsType;
	private readonly Type _sessionType;
	private readonly Type _correlationIdType;
	private readonly Type _subscriptionType;
	private readonly Type _datetimeType;
	private object _session;

	public BloombergSdkBridge(string sdkPath, string host, int port)
	{
		_assembly = LoadAssembly(sdkPath);
		_sessionOptionsType = GetType("Bloomberglp.Blpapi.SessionOptions");
		_sessionType = GetType("Bloomberglp.Blpapi.Session");
		_correlationIdType = GetType("Bloomberglp.Blpapi.CorrelationID");
		_subscriptionType = GetType("Bloomberglp.Blpapi.Subscription");
		_datetimeType = GetType("Bloomberglp.Blpapi.Datetime");

		var options = Activator.CreateInstance(_sessionOptionsType);
		SetProperty(options, "ServerHost", host);
		SetProperty(options, "ServerPort", port);
		_session = Activator.CreateInstance(_sessionType, options)
			?? throw new InvalidOperationException("The Bloomberg SDK did not create a session.");
	}

	public string Version => _assembly.GetName().Version?.ToString() ?? "unknown";

	public void Start()
	{
		if (Invoke(_session, "Start") is not bool isStarted || !isStarted)
			throw new InvalidOperationException("The Bloomberg BLPAPI session failed to start.");
	}

	public void OpenService(string service)
	{
		if (Invoke(_session, "OpenService", service) is not bool isOpened || !isOpened)
			throw new InvalidOperationException($"The Bloomberg service '{service}' failed to open.");
	}

	public object NextEvent(long timeoutMilliseconds)
		=> Invoke(_session, "NextEvent", timeoutMilliseconds);

	public string GetEventType(object eventData)
		=> GetProperty(eventData, "Type")?.ToString();

	public IEnumerable<object> GetMessages(object eventData)
	{
		if (eventData is not IEnumerable enumerable)
			yield break;
		foreach (var message in enumerable)
			yield return message;
	}

	public string GetMessageType(object message)
		=> GetProperty(message, "MessageType")?.ToString();

	public long GetCorrelationId(object message)
	{
		var correlation = GetProperty(message, "CorrelationID");
		return correlation == null ? 0 : Convert.ToInt64(GetProperty(correlation, "Value"), CultureInfo.InvariantCulture);
	}

	public object CreateRequest(string serviceName, string requestType)
	{
		var service = Invoke(_session, "GetService", serviceName)
			?? throw new InvalidOperationException($"Bloomberg service '{serviceName}' is unavailable.");
		return Invoke(service, "CreateRequest", requestType)
			?? throw new InvalidOperationException($"Bloomberg request '{requestType}' was not created.");
	}

	public void SendRequest(object request, long correlationId)
		=> Invoke(_session, "SendRequest", request, CreateCorrelationId(correlationId));

	public object Subscribe(string topic, string fields, string options, long correlationId)
	{
		var correlation = CreateCorrelationId(correlationId);
		var subscription = Activator.CreateInstance(_subscriptionType,
			topic, fields ?? string.Empty, options ?? string.Empty, correlation)
			?? throw new InvalidOperationException("The Bloomberg subscription was not created.");
		var subscriptions = Array.CreateInstance(_subscriptionType, 1);
		subscriptions.SetValue(subscription, 0);
		Invoke(_session, "Subscribe", subscriptions);
		return subscription;
	}

	public object Subscribe(string topic, long correlationId)
	{
		var subscription = Activator.CreateInstance(_subscriptionType,
			topic, CreateCorrelationId(correlationId))
			?? throw new InvalidOperationException("The Bloomberg subscription was not created.");
		var subscriptions = Array.CreateInstance(_subscriptionType, 1);
		subscriptions.SetValue(subscription, 0);
		Invoke(_session, "Subscribe", subscriptions);
		return subscription;
	}

	public void Unsubscribe(long correlationId)
		=> Invoke(_session, "Unsubscribe", CreateCorrelationId(correlationId));

	public void Set(object request, string name, object value)
	{
		if (value is DateTime time)
			value = Activator.CreateInstance(_datetimeType, time);
		else if (value is decimal decimalValue)
			value = (double)decimalValue;
		Invoke(request, "Set", name, value);
	}

	public object GetElement(object value, string name)
		=> Invoke(value, "GetElement", name);

	public object GetElement(object value, int index)
		=> Invoke(value, "GetElement", index);

	public object GetValueAsElement(object value, int index)
		=> Invoke(value, "GetValueAsElement", index);

	public object AppendElement(object value)
		=> Invoke(value, "AppendElement");

	public void AppendValue(object value, object item)
		=> Invoke(value, "AppendValue", item is decimal number ? (double)number : item);

	public void SetElement(object value, string name, object item)
		=> Invoke(value, "SetElement", name, item is decimal number ? (double)number : item);

	public void SetValue(object value, object item)
		=> Invoke(value, "SetValue", item is decimal number ? (double)number : item);

	public int GetNumValues(object value)
		=> Convert.ToInt32(GetProperty(value, "NumValues"), CultureInfo.InvariantCulture);

	public int GetNumElements(object value)
		=> Convert.ToInt32(GetProperty(value, "NumElements"), CultureInfo.InvariantCulture);

	public bool HasElement(object value, string name)
	{
		try
		{
			return Invoke(value, "HasElement", name) is true;
		}
		catch
		{
			return false;
		}
	}

	public string TryGetString(object value, string name)
		=> TryGet(value, "GetElementAsString", name)?.ToString();

	public decimal? TryGetDecimal(object value, string name)
	{
		var result = TryGet(value, "GetElementAsFloat64", name)
			?? TryGet(value, "GetElementAsInt64", name)
			?? TryGet(value, "GetElementAsInt32", name);
		return result == null ? null : Convert.ToDecimal(result, CultureInfo.InvariantCulture);
	}

	public long? TryGetInt64(object value, string name)
	{
		var result = TryGet(value, "GetElementAsInt64", name) ?? TryGet(value, "GetElementAsInt32", name);
		return result == null ? null : Convert.ToInt64(result, CultureInfo.InvariantCulture);
	}

	public DateTime? TryGetDateTime(object value, string name)
	{
		var result = TryGet(value, "GetElementAsDatetime", name);
		if (result == null)
			return null;
		var time = (DateTime)Invoke(result, "ToSystemDateTime");
		return ToUtc(time);
	}

	public string TryGetValueAsString(object value, int index)
		=> TryInvoke(value, "GetValueAsString", index)?.ToString();

	public decimal? TryGetValueAsDecimal(object value, int index)
	{
		var result = TryInvoke(value, "GetValueAsFloat64", index)
			?? TryInvoke(value, "GetValueAsInt64", index)
			?? TryInvoke(value, "GetValueAsInt32", index);
		return result == null ? null : Convert.ToDecimal(result, CultureInfo.InvariantCulture);
	}

	public DateTime? TryGetValueAsDateTime(object value, int index)
	{
		var result = TryInvoke(value, "GetValueAsDatetime", index);
		if (result == null)
			return null;
		var time = (DateTime)Invoke(result, "ToSystemDateTime");
		return ToUtc(time);
	}

	public string GetText(object value)
		=> value?.ToString();

	public void DisposeObject(object value)
	{
		if (value is IDisposable disposable)
			disposable.Dispose();
	}

	public void Dispose()
	{
		var session = Interlocked.Exchange(ref _session, null);
		if (session == null)
			return;
		try
		{
			Invoke(session, "Stop");
		}
		catch
		{
		}
		DisposeObject(session);
	}

	private object TryGet(object value, string method, string name)
		=> HasElement(value, name) ? TryInvoke(value, method, name) : null;

	private object CreateCorrelationId(long value)
		=> Activator.CreateInstance(_correlationIdType, value);

	private static DateTime ToUtc(DateTime time)
		=> time.Kind switch
		{
			DateTimeKind.Utc => time,
			DateTimeKind.Local => time.ToUniversalTime(),
			_ => DateTime.SpecifyKind(time, DateTimeKind.Utc),
		};

	private Type GetType(string name)
		=> _assembly.GetType(name, true, false);

	private static Assembly LoadAssembly(string sdkPath)
	{
		if (!sdkPath.IsEmpty())
		{
			var path = Directory.Exists(sdkPath)
				? Path.Combine(sdkPath, "Bloomberglp.Blpapi.dll")
				: sdkPath;
			if (!File.Exists(path))
				throw new FileNotFoundException("Bloomberglp.Blpapi.dll was not found.", path);
			return Assembly.LoadFrom(Path.GetFullPath(path));
		}

		try
		{
			return Assembly.Load("Bloomberglp.Blpapi");
		}
		catch (Exception error)
		{
			throw new FileNotFoundException(
				"Install the Bloomberg BLPAPI .NET SDK or specify SdkPath.",
				"Bloomberglp.Blpapi.dll", error);
		}
	}

	private static object GetProperty(object target, string name)
		=> target?.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(target);

	private static void SetProperty(object target, string name, object value)
	{
		var property = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
			?? throw new MissingMemberException(target.GetType().FullName, name);
		property.SetValue(target, ConvertArgument(value, property.PropertyType));
	}

	private static object TryInvoke(object target, string name, params object[] args)
	{
		try
		{
			return Invoke(target, name, args);
		}
		catch
		{
			return null;
		}
	}

	private static object Invoke(object target, string name, params object[] args)
	{
		if (target == null)
			throw new ArgumentNullException(nameof(target));

		var methods = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
			.Where(method => method.Name == name && !method.ContainsGenericParameters &&
				method.GetParameters().Length == args.Length)
			.Select(method => (Method: method, Score: GetScore(method.GetParameters(), args)))
			.Where(candidate => candidate.Score >= 0)
			.OrderByDescending(candidate => candidate.Score)
			.ToArray();
		if (methods.Length == 0)
			throw new MissingMethodException(target.GetType().FullName, name);

		var selected = methods[0].Method;
		var parameters = selected.GetParameters();
		var converted = new object[args.Length];
		for (var index = 0; index < args.Length; index++)
			converted[index] = ConvertArgument(args[index], parameters[index].ParameterType);

		try
		{
			return selected.Invoke(target, converted);
		}
		catch (TargetInvocationException error) when (error.InnerException != null)
		{
			throw error.InnerException;
		}
	}

	private static int GetScore(ParameterInfo[] parameters, object[] args)
	{
		var score = 0;
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

			var argumentType = argument.GetType();
			if (parameterType == argumentType)
				score += 4;
			else if (parameterType.IsAssignableFrom(argumentType))
				score += 3;
			else if (CanConvert(argument, parameterType))
				score++;
			else
				return -1;
		}
		return score;
	}

	private static bool CanConvert(object value, Type targetType)
	{
		try
		{
			ConvertArgument(value, targetType);
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
			return Enum.ToObject(actualType, value);
		return Convert.ChangeType(value, actualType, CultureInfo.InvariantCulture);
	}
}
