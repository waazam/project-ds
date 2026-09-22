using System.Threading.Tasks;
using Godot;
using ProjectDS.Systems;
using ProjectDS.World;

namespace ProjectDS.UI;

/// <summary>
/// Dev harness (scenes/ui/note_preview.tscn): opens a handful of real notes in the NoteOverlay over a
/// dim scene and screenshots each at the game's 640x360 to test-output/notes/, then quits.
/// Checks that every card sits fully on screen.
/// </summary>
public partial class NotePreview : Node3D
{
	public override void _Ready() => _ = Run();

	private async Task Run()
	{
		DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://test-output/notes"));
		var hud = GD.Load<PackedScene>("res://scenes/ui/hud.tscn").Instantiate();
		AddChild(hud);
		Callable.From(() => { if (hud.GetNodeOrNull<ScreenFader>("ScreenFader") is { } f) f.BlackAlpha = 0f; }).CallDeferred();
		AddChild(new Camera3D { Current = true });
		AddChild(new WorldEnvironment { Environment = new Environment { BackgroundMode = Environment.BGMode.Color, BackgroundColor = new Color(0.12f, 0.13f, 0.12f) } });
		await Frames(10);
		int failed = 0;
		var notes = new (string name, string text, Readable.NoteStyle style, string title)[]
		{
			("camp_note_rh", Camp.NoteText, Readable.NoteStyle.Handwritten, ""),
			("camp_note", Camp.NoteText, Readable.NoteStyle.Handwritten, ""),
			("page", "It came away in my hand at the top like it wanted to. The cap off the post.\nThe hand hasn't been mine since. I kept the rest of me.\nThe door won't open from in here. I didn't do that.\nDon't take it back up. Don't take it anywhere.", Readable.NoteStyle.Handwritten, ""),
			("register", "DATE   PARTY   NAME   DESTINATION   RETURN\n\n10/2   2   Hendry   Blackfern + overlook   4:30\n10/5   4   Ostrowski family   overlook   3 pm\n10/11  1   D. Paulk   Blackfern Trail\n10/12  2   M. & T.   overlook   by lunch\n10/18  3   Farris   Blackfern, back to lot   5\n10/24  1  C.  Blackfern, the old steps  back by dark\n10/25  1  C.  the steps", Readable.NoteStyle.Typed, "Trail register"),
			("station_log", ProjectDS.World.BunkerParts.CrtRoom.LogText, Readable.NoteStyle.Typed, ""),
			("long_scroll", string.Join("\n", System.Linq.Enumerable.Repeat("A long line of handwriting that goes on and on to test that long notes scroll.", 16)), Readable.NoteStyle.Handwritten, ""),
			("card", "BIRDS OF BLACKFERN TRAIL\n\nNorthern Cardinal\nIndigo Bunting\nPurple Finch", Readable.NoteStyle.Printed, ""),
		};
		foreach (var n in notes)
		{
			var r = new Readable { Text = n.text, Style = n.style, Title = n.title };
			AddChild(r);
			await Frames(2);
			if (hud.GetNodeOrNull<ScreenFader>("ScreenFader") is { } f2) f2.BlackAlpha = 0f;
			NoteOverlay.Instance.Open(r, null);
			await Frames(20);
			var card = FindCard(NoteOverlay.Instance);
			var vis = GetViewport().GetVisibleRect();
			var rect = card?.GetGlobalRect() ?? new Rect2();
			bool on = card != null && vis.Grow(1f).Encloses(rect);
			if (!on) failed++;
			GD.Print($"[notes] {(on ? "PASS" : "FAIL")} {n.name}: card {rect} in screen {vis}");
			GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath($"res://test-output/notes/{n.name}.png"));
			NoteOverlay.Instance.Close();
			await Frames(15);
			r.QueueFree();
		}
		GetTree().Quit(failed > 0 ? 1 : 0);
	}

	private static PanelContainer FindCard(Node n)
	{
		foreach (var c in n.GetChildren())
		{
			if (c is PanelContainer p) return p;
			var d = FindCard(c);
			if (d != null) return d;
		}
		return null;
	}

	private async Task Frames(int k) { for (int i = 0; i < k; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
}
