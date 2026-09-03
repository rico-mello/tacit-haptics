# Tacit

**Feel the state of a long-running task — through your thumb.**

A build, an export, an agent run: you press start, and then you either stare
at a progress bar or switch away and forget. Tacit reports the state of that
task through the haptic panel of a Logitech MX Master 4 while you keep working
in whatever application is in the foreground. No window, no notification, no
sound — a small tactile language your thumb learns in about thirty seconds.

It links to no application by design. Any tool that can append a line to a
text file can report — a Claude Code hook, a build script, a CI job, an
upload, a render.

## The vocabulary

Five states, encoded on rhythm and pulse count — never on intensity:

| State | What your thumb feels |
|---|---|
| `started` | One pulse — the task is real, you can look away |
| `progress` | A quiet heartbeat while work continues (every 15 s by default) |
| `needsInput` | A pulse **off the beat**, then a gently contracting rhythm until you answer |
| `completed` | One clear pulse — done |
| `failed` | A distinct three-pulse figure — unmistakably not "done" |

A task that finishes inside the quiet window (20 s by default) is never
announced at all — most fast tasks shouldn't have spoken in the first place.

## Requirements

- Windows, with [Logi Options+](https://www.logitech.com/software/logi-options-plus.html) installed
- A Logitech **MX Master 4** (the haptic panel is the display)
- To build from source: the .NET 10 SDK

## Install

**From the Logi Marketplace** — search for *Tacit*. *(Pending review; until
it is listed, build from source below.)*

**From source:**

```
dotnet build TacitPlugin/src/TacitPlugin.csproj
```

The build deploys the plugin to the Logi Plugin Service and reloads it
automatically. Requirements on the machine: Options+ at its default path
(the build references its `PluginApi.dll`), and the folder
`%LOCALAPPDATA%\Logi\LogiPluginService\Plugins\` existing (create it once if
this is your first side-loaded plugin).

## Reporting a task

Write one line to the channel file:

```
%LOCALAPPDATA%\Logi\LogiPluginService\tacit-channel.log
```

Format: `<taskId> <state> [label]`, where state is one of
`started progress needsInput completed failed`. Example from a shell:

```
echo build-42 started nightly build >> %LOCALAPPDATA%\Logi\LogiPluginService\tacit-channel.log
```

Report `progress` as often as you like — the plugin owns the rhythm and turns
your reports into a steady beat; you cannot spam someone's hand.

Single words drive the built-in demo and test harness: `demo` plays a
~29-second teaching run (two task runs — one succeeds, one fails);
`escalate` plays the needsInput escalation alone; `mute` / `unmute` pause and
resume real reporting; `flip`, `lab`, `insist` and `answered` are the
discrimination and calibration benches, described in the settings panel.

## Settings

Options+ → **Actions Ring** → CUSTOMIZE RING → ALL ACTIONS → **Tacit** → drag
*Tacit settings* into a ring slot, then open the slot's configuration. Four
controls: how long to stay quiet, how often the heartbeat reminds you,
whether to keep the beat at all, and **high contrast** — longer figures and
wider gaps for hands that want more separation. It changes how distinct the
patterns feel, never what they mean; for loudness, use the Options+ intensity
setting.

## Design notes

- **Rhythm, not amplitude.** Users control intensity globally in Options+;
  meaning must survive every setting, so it lives in timing and count.
- **Silence can end a run; it can never report one.** A silent working state
  and a dead plugin feel identical — that is why `progress` has a beat.
- **The escalation contracts to a plateau and never gives up.** Unbounded
  escalation is the seatbelt chime; a signal that falls silent leaves the
  task blocked with nobody knowing.
- **The gap between pulses is 450 ms, and the reason is mechanical.** A
  resonant actuator needs time to reach full excursion — pulses fired faster
  blur into one weak buzz. Found on the device, kept as a constant.

## License

MIT — see [LICENSE](LICENSE).
