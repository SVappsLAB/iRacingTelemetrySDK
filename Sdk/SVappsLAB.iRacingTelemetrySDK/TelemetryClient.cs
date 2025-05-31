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
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SVappsLAB.iRacingTelemetrySDK.DataProviders;
using SVappsLAB.iRacingTelemetrySDK.irSDKDefines;
using SVappsLAB.iRacingTelemetrySDK.Models;

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



    public class TelemetryClient<T> : ITelemetryClient<T> where T : struct
    {
        private const int DATA_READY_TIMEOUT_MS = 30;
        private const int INITIALIZATION_DELAY_MS = 1000;

        public event EventHandler<T>? OnTelemetryUpdate;
        public event EventHandler<string>? OnRawSessionInfoUpdate;
        public event EventHandler<TelemetrySessionInfo>? OnSessionInfoUpdate;
        public event EventHandler<ExceptionEventArgs>? OnError;
        public event EventHandler<ConnectStateChangedEventArgs>? OnConnectStateChanged;

        Task<int>? _task;

        public bool _lastConnectionStatus = false;

        private ISessionInfoParser _sessionInfoParser;
        private ILogger _logger;

        bool _isInitialized = false;

        IBTOptions? _ibtOptions;

        IDataProvider _dataProvider;
        private System.Reflection.ConstructorInfo _telemetryDataConstructorInfo;
        private IEnumerable<System.Reflection.ParameterInfo> _telemetryDataConstructorParameters;

        // factory to create instances of the client
        public static ITelemetryClient<T> Create(ILogger logger, IBTOptions? ibtOptions = null)
        {
            return new TelemetryClient<T>(logger, ibtOptions);
        }
        private TelemetryClient(ILogger logger, IBTOptions? ibtOptions = null)
        {
            // do a quick check for valid ibt file and bail immediately if not found
            if (ibtOptions != null && !File.Exists(ibtOptions.IbtFilePath))
            {
                throw new FileNotFoundException($"IBT file [{ibtOptions.IbtFilePath}] not found", ibtOptions.IbtFilePath);
            }

            _logger = logger;
            _ibtOptions = ibtOptions;

            _dataProvider = _ibtOptions == null ? new LiveDataProvider(_logger) : new IBTDataProvider(_logger, _ibtOptions);

            // get constructor and parameters of the TelemetryData type
            // we'll need them later when we create instances of the type
            _telemetryDataConstructorInfo = typeof(T).GetConstructors()[0];
            _telemetryDataConstructorParameters = _telemetryDataConstructorInfo.GetParameters();

            _sessionInfoParser = new YamlParser();
        }

        public Task<int> Monitor(CancellationToken ct)
        {
            _logger.LogDebug("monitoring '{mode}' data", IsOnlineMode ? "Live" : "IBT");

            _task = IsOnlineMode ?
                Task.Run<int>(() => ProcessLiveData(ct), ct) :
                Task.Run<int>(() => ProcessIbtData(ct), ct);

            return _task;
        }

        public Task<IEnumerable<TelemetryVariable>> GetTelemetryVariables()
        {
            // use local unsafe function to get the varHeaders
            unsafe IEnumerable<TelemetryVariable> unsafeInternal()
            {
                var list = new List<TelemetryVariable>();
                foreach (var vh in _dataProvider.GetVarHeaders()!.Values)
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
                            _ => throw new NotImplementedException($"{vh.type}, not implemented")
                        },

                        Length = vh.count,
                        IsTimeValue = vh.countAsTime,
                        Name = Marshal.PtrToStringAnsi((nint)vh.name) ?? string.Empty,
                        Desc = Marshal.PtrToStringAnsi((nint)vh.desc) ?? string.Empty,
                        Units = Marshal.PtrToStringAnsi((nint)vh.unit) ?? string.Empty,
                    };
                    list.Add(tVar);
                }
                return list;
            }

            var sortedList = unsafeInternal().OrderBy(v => v.Name) as IEnumerable<TelemetryVariable>;
            return Task.FromResult(sortedList);
        }

        public string GetRawTelemetrySessionInfoYaml()
        {
            return _dataProvider.GetSessionInfoYaml();
        }
        public bool IsConnected()
        {
            if (_isInitialized)
            {
                var isConnected = _dataProvider.IsConnected;
                return isConnected;
            }
            return false;
        }
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        private async Task Shutdown()
        {
            _logger.LogDebug("Shutting down");
            try
            {
                await _task!;

                //Uninitialize();

                if (_dataProvider != null)
                {
                    _dataProvider.Dispose();
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Shutdown() error");
            }
        }

        protected virtual async void Dispose(bool disposing)
        {
            if (disposing)
            {
                await Shutdown();
            }
        }
        ~TelemetryClient()
        {
            Dispose(false);
        }
        private bool IsOnlineMode => _ibtOptions == null;
        Task UpdateSession(string rawSessionInfoYaml)
        {
            var task = Task.Run(() => UpdateSessionHelper(rawSessionInfoYaml));
            return task;
        }
        void UpdateSessionHelper(string rawSessionInfoYaml)
        {
            var sw = new Stopwatch();
            sw.Start();

            try
            {
                var parseResult = _sessionInfoParser.Parse<TelemetrySessionInfo>(rawSessionInfoYaml);
                var sessionTelemetryInfo = parseResult.Model;
                _logger.LogDebug("sessionInfo deserialize complete. required {attempts} attempts. ({elapsed}ms)", parseResult.ParseAttemptsRequired, sw.ElapsedMilliseconds);

                // send event
                OnSessionInfoUpdate?.Invoke(this, sessionTelemetryInfo);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "error deserializing or sending sessionTelemetryInfo event");
                OnError?.Invoke(this, new ExceptionEventArgs(e));
            }
            _logger.LogDebug("UpdateSession complete ({elapsed}ms)", sw.ElapsedMilliseconds);
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
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await WaitForData(ct);
                }
                catch (TaskCanceledException)
                {
                    _logger.LogDebug("monitoring cancelled");
                    return -1;
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "error processing live data");
                    OnError?.Invoke(this, new ExceptionEventArgs(e));
                    throw;
                }
            }
            _logger.LogInformation("dataMonitor stopping");
            return -1;
        }

        Task _sessionInfoProcessingTask = Task.CompletedTask;
        private async Task WaitForData(CancellationToken ct)
        {
            // ensure initialization
            if (!_isInitialized && !Initialize())
            {
                await Task.Delay(INITIALIZATION_DELAY_MS, ct);
                return;
            }

            // check connection
            var isConnected = _dataProvider.IsConnected;

            // if connection state changed, send event
            if (isConnected != _lastConnectionStatus)
            {
                _logger.LogDebug("isConnected changed from {lastState} to {currState}", _lastConnectionStatus, isConnected);

                // inform listeners of connection state change
                OnConnectStateChanged?.Invoke(this, new ConnectStateChangedEventArgs { State = isConnected ? ConnectState.Connected : ConnectState.Disconnected });

                _lastConnectionStatus = isConnected;
            }

            // if we can't connect to iRacing, there is nothing to do
            if (!isConnected)
            {
                await Task.Delay(INITIALIZATION_DELAY_MS, ct);
                return;
            }

            // if new session info, send event 
            if (_dataProvider.IsSessionInfoUpdated())
            {
                var rawSessionInfoYaml = _dataProvider.GetSessionInfoYaml();

                // if anyone wants raw yaml, send it
                if (OnRawSessionInfoUpdate != null)
                {
                    OnRawSessionInfoUpdate.Invoke(this, rawSessionInfoYaml);
                }

                if (OnSessionInfoUpdate != null)
                {
                    if (!_sessionInfoProcessingTask.IsCompleted)
                    {
                        _logger.LogWarning("sessionInfo processing task is still running");
                    }
                    _sessionInfoProcessingTask = UpdateSession(rawSessionInfoYaml);
                }
            }

            // wait for new telemetry data
            var signaled = _dataProvider.WaitForDataReady(TimeSpan.FromMilliseconds(DATA_READY_TIMEOUT_MS));
            if (!signaled)
            {
                // no new data, return and try again
                return;
            }

            // send event if we have a listener
            if (OnTelemetryUpdate != null)
            {
                var telemetryData = GetTelemetryDataSample();

                OnTelemetryUpdate.Invoke(this, telemetryData);
            }
        }

        private T GetTelemetryDataSample()
        {
            var parameterValues = _telemetryDataConstructorParameters
                .Select(p =>
                {
                    var val = _dataProvider.GetVarValue(p.Name!);
                    return val;
                });
            var telemetryData = (T)_telemetryDataConstructorInfo.Invoke(parameterValues.ToArray());
            return telemetryData;
        }
        private async Task<int> ProcessIbtData(CancellationToken token)
        {
            int numRecords = 0;

            try
            {
                var sw = new Stopwatch();
                sw.Start();

                _dataProvider.OpenDataSource();

                // send connect event
                OnConnectStateChanged?.Invoke(this, new ConnectStateChangedEventArgs { State = ConnectState.Connected });

                // update and send session info event
                if (_dataProvider.IsSessionInfoUpdated())
                {
                    var rawSessionInfoYaml = _dataProvider.GetSessionInfoYaml();
                    if (OnRawSessionInfoUpdate != null)
                    {
                        OnRawSessionInfoUpdate.Invoke(this, rawSessionInfoYaml);
                    }
                    if (OnSessionInfoUpdate != null)
                    {
                        await UpdateSession(rawSessionInfoYaml);
                    }
                }

                // loop until we are at eof or cancelled
                for (numRecords = 0; !token.IsCancellationRequested; numRecords++)
                {
                    var dataAvailable = _dataProvider.WaitForDataReady(TimeSpan.Zero);
                    if (!dataAvailable)
                    {
                        break;
                    }

                    // send event if we have a listener
                    if (OnTelemetryUpdate != null)
                    {
                        var telemetryData = GetTelemetryDataSample();
                        OnTelemetryUpdate.Invoke(this, telemetryData);
                    }
                }

                sw.Stop();
                var recsPerSec = Math.Round(numRecords / sw.ElapsedMilliseconds * 1000f, 1);
                var minsOfData = Math.Round(numRecords / 60f / 60f);
                _logger.LogInformation("processed {_numRecords} IBT telemetry records ({minsOfData} mins worth of session data), in {milliseconds}ms. ({rate} recs/sec)", numRecords, minsOfData, sw.ElapsedMilliseconds, recsPerSec);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "error playing ibt file");
                OnError?.Invoke(this, new ExceptionEventArgs(e));
                throw;
            }
            finally
            {
                // send disconnect event
                OnConnectStateChanged?.Invoke(this, new ConnectStateChangedEventArgs { State = ConnectState.Disconnected });
            }
            return numRecords;
        }


    }
}
