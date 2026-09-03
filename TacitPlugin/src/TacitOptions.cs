namespace Loupedeck.TacitPlugin
{
    using System;

    // The three questions a user is allowed to answer, and nothing else.
    //
    // What is deliberately absent is as much of the design as what is here:
    //
    //   No per-state intensity. Options+ already ships a global four-level
    //   haptic control; a second one means two knobs for one sensation. And
    //   amplitude is the ~70% axis, hardest to encode and worst near threshold
    //   — a control on the weakest dimension invites people to make the
    //   vocabulary less legible while believing they are tuning it.
    //
    //   No remapping of what a pattern means. The case for a shared vocabulary
    //   is that plugins by different authors teach the same language; if every
    //   user remaps it, divergence returns one level down. A semantic layer
    //   works because it constrains.

    internal sealed class TacitOptions
    {
        public const String KeyQuietWindow = "quietWindowSeconds";
        public const String KeyHeartbeat = "heartbeatSeconds";
        public const String KeyHeartbeatOn = "heartbeatEnabled";
        public const String KeyHighContrast = "highContrast";

        /// How long a task must run before it is worth telling anyone about.
        /// The most personal setting there is: someone whose tasks take thirty
        /// seconds wants ten, someone who only cares about ten-minute builds
        /// wants two minutes. Nobody else can guess this.
        public Int32 QuietWindowSeconds { get; set; } = 20;

        /// How often the beat repeats while work continues. The line between
        /// reassurance and nagging sits somewhere different for each person,
        /// and the beat has to stay regular enough to be projected.
        public Int32 HeartbeatSeconds { get; set; } = 15;

        /// Some people want only the ends. That is a legitimate way to use
        /// this, and refusing it would be paternalism rather than restraint.
        public Boolean HeartbeatEnabled { get; set; } = true;

        /// High contrast is a rendering variant, never a remapping.
        ///
        /// It does NOT make a signal easier to FEEL. That is amplitude, and
        /// amplitude is not ours: Options+ ships a global four-level control and
        /// a hand that needs it sets it to Medium or High. More pulses do
        /// nothing for a stimulus below that hand's threshold -- it is below it
        /// every time it repeats.
        ///
        /// What this does is make a figure easier to READ once felt. More
        /// pulses and wider gaps mean the figure occupies more time, its pulses
        /// are better separated, and there is more of it to catch and to count.
        ///
        /// It is legibility, not sensitivity, and it costs energy: every extra
        /// actuation is battery on a device whose haptics disable themselves at
        /// 10%. That is why it is a setting rather than a default, and why
        /// wider gaps are preferred to more pulses wherever both would serve.
        ///
        /// What it never changes is what anything MEANS. You can swap the
        /// theme; you cannot redefine what danger means.
        public Boolean HighContrast { get; set; }

        public TimeSpan QuietWindow => TimeSpan.FromSeconds(Math.Max(1, this.QuietWindowSeconds));

        public TimeSpan Heartbeat => TimeSpan.FromSeconds(Math.Max(3, this.HeartbeatSeconds));

        /// Failure is a figure, not a texture. High contrast lengthens the
        /// figure rather than making it louder.
        public Int32 FailurePulses => this.HighContrast ? 4 : 3;

        /// The opening and the ending are single pulses by default, because a
        /// vocabulary that spends pulses on its cheapest distinctions has none
        /// left for its expensive one. Under high contrast they become two, so
        /// that each occupies enough time to be caught by a hand that is
        /// elsewhere. The pair that carries the perceptual budget stays two
        /// apart either way: one against three becomes two against four.
        public Int32 StartedPulses => this.HighContrast ? 2 : 1;

        public Int32 CompletedPulses => this.HighContrast ? 2 : 1;

        /// `progress` is deliberately absent from the list above, and the
        /// absence is a design statement rather than an omission.
        ///
        /// The beat's identity is its regularity -- it is not a message, it is
        /// a rhythm whose interruption carries the meaning. Adding pulses to it
        /// or shortening its interval under high contrast would alter the very
        /// thing a user learns to project, which is the one property the design
        /// cannot afford to make configurable.

        /// 450 ms, not the 220 ms this started at, and the reason is mechanical
        /// rather than perceptual.
        ///
        /// A resonant actuator needs time to reach full excursion and to settle.
        /// At 220 ms the second and third pulses never got there, so the figure
        /// rendered as one weak continuous buzz instead of three taps -- which
        /// is exactly how it was described on the device: "much more subtle...
        /// it is continuous, it is longer, but much weaker".
        ///
        /// Widening the gap makes each pulse STRONGER, which is the opposite of
        /// what a purely perceptual reading would predict. It also fixes the
        /// count encoding, because three blurred pulses do not read as three.
        ///
        /// Van Erp's 10 ms is a perceptual floor; the actuator's mechanical
        /// floor sits an order of magnitude higher, and it was found on the
        /// device rather than in a spec.
        /// One constant, serving every figure of more than one pulse, because
        /// the constraint is the actuator's rather than failure's.
        public TimeSpan FigureGap => TimeSpan.FromMilliseconds(this.HighContrast ? 600 : 450);

        /// needsInput escalates by contracting its interval. High contrast
        /// starts it tighter and lets it floor lower, which is the same
        /// legibility argument applied to the one pattern that already carries
        /// its urgency in an interval rather than in a count.
        public TimeSpan NeedsInputFirst => TimeSpan.FromSeconds(this.HighContrast ? 10 : 12);

        public TimeSpan NeedsInputFloor => TimeSpan.FromSeconds(this.HighContrast ? 3 : 4);

        public static TacitOptions Load(Func<String, String> read)
        {
            var o = new TacitOptions();
            o.QuietWindowSeconds = ReadInt(read, KeyQuietWindow, o.QuietWindowSeconds);
            o.HeartbeatSeconds = ReadInt(read, KeyHeartbeat, o.HeartbeatSeconds);
            o.HeartbeatEnabled = ReadBool(read, KeyHeartbeatOn, o.HeartbeatEnabled);
            o.HighContrast = ReadBool(read, KeyHighContrast, o.HighContrast);
            return o;
        }

        private static Int32 ReadInt(Func<String, String> read, String key, Int32 fallback) =>
            Int32.TryParse(read(key), out var v) ? v : fallback;

        private static Boolean ReadBool(Func<String, String> read, String key, Boolean fallback)
        {
            var raw = read(key);
            if (String.IsNullOrEmpty(raw))
            {
                return fallback;
            }

            // The editor writes checkbox values as "True"/"False"; be liberal.
            return raw.Equals("true", StringComparison.OrdinalIgnoreCase)
                || raw.Equals("1", StringComparison.Ordinal);
        }

        public override String ToString() =>
            $"quiet={this.QuietWindowSeconds}s heartbeat={(this.HeartbeatEnabled ? this.HeartbeatSeconds + "s" : "off")} contrast={(this.HighContrast ? "high" : "default")}";
    }
}
