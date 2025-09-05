/**
 * Copyright (C) 2024-2025 Scott Velez
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
 * limitations under the License.using Microsoft.CodeAnalysis;
**/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SVappsLAB.iRacingTelemetrySDK.DataProviders;
using SVappsLAB.iRacingTelemetrySDK.irSDKDefines;
using SVappsLAB.iRacingTelemetrySDK.Metrics;

namespace SVappsLAB.iRacingTelemetrySDK
{
    public enum ConnectState
    {
        Disconnected,
        Connected
    }

    public class ExceptionEventArgs : EventArgs
    {
        public ExceptionEventArgs(Exception exception)
        {
            Exception = exception;
        }

        public Exception Exception { get; private set; }
    }

    public class ConnectStateChangedEventArgs : EventArgs
    {
        public ConnectState State { get; set; }
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

    /**
     * Options for data channels
     */
    public record class ChannelOptions(int channelSize = 10, bool multipleReaders = false);

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

        /// <summary>
        /// Optional configuration for data stream channels.
        /// </summary>
        public ChannelOptions? ChannelOptions { get; init; }
    }

    public class TelemetryClient<T> : ITelemetryClient<T>, IDisposable, IAsyncDisposable where T : struct
    {
        private const int DATA_READY_TIMEOUT_MS = 30;
        private const int INITIALIZATION_DELAY_MS = 1000;

        private readonly Channel<ConnectStateChangedEventArgs> _connectStateChannel;
        private readonly Channel<ExceptionEventArgs> _errorChannel;
        private readonly Channel<string> _rawSessionDataChannel;
        private readonly Channel<TelemetrySessionInfo> _sessionDataChannel;
        private readonly Channel<T> _telemetryDataChannel;
        private readonly Channel<string> _internalSessionInfoChannel;

        public ChannelReader<ConnectStateChangedEventArgs> ConnectStateStream => _connectStateChannel.Reader;
        public ChannelReader<ExceptionEventArgs> ErrorStream => _errorChannel.Reader;
        public ChannelReader<string> RawSessionDataStream => _rawSessionDataChannel.Reader;
        public ChannelReader<TelemetrySessionInfo> SessionDataStream => _sessionDataChannel.Reader;
        public ChannelReader<T> TelemetryDataStream => _telemetryDataChannel.Reader;

        IMetricsService? _metricsService;
        Task<int>? _dataProcessingTask;
        Task? _sessionInfoProcessorTask;
        CancellationTokenSource? _sessionInfoProcessingCTS;    // background processing cancellation token source

        // Disposal state tracking
        private volatile bool _disposed = false;

        public bool _lastConnectionStatus = false;

        private ISessionInfoParser _sessionInfoParser;
        private ILogger _logger;

        bool _isInitialized = false;
        bool _isPaused = false;

        IBTOptions? _ibtOptions;

        IDataProvider _dataProvider;
        private readonly TelemetryDataAccessor<T> _telemetryAccessor;

        // factory to create instances of the client
        public static ITelemetryClient<T> Create(ILogger logger, IBTOptions? options) =>
            new TelemetryClient<T>(logger, null, options);
        public static ITelemetryClient<T> Create(ILogger logger, ClientOptions options) =>
            new TelemetryClient<T>(logger, options, null);
        public static ITelemetryClient<T> Create(ILogger logger, ClientOptions? clientOptions = null, IBTOptions? ibtOptions = null) =>
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
            var chOptions = clientOptions?.ChannelOptions ?? new ChannelOptions();
            var boundedChannelOptions = new BoundedChannelOptions(chOptions.channelSize)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = chOptions.multipleReaders == false,
                SingleWriter = true
            };
            _connectStateChannel = Channel.CreateBounded<ConnectStateChangedEventArgs>(boundedChannelOptions);
            _errorChannel = Channel.CreateBounded<ExceptionEventArgs>(boundedChannelOptions);
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
            _sessionInfoParser = new YamlParser();
        }

        public async Task<int> Monitor(CancellationToken ct)
        {
            _logger.LogDebug("monitoring '{mode}' data", IsOnlineMode ? "Live" : "IBT");

            _sessionInfoProcessingCTS = CancellationTokenSource.CreateLinkedTokenSource(ct);
            try
            {
                _sessionInfoProcessorTask = StartSessionInfoProcessorTask(_sessionInfoProcessingCTS.Token);
                _dataProcessingTask = StartDataProcessorTask(ct);

                var result = await _dataProcessingTask;
                await _sessionInfoProcessorTask;    // give the session info processor a chance to complete

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting processing tasks");
                throw;
            }
        }

        private Task StartSessionInfoProcessorTask(CancellationToken ct)
        {
            var task = Task.Run(async () =>
            {
                _logger.LogDebug("Session info processor started on task {taskId}", Task.CurrentId);
                await ProcessSessionInfoChannelAsync(ct).ConfigureAwait(false);
            }, ct);

            return task;
        }

        private Task<int> StartDataProcessorTask(CancellationToken ct)
        {
            // Start the main data processing task on thread pool for true concurrency
            _dataProcessingTask = Task.Run(async () =>
            {
                _logger.LogDebug("{mode} data processor started on task {taskId}", IsOnlineMode ? "Live" : "IBT", Task.CurrentId);

                var task = IsOnlineMode ?
                    ProcessLiveData(ct) :
                    ProcessIbtData(ct);

                task?.ConfigureAwait(false);

                // complete the session info channel. there will be no more data
                _internalSessionInfoChannel.Writer.Complete();

                return await task;

            }, ct);

            return _dataProcessingTask;
        }

        public Task<IEnumerable<TelemetryVariable>> GetTelemetryVariables()
        {
            try
            {
                // use local unsafe function to get the varHeaders
                unsafe IEnumerable<TelemetryVariable> unsafeInternal()
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
                            // Continue processing other variables
                        }
                    }
                    return list;
                }

                var sortedList = unsafeInternal().OrderBy(v => v.Name) as IEnumerable<TelemetryVariable>;
                return Task.FromResult(sortedList);
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

        public string GetRawTelemetrySessionInfoYaml()
        {
            try
            {
                return _dataProvider.GetSessionInfoYaml();
            }
            catch (ObjectDisposedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving raw session info YAML");
                throw;
            }
        }
        public bool IsConnected()
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

        public void Pause()
        {
            _isPaused = true;
            _logger.LogDebug("Telemetry client paused");
        }

        public void Resume()
        {
            _isPaused = false;
            _logger.LogDebug("Telemetry client resumed");
        }

        public void Dispose()
        {
            DisposeAsync().GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            try
            {
                await ShutdownAsync();
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

        private async Task ShutdownAsync()
        {
            _logger.LogDebug("Shutting down");

            Exception? shutdownException = null;

            try
            {
                _logger.LogDebug("Monitor completion - Channel states: " +
                    "ConnectState={connectStateCount}, " +
                    "Error={errorCount}, " +
                    "RawSessionData={rawSessionCount}, " +
                    "SessionData={sessionCount}, " +
                    "TelemetryData={telemetryCount}",
                    _connectStateChannel.Reader.Count,
                    _errorChannel.Reader.Count,
                    _rawSessionDataChannel.Reader.Count,
                    _sessionDataChannel.Reader.Count,
                    _telemetryDataChannel.Reader.Count);

                var shutdownTimeoutToken = new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;

                // shut down processing tasks
                await _dataProcessingTask;
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
            _dataProvider?.Dispose();
            _metricsService?.Dispose();

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
                //_internalSessionInfoChannel.Writer.TryComplete();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing channels");
            }
        }
        ~TelemetryClient()
        {
            if (!_disposed)
            {
                _logger?.LogWarning("TelemetryClient was not properly disposed. Use 'using' or 'async using' statements.");
            }
        }
        private bool IsOnlineMode => _ibtOptions == null;

        private async Task ProcessSessionInfoChannelAsync(CancellationToken ct)
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
                        _errorChannel.Writer.TryWrite(new ExceptionEventArgs(e));
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
                _errorChannel?.Writer.TryWrite(new ExceptionEventArgs(ex));
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
                _errorChannel.Writer.TryWrite(new ExceptionEventArgs(e));
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
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        await WaitForData(ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        _logger.LogDebug("Live data monitoring cancelled");
                        return -1;
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "Error processing live data");
                        _errorChannel.Writer.TryWrite(new ExceptionEventArgs(e));

                        // For other exceptions, wait a bit before retrying
                        await Task.Delay(1000, ct).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogDebug("Live data processing cancelled");
            }

            _logger.LogInformation("Live data monitor stopping");
            return -1;
        }

        private async Task WaitForData(CancellationToken ct)
        {
            // Check cancellation early
            ct.ThrowIfCancellationRequested();

            // ensure initialization
            if (!_isInitialized && !Initialize())
            {
                await Task.Delay(INITIALIZATION_DELAY_MS, ct).ConfigureAwait(false);
                return;
            }

            // check connection
            var isConnected = _dataProvider.IsConnected;

            // if connection state changed, send event
            if (isConnected != _lastConnectionStatus)
            {
                _logger.LogDebug("isConnected changed from {lastState} to {currState}", _lastConnectionStatus, isConnected);

                // write connection state change to channel if not disposed
                _connectStateChannel.Writer.TryWrite(new ConnectStateChangedEventArgs { State = isConnected ? ConnectState.Connected : ConnectState.Disconnected });

                _lastConnectionStatus = isConnected;
            }

            // if we can't connect to iRacing, there is nothing to do
            if (!isConnected)
            {
                await Task.Delay(INITIALIZATION_DELAY_MS, ct).ConfigureAwait(false);
                return;
            }

            // if new session info, queue for processing
            if (_dataProvider.IsSessionInfoUpdated())
            {
                if (!_isPaused)
                {
                    var rawSessionInfoYaml = _dataProvider.GetSessionInfoYaml();
                    _internalSessionInfoChannel.Writer.TryWrite(rawSessionInfoYaml);
                }
            }

            // wait for new telemetry data with cancellation support
            var signaled = await Task.Run(() => _dataProvider.WaitForDataReady(TimeSpan.FromMilliseconds(DATA_READY_TIMEOUT_MS)), ct).ConfigureAwait(false);
            if (!signaled)
            {
                // no new data, return and try again
                return;
            }

            // suppress events if paused or disposed
            if (!_isPaused)
            {
                // write to channel
                var telemetryData = GetTelemetryDataSample();
                _telemetryDataChannel.Writer.TryWrite(telemetryData);
            }
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

                _dataProvider.OpenDataSource();

                _connectStateChannel.Writer.TryWrite(new ConnectStateChangedEventArgs { State = ConnectState.Connected });

                // update and send session info event
                if (_dataProvider.IsSessionInfoUpdated())
                {
                    if (!_isPaused)
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
                    var dataAvailable = _dataProvider.WaitForDataReady(TimeSpan.Zero);
                    if (!dataAvailable)
                    {
                        break;
                    }

                    // suppress events if paused or disposed
                    if (!_isPaused)
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
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error playing IBT file after {numRecords} records", numRecords);
                _errorChannel.Writer.TryWrite(new ExceptionEventArgs(e));
                throw;
            }
            finally
            {
                _connectStateChannel.Writer.TryWrite(new ConnectStateChangedEventArgs { State = ConnectState.Disconnected });
            }

            // special case for IBT files. when we reach the end of the file, we need to cancel the session info processor
            _sessionInfoProcessingCTS?.Cancel();

            return numRecords;
        }


    }
}
