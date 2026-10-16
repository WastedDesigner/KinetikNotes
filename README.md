# Kinetik Notes: Markdown sticky notes for the Windows desktop

[![Latest release](https://img.shields.io/github/v/release/WastedDesigner/KinetikNotes?label=download)](../../releases/latest)
[![License: GPL v3](https://img.shields.io/github/license/WastedDesigner/KinetikNotes)](LICENSE)
![Windows 10 and 11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4)

**Kinetik Notes** is a free, open-source **sticky notes widget for Windows 11 and Windows 10** that shows a folder of Markdown files on your desktop, one note per file. Point it at an Obsidian folder (a to-do list, a project folder) and the notes follow it live: edit a file anywhere and the note updates; tick a task on the note and the file changes.

It's a small portable app with no installer and no account. It's a companion to [Kinetik](https://github.com/WastedDesigner/Kinetik), the taskbar system monitor, and [Kinetik Calendar](https://github.com/WastedDesigner/KinetikCalendar), and shares their look.

![Kinetik Notes in the Pastel paper theme: six notes in three columns with tasks, progress bars, a due date and tags](screenshots/notes.png)

![The same notes in the Navy theme](screenshots/notes-navy.png)

> **About this project:** I made this for my own desktop and decided to share it in case anyone else finds it useful. If you run into a bug or have an idea for a feature, please [open an issue](../../issues).

## Features

- **One note per Markdown file** in a folder you choose (optionally including subfolders), followed live as files change
- **Tick off tasks** (`- [ ] …`) right on the note; the change is written back to the file
- **Task count, progress bar, due dates and tags** on each note
- **Per-note options in front matter**, so Obsidian sees them too: `color: pink`, `title: …`, `pinned: true`, `due: 2026-10-20`, `tags: [web, marking]`
- **Sorting** by file name, title, last changed, due date or most open tasks, with pinned notes first
- **Filtering:** only notes with a tag, hide finished tasks, hide notes whose tasks are all done, hide or collapse individual notes
- **New notes** from the tray or right-click menu, optionally from a templates folder
- **Double-click** a note to open it in Obsidian, your default editor or File Explorer
- **Themes:** pastel and vivid paper, dark themes, or Auto to match Windows; six note colours; custom font, width, columns, rounding and opacity
- **Show / hide all notes with Ctrl+Alt+N**, lock position, snap to screen edges, run at startup
- **Settings backup** next to the exe, offered again on a fresh Windows

## Download and use

1. Download `KinetikNotes.zip` from [Releases](../../releases/latest) and extract it anywhere.
2. Run `KinetikNotes.exe`. It opens Settings so you can choose your notes folder.
3. Right-click a note or the tray icon for the menu.

Settings are kept in `HKCU\Software\KinetikNotes`. Nothing is sent anywhere: the app only reads and writes the Markdown files in the folder you pick.

## Requirements

Windows 10 or 11 (64-bit) with .NET Framework 4.8, which ships with both.

## Building from source

No SDK needed: `build.bat` compiles `KinetikNotes.cs` with the .NET Framework compiler that ships with Windows. `build.bat package` also makes `KinetikNotes.zip` for a release.

`KinetikNotes.exe --demo` runs a separate copy with made-up notes in a temporary folder that reads and saves no settings, which is how the screenshots were made. Add `--theme Navy` (or any theme name) to pick its theme.

## License

[GPL-3.0-or-later](LICENSE). Copyright © 2026 Akila Sella Hennedige.

## Local maintenance build 1.6.3

Includes the approved 16 October 2026 diagnostics fixes. Cross-compilation and regression results are recorded in the Apps diagnostics report; Windows GUI and hardware validation remains pending. Run `python tests/run-tests.py` for non-GUI regressions, and `build.bat package` to compile and create a hash-verified release package. Shared support is compiled into the EXE; no additional runtime DLL is required.

Note edits retain the previous bytes in a `.kinetik-recovery` folder beside the note. Concurrent changes are refused when detected before replacement; a change in the final replacement gap is preserved there and reported as a conflict. Refresh or reselect a changed task before retrying.
