namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Fix;
using StockSharp.Fix.Native;
using StockSharp.Messages;

/// <summary>
/// A FIX disconnect is over when the venue has ended the session, not when the logout is sent: a venue that holds a
/// login to one session refuses a logon that arrives while it still holds the old one.
/// </summary>
[TestClass]
[CLSCompliant(false)]
public class FixLogoutTests : BaseTestClass
{
	private const char _soh = (char)1;

	private sealed class QuickGiveUpAdapter(IdGenerator transactionIdGenerator) : FixMessageAdapter(transactionIdGenerator)
	{
		public override TimeSpan DisconnectTimeout => TimeSpan.FromSeconds(1);
	}

	[TestMethod]
	[Timeout(30000, CooperativeCancellation = true)]
	public async Task ADisconnectIsReportedOnlyOnceTheVenueHasLetTheSessionGo()
	{
		var released = false;

		var result = await DisconnectAsync(new FixMessageAdapter(new IncrementalIdGenerator()), async (stream, seqNum, ct) =>
		{
			// The venue takes its time letting the session go, and only then confirms the logout.
			await Task.Delay(300, ct);
			Volatile.Write(ref released, true);
			await stream.WriteAsync(Frame(FixMessages.Logout, seqNum, string.Empty), ct);
		}, () => !Volatile.Read(ref released), confirmLogon: true);

		IsFalse(result.isHeld, "the disconnect was reported while the venue still held the session");
	}

	[TestMethod]
	[Timeout(30000, CooperativeCancellation = true)]
	public async Task AVenueThatConfirmsAndStaysOnTheLineLetsTheDisconnectEndAtOnce()
	{
		var result = await DisconnectAsync(new FixMessageAdapter(new IncrementalIdGenerator()),
			(stream, seqNum, ct) => stream.WriteAsync(Frame(FixMessages.Logout, seqNum, string.Empty), ct).AsTask(),
			() => false, confirmLogon: true);

		IsTrue(result.elapsed < TimeSpan.FromSeconds(2), $"a confirmed logout was waited on for {result.elapsed}");
	}

	[TestMethod]
	[Timeout(30000, CooperativeCancellation = true)]
	public async Task AVenueThatNeverAnswersHoldsTheDisconnectNoLongerThanItsTimeout()
	{
		var result = await DisconnectAsync(new QuickGiveUpAdapter(new IncrementalIdGenerator()),
			(_, _, _) => Task.CompletedTask,
			() => false, confirmLogon: true);

		IsTrue(result.elapsed < TimeSpan.FromSeconds(4), $"an unanswered logout held the disconnect for {result.elapsed}");
		AreEqual(1, result.disconnects);
	}

	[TestMethod]
	[Timeout(30000, CooperativeCancellation = true)]
	public async Task AConfirmedLogoutIsReportedAsOneDisconnect()
	{
		var result = await DisconnectAsync(new FixMessageAdapter(new IncrementalIdGenerator()),
			(stream, seqNum, ct) => stream.WriteAsync(Frame(FixMessages.Logout, seqNum, string.Empty), ct).AsTask(),
			() => false, confirmLogon: true);

		AreEqual(1, result.disconnects);
	}

	[TestMethod]
	[Timeout(30000, CooperativeCancellation = true)]
	public async Task AVenueThatHangsUpOnTheLogoutIsReportedAsOneDisconnect()
	{
		var result = await DisconnectAsync(new FixMessageAdapter(new IncrementalIdGenerator()),
			(stream, _, _) =>
			{
				stream.Close();
				return Task.CompletedTask;
			},
			() => false, confirmLogon: true);

		AreEqual(1, result.disconnects);
	}

	[TestMethod]
	[Timeout(30000, CooperativeCancellation = true)]
	public async Task DisconnectBeforeLogonConfirmationDoesNotReportAConnectError()
	{
		var receivedLogout = false;
		var result = await DisconnectAsync(new FixMessageAdapter(new IncrementalIdGenerator()),
			(stream, _, _) =>
			{
				Volatile.Write(ref receivedLogout, true);
				stream.Socket.Shutdown(SocketShutdown.Send);
				return Task.CompletedTask;
			},
			() => !Volatile.Read(ref receivedLogout), confirmLogon: false);

		IsFalse(result.isHeld, "the venue did not receive the logout before the disconnect");
		AreEqual(1, result.disconnects);
		AreEqual(0, result.connectErrors);
	}

	/// <summary>
	/// Connects to a loopback venue and observes connection events during disconnect.
	/// </summary>
	private async Task<(bool isHeld, TimeSpan elapsed, int disconnects, int connectErrors)> DisconnectAsync(
		FixMessageAdapter adapter, Func<NetworkStream, int, CancellationToken, Task> onLogout, Func<bool> isHeld, bool confirmLogon)
	{
		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();

		using var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);

		try
		{
			var venue = ServeAsync(listener, onLogout, confirmLogon, cts.Token);

			using (adapter)
			{
				adapter.Address = listener.LocalEndpoint;
				adapter.SenderCompId = "CLIENT";
				adapter.TargetCompId = "VENUE";
				adapter.IsResetCounter = true;

				var connected = new TaskCompletionSource();
				bool? reportedWhileHeld = null;
				var disconnects = 0;
				var connectErrors = 0;

				adapter.NewOutMessageAsync += (message, _) =>
				{
					if (message is ConnectMessage { Error: null })
						connected.TrySetResult();
					else if (message is ConnectMessage)
						Interlocked.Increment(ref connectErrors);
					else if (message is DisconnectMessage)
					{
						reportedWhileHeld ??= isHeld();
						Interlocked.Increment(ref disconnects);
					}

					return default;
				};

				await adapter.SendInMessageAsync(new ConnectMessage(), CancellationToken);
				if (confirmLogon)
					await connected.Task.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken);

				var watch = Stopwatch.StartNew();
				await adapter.SendInMessageAsync(new DisconnectMessage(), CancellationToken);
				watch.Stop();
				await venue.WaitAsync(CancellationToken);

				// A second report would come from the reader, which may still be finishing its last frame.
				await Task.Delay(500, CancellationToken);

				IsNotNull(reportedWhileHeld, "no disconnect was reported");

				return (reportedWhileHeld.Value, watch.Elapsed, Volatile.Read(ref disconnects), Volatile.Read(ref connectErrors));
			}
		}
		finally
		{
			cts.Cancel();
			listener.Stop();
		}
	}

	/// <summary>
	/// Handles logout and sends a logon confirmation when requested by the test.
	/// </summary>
	private static async Task ServeAsync(TcpListener listener, Func<NetworkStream, int, CancellationToken, Task> onLogout, bool confirmLogon, CancellationToken cancellationToken)
	{
		using var client = await listener.AcceptTcpClientAsync(cancellationToken);
		var stream = client.GetStream();
		var seqNum = 1;

		await foreach (var msgType in ReadTypesAsync(stream, cancellationToken))
		{
			if (confirmLogon && msgType == FixMessages.Logon)
				await stream.WriteAsync(Frame(FixMessages.Logon, seqNum++, "98=0|108=60|"), cancellationToken);
			else if (msgType == FixMessages.Logout)
				await onLogout(stream, seqNum++, cancellationToken);
		}
	}

	// The type of each frame the client sends, until it hangs up.
	private static async IAsyncEnumerable<string> ReadTypesAsync(NetworkStream stream, [EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var buffer = new byte[4096];
		var text = new StringBuilder();

		while (true)
		{
			int read;

			try
			{
				read = await stream.ReadAsync(buffer, cancellationToken);
			}
			catch (Exception ex) when (ex is IOException or ObjectDisposedException)
			{
				yield break;
			}

			if (read == 0)
				yield break;

			text.Append(Encoding.ASCII.GetString(buffer, 0, read));

			while (true)
			{
				var received = text.ToString();
				var checkSum = received.IndexOf($"{_soh}10=", StringComparison.Ordinal);

				if (checkSum < 0)
					break;

				var end = received.IndexOf(_soh, checkSum + 1);

				if (end < 0)
					break;

				text.Remove(0, end + 1);

				var type = received.IndexOf($"{_soh}35=", StringComparison.Ordinal) + 4;
				yield return received[type..received.IndexOf(_soh, type)];
			}
		}
	}

	private static byte[] Frame(string msgType, int seqNum, string fields)
	{
		var body = $"35={msgType}|49=VENUE|56=CLIENT|34={seqNum}|52={DateTime.UtcNow:yyyyMMdd-HH:mm:ss.fff}|{fields}".Replace('|', _soh);
		var head = $"8={FixVersions.Fix44}|9={body.Length}|".Replace('|', _soh);
		var checkSum = (head + body).Sum(c => (int)c) % 256;

		return Encoding.ASCII.GetBytes($"{head}{body}10={checkSum:000}{_soh}");
	}
}
