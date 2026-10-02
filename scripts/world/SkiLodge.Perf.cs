using System.Collections.Generic;
using Godot;
using ProjectDS.Entities;
using ProjectDS.World.LodgeParts;

namespace ProjectDS.World;

/// <summary>
/// The lodge's performance (the owner: Act 23 was a little laggy and hitchy).
/// <list type="bullet">
/// <item><b>The woods put away indoors.</b> Nothing culls what walls hide, so inside the lodge every tree and drift of
/// the winter woods behind its walls was still drawn (thousands). Its windows are all opaque (lit glass), so while
/// the player's inside, the woods, the glade and the church are hidden; they're back the moment the player's out, and
/// for good once the front doors are gone (the doorway looks out on them).</item>
/// <item><b>Shaders warmed up.</b> Godot builds each material's pipeline the first time it's drawn: a hitch of a tenth of
/// a second or more at each first sight (a new room, a sheet off, the storm). On the way in, everything the lodge
/// will draw is drawn once, tiny, in front of the camera for a few frames: every material and particle system in it,
/// and the things that only appear later (the wendigo, its arm, the story's props).</item>
/// </list>
/// </summary>
public partial class SkiLodge
{
	public bool OutdoorHidden { get; private set; }
	public bool WarmedUp { get; private set; }
	private readonly List<Node3D> _outdoor = new();
	private Node3D _warm;
	private int _warmFrames;

	private void UpdateOutdoor(bool inside)
	{
		bool hide = inside && !FrontBroken;
		if (hide == OutdoorHidden) return;
		if (_outdoor.Count == 0)
		{
			// the woods (the lodge's parent) and everything beside it, and the church's own rooms
			if (GetParent() is Node woods)
			{
				foreach (var c in woods.GetChildren()) if (c is Node3D n && n != this) _outdoor.Add(n);
				if (woods.GetParent() is Node church)
					foreach (var c in church.GetChildren()) if (c is Node3D n && n != woods) _outdoor.Add(n);
			}
		}
		OutdoorHidden = hide;
		foreach (var n in _outdoor) if (IsInstanceValid(n)) n.Visible = !hide;
	}

	/// <summary>Draw everything the lodge will draw, once, tiny, before the eyes (see the class notes).</summary>
	private void WarmUp()
	{
		if (WarmedUp) return;
		WarmedUp = true;
		var cam = GetViewport()?.GetCamera3D();
		if (cam == null) return;
		_warm = new Node3D { Name = "ShaderWarmUp" };
		AddChild(_warm);
		var mats = new HashSet<Material>();
		var particles = new List<GpuParticles3D>();
		void Walk(Node n)
		{
			if (n == _warm) return;
			if (n is MeshInstance3D mi && mi.Mesh != null)
			{
				for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
				{
					var m = mi.GetSurfaceOverrideMaterial(s) ?? mi.Mesh.SurfaceGetMaterial(s);
					if (m != null) mats.Add(m);
				}
				if (mi.MaterialOverride != null) mats.Add(mi.MaterialOverride);
				if (mi.MaterialOverlay != null) mats.Add(mi.MaterialOverlay);
			}
			if (n is GpuParticles3D gp) particles.Add(gp);
			foreach (var c in n.GetChildren()) Walk(c);
		}
		Walk(this);
		// what only comes later
		foreach (var m in new Material[]
		{
			WinterWoods.IceOverlay, WinterWoods.IceMat, WinterWoods.SoftSnow, LodgeTextures.SilverMat, LodgeTextures.LinenMat, LodgeTextures.SheetLinenMat, LodgeTextures.TableclothMat,
			StationParts.StationTextures.BloodPoolMat, LodgeTextures.CopperMat, LodgeTextures.LeakMat,
		}) mats.Add(m);
		int i = 0;
		foreach (var m in mats)
		{
			var k = new MeshKit();
			k.Mat(m);
			k.Color = Colors.White;
			k.Quad(new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0), Vector3.Back);
			var q = k.CommitTo(_warm, $"W{i}", false);
			q.Position = new Vector3((i % 20 - 10) * 0.002f, (i / 20 - 5) * 0.002f, 0);
			q.Scale = Vector3.One * 0.0015f;
			i++;
		}
		foreach (var gp in particles)
		{
			if (!IsInstanceValid(gp)) continue;
			var d = (GpuParticles3D)gp.Duplicate();
			d.Emitting = true;
			d.OneShot = false;
			d.Scale = Vector3.One * 0.01f;
			d.Position = Vector3.Zero;
			_warm.AddChild(d);
		}
		// the wendigo and its arm (their skins, their eyes, the ice in its heart)
		var w = new Wendigo { Name = "WarmWendigo", Scale = Vector3.One * 0.001f };
		_warm.AddChild(w);
		w.Visible = true;
		var arm = new WendigoArm { Name = "WarmArm", Scale = Vector3.One * 0.001f };
		_warm.AddChild(arm);
		arm.Burst();
		_warmFrames = 0;
		GD.Print($"[perf] lodge warm-up: {mats.Count} materials, {particles.Count} particle systems");
	}

	/// <summary>Holds the warm-up before the eyes for a few frames, then clears it (from _Process).</summary>
	private void WarmProcess()
	{
		if (_warm == null) return;
		var cam = GetViewport()?.GetCamera3D();
		if (cam != null) _warm.GlobalTransform = new Transform3D(cam.GlobalBasis, cam.GlobalPosition - cam.GlobalBasis.Z * 0.6f);
		if (++_warmFrames > 4) { _warm.QueueFree(); _warm = null; }
	}
}
