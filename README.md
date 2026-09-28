# MyDockFinder Doctor

**English** · [Русский](README.ru.md)

A small watchdog for [MyDockFinder](https://store.steampowered.com/app/1787090/MyDockFinder/),
the macOS-style dock for Windows. It restarts `Dock_64.exe` when the dock leaks
video memory, hangs or crashes. It waits until you step away from the keyboard,
so you do not see the restart happen.

## Why

MyDockFinder's dedicated GPU memory keeps growing the longer you use it. A fresh
dock holds about 205 MB. Once it is past 500 MB it is worth restarting. The dock
also crashes now and then with heap corruption (`0xC0000374`).

MyDockFinder has no way to recover from either problem:

- The `MyDock` service that ships with it does not relaunch a dead dock.
- The dock ignores `WM_CLOSE`, so a script cannot restart it cleanly.

## What it does

Every 30 seconds it samples `Dock_64.exe`:

- dedicated GPU memory (the same number as the *Dedicated GPU memory* column in
  Task Manager, read from the `GPU Process Memory` performance counters);
- private bytes;
- handles;
- GDI objects;
- USER objects.

| Metric             | Normal (fresh dock) | Soft limit | Hard limit |
|--------------------|--------------------:|-----------:|-----------:|
| Dedicated VRAM     |             ~205 MB |     500 MB |    1000 MB |
| Private bytes      |             ~120 MB |    1024 MB |    2048 MB |
| Handles            |               ~1700 |       8000 |      15000 |
| GDI objects        |                 ~90 |       3000 |       8000 |
| USER objects       |                 ~90 |       3000 |       8000 |

How it reacts:

- **Soft limit:** restarts after 2 minutes with no mouse or keyboard input.
- **Hard limit:** restarts after 10 seconds of idle.
- **Young dock:** never touches a dock younger than 5 minutes.
- **Fullscreen:** waits while an exclusive-fullscreen D3D app or presentation
  mode is active.
- **Hang:** if the dock window does not respond for 90 seconds, it restarts the
  dock.
- **Crash:** if the dock exits with a code of `0xC0000000` or higher, it
  relaunches the dock, at most 3 times in 30 minutes.
- **Normal quit:** if you quit from the dock's own menu (exit code 0), it leaves
  the dock alone.

A restart goes like this:

1. Back up `ico.ini`, `config.ini` and `padconfig.ini`, which hold the pinned
   icons and settings.
2. Terminate the process. The dock ignores `WM_CLOSE`, and this is no worse than
   one of its own crashes.
3. If any of those files was cut short mid-write, restore it from the backup.
4. Start `Dock_64.exe` directly. Steam does not need to be running. The `MyDock`
   service injects its `Dockmod` hooks again about 2 seconds later.
5. Wait for the dock window to appear. If it does not, retry up to 3 times.

## Install

**Binary.** Download `DockDoctor.exe` from the [latest release](../../releases/latest)
and put it in its own folder. It keeps its `.ini`, log and backups next to
itself, so do not use Program Files. Run it. To start it at logon, put a shortcut
to it into `shell:startup`.

**From source.** Run:

```powershell
powershell -ExecutionPolicy Bypass -File install.ps1
```

The script:

1. builds the exe with the .NET Framework compiler that ships with Windows (no
   SDK needed);
2. installs it to `%USERPROFILE%\MyDockFinderDoctor`;
3. adds a Startup shortcut;
4. starts it.

Running it again updates the install in place and keeps your `.ini`.
`uninstall.ps1` removes everything.

## Usage

The daemon has no window. Two commands talk to it:

```
DockDoctor.exe --restart   restart the dock right now
DockDoctor.exe --stop      stop the daemon
```

Tip: pin a shortcut to `DockDoctor.exe --restart` on the dock itself.

Settings live in `DockDoctor.ini` next to the exe. The file is created on first
run, and the daemon picks up changes without a restart. Every 10 minutes it
writes the dock's current numbers to `DockDoctor.log`, which helps you tune the
limits to your own leak rate. The log messages and the `.ini` comments are in
Russian.

## Findings about MyDockFinder

These notes were checked on version 1.16.1 and may help anyone scripting around
the dock:

- `Dock_64.exe` runs as a normal user process: not elevated and without
  UIAccess. It works without Steam running.
- It ignores `WM_CLOSE` on both its `MYDOCKFINDER` and `MyDockAPP` windows. The
  only way to stop it from outside is `TerminateProcess`.
- The `MyDock` service (LocalSystem) does not relaunch the dock. All it does is
  start `Dockmod.exe` / `Dockmod64.exe` shortly after a dock appears and remove
  them when the dock dies.
- The visible dock window has the class `MyDockIconsCwnd`. With auto-hide on it
  is a 1 px strip at the bottom of the screen.

## Requirements

- Windows 10/11 x64 with .NET Framework 4.5 or newer (built in).
- 64-bit MyDockFinder (`Dock_64.exe`).

Tested with MyDockFinder 1.16.1 on Windows 11 (build 26200).

## License

[MIT](LICENSE)
