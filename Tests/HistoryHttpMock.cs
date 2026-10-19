namespace StockSharp.Connectors.Tests;

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

sealed class HistoryHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return Task.FromResult(response(request));
	}

	public static void ReplaceClient(object adapter, HttpClient client)
	{
		var field = adapter.GetType().GetField("_client", BindingFlags.Instance | BindingFlags.NonPublic);
		((HttpClient)field.GetValue(adapter)).Dispose();
		field.SetValue(adapter, client);
	}
}

sealed class HistoryHttpServer : IAsyncDisposable
{
	private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
	private readonly CancellationTokenSource _source;
	private readonly Task _run;

	public HistoryHttpServer(Func<string> response, CancellationToken cancellationToken)
	{
		_source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		_listener.Start();
		Address = new($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");
		_run = RunAsync(response, _source.Token);
	}

	public Uri Address { get; }
	public string RequestTarget { get; private set; }

	private async Task RunAsync(Func<string> response, CancellationToken cancellationToken)
	{
		while (true)
		{
			using var client = await _listener.AcceptTcpClientAsync(cancellationToken);
			await using var stream = client.GetStream();
			using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
			RequestTarget = (await reader.ReadLineAsync(cancellationToken))?.Split(' ')[1];
			while (!string.IsNullOrEmpty(await reader.ReadLineAsync(cancellationToken)))
			{
			}

			var body = Encoding.UTF8.GetBytes(response());
			var header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
			await stream.WriteAsync(header, cancellationToken);
			await stream.WriteAsync(body, cancellationToken);
		}
	}

	public async ValueTask DisposeAsync()
	{
		await _source.CancelAsync();
		try
		{
			await _run;
		}
		catch (OperationCanceledException) when (_source.IsCancellationRequested)
		{
		}
		finally
		{
			_listener.Stop();
			_source.Dispose();
		}
	}
}
