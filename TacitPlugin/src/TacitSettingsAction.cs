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

    public class TacitSettingsAction : ActionEditorCommand
    {
        private const String CtlQuiet = "quietWindow";
        private const String CtlHeartbeat = "heartbeat";
        private const String CtlHeartbeatOn = "heartbeatOn";
        private const String CtlContrast = "highContrast";
        private const String CtlDemo = "playRun";

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
            this.ActionEditor.AddControlEx(new ActionEditorSlider(
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
            this.ActionEditor.AddControlEx(new ActionEditorSlider(
                    CtlHeartbeat,
                    "Then remind you every",
                    "The beat that means it is still alive. It has to be regular - a beat you can predict is what makes a missing beat mean something.")
                .SetValues(5, 60, 15, 5)
                .SetFormatString("{0}s"));

            this.ActionEditor.AddControlEx(new ActionEditorCheckbox(
                    CtlHeartbeatOn,
                    "Keep the beat",
                    "Turn this off to feel only the start and the end.")
                .SetDefaultValue(true));

            this.ActionEditor.AddControlEx(new ActionEditorSeparator("sep1"));

            // Question 3. The accessibility carve-out.
            this.ActionEditor.AddControlEx(new ActionEditorCheckbox(
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
            this.DiagControls("after-ctor");
        }


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

        // Load the live values in, so the panel shows what is actually running
        // rather than the defaults.
        private void OnEditorStarted(Object sender, ActionEditorStartedEventArgs e)
        {
            this.DiagControls("on-started");
            PluginLog.Info($"[tacit] EDITOR on-started: Plugin={(this.Plugin == null ? "NULL" : this.Plugin.GetType().Name)}");
            var o = this.Host?.Options;
            if (o == null)
            {
                return;
            }

            e.ActionEditorState.SetValue(CtlQuiet, o.QuietWindowSeconds.ToString());
            e.ActionEditorState.SetValue(CtlHeartbeat, o.HeartbeatSeconds.ToString());
            e.ActionEditorState.SetValue(CtlHeartbeatOn, o.HeartbeatEnabled.ToString());
            e.ActionEditorState.SetValue(CtlContrast, o.HighContrast.ToString());
        }

        private void OnControlValueChanged(Object sender, ActionEditorControlValueChangedEventArgs e)
        {
            var host = this.Host;
            if (host == null)
            {
                return;
            }

            if (e.ControlName == CtlDemo)
            {
                host.PlayDemoRun();
                return;
            }

            var value = e.ActionEditorState.GetControlValue(e.ControlName);

            switch (e.ControlName)
            {
                case CtlQuiet: host.SaveOption(TacitOptions.KeyQuietWindow, value); break;
                case CtlHeartbeat: host.SaveOption(TacitOptions.KeyHeartbeat, value); break;
                case CtlHeartbeatOn: host.SaveOption(TacitOptions.KeyHeartbeatOn, value); break;
                case CtlContrast: host.SaveOption(TacitOptions.KeyHighContrast, value); break;
            }

            // Changing the contrast changes what the demo feels like, so it is
            // worth hearing the difference immediately rather than being told
            // about it.
            if (e.ControlName == CtlContrast)
            {
                host.PlayContrastSample();
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
