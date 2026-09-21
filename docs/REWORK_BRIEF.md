# Rework brief (2026-09-21)

Fix, harden, and raise the quality of everything added in commits d2d86b3..bc01e36 (Acts 1-11, written with Sonnet), **without changing the story**.

## The one hard rule: the story does not change
- Every act, beat, trigger condition, order of events, checkpoint, caption text, subtitle text, voice line, objective and ending stays exactly as it is now. This includes the giant, the friend, the newel post, the bunker, the CRT room, the maze and the Act 11 ending, and every existing line of text (keep typos in dialogue as written unless it is plainly a code bug).
- You may change *how* a beat is built (code structure, visuals, models, lighting, sound quality, bug fixes, save/restore, performance) but not *what happens*.
- If a fix seems to require a story change, don't make it. Report it to the lead instead.

## Shared foundations (already written by the lead: use them, don't reinvent)
- **Story state:** `scripts/systems/StoryManager.cs`.
  - The checkpoint, named flags (`StoryManager.Flag.*`, `SetFlag`/`HasFlag`) and the inventory are all saved.
  - **Restore contract:** every stateful system (door boarded/open, fire, storm, mood, clearing stairs, bunker open, maze, pickups already taken, etc.) must, in its own `_Ready` (or deferred), read `StoryManager.Instance.Current` + flags and put itself in the right state. Continue must never softlock or show the world in the wrong state.
  - Add new flags to `StoryManager.Flag` when a beat needs finer state.
- **Cutscenes:** `scripts/systems/Cutscene.cs`.
  - Every scripted sequence uses `Cutscene.Run(owner, async ct => { ... }, lockInput, freezeBody)`, with `Cutscene.Wait/Frame/Tween(owner, ..., ct)` (pausable).
  - Temporary nodes go under `Cutscene.SceneRoot(owner)`, never `GetTree().Root`.
  - No bare `_ = SomeAsync()` and no `async void` story code outside `Cutscene.Run`.
- **Input:** `scripts/player/PlayerInput.cs`.
  - Gameplay code must NOT read `Input` directly.
  - Use `InteractHeld/InteractPressed`, `LightPressed`, `PhotoHeld/PhotoPressed`, `ItemNextPressed/ItemPrevPressed`. They are false while input is disabled or the tree is paused.
  - The test bot drives the `Scripted*` fields.
- **Interaction:** `scripts/systems/Interactable.cs` + `scripts/player/PlayerInteraction.cs`.
  - Anything the player uses with E is an `Interactable` child node: set `Prompt`, `PickRadius`/`PickOffset`, and `HoldSeconds` for hold-to-use (the door break), then subscribe to `Interacted` or subclass.
  - Only the object under the centre-screen crosshair, within `MaxDistance` and with nothing in between, is focused. It gets a dim bone rim highlight (`assets/shaders/interact_highlight.gdshader`) and the HUD prompt.
  - The pick sphere must be bigger than the object's own collider, since world geometry blocks the look ray.
  - Blocked states explain themselves via `GetPrompt` (e.g. "Hands full").
- **Conventions:** `docs/CONVENTIONS.md` still applies.
  - Exported NodePaths or groups, not deep `FindChild` from the root.
  - Every sound on an explicit bus (supernatural stuff on `Unnatural`, voice on `Voice`, player on `Player`, weather on `Weather`); only `ForestAmbienceManager` sets the nature bus volumes.
  - Tabs; namespaces mirror folders.

## Quality bar for models
- Match the baseline pieces (the stone staircase `StaircaseBuilder.cs`, the stalker `StalkerBody.cs`, the signs `ParkProp.cs`, the trees `ForestScatter.cs`) and the moodboard `docs/reference/moodboard.jpg`.
- Procedural, code-built geometry with low-res procedural textures (linear + mipmaps) and a PS2 look.
- Judge everything at 640x360 in first person (eye 1.62 m, FOV 70), in the actual game lighting, not studio light.

## Working rules for agents
- **Ownership:** only edit files you own (see your brief). Need a change elsewhere? Message the lead.
- **Git:** don't run git commands that change anything.
- **Testing:** test lightly.
  - `dotnet build` and quick targeted preview scenes/scripts, which may go in `scenes/levels/<area>_preview.tscn` + `scripts/<area>/<Area>Preview.cs`.
  - Screenshots go to `test-output/<your-area>/`.
  - The owner does not want repeated full `--autotest` runs; the lead runs the final check.
  - The build may briefly fail because of another agent's half-written file. If the errors aren't in your files, wait and retry.
- **Godot:** `C:\Users\Dan\tools\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe`.
