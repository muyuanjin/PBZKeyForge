[center][size=6][b]PBZKeyForge[/b][/size]
[size=3]One key, several actions — the binding editor the game will not give you.[/size][/center]

[line]

[size=4][b]What this is[/b][/size]

Phantom Blade Zero keeps your bindings in a plain settings file:

[code]steamapps\common\PBZ\ProjectPBZ\Saved\EnhancedInputUserSettings.sav[/code]

The in-game rebinding screen validates when you save, and it refuses any binding where two actions share a key. The engine itself has no such rule — the file is a list of per-action overrides, and two actions on one key simply fire together.

PBZKeyForge edits those overrides directly. That is the whole idea: the same edit the menu rejects, written where the game reads it.

[line]

[size=4][b]Features[/b][/size]

[list]
[*][b]One key, several actions.[/b] Guard on your attack key, dodge on the old guard key, sprint on Space — whatever you want.
[*][b]Scheme aware.[/b] Choose which scheme you are editing (Default / Custom 1 / Custom 2) and make it the active one from the tool.
[*][b]Seven presets[/b], from one-key attack and guard to a Souls-style gamepad layout.
[*][b]Five languages[/b] — English, Simplified Chinese, Japanese, Korean, Russian. Detected from Windows, switchable in the toolbar.
[*][b]Nothing else is touched.[/b] Empty fields keep the game default; untouched records are written back byte-for-byte.
[*][b]Safe writes.[/b] The game must be closed, a timestamped backup is created, and the file is re-read and verified after saving.
[/list]

[line]

[size=4][b]Presets[/b][/size]

[list]
[*][b]One-key attack & guard[/b] — left mouse / X also guards.
[*][b]One-key + guard key becomes dodge[/b] — the freed key dodges and sprints.
[*][b]Souls-style gamepad[/b] — light RB, heavy RT, guard LB, dodge and sprint B, interact and execute Y.
[*][b]Space = sprint[/b] and [b]Space = dodge[/b] — extra keyboard keys, Shift stays.
[*][b]Undo[/b] and [b]Vanilla[/b] — put things back.
[/list]

[line]

[size=4][b]Install[/b][/size]

[list=1]
[*]Close Phantom Blade Zero completely.
[*]Back up [code]ProjectPBZ\Saved\EnhancedInputUserSettings.sav[/code].
[*]Pick one:
[b]GUI[/b] — run [code]PBZKeyForge.exe[/code], press [i]Auto-detect[/i], choose a scheme, apply a preset or type keys, then [i]Save to file[/i].
[b]Save files only[/b] — copy one file from [code]presets\[/code] over your [code]EnhancedInputUserSettings.sav[/code], then select that scheme in game.
[*]Play. Do not open the in-game keybind screen afterwards (see below).
[/list]

[line]

[size=4][b]Important[/b][/size]

[color=#d9534f][b]Do not open the in-game key bindings screen and press apply or reset after using this.[/b][/color]
The game validates on its own save and rewrites the whole file, which discards these bindings. If it happens, simply apply the preset again.

[color=#d9534f][b]One save-file mod at a time.[/b][/color]
Any other mod that replaces [code]EnhancedInputUserSettings.sav[/code] overwrites these layouts.

[line]

[size=4][b]Compatibility[/b][/size]

[list]
[*]Steam version, launch build [b]CL-301524[/b].
[*]No UE4SS. No pak files. No DLL. Nothing is injected into the game.
[*]Windows 10/11. The executable needs no runtime download.
[*]If a game update changes the file layout, the tool refuses to write instead of corrupting it.
[/list]

[line]

[size=4][b]Uninstall[/b][/size]

Restore the backup you made, or load the [b]Vanilla[/b] preset in the tool and save. Nothing else is modified.

[line]

[size=4][b]Source[/b][/size]

MIT licensed, source and issue tracker on GitHub: [url=https://github.com/muyuanjin/PBZKeyForge]muyuanjin/PBZKeyForge[/url]

Not affiliated with S-Game.
