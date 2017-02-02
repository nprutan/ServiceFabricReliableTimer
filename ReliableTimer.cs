using System;
using System.Collections.Generic;
using System.Fabric;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;

namespace ExternalStorageBroker
{
    public class ReliableTimer : System.Timers.Timer
    {
        public new bool AutoReset = true;
        public double ReliableInterval { get; set; }
        public double ReliableIntervalTolerance { get; set; } = 100.0;
        public event EventHandler<ReliableElapsedEventArgs> ReliableElapsed;
        public bool InProgress = false;
        private ConditionalValue<DateTime> _tickCache;
        private IReliableStateManager _stateManager;
        private readonly string _reliableDictName;
        private StatefulServiceContext _context;
        private CancellationToken _cancellationToken;
        public bool FastTimerOn { get; set; } = false;
        public bool SlowTimerOn { get; set; } = false;
        public TimerStatus Status { get; set; }

        public ReliableTimer(IReliableStateManager stateManager, string reliableDictName, StatefulServiceContext context, CancellationToken cancellationToken)
        {
            _stateManager = stateManager;
            _reliableDictName = reliableDictName;
            _context = context;
            _cancellationToken = cancellationToken;
            Initialize();
        }

        private async void Initialize()
        {
            IReliableDictionary<string, DateTime> reliableDictionary;
            using (var tx = _stateManager.CreateTransaction())
            {
                reliableDictionary =
                    await _stateManager.GetOrAddAsync<IReliableDictionary<string, DateTime>>(_reliableDictName);
                await tx.CommitAsync();
            }
            Interval = 1000;
            _tickCache = new ConditionalValue<DateTime>();
            Elapsed += async (sender, e) =>
            {
                try
                {
                    using (var tx = _stateManager.CreateTransaction())
                    {
                        _cancellationToken.ThrowIfCancellationRequested();
                        ConditionalValue<DateTime> tick;
                        if (_tickCache.HasValue)
                        {
                            tick = new ConditionalValue<DateTime>(true, _tickCache.Value);
                        }
                        else
                        {
                            tick = await reliableDictionary.TryGetValueAsync(tx, "tick", TimeSpan.FromMilliseconds(500), _cancellationToken);
                        }
                        if (tick.HasValue)
                        {
                            _tickCache = new ConditionalValue<DateTime>(true, tick.Value);
                        }
                        else
                        {
                            await
                                reliableDictionary.SetAsync(tx, "tick", DateTime.Now, TimeSpan.FromMilliseconds(500),
                                    _cancellationToken);
                            _tickCache = new ConditionalValue<DateTime>(true, DateTime.Now);
                        }
                        var timeSpan = tick.HasValue ? DateTime.Now - tick.Value : TimeSpan.Zero;
                        // if (Math.Abs(timeSpan.TotalMilliseconds - ReliableInterval) < ReliableIntervalTolerance)
                        if (timeSpan.TotalMilliseconds >= ReliableInterval)
                        {
                            var args = new ReliableElapsedEventArgs
                            {
                                ReliableSignalTime = e.SignalTime
                            };
                            await reliableDictionary.TryUpdateAsync(tx, "tick", e.SignalTime, tick.Value, TimeSpan.FromMilliseconds(500), _cancellationToken);
                            _tickCache = new ConditionalValue<DateTime>(true, e.SignalTime);
                            OnReliableElapsed(args);
                        }
                        await tx.CommitAsync();
                        tx.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    if (ex is OperationCanceledException)
                    {
                        ServiceEventSource.Current.Message(
                        $"Cancellation requested. Disabling timer: {_reliableDictName}, " +
                        $"Exception: {ex.Message}, " +
                        $"{_context.ServiceName.ToString()}, " +
                        $"{_context.ServiceTypeName}, " +
                        $"{_context.ReplicaId}, " +
                        $"{_context.PartitionId}, " +
                        $"{_context.CodePackageActivationContext.ApplicationName}, " +
                        $"{_context.CodePackageActivationContext.ApplicationTypeName}, " +
                        $"{_context.NodeContext.NodeName}, ");
                        Enabled = false;
                        FastTimerOn = false;
                        SlowTimerOn = false;
                        Status = TimerStatus.Paused;
                    }
                }
            };
        }

        public enum TimerStatus
        {
            Fast,
            Slow,
            Paused
        }

        public void OnReliableElapsed(ReliableElapsedEventArgs e)
        {
            if (!InProgress)
            {
                ReliableElapsed?.Invoke(this, e);
            }
        }
    }
}
