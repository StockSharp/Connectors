namespace StockSharp.Transaq.Native;

using System.IO;
using System.Text;

using Ecng.Interop;

class ApiClient : Disposable
{
	private readonly Api _api;
	private readonly Action<string> _callback;
	private static readonly Encoding _encoding = Encoding.UTF8;

	public ApiClient(Action<string> callback, string dllPath, string logsPath, ApiLogLevels logLevel)
	{
		_callback = callback ?? throw new ArgumentNullException(nameof(callback));

		Directory.CreateDirectory(logsPath);

		_api = new Api(dllPath, OnCallback);

		try
		{
			using var handle = _encoding.ToHGlobal(logsPath + "\0");
			CheckErrorResult(_api.Initialize(handle.DangerousGetHandle(), (int)logLevel));
		}
		catch (Exception)
		{
			_api.Dispose();
			throw;
		}
	}

	public ValueTask<string> SendCommand(string command, CancellationToken _)
	{
		using var handle = _encoding.ToHGlobal(command);

		var pResult = _api.SendCommand(handle.DangerousGetHandle());
		return new(ProcessPtrResult(pResult));
	}

	public void SetLogLevel(ApiLogLevels logLevel)
	{
		CheckErrorResult(_api.SetLogLevel((int)logLevel));
	}

	private void OnCallback(IntPtr pData)
	{
		_callback(ProcessPtrResult(pData));
	}

	private string ProcessPtrResult(IntPtr pResult)
	{
		if (pResult != IntPtr.Zero)
		{
			var result = _encoding.ToString(pResult).Replace("&", "&amp;");
			_api.FreeMemory(pResult);
			return result;
		}

		return string.Empty;
	}

	private void CheckErrorResult(IntPtr ptr)
	{
		var errorMessage = ProcessPtrResult(ptr);

		if (!errorMessage.IsEmpty())
			throw new InvalidOperationException(errorMessage);
	}

	protected override void DisposeManaged()
	{
		CheckErrorResult(_api.UnInitialize());
		_api.Dispose();

		base.DisposeManaged();
	}
}