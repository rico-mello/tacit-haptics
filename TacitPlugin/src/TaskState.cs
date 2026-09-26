namespace Loupedeck.TacitPlugin
{
    using System;

    // ---------------------------------------------------------------------
    // THE SEMANTIC LAYER
    // ---------------------------------------------------------------------
    //
    // This file is the contract, and it is deliberately the smallest file in
    // the plugin.
    //
    // A writer -- a Claude Code hook, a build script, an upload, a CI job --
    // reports one of these five words. It cannot express a waveform, an
    // intensity, an interval, or a repetition. It says what happened; the
    // plugin decides what that feels like.
    //
    // That separation is the whole argument. The Logi Actions SDK already
    // ships two of the three layers a token system needs: a mapping file
    // (events/extra/eventMapping.yaml) and a fixed set of primitives (the
    // fifteen waveforms). What it does not ship is a semantic layer -- shared
    // names that mean the same thing across plugins written by different
    // authors.
    //
    // Without one, every plugin author picks waveforms alone, and the user has
    // one thumb receiving contradictory languages from every plugin they have
    // installed. The cost of that lands on the device, not on any one plugin.
    //
    // So: writers speak meaning, never sensation.

    public enum TaskState
    {
        /// A task has begun. Fires once. Establishes the referent -- without it
        /// every later pulse arrives from nowhere.
        Started,

        /// The task is alive and doing work. Reported as often as the writer
        /// likes; the plugin rate-limits it into a cadence. The writer reports
        /// activity, the plugin owns the rhythm.
        Progress,

        /// The task cannot continue without the user. The one state that has to
        /// interrupt rather than inform.
        NeedsInput,

        /// The task finished, and it worked.
        Completed,

        /// The task ended, and it did not work. Must never be confusable with
        /// Completed. Position in the sequence tells most pairs apart, but these
        /// two hold the same position, at a moment an ending is already
        /// expected -- so this is the pair the perceptual budget gets spent on.
        Failed,
    }

    internal static class TaskStateParser
    {
        /// The channel accepts exactly these words, case-insensitively.
        /// Anything else is refused and logged, rather than guessed at.
        public static Boolean TryParse(String word, out TaskState state)
        {
            switch (word?.Trim().ToLowerInvariant())
            {
                case "started": state = TaskState.Started; return true;
                case "progress": state = TaskState.Progress; return true;
                case "needsinput": state = TaskState.NeedsInput; return true;
                case "completed": state = TaskState.Completed; return true;
                case "failed": state = TaskState.Failed; return true;
                default: state = default; return false;
            }
        }

        /// Semantic name -> the plugin event raised for it. The waveform each
        /// event resolves to lives in events/extra/eventMapping.yaml, so the
        /// tactile realisation can be changed without touching code -- which is
        /// what makes the vocabulary testable against real hands.
        public static String EventName(TaskState state) => state switch
        {
            TaskState.Started => "taskStarted",
            TaskState.Progress => "taskTick",
            TaskState.NeedsInput => "taskNeedsInput",
            TaskState.Completed => "taskCompleted",
            TaskState.Failed => "taskFailed",
            _ => null,
        };

        public static Boolean IsTerminal(TaskState state) =>
            state == TaskState.Completed || state == TaskState.Failed;
    }
}
