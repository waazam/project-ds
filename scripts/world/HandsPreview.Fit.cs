using System.Linq;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Player;

namespace ProjectDS.World;

/// <summary>
/// `-- --fit[=Name,Name]` on hands_preview.tscn (2026-10-15, the owner: "make sure items are properly placed in the palm of
/// the hands when needed and the thumb is correct and not bent wrong"): every thing the hands hold that isn't a carried tool
/// (the lantern, the camera, the crowbar, the flamethrower's wand, the flare, the tin, and each thing shown as it's used:
/// keys, cards, the chalice, the bookmark, the station's three, the stone), shot from the eye, from the side and from the
/// front three-quarter by a second camera near the hand: test-output/fit/&lt;name&gt;_{eye,side,front}.png.
/// </summary>
public partial class HandsPreview
{
	private async Task FitShots(string[] only)
	{
		string dir = ProjectSettings.GlobalizePath("res://test-output/fit");
		DirAccess.MakeDirRecursiveAbsolute(dir);
		var cam = _p.CameraRig.Camera;
		var side = new Camera3D { Name = "FitCam", Fov = 38f, Near = 0.01f, CullMask = 0xFFFFF };
		GetTree().Root.AddChild(side);
		foreach (var k in new[] { ToolKind.Camera, ToolKind.Lantern, ToolKind.Crowbar }) _inv.TryPickup(k);
		if (_p.GetNodeOrNull<Flamethrower>("Flamethrower") == null) _p.AddChild(new Flamethrower { Name = "Flamethrower" });
		_inv.TryPickup(ToolKind.Flamethrower);
		Look(0f);

		bool Want(string n) => only == null || only.Contains(n, System.StringComparer.OrdinalIgnoreCase);

		async Task Three(string name, Node3D at)
		{
			if (at == null) { GD.Print($"[fit] {name}: nothing to aim at"); return; }
			await Seconds(0.15);
			GetViewport().GetTexture().GetImage().SavePng($"{dir}/{name}_eye.png");
			var b = cam.GlobalBasis;
			Vector3 c = at.GlobalPosition;
			foreach (var (view, off) in new[] { ("side", b.X * 0.42f + b.Y * 0.04f + b.Z * 0.02f), ("front", -b.Z * 0.42f + b.X * 0.2f + b.Y * 0.06f), ("below", -b.Z * 0.25f - b.Y * 0.3f + b.X * 0.12f) })
			{
				side.GlobalPosition = c + off;
				side.LookAt(c, b.Y);
				side.MakeCurrent();
				await Frames(3);
				GetViewport().GetTexture().GetImage().SavePng($"{dir}/{name}_{view}.png");
			}
			cam.MakeCurrent();
			await Frames(2);
			GD.Print($"[fit] {name}");
		}

		// the right hand's own: the camera, the crowbar, the wand
		foreach (var k in new[] { ToolKind.Camera, ToolKind.Crowbar, ToolKind.Flamethrower })
		{
			if (!Want(k.ToString())) continue;
			_inv.Select(k);
			await Seconds(1.0);
			var t = PlayerHands.Instance?.Tool;
			Node3D aim = t?.FindChild("Arm", true, false) as Node3D ?? t;
			await Three(k.ToString().ToLowerInvariant(), aim);
		}
		_inv.Select(ToolKind.None);
		await Seconds(0.6);
		// the lantern in the left
		if (Want("Lantern"))
		{
			var hl = cam.GetNodeOrNull<HeldLantern>(nameof(HeldLantern));
			await Three("lantern", hl?.FindChild("Arm", true, false) as Node3D ?? hl);
		}
		// the flare
		if (Want("Flare"))
		{
			HeldFlare.For(_p).Take(30f);
			await Seconds(1.0);
			var fl = cam.GetNodeOrNull<HeldFlare>(nameof(HeldFlare));
			await Three("flare", fl?.FindChild("Arm", true, false) as Node3D ?? fl);
			await Seconds(0.2);
		}
		// each thing shown as it's used (long, so it holds still mid-use)
		foreach (var k in new[] { ToolKind.Key, ToolKind.FontKey, ToolKind.PantryKey, ToolKind.DiningKey, ToolKind.Keycard201, ToolKind.Chalice, ToolKind.Bookmark, ToolKind.DeadEye, ToolKind.PaleHand, ToolKind.StairTread, ToolKind.Compass, ToolKind.Lighter })
		{
			if (!Want(k.ToString())) continue;
			HeldUse.Do(_p, k, cam.GlobalPosition - cam.GlobalBasis.Z * 1.2f, 30f);
			await Seconds(0.9);
			var u = cam.GetNodeOrNull<HeldUse>(nameof(HeldUse));
			await Three("use_" + k.ToString().ToLowerInvariant(), u?.FindChild("Arm", true, false) as Node3D ?? u);
		}
		side.QueueFree();
	}
}
