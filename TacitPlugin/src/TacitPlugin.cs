namespace Loupedeck.TacitPlugin
{
    using System;
    using System.Threading;

    // Tacit -- the state of a long-running task, in your thumb.
    //
    // You start something that will not finish now, and then you are blind. The
    // market's answer is a visual notification, which takes you out of the
    // application the device exists to keep you inside of.
    //
    // Three layers, and the middle one is what the SDK is missing:
    //
    //   semantic   TaskState.cs        five meanings a writer can report
    //   mapping    eventMapping.yaml   which primitive each meaning resolves to
    //   primitive  the fifteen waveforms shipped with the device
    //
    // The SDK ships the bottom two. Without the top one every plugin author
    // chooses alone, and one thumb receives a different language from every
    // plugin installed -- a cost the user charges to the device, because they
    // cannot tell whose vibration it was.
    //
    // The job is smaller than it first looks. The thumb does not carry what
    // happened; it carries the decision to turn your head. The detail is
    // already on a screen -- often a second monitor, in plain sight, unread.

    public class TacitPlugin : Plugin
    {
        private StateChannel _channel;

        // This also governs whether Options+ lists the plugin. Isolated over
        // three states with one variable moving:
        //   false + manifest devices            -> not listed
        //   false + SupportedDevices = All      -> not listed
        //   true  + manifest devices            -> LISTED, actions browsable
        public override Boolean UsesApplicationApiOnly => true;

        // Deliberately not linked to any application. Linking ties activation
        // to foreground focus, and reaching someone working somewhere else is
        // the entire point. Verified with a dedicated probe before anything
        // else was built: an unlinked plugin keeps running and its pulses keep
        // arriving across five different foreground applications.
        public override Boolean HasNoApplication => true;

        internal TacitOptions Options { get; private set; } = new TacitOptions();

        public TacitPlugin()
        {
            PluginLog.Init(this.Log);
            PluginResources.Init(this.Assembly);

        }

        public override void Load()
        {
            this.Options = TacitOptions.Load(this.ReadOption);

            // Registration has to happen here: doing it in the constructor
            // throws, and the service disables the plugin.
            // NO AddAction. The SDK discovers ActionEditorCommand subclasses on
            // its own; a manual call registers a SECOND instance of the same
            // action, and the panel then renders no controls -- the editor the
            // UI binds to is not the editor the constructor filled.
            this._channel = new StateChannel(this.Raise, () => this.Options, this.PlayDemoRun, this.FlipTerminal, this.PlayFailureLab, this.PlayEscalation, this.PlayInsistence, this.StopInsistence);
            this._channel.Start();

            PluginLog.Info("[tacit] loaded. Nothing fires until a writer reports a task.");
            PluginLog.Info($"[tacit] settings: {this.Options}");
            PluginLog.Info($"[tacit] channel: {this._channel.Path}");
            PluginLog.Info("[tacit] line format: <taskId> <state> [label]");
            PluginLog.Info("[tacit] states: started | progress | needsInput | completed | failed");
            PluginLog.Info("[tacit] write \"demo\" alone on a line to play the teaching run");
        }

        public override void Unload()
        {
            this._channel?.Dispose();
            this._channel = null;
            PluginLog.Info("[tacit] unloaded.");
        }

        // ---- settings -----------------------------------------------------

        private String ReadOption(String key) =>
            this.TryGetPluginSetting(key, out var value) ? value : null;

        internal void SaveOption(String key, String value)
        {
            try
            {
                this.SetPluginSetting(key, value);
                this.Options = TacitOptions.Load(this.ReadOption);
                PluginLog.Info($"[tacit] settings: {this.Options}");
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"[tacit] could not save {key}");
            }
        }

        // ---- teaching -----------------------------------------------------

        // One compressed run, because the only state that genuinely needs
        // teaching is defined relationally -- "needs you" is a pulse OFF THE
        // BEAT, and a violation of a rhythm cannot be recognised without the
        // rhythm. A sample teaches a word; the meaning is in the sentence.
        internal void PlayDemoRun()
        {
            lock (this._demoGate)
            {
                if (this._demoRunning)
                {
                    PluginLog.Info("[tacit] demo already running - ignored");
                    return;
                }

                this._demoRunning = true;
            }

            PluginLog.Info("[tacit] demo run");

            // TWO sentences, not one with a compound ending.
            //
            // The first version played completion, waited three seconds, then
            // played failure -- and three seconds inside a 2.5 s beat is not a
            // phrase boundary, it is the next beat. So the two endings arrived
            // inside the same rhythmic phrase and were read as one thing.
            //
            // It also established only two beats before violating the rhythm.
            // You cannot violate an expectation that has not formed yet, which
            // is very likely why the off-beat pulse went unmentioned.
            //
            // Now: four beats to establish the cadence, the interruption
            // exactly between two of them, the rhythm resumed, then an ending.
            // Then seven seconds of silence -- three and a half beats, long
            // enough to read as "that sentence is over" -- and a second, shorter
            // run that ends the other way.
            //
            // Failure is shown in context, as an ending to a run, because that
            // is how it actually arrives. Never as an isolated triple.
            var steps = new (Int32 AtMs, TaskState State, String Note)[]
            {
                (0,     TaskState.Started,    "one -- it starts"),
                (2000,  TaskState.Progress,   "beat"),
                (4000,  TaskState.Progress,   "beat"),
                (6000,  TaskState.Progress,   "beat"),
                (8000,  TaskState.Progress,   "beat  (cadence established)"),
                // The interruption stays a single off-beat pulse here, and the
                // escalation deliberately does not appear. needsInput is the
                // one state that must be TAUGHT, because it is defined against
                // a rhythm -- and that is the off-beat placement, which this
                // run does teach. The contraction leans on a convention people
                // already hold, so it needs verifying rather than teaching --
                // that is what `escalate` is for. Keeping it out is what keeps
                // this run at about twenty-nine seconds.
                (9000,  TaskState.NeedsInput, "*** OFF THE BEAT - it needs you ***"),
                (10000, TaskState.Progress,   "beat  (you answered, it resumes)"),
                (12000, TaskState.Progress,   "beat"),
                (14000, TaskState.Completed,  "DONE - one pulse"),

                // 7 s of nothing. A phrase boundary, not a gap in the rhythm.
                (21000, TaskState.Started,    "two -- another run starts"),
                (23000, TaskState.Progress,   "beat"),
                (25000, TaskState.Progress,   "beat"),
            };

            var raise = this.Raise;
            var opts = this.Options;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var elapsed = 0;

                    foreach (var step in steps)
                    {
                        Thread.Sleep(Math.Max(0, step.AtMs - elapsed));
                        elapsed = step.AtMs;
                        PluginLog.Info($"[tacit] demo +{step.AtMs / 1000.0,5:F1}s  {step.Note}");

                        // The teaching run has to play what the plugin plays.
                        // started and completed are figures too once high
                        // contrast is on -- and a demo that
                        // taught single pulses would teach a vocabulary the
                        // plugin does not speak.
                        var pulses = step.State switch
                        {
                            TaskState.Started => opts.StartedPulses,
                            TaskState.Completed => opts.CompletedPulses,
                            _ => 1,
                        };

                        if (pulses > 1)
                        {
                            Sequence.Pulses(raise, TaskStateParser.EventName(step.State), pulses, opts.FigureGap);
                        }
                        else
                        {
                            raise(TaskStateParser.EventName(step.State));
                        }
                    }

                    Thread.Sleep(2000);
                    PluginLog.Info($"[tacit] demo +{27.0,5:F1}s  FAILED - {opts.FailurePulses} pulses");
                    Sequence.Pulses(raise, TaskStateParser.EventName(TaskState.Failed), opts.FailurePulses, opts.FigureGap);
                    Thread.Sleep(2000);
                }
                finally
                {
                    lock (this._demoGate)
                    {
                        this._demoRunning = false;
                    }
                }
            });
        }

        // Four renderings of the same failure figure, separated by long
        // silences, so the only variable is the gap between its pulses.
        //
        // The hypothesis: an LRA needs time to reach full excursion and to
        // settle. At 220 ms the second and third pulses may never get there, so
        // the figure reads as weak and continuous -- which is exactly how it was
        // described. If that is right, the fix is a WIDER gap, which makes each
        // pulse stronger rather than weaker, and no waveform needs replacing.
        //
        // A completion pulse is played first as a reference, because the whole
        // question is "would I notice this the way I notice that one".
        private volatile Boolean _insisting;

        // The plateau, which `escalate` is structurally unable to measure.
        //
        // The design bounds the escalation AND refuses to let it fall silent:
        // this is the highest-cost state, and a signal that gives up leaves the
        // task blocked with nobody knowing. So what the plateau means is "it
        // will keep asking until you answer" -- a relationship, not a tempo --
        // and the only way to feel that is to be the one who has to go and end
        // it, for however long that takes.
        //
        // Therefore this does not stop. It escalates, reaches the floor, and
        // holds there until `answered` is written.
        internal void PlayInsistence()
        {
            if (this._insisting)
            {
                PluginLog.Info("[tacit] insistence already running - write 'answered' to end it");
                return;
            }

            this._insisting = true;
            var opts = this.Options;
            var raise = this.Raise;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                var interval = opts.NeedsInputFirst;
                var floor = opts.NeedsInputFloor;
                var pulses = 0;
                var began = DateTime.UtcNow;

                PluginLog.Info($"[tacit] insistence - holding until answered. first {interval.TotalSeconds:F2}s, floor {floor.TotalSeconds:F2}s, contrast {(opts.HighContrast ? "high" : "default")}");

                while (this._insisting)
                {
                    raise(TaskStateParser.EventName(TaskState.NeedsInput));
                    pulses++;

                    // Slice the wait so that answering ends it promptly rather
                    // than at the next interval boundary. A demand that keeps
                    // going after you have answered is a different defect.
                    var waited = TimeSpan.Zero;
                    while (this._insisting && waited < interval)
                    {
                        Thread.Sleep(250);
                        waited += TimeSpan.FromMilliseconds(250);
                    }

                    var next = TimeSpan.FromMilliseconds(interval.TotalMilliseconds * 0.75);
                    interval = next < floor ? floor : next;
                }

                PluginLog.Info($"[tacit] insistence - answered after {pulses} pulses and {(DateTime.UtcNow - began).TotalSeconds:F0}s");
            });
        }

        internal void StopInsistence()
        {
            if (!this._insisting)
            {
                PluginLog.Info("[tacit] nothing was insisting");
                return;
            }

            this._insisting = false;
        }

        // The escalation, alone, long enough to read as a trend.
        //
        // Six pulses at 12, 9, 6.75, 5.06, 4 and 4 seconds -- the real
        // parameters, not a compressed illustration, because a compression
        // would answer a question nobody asked. Roughly 41 s at default
        // contrast and 34 s at high, and it stops on its own rather than
        // holding at the plateau forever: this is a bench, not the plugin.
        //
        // What it is for: the escalation is the one figure real use had never
        // produced -- the teaching demo answers it a second after raising it,
        // and needsInput has fired zero times in the whole log.
        internal void PlayEscalation()
        {
            var opts = this.Options;
            var raise = this.Raise;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                var interval = opts.NeedsInputFirst;
                var floor = opts.NeedsInputFloor;

                PluginLog.Info($"[tacit] escalation bench - first {interval.TotalSeconds:F2}s, floor {floor.TotalSeconds:F2}s, contrast {(opts.HighContrast ? "high" : "default")}");

                for (var i = 0; i < 6; i++)
                {
                    raise(TaskStateParser.EventName(TaskState.NeedsInput));
                    PluginLog.Info($"[tacit] escalation +{i + 1} - next in {interval.TotalSeconds:F2}s");
                    Thread.Sleep(interval);

                    var next = TimeSpan.FromMilliseconds(interval.TotalMilliseconds * 0.75);
                    interval = next < floor ? floor : next;
                }

                PluginLog.Info("[tacit] escalation bench - done. In the plugin it would hold at the floor, never stop.");
            });
        }

        // A variant label that cannot disagree with the figure it announces.
        private static String Variant(String letter, TacitOptions o, String note) =>
            $"{letter}  {o.FailurePulses} pulses @ {o.FigureGap.TotalMilliseconds:F0}ms  ({note})";

        internal void PlayFailureLab()
        {
            var raise = this.Raise;
            var failed = TaskStateParser.EventName(TaskState.Failed);

            // The four figures, and every label states what it IS rather than
            // what it once was. Both of these were wrong until 28 August, and
            // the second was worse than a wrong label:
            //
            //   A was labelled "current default" and has not been the default
            //   since the gap moved to 450 ms. It stays in the set because it is the
            //   comparison that settled the question, and comparing against
            //   the superseded figure is the whole point of running this.
            //
            //   D played 4 pulses at 450 ms and called itself high contrast.
            //   High contrast is 4 at 600 (TacitOptions.FigureGap). So the
            //   bench played a stimulus the plugin does
            //   not produce, and anyone judging high contrast here judged
            //   something that does not ship.
            //
            // A bench that plays the wrong figure does not give a wrong
            // opinion, it gives an opinion about a different thing -- and the
            // reading looks exactly like data about the design.
            // A comment is not a guard, so the two variants that claim to BE
            // something are read off the thing they claim to be. B and D can
            // no longer drift from the plugin; A and C are literal because
            // they are deliberately not what ships.
            var shipped = new TacitOptions();
            var contrast = new TacitOptions { HighContrast = true };

            var variants = new (String Label, Int32 Count, Int32 GapMs)[]
            {
                ("A  3 pulses @ 220ms  (superseded - decision 24)", 3, 220),
                (Variant("B", shipped, "current default"),
                    shipped.FailurePulses, (Int32)shipped.FigureGap.TotalMilliseconds),
                ("C  3 pulses @ 700ms", 3, 700),
                (Variant("D", contrast, "high contrast, as it ships"),
                    contrast.FailurePulses, (Int32)contrast.FigureGap.TotalMilliseconds),
            };

            ThreadPool.QueueUserWorkItem(_ =>
            {
                PluginLog.Info("[tacit] failure lab - reference first");
                raise(TaskStateParser.EventName(TaskState.Completed));
                Thread.Sleep(5000);

                foreach (var v in variants)
                {
                    PluginLog.Info($"[tacit] failure lab  {v.Label}");

                    for (var i = 0; i < v.Count; i++)
                    {
                        raise(failed);
                        if (i < v.Count - 1)
                        {
                            Thread.Sleep(v.GapMs);
                        }
                    }

                    Thread.Sleep(5000);
                }

                PluginLog.Info("[tacit] failure lab - done");
            });
        }

        // Fires ONE terminal state, chosen at random, and writes which one it
        // was to the log. Nobody knows in advance -- not the hand, not whoever
        // typed the trigger. This is the blind half of the discrimination test
        // that decides whether the completed/failed pair actually separates.
        private readonly Random _coin = new Random();
        private readonly Object _demoGate = new Object();
        private Boolean _demoRunning;

        internal void FlipTerminal()
        {
            var failed = this._coin.Next(2) == 1;
            var state = failed ? TaskState.Failed : TaskState.Completed;
            var opts = this.Options;

            if (failed)
            {
                Sequence.Pulses(this.Raise, TaskStateParser.EventName(state), opts.FailurePulses, opts.FigureGap);
            }
            else
            {
                this.Raise(TaskStateParser.EventName(state));
            }

            PluginLog.Info($"[tacit] flip -> {state.ToString().ToUpperInvariant()}");
        }

        // Fired when the contrast setting changes, so the difference is felt at
        // the moment it is chosen instead of being described in a tooltip.
        internal void PlayContrastSample()
        {
            var opts = this.Options;
            Sequence.Pulses(this.Raise, TaskStateParser.EventName(TaskState.Failed), opts.FailurePulses, opts.FigureGap);
        }

        // ---- output -------------------------------------------------------

        private void Raise(String eventName)
        {
            if (String.IsNullOrEmpty(eventName))
            {
                return;
            }

            try
            {
                this.PluginEvents.RaiseEvent(eventName);
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"[tacit] RaiseEvent('{eventName}') threw");
            }
        }
    }
}
