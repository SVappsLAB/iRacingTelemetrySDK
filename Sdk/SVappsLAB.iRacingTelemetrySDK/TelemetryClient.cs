/**
 * Copyright (C) 2024-2026 Scott Velez
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
**/

using Microsoft.Extensions.Logging;
using SVappsLAB.iRacingTelemetrySDK.DataProviders;
using SVappsLAB.iRacingTelemetrySDK.Metrics;
using SVappsLAB.iRacingTelemetrySDK.SimControl;
using SVappsLAB.iRacingTelemetrySDK.YamlParsing;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace SVappsLAB.iRacingTelemetrySDK;

public enum ConnectState
{
    Disconnected,
    Connected
}


public record class TelemetryVariable
{
    // type of the data value
    public Type Type { get; init; } = default!;
    // number of entries (array)
    public int Length { get; init; }
    // treat value as time
    public bool IsTimeValue { get; init; }
    // variable name
    public string Name { get; init; } = default!;
    // description
    public string Desc { get; init; } = default!;
    // unit.  something like "kg/m^2"
    public string Units { get; init; } = default!;
}

/// <summary>
/// Placeholder telemetry data type for use with <see cref="TelemetryClient{T}"/> when you only need
/// dynamic, string-based variable lookup (<see cref="TelemetryClient{T}.GetValue(string)"/>)
/// and don't want to declare a <c>RequiredTelemetryVars</c>-annotated struct.
/// </summary>
/// <remarks>
/// Useful when embedding the SDK somewhere the Roslyn source generator is inconvenient to use (e.g. some
/// game engine build pipelines), or when building a fully dynamic dashboard where the set of telemetry
/// values is chosen by the end user at runtime rather than fixed at compile time.
/// <code>
/// await using var client = TelemetryClient&lt;DynamicTelemetryData&gt;.Create(logger);
/// await client.Monitor(new TelemetryHandlers&lt;DynamicTelemetryData&gt;
/// {
///     OnTelemetryUpdate = _ =>
///     {
///         if (client.GetValue("Speed") is float speed)
///         {
///             // use speed
///         }
///         return Task.CompletedTask;
///     }
/// }, cancellationToken);
/// </code>
/// </remarks>
public readonly struct DynamicTelemetryData
{
}

/**
 * Options for IBT file processing.
 *
 * @param ibtFilePath The file path of the IBT file.
 * @param playBackSpeedMultiplier The playback speed multiplier for the IBT file.
 * A value of 1 is 1x speed, a value of 10 is 10x speed. a value of int.MaxValue is as fast as possible
 */
public record class IBTOptions
{
    public string IbtFilePath
    {
        get; init;
    }
    public int PlayBackSpeedMultiplier { get; init; }

    public IBTOptions(string ibtFilePath, int playBackSpeedMultiplier = int.MaxValue)
    {
        IbtFilePath = ibtFilePath;
        if (playBackSpeedMultiplier <= 0)
        {
            throw new ArgumentOutOfRangeException("Playback speed must be greater than 0");
        }

        PlayBackSpeedMultiplier = playBackSpeedMultiplier;
    }
}

/// <summary>
/// Controls how telemetry samples are handed off from the background processing loop to
/// <see cref="TelemetryHandlers{T}.OnTelemetryUpdate"/>, and what that means for
/// <see cref="TelemetryClient{T}.GetValue(string)"/>.
/// </summary>
public enum TelemetryDeliveryMode
{
    /// <summary>
    /// This is the highly preferred default, high-throughput mode.
    /// </summary>
    Async,

    /// <summary>
    /// This is the slower, blocking, optional sychronous mode. 
    /// Only supported via
    /// <see cref="TelemetryClient{T}.Monitor(TelemetryHandlers{T}, CancellationToken)"/> with
    /// <see cref="TelemetryHandlers{T}.OnTelemetryUpdate"/> set - both <c>Monitor(CancellationToken)</c>
    /// and a handlers object without <c>OnTelemetryUpdate</c> throw <see cref="InvalidOperationException"/>
    /// immediately, since telemetry would otherwise be silently discarded. The
    /// <see cref="TelemetryClient{T}.TelemetryData"/> async-enumerable stream is not populated in this mode.
    /// </summary>
    Synchronous
}

/// <summary>
/// Configuration options that control various aspects of telemetry processing.
/// </summary>
public class ClientOptions
{
    /// <summary>
    /// Controls how telemetry samples are delivered to handlers, and whether
    /// <see cref="TelemetryClient{T}.GetValue(string)"/> is usable at all. Defaults to
    /// <see cref="TelemetryDeliveryMode.Async"/>, in which <c>GetValue</c> throws
    /// <see cref="InvalidOperationException"/> - use <see cref="TelemetryDeliveryMode.Synchronous"/> if you
    /// need <c>GetValue</c>.
    /// </summary>
    public TelemetryDeliveryMode DeliveryMode { get; init; } = TelemetryDeliveryMode.Async;

    /// <summary>
    /// Optional factory for creating metrics to monitor telemetry processing performance.
    /// Tracks processing duration and record counts.
    /// </summary>
    public IMeterFactory? MeterFactory { get; init; }
}

public class TelemetryClient<T> : ITelemetryClient<T>, IAsyncDisposable where T : struct
{
    const int DATA_READY_TIMEOUT_MS = 30;
    const int INITIALIZATION_DELAY_MS = 1000;
    private const int CHANNEL_SIZE = 60;        // 1 second of buffering at 60 Hz - helps slow consumers
    private const string MONITOR_UNAVAILABLE_MESSAGE =
        "Monitor() is already running or has already completed on this instance. " +
        "Create a new TelemetryClient instance to monitor again.";
    private static readonly TimeSpan HANDLER_SHUTDOWN_TIMEOUT = TimeSpan.FromSeconds(5);
    readonly Channel<ConnectState> _connectStateChannel;
    readonly Channel<Exception> _errorChannel;
    readonly Channel<string> _rawSessionDataChannel;
    readonly Channel<TelemetrySessionInfo> _sessionDataChannel;
    readonly Channel<StampedSample<T>> _telemetryDataChannel;
    readonly Channel<string> _internalSessionInfoChannel;
    IMetricsService? _metricsService;
    Task<int>? _dataProcessingTask;
    Task? _sessionInfoProcessorTask;
    CancellationTokenSource? _internalCts;    // internal cancellation token source - linked to external token, owned by TelemetryClient
    private ISessionInfoParser _sessionInfoParser;
    private ILogger _logger;

    private volatile bool _isInitialized = false;
    private volatile bool _isPaused = false;
    private bool _lastConnectionStatus = false;

    IBTOptions? _ibtOptions;

    IDataProvider _dataProvider;
    private readonly TelemetryDataAccessor<T> _telemetryAccessor;

    // lazy so cross-platform IBT playback works - the win32 sender throws on non-windows
    private readonly Lazy<ISimController> _simControl;

    // Disposal state tracking
    private volatile bool _disposed = false;

    // one-shot monitor guard - 0=not started, 1=started (never reset)
    private int _monitorStarted = 0;

    // telemetry variables caching
    private IReadOnlyList<TelemetryVariable>? _cachedTelemetryVariables;
    private VarHeaderDictionary? _cachedTelemetryVariablesSourceHeaders;
    private readonly object _telemetryVariablesLock = new object();

    private readonly TelemetryDeliveryMode _deliveryMode;
    // set only when _deliveryMode == Synchronous; invoked inline by the producer loop
    private Func<T, Task>? _syncTelemetryHandler;

    // internal usage only. thrown when _syncTelemetryHandler itself throws. used to distguish
    // between handler fault and provider error
    private sealed class SyncHandlerFaultException(Exception innerException) : Exception(innerException.Message, innerException);

    /// <inheritdoc />
    public IAsyncEnumerable<ConnectState> ConnectStates => GetConnectStatesEnumerable();

    /// <inheritdoc />
    public IAsyncEnumerable<Exception> Errors => GetErrorsEnumerable();

    /// <inheritdoc />
    public IAsyncEnumerable<string> SessionDataYaml => GetRawSessionDataEnumerable();

    /// <inheritdoc />
    public IAsyncEnumerable<TelemetrySessionInfo> SessionData => GetSessionDataEnumerable();

    /// <inheritdoc />
    public IAsyncEnumerable<T> TelemetryData =>
        _deliveryMode == TelemetryDeliveryMode.Synchronous
            ? throw new InvalidOperationException(
                $"{nameof(TelemetryData)} is not available when {nameof(ClientOptions.DeliveryMode)} is " +
                $"{nameof(TelemetryDeliveryMode.Synchronous)} - use {nameof(Monitor)}({nameof(TelemetryHandlers<T>)}<T>, CancellationToken) instead.")
            : GetTelemetryDataEnumerable();

    public bool IsPaused => _isPaused;

    public ISimController SimControl => _simControl.Value;

    // Async stream wrapper methods that expose streams as IAsyncEnumerable
    private async IAsyncEnumerable<ConnectState> GetConnectStatesEnumerable([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in _connectStateChannel.Reader.ReadAllAsync(cancellationToken))
            yield return item;
    }

    private async IAsyncEnumerable<Exception> GetErrorsEnumerable([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in _errorChannel.Reader.ReadAllAsync(cancellationToken))
            yield return item;
    }

    private async IAsyncEnumerable<string> GetRawSessionDataEnumerable([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in _rawSessionDataChannel.Reader.ReadAllAsync(cancellationToken))
            yield return item;
    }

    private async IAsyncEnumerable<TelemetrySessionInfo> GetSessionDataEnumerable([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in _sessionDataChannel.Reader.ReadAllAsync(cancellationToken))
            yield return item;
    }

    private async IAsyncEnumerable<T> GetTelemetryDataEnumerable([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var stamped in GetStampedTelemetryDataEnumerable(cancellationToken))
            yield return stamped.Sample;
    }

    // the metered side of the telemetry stream. both async paths read through here - the handler
    // loop and the public TelemetryData stream - so they report the same measurements
    private async IAsyncEnumerable<StampedSample<T>> GetStampedTelemetryDataEnumerable([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var stamped in _telemetryDataChannel.Reader.ReadAllAsync(cancellationToken))
        {
            var meters = _metricsService?.Telemetry;
            var handlerStart = meters?.RecordHandoff(stamped.AcquiredTimestamp) ?? TelemetryMeters.NotTiming;

            // an iterator only resumes past its yield when the consumer asks for the next record.
            // this span is exactly the consumer's work duration.
            // the finally covers a consumer breaking out of its loop, which disposes the enumerator instead of resuming it
            try
            {
                yield return stamped;
            }
            finally
            {
                meters?.RecordConsumed(stamped.AcquiredTimestamp, handlerStart);
            }
        }
    }

    /// <summary>
    /// creates a new instance of the telemetry client.
    /// </summary>
    /// <param name="logger">logger instance for diagnostic output.</param>
    /// <param name="ibtOptions">
    /// optional IBT file playback options. if null, client operates in live mode connecting to iRacing.
    /// </param>
    /// <returns>a new telemetry client instance configured for the specified mode.</returns>
    /// <remarks>
    /// <para><strong>Live Mode:</strong> when ibtOptions is null, the client connects to iRacing's shared memory.</para>
    /// <para><strong>IBT Mode:</strong> when ibtOptions is provided, the client plays back the specified IBT file.</para>
    /// </remarks>
    /// <exception cref="FileNotFoundException">
    /// thrown if ibtOptions specifies a file that doesn't exist.
    /// </exception>
    public static ITelemetryClient<T> Create(ILogger logger, IBTOptions? ibtOptions = null) =>
        new TelemetryClient<T>(logger, null, ibtOptions);

    /// <summary>
    /// creates a new instance of the telemetry client with advanced configuration options.
    /// </summary>
    /// <param name="logger">logger instance for diagnostic output.</param>
    /// <param name="ibtOptions">
    /// optional IBT file playback options. if null, client operates in live mode connecting to iRacing.
    /// </param>
    /// <param name="clientOptions">
    /// configuration options for metrics and other client behavior.
    /// </param>
    /// <returns>a new telemetry client instance configured for the specified mode.</returns>
    /// <remarks>
    /// <para><strong>Live Mode:</strong> when ibtOptions is null, the client connects to iRacing's shared memory.</para>
    /// <para><strong>IBT Mode:</strong> when ibtOptions is provided, the client plays back the specified IBT file.</para>
    /// <para><strong>Metrics:</strong> provide a MeterFactory via clientOptions to enable built-in diagnostic metrics.</para>
    /// </remarks>
    /// <exception cref="FileNotFoundException">
    /// thrown if ibtOptions specifies a file that doesn't exist.
    /// </exception>
    public static ITelemetryClient<T> Create(ILogger logger, IBTOptions? ibtOptions, ClientOptions clientOptions) =>
        new TelemetryClient<T>(logger, clientOptions, ibtOptions);

    private TelemetryClient(ILogger logger, ClientOptions? clientOptions = null, IBTOptions? ibtOptions = null)
    {
        // do a quick check for valid ibt file and bail immediately if not found
        if (ibtOptions != null && !File.Exists(ibtOptions.IbtFilePath))
        {
            throw new FileNotFoundException($"IBT file [{ibtOptions.IbtFilePath}] not found", ibtOptions.IbtFilePath);
        }

        _logger = logger;
        _ibtOptions = ibtOptions;
        _deliveryMode = clientOptions?.DeliveryMode ?? TelemetryDeliveryMode.Async;

        // initialize bounded channels
        var boundedChannelOptions = new BoundedChannelOptions(CHANNEL_SIZE)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        };
        _connectStateChannel = Channel.CreateBounded<ConnectState>(boundedChannelOptions);
        _errorChannel = Channel.CreateBounded<Exception>(boundedChannelOptions);
        _rawSessionDataChannel = Channel.CreateBounded<string>(
            boundedChannelOptions, _ => _metricsService?.SessionInfo.SessionDataYamlOverflow(1));
        _sessionDataChannel = Channel.CreateBounded<TelemetrySessionInfo>(
            boundedChannelOptions, _ => _metricsService?.SessionInfo.SessionDataOverflow(1));
        // a slow async consumer loses the oldest records rather than stalling the producer - count them
        _telemetryDataChannel = Channel.CreateBounded<StampedSample<T>>(
            boundedChannelOptions, _ => _metricsService?.Telemetry.ConsumerOverflow(1));

        // Internal session info processing channel
        var sessionInfoChannelOptions = new BoundedChannelOptions(10)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false // Both Live and IBT can write
        };
        // updates arriving faster than they can be parsed are evicted unparsed - count them
        _internalSessionInfoChannel = Channel.CreateBounded<string>(
            sessionInfoChannelOptions, _ => _metricsService?.SessionInfo.ParseBacklogOverflow(1));

        // initialize data provider
        _dataProvider = _ibtOptions == null ? new LiveDataProvider(_logger, RecordMissedTicks) : new IBTDataProvider(_logger, _ibtOptions, RecordPlaybackLag);

        _metricsService = new MetricsService(clientOptions?.MeterFactory, _ibtOptions == null ? "live" : "ibt");

        _telemetryAccessor = new TelemetryDataAccessor<T>(_logger);
        _sessionInfoParser = new YamlParser(_logger);

        _simControl = new Lazy<ISimController>(() => new SimController(_logger, () => IsConnected));
    }

    /// <inheritdoc />
    public async Task<int> Monitor(CancellationToken ct)
    {
        if (_deliveryMode == TelemetryDeliveryMode.Synchronous)
        {
            throw new InvalidOperationException(
                $"{nameof(TelemetryDeliveryMode)}.{nameof(TelemetryDeliveryMode.Synchronous)} requires an " +
                $"{nameof(TelemetryHandlers<T>.OnTelemetryUpdate)} handler - use " +
                $"{nameof(Monitor)}({nameof(TelemetryHandlers<T>)}<T>, CancellationToken) instead.");
        }

        BeginMonitor();
        return await RunMonitor(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> Monitor(TelemetryHandlers<T> handlers, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(handlers);

        if (_deliveryMode == TelemetryDeliveryMode.Synchronous && handlers.OnTelemetryUpdate == null)
        {
            throw new InvalidOperationException(
                $"{nameof(TelemetryDeliveryMode)}.{nameof(TelemetryDeliveryMode.Synchronous)} requires " +
                $"{nameof(TelemetryHandlers<T>.OnTelemetryUpdate)} to be set.");
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        BeginMonitor();

        var subscriptionTask = SubscribeToHandlers(handlers, linkedCts);
        var monitorTask = RunMonitor(linkedCts.Token);

        var completedTask = await Task.WhenAny(monitorTask, subscriptionTask).ConfigureAwait(false);

        if (completedTask == subscriptionTask && subscriptionTask.IsFaulted)
        {
            linkedCts.Cancel();
        }

        try
        {
            var result = await monitorTask.ConfigureAwait(false);
            await WaitForHandlersToComplete(subscriptionTask).ConfigureAwait(false);

            return result;
        }
        catch
        {
            linkedCts.Cancel();

            try
            {
                await WaitForHandlersToComplete(subscriptionTask).ConfigureAwait(false);
            }
            catch
            {
                // Preserve the original monitor or handler exception.
            }

            throw;
        }
    }

    private void BeginMonitor()
    {
        // atomically claim the one-shot monitor slot. compareExchange returns the original
        // value - if it was 0 (not started), we successfully claimed it. the flag is never
        // reset, enforcing both "already running" and "already completed"
        if (Interlocked.CompareExchange(ref _monitorStarted, 1, 0) != 0)
        {
            throw new InvalidOperationException(MONITOR_UNAVAILABLE_MESSAGE);
        }
    }

    private async Task<int> RunMonitor(CancellationToken ct)
    {
        Exception? monitorException = null;

        try
        {
            _logger.LogDebug("monitoring '{mode}' data", IsOnlineMode ? "Live" : "IBT");

            // create internal CTS linked to external token
            // this allows both external cancellation and internal control during disposal
            _internalCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            try
            {
                _sessionInfoProcessorTask = StartSessionInfoProcessorTask(_internalCts.Token);
                _dataProcessingTask = StartDataProcessorTask(_internalCts.Token);

                await Task.WhenAll(_dataProcessingTask, _sessionInfoProcessorTask).ConfigureAwait(false);
                var result = _dataProcessingTask.Result;  // safe since task already completed
                return result;
            }
            catch (Exception ex)
            {
                // unwrap a synchronous-handler fault so Monitor() sees the original
                var toThrow = ex is SyncHandlerFaultException fault ? fault.InnerException! : ex;

                _logger.LogError(toThrow, "Error in processing tasks");
                monitorException = toThrow;

                ExceptionDispatchInfo.Capture(toThrow).Throw();
                throw; // unreachable - Throw() always throws, but the compiler can't tell
            }
        }
        finally
        {
            CompleteAllChannels(monitorException);
            _internalCts?.Dispose();
            _internalCts = null;
        }
    }

    private Task SubscribeToHandlers(TelemetryHandlers<T> handlers, CancellationTokenSource faultCts)
    {
        var tasks = new List<Task>();

        if (handlers.OnTelemetryUpdate != null)
        {
            if (_deliveryMode == TelemetryDeliveryMode.Synchronous)
                // delivered inline by the producer loop instead - see DeliverTelemetryData
                _syncTelemetryHandler = handlers.OnTelemetryUpdate;
            else
                tasks.Add(RunHandlerLoop(HandleTelemetryData(handlers.OnTelemetryUpdate), faultCts));
        }

        if (handlers.OnSessionInfoUpdate != null)
            tasks.Add(RunHandlerLoop(HandleSessionData(handlers.OnSessionInfoUpdate), faultCts));

        if (handlers.OnRawSessionInfoUpdate != null)
            tasks.Add(RunHandlerLoop(HandleRawSessionData(handlers.OnRawSessionInfoUpdate), faultCts));

        if (handlers.OnConnectStateChanged != null)
            tasks.Add(RunHandlerLoop(HandleConnectStates(handlers.OnConnectStateChanged), faultCts));

        if (handlers.OnError != null)
            tasks.Add(RunHandlerLoop(HandleErrors(handlers.OnError), faultCts));

        return tasks.Count == 0 ? Task.CompletedTask : Task.WhenAll(tasks);
    }

    // a faulting handler must stop monitoring promptly, even when other handlers are
    // still draining their streams. cancelling the shared token makes RunMonitor exit,
    // which completes the channels and lets the remaining handler loops end.
    private static async Task RunHandlerLoop(Task handlerLoop, CancellationTokenSource faultCts)
    {
        try
        {
            await handlerLoop.ConfigureAwait(false);
        }
        catch
        {
            try
            {
                faultCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Monitor() already returned (handler-shutdown timeout) and disposed the CTS.
                // monitoring is over, so there is nothing left to cancel
            }
            throw;
        }
    }

    private async Task WaitForHandlersToComplete(Task subscriptionTask)
    {
        try
        {
            await subscriptionTask.WaitAsync(HANDLER_SHUTDOWN_TIMEOUT).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException(
                "Telemetry handler did not complete within 5 seconds during shutdown. Ensure callbacks return promptly.",
                ex);
        }
    }

    private async Task HandleTelemetryData(Func<T, Task> handler)
    {
        await foreach (var stamped in GetStampedTelemetryDataEnumerable().ConfigureAwait(false))
        {
            await handler(stamped.Sample).ConfigureAwait(false);
        }
    }

    private async Task HandleSessionData(Func<TelemetrySessionInfo, Task> handler)
    {
        await foreach (var data in SessionData.ConfigureAwait(false))
        {
            await handler(data).ConfigureAwait(false);
        }
    }

    private async Task HandleRawSessionData(Func<string, Task> handler)
    {
        await foreach (var data in SessionDataYaml.ConfigureAwait(false))
        {
            await handler(data).ConfigureAwait(false);
        }
    }

    private async Task HandleConnectStates(Func<ConnectState, Task> handler)
    {
        await foreach (var data in ConnectStates.ConfigureAwait(false))
        {
            await handler(data).ConfigureAwait(false);
        }
    }

    private async Task HandleErrors(Func<Exception, Task> handler)
    {
        await foreach (var data in Errors.ConfigureAwait(false))
        {
            await handler(data).ConfigureAwait(false);
        }
    }

    private Task StartSessionInfoProcessorTask(CancellationToken ct)
    {
        return ProcessSessionInfoChannel(ct);
    }

    private async Task<int> StartDataProcessorTask(CancellationToken ct)
    {
        try
        {
            _logger.LogDebug("{mode} data processor started", IsOnlineMode ? "Live" : "IBT");

            var task = IsOnlineMode ?
                ProcessLiveData(ct) :
                ProcessIbtData(ct);

            return await task.ConfigureAwait(false);
        }
        finally
        {
            // complete the session info channel. there will be no more data
            _internalSessionInfoChannel.Writer.TryComplete();
        }
    }

    public IReadOnlyList<TelemetryVariable> GetTelemetryVariables()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // the provider re-reads and reassigns its header dictionary whenever the var-buffer layout
        // changes (see DataProviderBase.GetHeader()), so reference identity doubles as a cheap version
        // check - a different instance means the cache is stale and must be rebuilt
        var varHeaders = _dataProvider.GetVarHeaders();

        // fast path - already cached against the current header set
        if (_cachedTelemetryVariables != null && ReferenceEquals(_cachedTelemetryVariablesSourceHeaders, varHeaders))
            return _cachedTelemetryVariables;

        if (varHeaders == null)
        {
            // not yet initialized (e.g. live mode before iRacing connects) - nothing to cache, so a
            // later call once headers are available isn't stuck returning this empty list forever
            _logger.LogDebug("Telemetry variable headers not available yet");
            return Array.Empty<TelemetryVariable>();
        }

        lock (_telemetryVariablesLock)
        {
            // double-check pattern
            if (_cachedTelemetryVariables != null && ReferenceEquals(_cachedTelemetryVariablesSourceHeaders, varHeaders))
                return _cachedTelemetryVariables;

            try
            {
                IReadOnlyList<TelemetryVariable> list = BuildTelemetryVariablesList(varHeaders);
                _cachedTelemetryVariables = list;
                _cachedTelemetryVariablesSourceHeaders = varHeaders;
                return list;
            }
            catch (ObjectDisposedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving telemetry variables");
                throw;
            }
        }
    }

    /// <inheritdoc/>
    public object? GetValue(string varName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_deliveryMode == TelemetryDeliveryMode.Async)
        {
            throw new InvalidOperationException(
                $"{nameof(GetValue)} is not available when {nameof(ClientOptions.DeliveryMode)} is " +
                $"{nameof(TelemetryDeliveryMode.Async)} - create the client with " +
                $"{nameof(TelemetryDeliveryMode.Synchronous)} delivery mode instead.");
        }

        return _dataProvider.GetVarValue(varName);
    }

    private unsafe IReadOnlyList<TelemetryVariable> BuildTelemetryVariablesList(VarHeaderDictionary varHeaders)
    {
        var list = new List<TelemetryVariable>();

        foreach (var vh in varHeaders.Values)
        {
            try
            {
                var tVar = new TelemetryVariable
                {
                    Type = DataProviderBase.GetClrType(vh.type, vh.count),

                    Length = vh.count,
                    IsTimeValue = vh.countAsTime,
                    Name = Marshal.PtrToStringAnsi((nint)vh.name) ?? string.Empty,
                    Desc = Marshal.PtrToStringAnsi((nint)vh.desc) ?? string.Empty,
                    Units = Marshal.PtrToStringAnsi((nint)vh.unit) ?? string.Empty,
                };
                list.Add(tVar);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error processing telemetry variable header");
                // continue processing other variables
            }
        }
        list.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return list;
    }

    /// <inheritdoc />
    public bool IsConnected
    {
        get
        {
            try
            {
                if (_isInitialized)
                {
                    return _dataProvider.IsConnected;
                }
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error checking connection status");
                return false;
            }
        }
    }

    /// <inheritdoc />
    public void Pause()
    {
        _isPaused = true;
        _logger.LogDebug("Telemetry client paused");
    }

    /// <inheritdoc />
    public void Resume()
    {
        _isPaused = false;
        _logger.LogDebug("Telemetry client resumed");
    }


    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        try
        {
            await Shutdown().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during disposal");
            throw;
        }
        finally
        {
            _disposed = true;
        }
    }

    private async Task Shutdown()
    {
        _logger.LogDebug("Shutting down");

        Exception? shutdownException = null;

        try
        {
            _logger.LogDebug("Monitor completion - shutting down processing tasks");

            // cancel internal CTS to signal graceful shutdown to processing tasks
            // this ensures tasks can exit their loops cleanly
            try
            {
                var internalCts = _internalCts;
                if (internalCts != null && !internalCts.IsCancellationRequested)
                {
                    _logger.LogDebug("Cancelling internal processing token");
                    internalCts.Cancel();
                }
            }
            catch (ObjectDisposedException)
            {
                // RunMonitor's finally disposed the CTS concurrently - monitoring already ended
            }

            // shut down processing tasks
            var tasksToWait = new List<Task>();
            if (_dataProcessingTask != null)
                tasksToWait.Add(_dataProcessingTask);
            if (_sessionInfoProcessorTask != null)
                tasksToWait.Add(_sessionInfoProcessorTask);

            if (tasksToWait.Count > 0)
            {
                // tasks should exit quickly now that token is cancelled
                // timeout is a safety net for unexpected hangs
                var shutdownTimeoutToken = new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;
                await Task.WhenAll(tasksToWait).WaitAsync(shutdownTimeoutToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during cancellation
        }
        catch (Exception ex)
        {
            if (ex is SyncHandlerFaultException fault)
            {
                // in Synchronous mode, a handler fault lives on _dataProcessingTask itself and was
                // already thrown so don't surface it a second time here.
                _logger.LogDebug(fault.InnerException, "Synchronous telemetry handler fault already reported by Monitor()");
            }
            else
            {
                _logger.LogError(ex, "Error shutting down main processing task");
                shutdownException ??= ex;
            }
        }

        CompleteAllChannels();

        // Dispose services
        if (_dataProvider != null)
        {
            await _dataProvider.DisposeAsync().ConfigureAwait(false);
        }
        if (_metricsService != null)
        {
            await _metricsService.DisposeAsync().ConfigureAwait(false);
        }

        // dispose internal CTS
        _internalCts?.Dispose();
        _internalCts = null;

        _logger.LogDebug("Shutdown completed");

        if (shutdownException != null)
        {
            throw shutdownException;
        }
    }

    private void CompleteAllChannels(Exception? exception = null)
    {
        try
        {
            _connectStateChannel.Writer.TryComplete(exception);
            _errorChannel.Writer.TryComplete(exception);
            _rawSessionDataChannel.Writer.TryComplete(exception);
            _sessionDataChannel.Writer.TryComplete(exception);
            _telemetryDataChannel.Writer.TryComplete(exception);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error completing channels");
        }
    }
    private bool IsOnlineMode => _ibtOptions == null;

    private void RecordMissedTicks(long count) => _metricsService?.Telemetry.TicksMissed(count);

    private void RecordPlaybackLag(TimeSpan lag) => _metricsService?.Playback.RecordLag(lag);

    private long MarkAcquired() => _metricsService?.Telemetry.MarkAcquired() ?? TelemetryMeters.NotTiming;

    private async Task ProcessSessionInfoChannel(CancellationToken ct)
    {
        _logger.LogDebug("Session info processor started");

        try
        {
            await foreach (var rawYaml in _internalSessionInfoChannel.Reader.ReadAllAsync(ct))
            {
                try
                {
                    _rawSessionDataChannel.Writer.TryWrite(rawYaml);

                    var sessionInfo = ParseSessionInfo(rawYaml);
                    if (sessionInfo != null)
                    {
                        _sessionDataChannel.Writer.TryWrite(sessionInfo);
                    }
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Error processing session info");
                    _errorChannel.Writer.TryWrite(e);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("Session info processor cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in session info processor");

            // write error to channel
            _errorChannel?.Writer.TryWrite(ex);
            throw;
        }

        _logger.LogDebug("Session info processor ended");
    }

    private TelemetrySessionInfo? ParseSessionInfo(string rawSessionInfoYaml)
    {
        TelemetrySessionInfo? sessionInfo = null;
        // a full pass over the yaml, but it's trivial
        var sizeBytes = System.Text.Encoding.UTF8.GetByteCount(rawSessionInfoYaml);
        var startTimestamp = Stopwatch.GetTimestamp();
        try
        {
            var parseResult = _sessionInfoParser.Parse<TelemetrySessionInfo>(rawSessionInfoYaml);
            sessionInfo = parseResult.Model;

            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
            _metricsService?.SessionInfo.RecordProcessed(elapsed, sizeBytes, parseResult.ParseAttemptsRequired);
            _logger.LogDebug("sessionInfo deserialize complete. required {attempts} attempts. ({elapsedMs}ms)", parseResult.ParseAttemptsRequired, (long)elapsed.TotalMilliseconds);
        }
        catch (Exception e)
        {
            _metricsService?.SessionInfo.RecordFailed(Stopwatch.GetElapsedTime(startTimestamp), sizeBytes, e);
            _logger.LogError(e, "error deserializing or sending sessionTelemetryInfo event");
            _errorChannel.Writer.TryWrite(e);
        }
        return sessionInfo;
    }

    private bool Initialize()
    {
        try
        {
            _dataProvider.OpenDataSource();
            _isInitialized = true;
        }
        catch (FileNotFoundException)
        {
            // do nothing. iRacing not ready yet
            _isInitialized = false;
        }
        return _isInitialized;
    }

    private async Task<int> ProcessLiveData(CancellationToken ct)
    {
        int numUpdates = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var telemetryProcessed = await WaitForData(ct).ConfigureAwait(false);
                if (telemetryProcessed)
                {
                    numUpdates++;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogDebug("Live data monitoring cancelled after {numUpdates} updates", numUpdates);
                return numUpdates;
            }
            catch (SyncHandlerFaultException)
            {
                // synchronous telemetry handler threw - fault Monitor() directly, same as Async
                // mode, instead of treating it as a retryable SDK/provider error
                _logger.LogDebug("Synchronous telemetry handler threw after {numUpdates} updates; faulting monitoring", numUpdates);
                throw;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error processing live data");
                _errorChannel.Writer.TryWrite(e);

                // For other exceptions, wait a bit before retrying
                try
                {
                    await Task.Delay(1000, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    _logger.LogDebug("Live data monitoring cancelled after {numUpdates} updates", numUpdates);
                    return numUpdates;
                }
            }
        }

        return numUpdates;
    }

    private async Task<bool> WaitForData(CancellationToken ct)
    {
        // Check cancellation early
        ct.ThrowIfCancellationRequested();

        // ensure initialization
        if (!_isInitialized && !Initialize())
        {
            await Task.Delay(INITIALIZATION_DELAY_MS, ct).ConfigureAwait(false);
            return false;
        }

        // check connection
        var isConnected = _dataProvider.IsConnected;

        // if connection state changed, send event
        if (isConnected != _lastConnectionStatus)
        {
            _logger.LogDebug("isConnected changed from {lastState} to {currState}", _lastConnectionStatus, isConnected);

            // write connection state change to channel if not disposed
            _connectStateChannel.Writer.TryWrite(isConnected ? ConnectState.Connected : ConnectState.Disconnected);

            _lastConnectionStatus = isConnected;
        }

        // if we can't connect to iRacing, there is nothing to do
        if (!isConnected)
        {
            await Task.Delay(INITIALIZATION_DELAY_MS, ct).ConfigureAwait(false);
            return false;
        }

        // if new session info, queue for processing
        if (_dataProvider.IsSessionInfoUpdated())
        {
            if (!IsPaused)
            {
                var rawSessionInfoYaml = _dataProvider.GetSessionInfoYaml();
                _internalSessionInfoChannel.Writer.TryWrite(rawSessionInfoYaml);
            }
        }

        // wait for new telemetry data with cancellation support
        var signaled = await _dataProvider.WaitForDataReady(TimeSpan.FromMilliseconds(DATA_READY_TIMEOUT_MS), ct).ConfigureAwait(false);
        if (!signaled)
        {
            // no new data, return and try again
            return false;
        }

        // taken before decoding, so decode cost lands inside every pipeline measurement
        var acquiredTimestamp = MarkAcquired();

        // suppress events if paused or disposed
        if (!IsPaused)
        {
            await DeliverTelemetryData(new(GetTelemetryDataSample(), acquiredTimestamp)).ConfigureAwait(false);
        }

        return true;	// data processed
    }

    private T GetTelemetryDataSample()
    {
        // hot path
        var meters = _metricsService?.Telemetry;
        var startTimestamp = meters?.StartTiming() ?? TelemetryMeters.NotTiming;

        T val;
        try
        {
            val = _telemetryAccessor.CreateTelemetryDataSample(_dataProvider);
        }
        catch (Exception ex)
        {
            meters?.RecordFailed(startTimestamp, ex);
            throw;
        }

        meters?.RecordProcessed(startTimestamp);
        return val;
    }

    // keep this method synchronous to avoid building an async
    private Task DeliverTelemetryData(StampedSample<T> stamped)
    {
        if (_deliveryMode == TelemetryDeliveryMode.Synchronous)
        {
            return _syncTelemetryHandler != null
                ? InvokeSyncTelemetryHandler(stamped)
                : Task.CompletedTask;
        }

        _telemetryDataChannel.Writer.TryWrite(stamped);
        return Task.CompletedTask;
    }

    private async Task InvokeSyncTelemetryHandler(StampedSample<T> stamped)
    {
        // no queue in synchronous mode, so sdk latency is only decode cost. recorded anyway,
        // so the sync/async tradeoff shows up in the data instead of having to be inferred
        var meters = _metricsService?.Telemetry;
        var handlerStart = meters?.RecordHandoff(stamped.AcquiredTimestamp) ?? TelemetryMeters.NotTiming;
        try
        {
            await _syncTelemetryHandler!(stamped.Sample).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // this is the caller's handler exception, not an SDK/provider error - wrap it so
            // catch blocks fault Monitor() instead of treating it as a retryable error routed to OnError
            throw new SyncHandlerFaultException(ex);
        }
        finally
        {
            meters?.RecordConsumed(stamped.AcquiredTimestamp, handlerStart);
        }
    }
    private async Task<int> ProcessIbtData(CancellationToken token)
    {
        int numRecords = 0;

        try
        {
            // Check cancellation before starting
            token.ThrowIfCancellationRequested();

            var sw = new Stopwatch();
            sw.Start();

            if (!_isInitialized)
                Initialize();

            _connectStateChannel.Writer.TryWrite(ConnectState.Connected);

            // update and send session info event
            if (_dataProvider.IsSessionInfoUpdated())
            {
                if (!IsPaused)
                {
                    var rawSessionInfoYaml = _dataProvider.GetSessionInfoYaml();
                    _internalSessionInfoChannel.Writer.TryWrite(rawSessionInfoYaml);
                }
            }

            // Process with respect for playback speed multiplier
            var playbackDelay = _ibtOptions?.PlayBackSpeedMultiplier == int.MaxValue ? TimeSpan.Zero :
                TimeSpan.FromMilliseconds(16.67 / (_ibtOptions?.PlayBackSpeedMultiplier ?? 1)); // ~60 FPS base rate

            // loop until we are at eof, cancelled, or shutdown requested
            for (numRecords = 0; !token.IsCancellationRequested; numRecords++)
            {
                var dataAvailable = await _dataProvider.WaitForDataReady(TimeSpan.Zero, token).ConfigureAwait(false);
                if (!dataAvailable)
                {
                    break;
                }

                // taken before decoding, so decode cost lands inside pipeline measurement
                var acquiredTimestamp = MarkAcquired();

                // suppress events if paused or disposed
                if (!IsPaused)
                {
                    await DeliverTelemetryData(new(GetTelemetryDataSample(), acquiredTimestamp)).ConfigureAwait(false);
                }

                // Apply playback speed delay if configured
                if (playbackDelay > TimeSpan.Zero)
                {
                    await Task.Delay(playbackDelay, token).ConfigureAwait(false);
                }

                // Yield periodically for responsive cancellation
                if (numRecords % 100 == 0)
                {
                    await Task.Yield();
                    token.ThrowIfCancellationRequested();
                }
            }

            sw.Stop();
            var recsPerSec = Math.Round(numRecords / (sw.ElapsedMilliseconds + 1) * 1000f, 1); // +1 to avoid division by zero
            var minsOfData = Math.Round(numRecords / 60f / 60f, 2);
            _logger.LogInformation("processed {numRecords} IBT telemetry records ({minsOfData} mins worth of session data), in {milliseconds}ms. ({rate} recs/sec)", numRecords, minsOfData, sw.ElapsedMilliseconds, recsPerSec);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _logger.LogDebug("IBT data processing cancelled after {numRecords} records", numRecords);
            return numRecords;
        }
        catch (SyncHandlerFaultException)
        {
            _logger.LogDebug("Synchronous telemetry handler threw after {numRecords} records; faulting monitoring", numRecords);
            throw;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error playing IBT file after {numRecords} records", numRecords);
            _errorChannel.Writer.TryWrite(e);
            throw;
        }
        finally
        {
            _connectStateChannel.Writer.TryWrite(ConnectState.Disconnected);
        }

        // session info processor will complete naturally when _internalSessionInfoChannel is completed
        // (which happens in StartDataProcessorTask's finally block)
        return numRecords;
    }


}
