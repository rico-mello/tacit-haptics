namespace Loupedeck.TacitPlugin
{
    using System;

    // The configuration surface, and it exists under protest.
    //
    // Tacit has no actions. It runs in the background, which is its entire
    // purpose. But the SDK's configuration UI -- slider, checkbox, button,
    // label, separator, and it is genuinely good -- is reachable only by
    // configuring a bound action. There is a plugin settings API, and it is
    // encrypted key-value storage with no user-facing surface at all.
    //
    // So a background plugin can remember a preference and has nowhere to let
    // anyone change it.
    //
    // This class is the workaround: an action whose only job is to be bound to
    // a key so that its editor panel can be opened. **Spending a physical key
    // on a settings dialog is the wrong answer, and it is deliberately the
    // answer shipped here** -- because the discomfort of it is the argument for
    // what should exist instead.
    //
    // A second consequence, and it is not hypothetical: bind this twice and you
    // get two panels writing the same global settings, because the editor was
    // designed for per-binding parameters rather than plugin-wide ones.
    //
    // A third: each slot keeps the values it was saved
    // with and opens on them, whatever is running, and SetValue when the dialog
    // opens does not change that. So SAVE makes what the dialog shows the
    // running settings, and CANCEL changes nothing.

    public class TacitSettingsAction : ActionEditorCommand
    {
        private const String CtlQuiet = "quietWindow";
        private const String CtlHeartbeat = "heartbeat";
        private const String CtlHeartbeatOn = "heartbeatOn";
        private const String CtlContrast = "highContrast";
        private const String CtlDemo = "playRun";

        private static readonly Dictionary<String, String> ControlFor = new Dictionary<String, String>
        {
            [TacitOptions.KeyQuietWindow] = CtlQuiet,
            [TacitOptions.KeyHeartbeat] = CtlHeartbeat,
            [TacitOptions.KeyHeartbeatOn] = CtlHeartbeatOn,
            [TacitOptions.KeyHighContrast] = CtlContrast,
        };

        // Kept so that their defaults can follow the running values.
        private readonly ActionEditorNumberSelector _quiet;
        private readonly ActionEditorNumberSelector _heartbeat;
        private readonly ActionEditorCheckbox _heartbeatOn;
        private readonly ActionEditorCheckbox _contrast;

        // What the last dialog showed, set when it opens and kept until the next
        // one opens. Null until a dialog has opened since the plugin loaded.
        private readonly Object _edit = new Object();
        private TacitOptions _shown;

        public TacitSettingsAction()
            // The Actions Ring of the MX Master 4, and nothing else: the device the
            // manifest declares (supportedDevices: ActionsRing, which the SDK parses
            // to Loupedeck72). DeviceType.All put this action on devices Tacit was
            // never designed or tested for, such as the MX Creative Keypad.
            : base(DeviceType.Loupedeck72)
        {
            this.DisplayName = "Tacit settings";
            this.Description = "How long before a task is worth telling you about, and how often it reminds you. Both answers are personal - nobody can guess them for you.";
            this.GroupName = "Tacit";

            // Question 1. The most personal setting there is.
            this._quiet = this.ActionEditor.AddControlEx(new ActionEditorSlider(
                    CtlQuiet,
                    "Stay quiet for",
                    "A task that finishes inside this window is one you could have just watched. It is never announced at all - not its start, not its end.")
                .SetValues(5, 90, 20, 5)
                // 90s, not 120. Two minutes is longer than anything this question is
                // really asking, and it also holds the value to three characters.
                //
                // {0} substitutes; {0:F0} renders literally, and silently. The value
                // area is a proportion of the LABEL, not of the panel: a 21-character
                // label leaves room for three characters, a 12-character one for about
                // nine, and widening the dialog does not move it. That is the whole of
                // an author's control over this layout.
                .SetFormatString("{0}s"));

            // Question 2.
            this._heartbeat = this.ActionEditor.AddControlEx(new ActionEditorSlider(
                    CtlHeartbeat,
                    "Then remind you every",
                    "The beat that means it is still alive. It has to be regular - a beat you can predict is what makes a missing beat mean something.")
                .SetValues(5, 60, 15, 5)
                .SetFormatString("{0}s"));

            this._heartbeatOn = this.ActionEditor.AddControlEx(new ActionEditorCheckbox(
                    CtlHeartbeatOn,
                    "Keep the beat",
                    "Turn this off to feel only the start and the end.")
                .SetDefaultValue(true));

            this.ActionEditor.AddControlEx(new ActionEditorSeparator("sep1"));

            // Question 3. The accessibility carve-out.
            this._contrast = this.ActionEditor.AddControlEx(new ActionEditorCheckbox(
                    CtlContrast,
                    "Push them apart",
                    "This changes how distinct the patterns feel, never what they mean - done means done on every machine. Longer figures and wider gaps, for hands that need more separation. It does not make anything louder; use the Options+ intensity setting for that.")
                .SetDefaultValue(false));

            this.ActionEditor.AddControlEx(new ActionEditorSeparator("sep2"));

            // The teaching affordance. NOT a list of patterns
            // with a preview button beside each. The one state that genuinely
            // needs teaching is "needs you", and it is a pulse OFF THE BEAT --
            // a violation of a rhythm cannot be recognised without the rhythm.
            // Playing it in isolation is a knock with no beat to be off.
            //
            // So this plays a whole compressed run. A sample teaches a word;
            // the meaning here is in the sentence.
            this.ActionEditor.AddControlEx(new ActionEditorButton(
                CtlDemo,
                // Do not try to widen this button. It has been tried and it does not
                // work, and the next person to look at that lonely centred button will
                // have the same idea:
                //
                //   - no ActionEditor* type carries an alignment, width or padding
                //     member, and the assembly names none anywhere;
                //   - the button uses its LabelText as its own face, so there is no
                //     separate label to sit on the left like every other control;
                //   - a longer face does widen it, but only by lying about the copy;
                //   - and padding the face with spaces is TRIMMED before render, so
                //     the one lever the API leaves is closed too.
                //
                // Centred and content-width is what an author gets.
                "Play a run",
                "Rest your thumb on the panel and press. About twenty-nine seconds: it starts, it keeps going, it interrupts to ask you something, it finishes. Then the other ending, so you can feel the difference between done and failed."));

            this.ActionEditor.Started += this.OnEditorStarted;
            this.ActionEditor.ControlValueChanged += this.OnControlValueChanged;
            this.ActionEditor.Finished += this.OnEditorFinished;
            this.DiagControls("after-ctor");
        }

        // A slot that has never been saved opens on the controls' defaults, and
        // the service reads them off these objects each time it sends the action
        // to Options+. So the defaults carry the running values.
        internal void ShowAsDefaults(TacitOptions o)
        {
            SetDefault(this._quiet, o.QuietWindowSeconds);
            SetDefault(this._heartbeat, o.HeartbeatSeconds);
            this._heartbeatOn.SetDefaultValue(o.HeartbeatEnabled);
            this._contrast.SetDefaultValue(o.HighContrast);
        }

        private static void SetDefault(ActionEditorNumberSelector slider, Int32 value) =>
            slider.SetValues(slider.MinimumValue, slider.MaximumValue, Math.Clamp(value, slider.MinimumValue, slider.MaximumValue), slider.Step);

        // What the dialog shows, over `fallback` for any value it did not send.
        // Options+ sends the dialog's values with each editor event.
        private static TacitOptions Read(ActionEditorState state, TacitOptions fallback) =>
            TacitOptions.Load(key => state.GetControlValue(ControlFor[key]), fallback);


        // Diagnostic. Count the controls at the two moments that matter: at the
        // end of construction, where AddControlEx was called, and again when
        // the editor opens. If the second count is lower, the controls were
        // added to an editor the framework later replaced. Must never throw.
        private void DiagControls(String moment)
        {
            try
            {
                var ed = (Object)this.ActionEditor;
                if (ed == null) { PluginLog.Info($"[tacit] EDITOR {moment}: ActionEditor is NULL"); return; }
                var parts = new System.Collections.Generic.List<String>();
                var flags = System.Reflection.BindingFlags.Instance
                          | System.Reflection.BindingFlags.Public
                          | System.Reflection.BindingFlags.NonPublic;
                foreach (var fi in ed.GetType().GetFields(flags))
                {
                    Object v = null;
                    try { v = fi.GetValue(ed); } catch { continue; }
                    if (v is System.Collections.ICollection col) { parts.Add($"{fi.Name}={col.Count}"); }
                }
                PluginLog.Info($"[tacit] EDITOR {moment}: action#{this.GetHashCode()} editor#{ed.GetHashCode()} | {String.Join(" ", parts)}");
            }
            catch (Exception ex) { PluginLog.Info($"[tacit] EDITOR {moment}: diag failed {ex.GetType().Name}"); }
        }

        private TacitPlugin Host => this.Plugin as TacitPlugin;

        // The dialog cannot be told what to open on. Options+ opens it on the
        // slot's saved parameters, or on the controls' defaults for a slot never
        // saved.
        // 1.0.2 called SetValue here for all four controls. SetValue only queues
        // a "setValue" message in the reply to this event, Options+ does not
        // apply it, and the dialog showed 20s and 15s while 90s and 10s ran.
        //
        // So read what it shows, and log it beside what is running.
        private void OnEditorStarted(Object sender, ActionEditorStartedEventArgs e)
        {
            this.DiagControls("on-started");
            PluginLog.Info($"[tacit] EDITOR on-started: Plugin={(this.Plugin == null ? "NULL" : this.Plugin.GetType().Name)}");
            var running = this.Host?.Options;
            if (running == null)
            {
                return;
            }

            var shown = Read(e.ActionEditorState, running);
            lock (this._edit)
            {
                this._shown = shown;
            }

            PluginLog.Info($"[tacit] EDITOR shows {shown} | running {running}");
        }

        // A change is held, not saved. Options+ reports each one as it happens,
        // before the user has pressed SAVE or CANCEL; 1.0.2 saved it here, so
        // CANCEL could not undo it.
        private void OnControlValueChanged(Object sender, ActionEditorControlValueChangedEventArgs e)
        {
            var host = this.Host;
            if (host == null)
            {
                return;
            }

            TacitOptions shown;
            lock (this._edit)
            {
                shown = this._shown = Read(e.ActionEditorState, this._shown ?? host.Options);
            }

            // The previews play what the dialog shows, saved or not.
            if (e.ControlName == CtlDemo)
            {
                host.PlayDemoRun(shown);
                return;
            }

            // Changing the contrast changes what the demo feels like, so it is
            // worth hearing the difference immediately rather than being told
            // about it.
            if (e.ControlName == CtlContrast)
            {
                host.PlayContrastSample(shown);
            }
        }

        // SAVE makes what the dialog shows the running settings. CANCEL, and
        // closing the dialog any other way, report IsCanceled and change nothing.
        //
        // A cancel must not discard what the dialog shows. Options+ also reports
        // a cancel when a dialog closes after SAVE, sometimes on another thread,
        // so the two can arrive in either order. The next Started
        // replaces the held values instead.
        private void OnEditorFinished(Object sender, ActionEditorFinishedEventArgs e)
        {
            PluginLog.Info($"[tacit] EDITOR finished: {(e.IsCanceled ? "cancelled" : "saved")}");
            if (e.IsCanceled)
            {
                return;
            }

            TacitOptions shown;
            lock (this._edit)
            {
                shown = this._shown;
            }

            if (shown != null)
            {
                this.Host?.SaveOptions(shown);
            }
        }

        // Pressing the physical key plays the run too. The key is not really
        // the point of this action -- opening its panel is -- but a bound key
        // that does nothing when pressed would be worse.
        protected override Boolean RunCommand(ActionEditorActionParameters actionParameters)
        {
            this.Host?.PlayDemoRun();
            return true;
        }

        protected override String GetCommandDisplayName(ActionEditorActionParameters actionParameters) =>
            $"Tacit{Environment.NewLine}feel a run";
    }
}
