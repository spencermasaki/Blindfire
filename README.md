# Blindfire 🎯

ok so this is basically a lil app i made to figure out what mouse sensitivity you should actually be using in Apex Legends instead of just guessing lol. it watches your RAW mouse movement (not your cursor, the actual hardware counts) while you click some targets blind, then does some math and spits out a sensitivity + ADS multiplier recommendation. it also tracks how straight your aim is and draws out little trace replays of every trial on the results screen bc that's kinda cool ngl

no cap this thing is windows only (it's a WPF app) and you need a mouse lol

## what it actually does

- **hipfire trials**: click a target, then move blind to a second target and click again (cursor's hidden so you can't cheat)
- **ADS trials**: same thing but the targets are way closer together since ADS gaps are tighter irl, and you gotta hold right click the whole time
- **tracking trials**: click and hold a moving target and just vibe with it for a few seconds
- all 30 of these get mixed together in one go, then you get a results screen with your recommended sensitivity, ADS multiplier, and a whole background of little tiles showing your mouse traces from every single trial (hover over one to zoom in, it's actually pretty fire)

## stuff you need before building this

1. **Windows** (sorry mac/linux gang, raw input + WPF means this is windows-only)
2. **.NET 8 SDK** — grab it here if you don't have it: https://dotnet.microsoft.com/download/dotnet/8.0
   - check it worked with `dotnet --version`, should say something like `8.0.x`

that's literally it, no other dependencies, it's all built into .NET

## just want the exe, don't care about the code

cool, grab it from the [releases page](https://github.com/spencermasaki/Blindfire/releases/latest) — download `Blindfire.exe` and run it, no .NET install needed. windows might throw a "we don't recognize this app" smartscreen warning since it's unsigned, just click "more info" then "run anyway"

## how to actually run it (from source)

clone the repo:

```powershell
git clone https://github.com/spencermasaki/Blindfire.git
cd Blindfire
```

then just run it straight from source, no build step needed:

```powershell
dotnet run --project src/Blindfire/Blindfire.csproj
```

a window should pop up. if it doesn't, something's wrong and you should yell at me (or check that `dotnet --version` actually works)

## how to build it (if you just want the exe)

```powershell
dotnet build src/Blindfire/Blindfire.csproj
```

the exe ends up somewhere like `src/Blindfire/bin/Debug/net8.0-windows/Blindfire.exe`

## how to make a standalone exe to send to your friends

there's a script that already does this for you, it bundles the whole .NET runtime into ONE exe so your friends don't need to install anything:

```powershell
./publish.ps1
```

gives you a single ~70MB exe at `src/Blindfire/bin/Release/net8.0-windows/win-x64/publish/Blindfire.exe` that just works on any 64-bit windows machine, no .NET install required. heads up tho, windows might show a "we don't recognize this app" smartscreen warning since it's not signed — that's normal, just click "more info" then "run anyway"

## running the tests (if ur into that)

```powershell
dotnet test tests/Blindfire.Tests/Blindfire.Tests.csproj
```

there's 33 tests and they should all pass, they're all just testing the math stuff (sensitivity calculations, target placement, etc) not the actual UI

## quick project tour (in case you wanna poke around the code)

- `src/Blindfire/MainWindow.xaml(.cs)` — the actual app, all the UI logic lives here
- `src/Blindfire/Trials/` — the click-target trial logic (hipfire + ADS)
- `src/Blindfire/Tracking/` — the moving-target tracking trial logic
- `src/Blindfire/Calibration/` — the actual sensitivity math
- `src/Blindfire/Results/` — the trace tile visualization stuff on the results screen
- `src/Blindfire/Input/` + `Native/` — the raw mouse input plumbing (this is how it reads your ACTUAL mouse movement instead of the cursor, which matters bc cursor position gets clamped at screen edges and messed with by windows pointer acceleration)
- `tests/Blindfire.Tests/` — unit tests for all the math-y stuff

anyway have fun, go find your perfect sensitivity 🖱️ ur mom.
