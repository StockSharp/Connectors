namespace StockSharp.Directa.Native;

sealed class DirectaHistoryClient : BaseLogReceiver
{
    private sealed class RequestState(string prefix,
        string endMarker)
    {
        private readonly List<string> _lines = [];

        public TaskCompletionSource<string[]> Completion
        { get; } = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

        public void Process(string line)
        {
            if (line.EqualsIgnoreCase(endMarker))
            {
                Completion.TrySetResult(_lines.ToArray());
                return;
            }
            if (line.StartsWith(
                "ERR;", StringComparison.OrdinalIgnoreCase))
            {
                var parts = DirectaProtocol.Split(line);
                var code = parts.Length > 2 &&
                    int.TryParse(
                        parts[^1],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var parsed)
                            ? parsed : 0;
                Completion.TrySetException(
                    new InvalidOperationException(
                        DirectaProtocol.GetError(code)));
                return;
            }
            if (line.StartsWith(
                    "Wrong ", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith(
                    "Not enough ", StringComparison.OrdinalIgnoreCase))
            {
                Completion.TrySetException(
                    new InvalidOperationException(line));
                return;
            }
            if (line.StartsWith(
                prefix, StringComparison.OrdinalIgnoreCase))
                _lines.Add(line);
        }
    }

    private readonly DirectaLineClient _client;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _requestSync =
        new(1, 1);
    private RequestState _request;

    public DirectaHistoryClient(
        EndPoint address, TimeSpan timeout)
    {
        _timeout = timeout;
        _client = new(address, "Directa_History")
        {
            Parent = this,
        };
        _client.LineReceived += ProcessLine;
        _client.Error += ProcessError;
    }

    public override string Name => "Directa_HistoryClient";

    public event Func<Exception,
        CancellationToken, ValueTask> Error;

    public Task Connect(
        CancellationToken cancellationToken)
        => _client.Connect(cancellationToken);

    public Task Disconnect(
        CancellationToken cancellationToken)
        => _client.Disconnect(cancellationToken);

    public Task<string[]> GetTicks(
        string ticker, DateTime? from, DateTime? to,
        int days, TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        ticker = DirectaProtocol.NormalizeTicker(ticker);
        var command = from is not null || to is not null
            ? $"TBTRANGE {ticker} " +
                $"{DirectaProtocol.ToHistoryTimestamp(
                    from ?? (to ?? DateTime.UtcNow).AddDays(-1),
                    timeZone)} " +
                DirectaProtocol.ToHistoryTimestamp(
                    to ?? DateTime.UtcNow, timeZone)
            : $"TBT {ticker} {Math.Max(1, days)}";
        return Request(
            command, "TBT;", "END TBT",
            cancellationToken);
    }

    public Task<string[]> GetCandles(
        string ticker, TimeSpan timeFrame,
        DateTime? from, DateTime? to, int days,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        ticker = DirectaProtocol.NormalizeTicker(ticker);
        var seconds = timeFrame.ToCandleSeconds();
        var command = from is not null || to is not null
            ? $"CANDLERANGE {ticker} " +
                $"{DirectaProtocol.ToHistoryTimestamp(
                    from ?? (to ?? DateTime.UtcNow).AddDays(-1),
                    timeZone)} " +
                $"{DirectaProtocol.ToHistoryTimestamp(
                    to ?? DateTime.UtcNow, timeZone)} " +
                seconds.ToString(
                    CultureInfo.InvariantCulture)
            : $"CANDLE {ticker} {Math.Max(1, days)} " +
                seconds.ToString(
                    CultureInfo.InvariantCulture);
        return Request(
            command, "CANDLE;", "END CANDLES",
            cancellationToken);
    }

    private async Task<string[]> Request(
        string command, string prefix,
        string endMarker,
        CancellationToken cancellationToken)
    {
        if (_timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(_timeout), _timeout,
                "Directa request timeout must be positive.");
        }

        await _requestSync.WaitAsync(cancellationToken);
        try
        {
            var request = new RequestState(
                prefix, endMarker);
            _request = request;
            await _client.Send(
                command, cancellationToken);
            return await request.Completion.Task.WaitAsync(
                _timeout, cancellationToken);
        }
        finally
        {
            _request = null;
            _requestSync.Release();
        }
    }

    private ValueTask ProcessLine(
        string line, CancellationToken cancellationToken)
    {
        _request?.Process(line);
        return default;
    }

    private ValueTask ProcessError(
        Exception error,
        CancellationToken cancellationToken)
    {
        _request?.Completion.TrySetException(error);
        return Error is { } handler
            ? handler(error, cancellationToken) : default;
    }

    protected override void DisposeManaged()
    {
        _client.LineReceived -= ProcessLine;
        _client.Error -= ProcessError;
        _client.Dispose();
        _request?.Completion.TrySetCanceled();
        _request = null;
        _requestSync.Dispose();
        base.DisposeManaged();
    }
}
