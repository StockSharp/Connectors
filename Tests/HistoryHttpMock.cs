namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

	public static void ReplaceClient(object adapter, HttpClient client, string fieldName = "_client")
	{
		var field = adapter.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
		((HttpClient)field.GetValue(adapter)).Dispose();
		field.SetValue(adapter, client);
	}
}

sealed class HistoryHttpServer : IAsyncDisposable
{
	private readonly Dictionary<string, string> _requestHeaders = new(StringComparer.OrdinalIgnoreCase);
	private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
	private readonly CancellationTokenSource _source;
	private readonly Task _run;

	public HistoryHttpServer(Func<string> response, CancellationToken cancellationToken)
		: this(() => new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent(response(), Encoding.UTF8, "application/json"),
		}, cancellationToken)
	{
	}

	public HistoryHttpServer(Func<HttpResponseMessage> response, CancellationToken cancellationToken)
	{
		_source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		_listener.Start();
		Address = new($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");
		_run = RunAsync(response, _source.Token);
	}

	public ConcurrentQueue<DateTime> RequestTimes { get; } = new();
	public Uri Address { get; }
	public string RequestTarget { get; private set; }
	public string RequestBody { get; private set; }
	public IReadOnlyDictionary<string, string> RequestHeaders => _requestHeaders;

	private async Task RunAsync(Func<HttpResponseMessage> response, CancellationToken cancellationToken)
	{
		while (true)
		{
			using var client = await _listener.AcceptTcpClientAsync(cancellationToken);
			await using var stream = client.GetStream();
			using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
			RequestTarget = (await reader.ReadLineAsync(cancellationToken))?.Split(' ')[1];
			_requestHeaders.Clear();
			while (await reader.ReadLineAsync(cancellationToken) is string line && !string.IsNullOrEmpty(line))
			{
				var separator = line.IndexOf(':');
				if (separator > 0)
					_requestHeaders[line[..separator]] = line[(separator + 1)..].Trim();
			}

			RequestBody = string.Empty;
			if (_requestHeaders.TryGetValue("Content-Length", out var contentLength) && int.Parse(contentLength) > 0)
			{
				var requestBody = new char[int.Parse(contentLength)];
				await reader.ReadBlockAsync(requestBody, cancellationToken);
				RequestBody = new string(requestBody);
			}

			RequestTimes.Enqueue(DateTime.UtcNow);
			using var reply = response();
			var body = reply.Content is null ? [] : await reply.Content.ReadAsByteArrayAsync(cancellationToken);
			var extraHeaders = string.Concat(reply.Headers.Select(pair => $"{pair.Key}: {string.Join(", ", pair.Value)}\r\n"));
			var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {(int)reply.StatusCode} {reply.StatusCode}\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\n{extraHeaders}Connection: close\r\n\r\n");
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
