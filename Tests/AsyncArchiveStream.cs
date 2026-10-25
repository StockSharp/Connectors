namespace StockSharp.Connectors.Tests;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

sealed class AsyncArchiveStream(byte[] data) : Stream
{
	private readonly MemoryStream _source = data is null ? null : new(data, false);

	public bool IsDisposed { get; private set; }

	public bool IsReadStarted { get; private set; }

	public bool IsReadCanceled { get; private set; }

	public override bool CanRead => !IsDisposed;

	public override bool CanSeek => false;

	public override bool CanWrite => false;

	public override long Length => throw new NotSupportedException();

	public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

	public override int Read(byte[] buffer, int offset, int count)
		=> throw new InvalidOperationException("Archive body must not be read synchronously.");

	public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
	{
		IsReadStarted = true;

		if (_source is not null)
			return await _source.ReadAsync(buffer, cancellationToken);

		try
		{
			await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
		}
		catch (OperationCanceledException)
		{
			IsReadCanceled = true;
			throw;
		}
		return 0;
	}

	public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
		=> ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

	public override void Flush() => throw new NotSupportedException();

	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

	public override void SetLength(long value) => throw new NotSupportedException();

	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

	protected override void Dispose(bool disposing)
	{
		IsDisposed = true;
		if (disposing)
			_source?.Dispose();
		base.Dispose(disposing);
	}
}
