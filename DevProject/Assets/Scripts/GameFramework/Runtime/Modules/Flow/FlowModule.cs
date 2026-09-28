using System;
using System.Collections.Generic;

namespace GameFramework
{
    /// <summary>
    /// 流程模块默认实现。基于 OnUpdate 逐帧轮询根流程，无线程、无协程。
    /// </summary>
    public sealed class FlowModule : ModuleBase, IFlowModule
    {
        private const string Tag = "Flow";

        /// <inheritdoc />
        public override int Priority => -40;

        private int _nextId = 1;
        private readonly List<Entry> _entries = new(32);
        private readonly Dictionary<int, FlowState> _states = new(32);
        private ILogModule _log;

        /// <inheritdoc />
        public override void OnInit()
        {
            _log = GameEntry.Get<ILogModule>();
        }

        /// <inheritdoc />
        public override void OnShutdown()
        {
            CancelAll();
            _entries.Clear();
            _states.Clear();
            _log = null;
        }

        /// <inheritdoc />
        public FlowHandle Start(IFlow flow)
        {
            if (flow == null) throw new ArgumentNullException(nameof(flow));

            var entry = new Entry
            {
                Id = _nextId++,
                Flow = flow,
                IsActive = true,
            };
            _entries.Add(entry);
            _states.Add(entry.Id, flow.State);

            try
            {
                flow.Start();
                _states[entry.Id] = flow.State;
            }
            catch (Exception ex)
            {
                flow.Fail($"Flow {flow.Name} OnStart failed: {ex}");
                _states[entry.Id] = flow.State;
                Deactivate(entry.Id);
                if (_log != null && _log.IsEnabled(LogLevel.Error))
                    _log.Error(Tag, $"Flow {flow.Name} OnStart failed: {ex}");
            }

            return new FlowHandle(entry.Id);
        }

        /// <inheritdoc />
        public bool Pause(FlowHandle handle)
        {
            if (!TryFindIndex(handle, out int index)) return false;

            _entries[index].Flow.Pause();
            _states[handle.Id] = _entries[index].Flow.State;
            return true;
        }

        /// <inheritdoc />
        public bool Resume(FlowHandle handle)
        {
            if (!TryFindIndex(handle, out int index)) return false;

            _entries[index].Flow.Resume();
            _states[handle.Id] = _entries[index].Flow.State;
            return true;
        }

        /// <inheritdoc />
        public bool Cancel(FlowHandle handle)
        {
            if (!TryFindIndex(handle, out int index)) return false;

            var entry = _entries[index];
            entry.Flow.Cancel();
            entry.IsActive = false;
            _entries[index] = entry;
            _states[handle.Id] = entry.Flow.State;
            return true;
        }

        /// <inheritdoc />
        public bool IsRunning(FlowHandle handle)
        {
            if (!TryFindIndex(handle, out int index)) return false;

            FlowState state = _entries[index].Flow.State;
            return state == FlowState.Running || state == FlowState.Paused;
        }

        /// <inheritdoc />
        public FlowState GetState(FlowHandle handle)
        {
            return handle.IsValid && _states.TryGetValue(handle.Id, out FlowState state) ? state : FlowState.Idle;
        }

        /// <inheritdoc />
        public bool TryGetFlow(FlowHandle handle, out IFlow flow)
        {
            if (TryFindIndex(handle, out int index))
            {
                flow = _entries[index].Flow;
                return true;
            }

            flow = null;
            return false;
        }

        /// <inheritdoc />
        public void CancelAll()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                if (entry.IsActive)
                {
                    entry.Flow.Cancel();
                    entry.IsActive = false;
                    _entries[i] = entry;
                    _states[entry.Id] = entry.Flow.State;
                }
            }
        }

        /// <inheritdoc />
        public override void OnUpdate(float deltaTime)
        {
            if (GameEntry.IsGameplayPaused) return;
            int count = _entries.Count;
            for (int i = 0; i < count; i++)
            {
                var entry = _entries[i];
                if (!entry.IsActive) continue;

                try
                {
                    entry.Flow.Tick(deltaTime);
                }
                catch (Exception ex)
                {
                    entry.Flow.Fail($"Flow {entry.Flow.Name} Tick failed: {ex}");
                    if (_log != null && _log.IsEnabled(LogLevel.Error))
                        _log.Error(Tag, $"Flow {entry.Flow.Name} Tick failed: {ex}");
                }

                if (entry.Flow.IsDone)
                {
                    entry.IsActive = false;
                }

                _states[entry.Id] = entry.Flow.State;
                _entries[i] = entry;
            }

            CompactInactive();
        }

        private bool TryFindIndex(FlowHandle handle, out int index)
        {
            if (!handle.IsValid)
            {
                index = -1;
                return false;
            }

            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Id != handle.Id || !_entries[i].IsActive) continue;

                index = i;
                return true;
            }

            index = -1;
            return false;
        }

        private void Deactivate(int id)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Id != id) continue;

                var entry = _entries[i];
                entry.IsActive = false;
                _entries[i] = entry;
                return;
            }
        }

        private void CompactInactive()
        {
            int write = 0;
            for (int read = 0; read < _entries.Count; read++)
            {
                if (!_entries[read].IsActive)
                {
                    _states.Remove(_entries[read].Id);
                    continue;
                }

                if (write != read) _entries[write] = _entries[read];
                write++;
            }

            if (write < _entries.Count)
            {
                _entries.RemoveRange(write, _entries.Count - write);
            }
        }

        private struct Entry
        {
            public int Id;
            public IFlow Flow;
            public bool IsActive;
        }
    }
}
