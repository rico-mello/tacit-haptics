namespace Loupedeck.TacitPlugin
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;

    // The channel any writer can speak into.
    //
    // The plugin integrates with no application. It exposes one file, and
    // anything that can append a line to a file can report the state of a long
    // task -- a Claude Code hook, a build script, an upload, a CI job, a render.
    //
    // This is deliberate. Elgato's ecosystem is older and larger, and a plugin
    // that maps one app's commands is a game already lost. One vocabulary with
    // many writers is a different game, and it is the only one where a shared
    // semantic layer means anything.
    //
    // Line format, and it is the whole API:
    //
    //     <taskId> <state> [label]
    //
    // where <state> is one of: started progress needsInput completed failed
    // and the bench words are: demo flip lab escalate insist answered mute unmute
    //
    // Nothing in that line can express a waveform, an intensity or an interval.
    // A writer says what happened. What it feels like is not its business.

    internal sealed class StateChannel : IDisposable
    {
        private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(400);

        private readonly Object _gate = new Object();
        private readonly Dictionary<String, TaskRunner> _tasks = new Dictionary<String, TaskRunner>(StringComparer.OrdinalIgnoreCase);
        private readonly Action<String> _raise;
        private readonly Func<TacitOptions> _options;
        private readonly Action _demo;
        private readonly Action _flip;
        private readonly Action _lab;
        private readonly Action _escalate;
        private readonly Action _insist;
        private readonly Action _answered;

        private Timer _poll;
        private Int64 _offset;

        // Test mode. An ambient signal cannot be tested on a machine that is
        // generating it -- the tester's own agent turns arrive in the same
        // thumb as the stimuli, and the participant has no way to tell them
        // apart. Muting suspends writer-driven tasks while leaving deliberate
        // triggers working.
        private Boolean _muted;

        public StateChannel(Action<String> raise, Func<TacitOptions> options, Action demo, Action flip, Action lab, Action escalate, Action insist, Action answered)
        {
            this._raise = raise;
            this._options = options;
            this._demo = demo;
            this._flip = flip;
            this._lab = lab;
            this._escalate = escalate;
            this._insist = insist;
            this._answered = answered;
            this.Path = System.IO.Path.Combine(ServiceRoot(), "tacit-channel.log");
        }

        // The channel sits beside the service, so a writer can find it from the
        // same place the plugin was installed to.
        //
        // Do not use SpecialFolder.LocalApplicationData for both. On Windows it is
        // %LOCALAPPDATA%, which is right. On macOS .NET resolves it through XDG, to
        // ~/.local/share -- while the Logi Plugin Service lives under
        // ~/Library/Application Support. Left alone, the plugin would load, log
        // that it loaded, and watch a file no writer would ever write to.
        private static String ServiceRoot()
        {
            if (OperatingSystem.IsMacOS())
            {
                return System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library", "Application Support", "Logi", "LogiPluginService");
            }

            return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Logi", "LogiPluginService");
        }

        public String Path { get; }

        public void Start()
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(this.Path);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                // Start from the end. Lines written while the plugin was not
                // running describe tasks that are long over.
                this._offset = File.Exists(this.Path) ? new FileInfo(this.Path).Length : 0;
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "[tacit] could not open the channel");
            }

            this._poll = new Timer(this.OnPoll, null, Poll, Poll);
        }

        private void OnPoll(Object _)
        {
            String[] lines;

            try
            {
                if (!File.Exists(this.Path))
                {
                    return;
                }

                var length = new FileInfo(this.Path).Length;

                if (length < this._offset)
                {
                    // Truncated or rotated. Start over rather than read garbage.
                    this._offset = 0;
                }

                if (length == this._offset)
                {
                    return;
                }

                using (var stream = new FileStream(this.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    stream.Seek(this._offset, SeekOrigin.Begin);
                    using (var reader = new StreamReader(stream))
                    {
                        lines = reader.ReadToEnd().Split('\n');
                    }

                    this._offset = length;
                }
            }
            catch (IOException)
            {
                // Being written to. Next poll gets it.
                return;
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "[tacit] channel read failed");
                return;
            }

            foreach (var line in lines)
            {
                this.Handle(line);
            }
        }

        private void Handle(String rawLine)
        {
            var line = rawLine?.Trim();

            if (String.IsNullOrEmpty(line) || line.StartsWith("#", StringComparison.Ordinal))
            {
                return;
            }

            // One word, for testing the vocabulary without needing a bound
            // key. The teaching affordance also exists as a real action, but
            // requiring a UI to hear the design is a bad way to iterate on it.
            if (line.Equals("mute", StringComparison.OrdinalIgnoreCase))
            {
                lock (this._gate)
                {
                    this._muted = true;
                    foreach (var r in this._tasks.Values)
                    {
                        r.Dispose();
                    }

                    this._tasks.Clear();
                }

                PluginLog.Info("[tacit] MUTED - real tasks are ignored. demo and flip still work.");
                return;
            }

            if (line.Equals("unmute", StringComparison.OrdinalIgnoreCase))
            {
                lock (this._gate)
                {
                    this._muted = false;
                }

                PluginLog.Info("[tacit] unmuted - reporting real tasks again.");
                return;
            }

            if (line.Equals("demo", StringComparison.OrdinalIgnoreCase))
            {
                this._demo?.Invoke();
                return;
            }

            // Blind discrimination on the one pair the whole perceptual budget
            // was spent on. The plugin picks, so neither the person feeling it
            // nor the person firing it knows which one it was until afterwards.
            if (line.Equals("flip", StringComparison.OrdinalIgnoreCase))
            {
                this._flip?.Invoke();
                return;
            }

            if (line.Equals("lab", StringComparison.OrdinalIgnoreCase))
            {
                this._lab?.Invoke();
                return;
            }

            // The escalation, on its own, so it can be judged. It is kept out
            // of the teaching run deliberately: the run teaches the off-beat
            // PLACEMENT, which has to be learned against a rhythm. The
            // contraction leans on a convention people already hold, so it
            // needs verifying rather than teaching.
            if (line.Equals("escalate", StringComparison.OrdinalIgnoreCase))
            {
                this._escalate?.Invoke();
                return;
            }

            // The plateau, which `escalate` cannot measure. Its
            // meaning is not a tempo, it is a standing demand: it holds at the
            // floor and does not stop until somebody answers. So this one does
            // not stop either. Writing `answered` is the action that ends it,
            // and having to go and perform that action is the whole condition.
            if (line.Equals("insist", StringComparison.OrdinalIgnoreCase))
            {
                this._insist?.Invoke();
                return;
            }

            if (line.Equals("answered", StringComparison.OrdinalIgnoreCase))
            {
                this._answered?.Invoke();
                return;
            }

            var parts = line.Split(new[] { ' ' }, 3);

            if (parts.Length < 2)
            {
                PluginLog.Warning($"[tacit] ignored, expected '<taskId> <state> [label]': {line}");
                return;
            }

            var taskId = parts[0];
            var label = parts.Length > 2 ? parts[2] : String.Empty;

            if (!TaskStateParser.TryParse(parts[1], out var state))
            {
                PluginLog.Warning($"[tacit] ignored, '{parts[1]}' is not one of started/progress/needsInput/completed/failed");
                return;
            }

            lock (this._gate)
            {
                if (this._muted)
                {
                    // Silently dropped, not queued. A test run should not be
                    // followed by a burst of everything that happened during it.
                    return;
                }

                if (state == TaskState.Started)
                {
                    if (this._tasks.ContainsKey(taskId))
                    {
                        return;
                    }

                    var runner = new TaskRunner(taskId, label, this._options(), this._raise, this.Remove);
                    this._tasks[taskId] = runner;

                    if (this._tasks.Count > 1)
                    {
                        // One thumb, two tasks. Left deliberately unresolved:
                        // whether concurrent tasks are legible at all is an open
                        // question and the honest answer may be that the channel
                        // holds one thing at a time.
                        PluginLog.Warning($"[tacit] {this._tasks.Count} tasks now running — concurrent legibility is untested");
                    }

                    runner.Start();
                    return;
                }

                if (this._tasks.TryGetValue(taskId, out var existing))
                {
                    existing.Report(state, label);
                }

                // A terminal or progress word for a task we never saw start is
                // dropped on purpose. Without a start there is no referent, and
                // a pulse with nothing to attribute it to reads as a malfunction
                // rather than as information.
            }
        }

        private void Remove(String taskId)
        {
            lock (this._gate)
            {
                if (this._tasks.TryGetValue(taskId, out var runner))
                {
                    this._tasks.Remove(taskId);
                    runner.Dispose();
                }
            }
        }

        public void Dispose()
        {
            this._poll?.Dispose();
            this._poll = null;

            lock (this._gate)
            {
                foreach (var runner in this._tasks.Values)
                {
                    runner.Dispose();
                }

                this._tasks.Clear();
            }
        }
    }
}
