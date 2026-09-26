namespace Loupedeck.TacitPlugin
{
    using System;
    using System.Threading;

    // One long-running task, and the rhythm it is given.
    //
    // Everything about *how* a state feels is decided here, never by the writer.
    // The writer says "progress" as often as it likes -- a Claude Code hook
    // fires after every batch of tool calls, several times a minute -- and
    // turning that straight into pulses would be noise. So the writer reports
    // activity and this class owns the cadence.
    //
    // The cadence is not decoration. A regular interval is what lets someone
    // project when the next beat is due, and a beat that does not arrive is
    // then itself an event. Timing on this platform holds to 14 ms over a
    // nominal 10 s, so the projection is reliable enough to build on.

    internal sealed class TaskRunner : IDisposable
    {
        // needsInput escalates by CONTRACTING its interval.
        // Tightening means urgency in nearly every signal a person already
        // knows: indicator, reversing sensor, alarm, heart monitor. For the one
        // state that genuinely has to be learned, leaning on a grammar people
        // already hold is the best outcome available.
        //
        // It contracts to a plateau and holds. Unbounded escalation is the
        // seatbelt chime, the most disliked object in automotive design. It
        // never becomes a buzz -- and it never gives up, because this is the
        // highest-cost state and a signal that falls silent leaves the task
        // blocked with nobody knowing.
        // The first interval and the floor live in TacitOptions, because high
        // contrast tightens both. The contraction ratio does not:
        // it is the shape of the escalation rather than its size.
        private const Double NeedsInputContraction = 0.75;

        // A writer can die without ever reporting a terminal state -- the
        // process is killed, the machine sleeps. Rather than beat forever,
        // give up quietly after this long with no word at all.
        private static readonly TimeSpan Abandon = TimeSpan.FromMinutes(15);

        private readonly Object _gate = new Object();
        private readonly String _taskId;
        private readonly String _label;
        private readonly TacitOptions _options;
        private readonly Action<String> _raise;
        private readonly Action<String> _onFinished;

        private Timer _timer;
        private DateTime _lastWord;
        private TimeSpan _needsInputInterval;
        private Boolean _announced;
        private Boolean _awaitingInput;
        private Boolean _done;

        public TaskRunner(String taskId, String label, TacitOptions options, Action<String> raise, Action<String> onFinished)
        {
            this._taskId = taskId;
            this._label = label;
            this._options = options;
            this._raise = raise;
            this._onFinished = onFinished;
            this._lastWord = DateTime.UtcNow;
            this._needsInputInterval = options.NeedsInputFirst;
        }

        public void Start()
        {
            lock (this._gate)
            {
                PluginLog.Info($"[tacit] {this._taskId} watching - {this._label}");

                // Silent for now. The opening pulse waits until the task has
                // earned it.
                this._timer = new Timer(this.OnBeat, null, this._options.QuietWindow, this._options.Heartbeat);
            }
        }

        // The opening. Establishes the referent -- without it every later pulse
        // arrives from nowhere, and a pulse with nothing to attribute it to
        // reads as a malfunction rather than as information.
        private void Announce()
        {
            if (this._announced)
            {
                return;
            }

            this._announced = true;
            PluginLog.Info($"[tacit] {this._taskId} announced after {this._options.QuietWindow.TotalSeconds:F0}s");
            this.RaiseFigure(TaskState.Started, this._options.StartedPulses);
        }

        public void Report(TaskState state, String label)
        {
            lock (this._gate)
            {
                if (this._done)
                {
                    return;
                }

                this._lastWord = DateTime.UtcNow;

                switch (state)
                {
                    case TaskState.Progress:
                        // Deliberately silent. Activity keeps the task alive and
                        // resets the abandon clock; it does not produce a pulse.
                        if (this._awaitingInput)
                        {
                            // Work resumed after the question was answered.
                            this._awaitingInput = false;
                            this._needsInputInterval = this._options.NeedsInputFirst;
                            this.Reschedule(this._options.Heartbeat);
                            PluginLog.Info($"[tacit] {this._taskId} resumed");
                        }
                        break;

                    case TaskState.NeedsInput:
                        if (!this._awaitingInput)
                        {
                            this._awaitingInput = true;

                            // Forces announcement even inside the quiet window.
                            // A task waiting on a person is not a short task you
                            // could have watched -- it is one that has stopped
                            // and will not restart on its own.
                            this._announced = true;

                            PluginLog.Info($"[tacit] {this._taskId} needs input - {label}");
                            this._raise(TaskStateParser.EventName(TaskState.NeedsInput));
                            this._needsInputInterval = this._options.NeedsInputFirst;
                            this.Reschedule(this._needsInputInterval);
                        }
                        break;

                    case TaskState.Completed:
                        this.Finish(TaskState.Completed, label);
                        break;

                    case TaskState.Failed:
                        this.Finish(TaskState.Failed, label);
                        break;

                    case TaskState.Started:
                        // Already running. A second start for the same id is a
                        // writer bug, not a new task.
                        break;
                }
            }
        }

        private void OnBeat(Object _)
        {
            lock (this._gate)
            {
                if (this._done || this._timer == null)
                {
                    return;
                }

                if (this._awaitingInput)
                {
                    // Keep asking, and ask a little sooner each time, down to
                    // the floor. The escalation is proportionate to the cost of
                    // being ignored -- which is what that cost actually does.
                    this._raise(TaskStateParser.EventName(TaskState.NeedsInput));

                    var next = TimeSpan.FromMilliseconds(this._needsInputInterval.TotalMilliseconds * NeedsInputContraction);
                    var floor = this._options.NeedsInputFloor;
                    this._needsInputInterval = next < floor ? floor : next;
                    this.Reschedule(this._needsInputInterval);
                    return;
                }

                if (DateTime.UtcNow - this._lastWord > Abandon)
                {
                    PluginLog.Warning($"[tacit] {this._taskId} abandoned - no word for {Abandon.TotalMinutes:F0} min");
                    this.Close();
                    return;
                }

                if (!this._announced)
                {
                    // First beat of a task that has now run long enough to be
                    // worth knowing about. The opening lands here; the heartbeat
                    // starts from the next one.
                    this.Announce();
                    return;
                }

                if (this._options.HeartbeatEnabled)
                {
                    this._raise(TaskStateParser.EventName(TaskState.Progress));
                }
            }
        }

        private void Finish(TaskState terminal, String label)
        {
            PluginLog.Info($"[tacit] {this._taskId} {terminal.ToString().ToLowerInvariant()} - {label}");

            if (!this._announced)
            {
                // Never announced, so there is nothing to close. The task
                // finished inside the quiet window and the hand was never
                // involved. Ending it with a pulse would be announcing a thing
                // only at the moment it stopped mattering.
                PluginLog.Info($"[tacit] {this._taskId} finished inside the quiet window - stayed silent");
                this.Close();
                return;
            }

            this.RaiseFigure(terminal, terminal == TaskState.Failed
                ? this._options.FailurePulses
                : this._options.CompletedPulses);

            this.Close();
        }

        // Every figure of more than one pulse goes through here, so the
        // actuator's gap is honoured wherever pulses are counted -- not only
        // in failure, which is merely where it was discovered.
        private void RaiseFigure(TaskState state, Int32 pulses)
        {
            var eventName = TaskStateParser.EventName(state);

            if (pulses <= 1)
            {
                this._raise(eventName);
                return;
            }

            Sequence.Pulses(this._raise, eventName, pulses, this._options.FigureGap);
        }

        private void Reschedule(TimeSpan period) => this._timer?.Change(period, period);

        private void Close()
        {
            this._done = true;
            this._timer?.Dispose();
            this._timer = null;
            this._onFinished?.Invoke(this._taskId);
        }

        public void Dispose()
        {
            lock (this._gate)
            {
                this._done = true;
                this._timer?.Dispose();
                this._timer = null;
            }
        }
    }

    // Fires a figure of N pulses off the calling thread, so the gap between
    // them is real rather than blocking whatever produced them.
    internal static class Sequence
    {
        public static void Pulses(Action<String> raise, String eventName, Int32 count, TimeSpan gap)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                for (var i = 0; i < count; i++)
                {
                    raise(eventName);
                    if (i < count - 1)
                    {
                        Thread.Sleep(gap);
                    }
                }
            });
        }
    }
}
