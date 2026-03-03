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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SVappsLAB.iRacingTelemetrySDK.DataProviders;
using SVappsLAB.iRacingTelemetrySDK.irSDKDefines;
using SVappsLAB.iRacingTelemetrySDK.Metrics;
using SVappsLAB.iRacingTelemetrySDK.YamlParsing;

namespace SVappsLAB.iRacingTelemetrySDK
{
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
    /// Configuration options that control various aspects of telemetry processing.
    /// </summary>
    public class ClientOptions
    {
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
        private const int CHANNEL_SIZE = 60;
        readonly Channel<ConnectState> _connectStateChannel;
        readonly Channel<Exception> _errorChannel;
        readonly Channel<string> _rawSessionDataChannel;
        readonly Channel<TelemetrySessionInfo> _sessionDataChannel;
        readonly Channel<T> _telemetryDataChannel;
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

        // Disposal state tracking
        private volatile bool _disposed = false;

        // Monitor state tracking - 0=idle, 1=running
        private int _monitorState = 0;

        // telemetry variables caching
        private IReadOnlyList<TelemetryVariable>? _cachedTelemetryVariables;
        private readonly object _telemetryVariablesLock = new object();

        /// <inheritdoc />
        public IAsyncEnumerable<ConnectState> ConnectStates => GetConnectStatesEnumerable();

        /// <inheritdoc />
        public IAsyncEnumerable<Exception> Errors => GetErrorsEnumerable();

        /// <inheritdoc />
        public IAsyncEnumerable<string> SessionDataYaml => GetRawSessionDataEnumerable();

        /// <inheritdoc />
        public IAsyncEnumerable<TelemetrySessionInfo> SessionData => GetSessionDataEnumerable();

        /// <inheritdoc />
        public IAsyncEnumerable<T> TelemetryData => GetTelemetryDataEnumerable();

        public bool IsPaused => _isPaused;

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
            await foreach (var item in _telemetryDataChannel.Reader.ReadAllAsync(cancellationToken))
                yield return item;
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

            // initialize bounded channels
            var boundedChannelOptions = new BoundedChannelOptions(CHANNEL_SIZE)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true
            };
            _connectStateChannel = Channel.CreateBounded<ConnectState>(boundedChannelOptions);
            _errorChannel = Channel.CreateBounded<Exception>(boundedChannelOptions);
            _rawSessionDataChannel = Channel.CreateBounded<string>(boundedChannelOptions);
            _sessionDataChannel = Channel.CreateBounded<TelemetrySessionInfo>(boundedChannelOptions);
            _telemetryDataChannel = Channel.CreateBounded<T>(boundedChannelOptions);

            // Internal session info processing channel
            var sessionInfoChannelOptions = new BoundedChannelOptions(10)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false // Both Live and IBT can write
            };
            _internalSessionInfoChannel = Channel.CreateBounded<string>(sessionInfoChannelOptions);

            // initialize data provider
            _dataProvider = _ibtOptions == null ? new LiveDataProvider(_logger) : new IBTDataProvider(_logger, _ibtOptions);

            _metricsService = new MetricsService(clientOptions?.MeterFactory, _ibtOptions == null ? "Live" : "IBT");

            _telemetryAccessor = new TelemetryDataAccessor<T>(_logger);
            _sessionInfoParser = new YamlParser(_logger);
        }

        /// <summary>
        /// starts monitoring telemetry data from either live iRacing or IBT file playback.
        /// this method can only be called once per TelemetryClient instance.
        /// </summary>
        /// <param name="ct">cancellation token to stop monitoring</param>
        /// <returns>number of telemetry records processed</returns>
        /// <exception cref="InvalidOperationException">thrown if Monitor() is called while already running</exception>
        /// <exception cref="ObjectDisposedException">thrown if the client has been disposed</exception>
        /// <remarks>
        /// <para><strong>IMPORTANT:</strong> This method can only be called ONCE per TelemetryClient instance.
        /// After Monitor() completes (via cancellation or IBT file EOF), the client cannot be restarted.
        /// Create a new client instance for subsequent monitoring sessions.</para>
        /// <para><strong>Concurrent Calls:</strong> Calling Monitor() while already running will throw
        /// InvalidOperationException. Ensure the previous Monitor() call has completed before starting
        /// a new client instance.</para>
        /// </remarks>
        public async Task<int> Monitor(CancellationToken ct)
        {
            // check if channels have already been completed from a previous run
            if (_telemetryDataChannel.Reader.Completion.IsCompleted)
            {
                throw new InvalidOperationException(
                    "Monitor() has already completed on this instance. " +
                    "Create a new TelemetryClient instance to monitor again.");
            }

            // atomically check and set monitor state to prevent re-entrancy
            // compareExchange returns the original value - if it was 0 (idle), we successfully set it to 1 (running)
            if (Interlocked.CompareExchange(ref _monitorState, 1, 0) != 0)
            {
                throw new InvalidOperationException("Monitor() is already running. concurrent calls to Monitor() are not allowed.");
            }

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
                    _logger.LogError(ex, "Error starting processing tasks");
                    throw;
                }
            }
            finally
            {
                // reset monitor state to idle on any exit path (success, exception, cancellation)
                // this ensures state is cleaned up even if Monitor() throws during startup
                Interlocked.Exchange(ref _monitorState, 0);
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
                _internalSessionInfoChannel.Writer.Complete();
            }
        }

        public IReadOnlyList<TelemetryVariable> GetTelemetryVariables()
        {
            // fast path - already cached
            if (_cachedTelemetryVariables != null)
                return _cachedTelemetryVariables;

            lock (_telemetryVariablesLock)
            {
                // double-check pattern
                if (_cachedTelemetryVariables != null)
                    return _cachedTelemetryVariables;

                try
                {
                    IReadOnlyList<TelemetryVariable> list = BuildTelemetryVariablesList();
                    _cachedTelemetryVariables = list;
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

        private unsafe IReadOnlyList<TelemetryVariable> BuildTelemetryVariablesList()
        {
            var list = new List<TelemetryVariable>();
            var varHeaders = _dataProvider.GetVarHeaders();

            if (varHeaders == null)
            {
                _logger.LogWarning("Variable headers are null, returning empty list");
                return list;
            }

            foreach (var vh in varHeaders.Values)
            {
                try
                {
                    var tVar = new TelemetryVariable
                    {
                        Type = vh.type switch
                        {
                            irsdk_VarType.irsdk_char => vh.count > 1 ? typeof(string[]) : typeof(string),
                            irsdk_VarType.irsdk_bool => vh.count > 1 ? typeof(bool[]) : typeof(bool),
                            irsdk_VarType.irsdk_int => vh.count > 1 ? typeof(int[]) : typeof(int),
                            irsdk_VarType.irsdk_bitField => vh.count > 1 ? typeof(uint[]) : typeof(uint),
                            irsdk_VarType.irsdk_float => vh.count > 1 ? typeof(float[]) : typeof(float),
                            irsdk_VarType.irsdk_double => vh.count > 1 ? typeof(double[]) : typeof(double),
                            _ => throw new NotImplementedException($"{vh.type} not implemented")
                        },

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

        /// <summary>
        /// Gets a value indicating whether the client is connected to the telemetry source.
        /// </summary>
        /// <value>
        /// <c>true</c> if connected to iRacing (live mode) or an IBT file is open; otherwise <c>false</c>.
        /// </value>
        /// <remarks>
        /// This property is safe to call from any thread and will return <c>false</c> if the client
        /// has been disposed or is not yet initialized.
        /// </remarks>
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

        /// <summary>
        /// Pauses stream data writing. Processing continues, but stream writes are suppressed.
        /// </summary>
        /// <remarks>
        /// <para><strong>Thread Safety:</strong> This method is thread-safe and can be called from any thread.</para>
        /// <para><strong>Idempotency:</strong> Safe to call multiple times. Calling Pause() when already paused has no effect.</para>
        /// <para><strong>Eventual Consistency:</strong> Changes are not immediate. A few telemetry samples may be written
        /// to streams before the pause takes effect (typically 1-2 samples, ~16-32ms at 60Hz).</para>
        /// </remarks>
        public void Pause()
        {
            _isPaused = true;
            _logger.LogDebug("Telemetry client paused");
        }

        /// <summary>
        /// Resumes stream data writing.
        /// </summary>
        /// <remarks>
        /// <para><strong>Thread Safety:</strong> This method is thread-safe and can be called from any thread.</para>
        /// <para><strong>Idempotency:</strong> Safe to call multiple times. Calling Resume() when not paused has no effect.</para>
        /// <para><strong>Eventual Consistency:</strong> Changes are not immediate. A few telemetry samples may be suppressed
        /// before the resume takes effect (typically 1-2 samples, ~16-32ms at 60Hz).</para>
        /// </remarks>
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
                if (_internalCts != null && !_internalCts.IsCancellationRequested)
                {
                    _logger.LogDebug("Cancelling internal processing token");
                    _internalCts.Cancel();
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
                _logger.LogError(ex, "Error shutting down main processing task");
                shutdownException ??= ex;
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

        private void CompleteAllChannels()
        {
            try
            {
                _connectStateChannel.Writer.Complete();
                _errorChannel.Writer.Complete();
                _rawSessionDataChannel.Writer.Complete();
                _sessionDataChannel.Writer.Complete();
                _telemetryDataChannel.Writer.Complete();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing channels");
            }
        }
        private bool IsOnlineMode => _ibtOptions == null;

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
            using var timer = new ScopeTimeSpanTimer(elapsed => _metricsService?.SessionInfo.ProcessingDuration(elapsed));
            using var counter = new ScopeLambda(() => _metricsService?.SessionInfo.RecordsProcessed(1));

            TelemetrySessionInfo? sessionInfo = null;
            try
            {
                Stopwatch sw = Stopwatch.StartNew();

                var parseResult = _sessionInfoParser.Parse<TelemetrySessionInfo>(rawSessionInfoYaml);
                sessionInfo = parseResult.Model;
                _logger.LogDebug("sessionInfo deserialize complete. required {attempts} attempts. ({ScopeTimeSpanTimer}ms)", parseResult.ParseAttemptsRequired, sw.ElapsedMilliseconds);
            }
            catch (Exception e)
            {
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
                    throw; // propagate cancellation to caller
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Error processing live data");
                    _errorChannel.Writer.TryWrite(e);

                    // For other exceptions, wait a bit before retrying
                    await Task.Delay(1000).ConfigureAwait(false);
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

            // suppress events if paused or disposed
            if (!IsPaused)
            {
                // write to channel
                var telemetryData = GetTelemetryDataSample();
                _telemetryDataChannel.Writer.TryWrite(telemetryData);
            }

            return true;	// data processed
        }

        private T GetTelemetryDataSample()
        {
            using var elapsedTimer = new ScopeTimeSpanTimer(elapsed => _metricsService?.Telemetry.ProcessingDuration(elapsed));
            using var counter = new ScopeLambda(() => _metricsService?.Telemetry.RecordsProcessed(1));

            T val = _telemetryAccessor.CreateTelemetryDataSample(_dataProvider);
            return val;
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

                    // suppress events if paused or disposed
                    if (!IsPaused)
                    {
                        // write to channel
                        var telemetryData = GetTelemetryDataSample();
                        _telemetryDataChannel.Writer.TryWrite(telemetryData);
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
                throw; // propagate cancellation to caller
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
}
