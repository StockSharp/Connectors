namespace StockSharp.Connectors.Tests;

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;

sealed class WebSocketTestServer : IDisposable
{
	private readonly HttpListener _listener = new();

	public WebSocketTestServer()
	{
		using var reservation = new TcpListener(IPAddress.Loopback, 0);
		reservation.Start();
		var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
		reservation.Stop();
		_listener.Prefixes.Add($"http://127.0.0.1:{port}/");
		_listener.Start();
		Endpoint = $"ws://127.0.0.1:{port}/";
	}

	public string Endpoint { get; }

	public async Task<WebSocket> AcceptAsync(CancellationToken cancellationToken)
	{
		var context = await _listener.GetContextAsync().WaitAsync(cancellationToken);
		return (await context.AcceptWebSocketAsync(null)).WebSocket;
	}

	public static async Task<JObject> ReadRequestAsync(WebSocket socket, CancellationToken cancellationToken)
	{
		using var body = new MemoryStream();
		var buffer = new byte[4096];
		WebSocketReceiveResult frame;
		do
		{
			frame = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
			body.Write(buffer, 0, frame.Count);
		}
		while (!frame.EndOfMessage);
		return JObject.Parse(Encoding.UTF8.GetString(body.ToArray()));
	}

	public static Task ReplyAsync(WebSocket socket, long requestId, long subscriptionId, CancellationToken cancellationToken)
	{
		var reply = Encoding.UTF8.GetBytes($$"""{"jsonrpc":"2.0","id":{{requestId}},"result":{{subscriptionId}}}""");
		return socket.SendAsync(new ArraySegment<byte>(reply), WebSocketMessageType.Text, true, cancellationToken);
	}

	public void Dispose() => _listener.Close();
}
