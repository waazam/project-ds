using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace ProjectDS.Systems;

/// <summary>
/// The game's one way to die. A beat that can kill (Act 12's lake, Act 13's flooding room) plays its
/// own death on screen, then calls <see cref="Reload"/>: the screen goes to black, a single line is
/// shown, and the game Continues from the latest checkpoint save — exactly what the menu's Continue
/// does, so every system restores itself the usual way and nothing from the failed attempt survives.
///
/// <see cref="ProjectDS.UI.UnderwaterView"/> is the shared "under the surface" screen layer both drownings use.
/// </summary>
public static class PlayerDeath
{
	/// <summary>True from the moment a death starts until the reload is on its way.</summary>
	public static bool Dying { get; private set; }
	/// <summary>How many deaths this run (tests read it).</summary>
	public static int Deaths { get; private set; }

	/// <summary>Marks a death as begun (so other beats can stand down); call before the death animation.</summary>
	public static void Begin() { Dying = true; Deaths++; StoryManager.Instance?.NoteDeath(); }

	/// <summary>Black, the line, and back to the last checkpoint.</summary>
	public static async Task Reload(Node owner, string line, CancellationToken ct)
	{
		Dying = true;
		ProjectDS.UI.StyleFx.DeathDrips();
		var fader = StoryBeat.Fader(owner);
		if (fader != null)
		{
			// (2026-10-31, the owner: "the background should remain black") the drips paint their own black now, the blood
			// running down it and pooling: the fader stands aside for them (a death that went to black first included), the
			// line comes up over them, and the fader's black is only the cut to the reload
			if (ProjectDS.UI.StyleFx.Instance != null) fader.SetBlack(false);
			else if (!fader.IsBlack) await fader.Fade(1f, 1.2f, ct);   // (no drips here: plain black)
			await Cutscene.Wait(owner, 2.0, ct);
			if (!string.IsNullOrEmpty(line)) await fader.ShowCaption(line, "", 1.2f, 2.2f, 1.2f, ct);
			await Cutscene.Wait(owner, 0.6, ct);
			fader.SetBlack(true);
		}
		GD.Print($"[story] death: '{line}' - back to the last checkpoint");
		ProjectDS.UI.StyleFx.Clear();
		Dying = false;
		if (StoryManager.Instance?.ContinueGame() != true) StoryManager.Instance?.ReturnToMenu();
	}
}
