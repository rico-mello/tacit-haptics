# Tacit

**Feel the state of a long-running task — through your thumb.**

A build, an export, an agent run: you press start, and then you either stare
at a progress bar or switch away and forget. Tacit reports the state of that
task through the haptic panel of a Logitech MX Master 4 while you keep working
in whatever application is in the foreground. No window, no notification, no
sound — a small tactile language, taught by a ~29-second demo run.

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
automatically. If another copy of Tacit is already loaded from a different
folder, the reload a build sends is refused as "already loaded" and the old
copy keeps running. Requirements on the machine: Options+ at its default path
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
resume real reporting. Three more are benches for checking the figures by
hand, and each logs what it plays to
`%LOCALAPPDATA%\Logi\LogiPluginService\Logs\plugin_logs\Tacit.log`:

- `flip` plays one ending, completed or failed, chosen at random by the
  plugin, and logs which one it was as it plays. Nobody knows in advance:
  not the hand, and not whoever typed the word.
- `lab` plays a completion pulse as a reference, then four versions of the
  failure figure five seconds apart: 3 pulses at 220 ms (the superseded
  gap), the default 3 at 450 ms, 3 at 700 ms, and the 4 at 600 ms that
  **Push them apart** plays.
- `insist` plays the needsInput escalation and, unlike `escalate`, does not
  stop on its own: it contracts to its floor and holds there until
  `answered` is written.

## Settings

Options+ → the **Actions Ring** icon in the top bar → **CUSTOMIZE RING** →
**ALL ACTIONS** → **INSTALLED PLUGINS** → **Tacit** → drag *Tacit settings*
into a ring slot, then click the slot. ALL ACTIONS opens on whichever plugin
was installed last, which is why the INSTALLED PLUGINS step is there.

The panel holds four controls and a button:

- **Stay quiet for**: how long a task can run before it is worth announcing
  (20 s by default). A task that finishes inside it is never announced.
- **Then remind you every**: how often the heartbeat repeats (15 s by default).
- **Keep the beat**: turn it off to feel only the start and the end.
- **Push them apart**: longer figures and wider gaps for hands that want more
  separation. It changes how distinct the patterns feel, never what they
  mean; for loudness, use the Options+ intensity setting.
- **Play a run**: plays the ~29-second teaching run, the same as writing
  `demo`.

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
