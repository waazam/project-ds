using System.Collections.Generic;
using Godot;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// The one cabin in Act 7 and Act 25 (2026-10-16, STORY.md Rev 5):
/// <list type="bullet">
/// <item><b>In the wrong place once more.</b> After the clearing's fall the Hollow's cabin is carried, behind the black, to
/// the level's <see cref="Act7SpotGroup"/> marker ahead on the trail past the clearing (<see cref="MoveToAct7Spot"/>): the
/// same node, the same Seed and the same things in it, gone from where it stood in Act 5. Its window lit, its door open,
/// the hearth lit (<see cref="SetHearthLit"/>).</item>
/// <item><b>The cave-in.</b> The burning beams fallen across the doorway (cabin_fire.py's BeamA, B, C over its RoofDebris),
/// solid until the axe has taken all three (<see cref="BuildFallenBeams"/>, <see cref="ChopBeam"/>).</item>
/// <item><b>Sealed by the fire</b> once they're out (<see cref="SealByFire"/>): the doorway stays solid, its leaf left
/// where it hung.</item>
/// <item><b>Burned</b> (Act 25, the trailhead's cabin on the way back to the car; <see cref="SetBurnedOut"/>): charred
/// through, dark and cold, the door shut until E pushes it in, the board charred and empty, the assignment sheet ash on
/// the table, what fell in Act 7 still on the floor inside the door.</item>
/// </list>
/// The models are Blender's (tools/Blender/cabin_fire.py -> assets/models/items/cabin_fire.glb).
/// </summary>
public partial class Cabin
{
	/// <summary>The level's marker for where the cabin stands in Act 7 (its +Z the way the door faces).</summary>
	public const string Act7SpotGroup = "act7_cabin_spot";

	/// <summary>Carried to the Act 7 spot (the wrong place once more).</summary>
	public bool AtAct7Spot { get; private set; }
	/// <summary>Act 25: the burned cabin (see <see cref="SetBurnedOut"/>).</summary>
	public bool BurnedOut { get; private set; }
	/// <summary>Act 25: its door has been pushed in.</summary>
	public bool BurnedDoorOpen { get; private set; }

	/// <summary>The Act 7 spot in the level, or null.</summary>
	public static Node3D Act7Spot(Node n) => n.GetTree().GetFirstNodeInGroup(Act7SpotGroup) as Node3D;

	/// <summary>
	/// Carries the cabin to the level's Act 7 spot and builds it again there (grounded on its new slope). Idempotent. Every
	/// child goes with it (the friend's table, the ink on its walls, the indoor zone). Behind the black only: the old place
	/// is simply empty from then on.
	/// </summary>
	public void MoveToAct7Spot()
	{
		if (AtAct7Spot || Hub || Derelict) return;
		var spot = Act7Spot(this);
		if (spot == null) return;
		AtAct7Spot = true;
		var b = spot.GlobalBasis.Orthonormalized();
		GlobalTransform = new Transform3D(b, spot.GlobalPosition);
		_grounded = false;
		if (AutoGround && _terrain != null) GroundSelf();
		Build();
		GD.Print($"[story] Act 7: the cabin stands ahead on the trail, at {GlobalPosition.Snapped(Vector3.One * 0.1f)}");
	}

	// ------------------------------------------------------------------ the hearth

	private bool _hearthLit;
	private Node3D _hearth;

	/// <summary>The stove lit: a red glow at its door and the draft, a warm flicker on the floor and the wall.</summary>
	public void SetHearthLit(bool lit)
	{
		_hearthLit = lit;
		if (_hearth != null && IsInstanceValid(_hearth)) _hearth.QueueFree();
		_hearth = null;
		if (lit) BuildHearth();
	}

	private void BuildHearth()
	{
		if (_gen == null || !IsInstanceValid(_gen)) return;
		if (_hearth != null && IsInstanceValid(_hearth)) _hearth.QueueFree();
		// the stove (Cabin.Interior: on the left wall, its door to +X, at (-ix + 0.42, 0, -1.0))
		float ix = Hw - LogT * 0.5f;
		var c = new Vector3(-ix + 0.42f, 0f, -1.0f);
		_hearth = new Hearth { Name = "Hearth", Position = c };
		_gen.AddChild(_hearth);
	}

	/// <summary>The stove's fire: the light through its door's seams and the draft, and its flicker on the room.</summary>
	private sealed partial class Hearth : Node3D
	{
		private OmniLight3D _light;
		private StandardMaterial3D _glow;
		private float _t;
		private readonly RandomNumberGenerator _rng = new();

		public override void _Ready()
		{
			_glow = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.2f, 0.05f, 0.01f), EmissionEnabled = true, Emission = new Color(1f, 0.38f, 0.08f),
				EmissionEnergyMultiplier = 2.2f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			};
			// the seam round the door and the round draft under it, just proud of the iron (the door faces +X)
			foreach (var (at, size) in new[] { (new Vector3(0.262f, 0.47f, 0f), new Vector2(0.3f, 0.012f)), (new Vector3(0.262f, 0.34f, 0f), new Vector2(0.3f, 0.01f)),
				(new Vector3(0.262f, 0.405f, 0.15f), new Vector2(0.012f, 0.14f)), (new Vector3(0.262f, 0.27f, 0f), new Vector2(0.07f, 0.045f)) })
				AddChild(new MeshInstance3D
				{
					Mesh = new QuadMesh { Size = size }, MaterialOverride = _glow, Position = at,
					Rotation = new Vector3(0, Mathf.Pi * 0.5f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				});
			_light = new OmniLight3D
			{
				Name = "HearthLight", LightColor = new Color(1f, 0.5f, 0.2f), LightEnergy = 1.1f, OmniRange = 3.6f, OmniAttenuation = 1.3f,
				ShadowEnabled = false, Position = new Vector3(0.5f, 0.38f, 0f),
			};
			AddChild(_light);
			_t = _rng.Randf() * 10f;
		}

		public override void _Process(double delta)
		{
			_t += (float)delta;
			// a slow, soft flicker (never a strobe)
			float f = 0.82f + 0.1f * Mathf.Sin(_t * 2.1f) + 0.06f * Mathf.Sin(_t * 5.3f + 1.3f) + 0.03f * Mathf.Sin(_t * 9.7f);
			_light.LightEnergy = 1.1f * f;
			_glow.EmissionEnergyMultiplier = 2.2f * f;
		}
	}

	// ------------------------------------------------------------------ the cave-in

	private Node3D _fallen;
	private StaticBody3D _fallenBody;
	private readonly List<Node3D> _beams = new();

	/// <summary>The beams still across the doorway (0 once all three are chopped through).</summary>
	public int BeamsLeft { get { int n = 0; foreach (var b in _beams) if (IsInstanceValid(b) && b.Visible) n++; return n; } }
	/// <summary>The doorway's threshold, inside, in world space (where the beams lie).</summary>
	public Vector3 DoorwayInside => GlobalTransform * new Vector3(0, 1.0f, Hd - 0.3f);
	/// <summary>Each beam's middle (world), for the fire on it and the axe's aim; empty before the cave-in.</summary>
	public IEnumerable<Node3D> Beams => _beams;

	/// <summary>
	/// The beams down across the doorway, burning, and the roof's boards heaped under them; solid until all three are
	/// chopped away. <paramref name="drop"/>: they come down from the roof over <paramref name="seconds"/> (heavy, never
	/// floating); else they lie there at once. Returns the root (the cabin's local frame at the threshold).
	/// </summary>
	public Node3D BuildFallenBeams(bool drop, float seconds = 0.55f)
	{
		if (_fallen != null && IsInstanceValid(_fallen)) return _fallen;
		_beams.Clear();
		_fallen = new Node3D { Name = "FallenBeams", Position = new Vector3(0, 0, Hd) };
		AddChild(_fallen);
		ItemModels.Add(_fallen, "cabin_fire", "RoofDebris", Transform3D.Identity, "RoofDebris", FirePaint);
		foreach (string name in new[] { "BeamA", "BeamB", "BeamC" })
		{
			var pivot = new Node3D { Name = name };
			_fallen.AddChild(pivot);
			if (ItemModels.Add(pivot, "cabin_fire", name, Transform3D.Identity, "Mesh", FirePaint) == null)
			{
				// (no model: a plain charred pole where it lies)
				var at = name == "BeamA" ? (new Vector3(-0.92f, 0.62f, -0.22f), new Vector3(0.95f, 0.48f, -0.3f))
					: name == "BeamB" ? (new Vector3(-0.78f, 1.78f, -0.12f), new Vector3(0.88f, 0.06f, -0.55f))
					: (new Vector3(0.8f, 1.62f, -0.08f), new Vector3(-0.86f, 0.05f, -0.48f));
				var k = new MeshKit();
				k.Mat(FirePaint("char"));
				k.Cylinder(at.Item1, at.Item2, 0.075f, 0.07f, 8, true);
				k.CommitTo(pivot, "Mesh");
			}
			_beams.Add(pivot);
			if (drop)
			{
				// down from the roof over the door: each tips from where it hung, a little apart, and stops dead
				var from = new Transform3D(new Basis(Vector3.Right, -0.5f) * new Basis(Vector3.Forward, name == "BeamA" ? 0.3f : -0.25f), new Vector3(0, WallHeight - 0.4f, -0.4f));
				pivot.Transform = from;
				var tw = pivot.CreateTween();
				tw.TweenInterval(_beams.Count * 0.12f);
				tw.TweenProperty(pivot, "transform", Transform3D.Identity, seconds).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
			}
		}
		if (BuildCollision)
		{
			_fallenBody = new StaticBody3D { Name = "Blocked", CollisionLayer = 1, CollisionMask = 0 };
			_fallenBody.SetMeta("surface", "wood");
			_fallenBody.AddChild(new CollisionShape3D { Position = new Vector3(0, 1.05f, -0.3f), Shape = new BoxShape3D { Size = new Vector3(DoorWidth + 0.3f, 2.1f, 0.55f) } });
			_fallen.AddChild(_fallenBody);
		}
		return _fallen;
	}

	/// <summary>One blow's worth: the next beam knocked away (it drops to the floor and goes dark). True when the doorway is
	/// clear (all three gone: walkable).</summary>
	public bool ChopBeam()
	{
		foreach (var b in _beams)
		{
			if (!IsInstanceValid(b) || !b.Visible) continue;
			// knocked loose: it drops out of the doorway to the floor beside it, and is gone into the heap
			var tw = b.CreateTween();
			tw.TweenProperty(b, "position", b.Position + new Vector3(0, -0.35f, -0.25f), 0.25f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
			tw.TweenCallback(Callable.From(() => b.Visible = false));
			b.SetMeta("chopped", true);
			break;
		}
		int left = 0;
		foreach (var b in _beams) if (IsInstanceValid(b) && !b.HasMeta("chopped")) left++;
		if (left == 0 && _fallenBody != null && IsInstanceValid(_fallenBody))
		{
			_fallenBody.QueueFree();
			_fallenBody = null;
		}
		return left == 0;
	}

	/// <summary>The beams gone from the doorway (a restore past the break-out): only the heap on the floor stays.</summary>
	public void ClearFallenBeams()
	{
		foreach (var b in _beams) if (IsInstanceValid(b)) { b.SetMeta("chopped", true); b.Visible = false; }
		if (_fallenBody != null && IsInstanceValid(_fallenBody)) _fallenBody.QueueFree();
		_fallenBody = null;
	}

	/// <summary>
	/// Out of the fire: the doorway is solid for good (it's all burning; nobody goes back in). The leaf stays where it hung
	/// (open, against the wall inside). Not saved on its own: the fire's restore calls it again.
	/// </summary>
	public void SealByFire()
	{
		if (Sealed) return;
		Sealed = true;
		if (_doorCollision != null && IsInstanceValid(_doorCollision)) _doorCollision.Disabled = false;
		if (_reopen != null && IsInstanceValid(_reopen)) { _reopen.QueueFree(); _reopen = null; }
	}

	// ------------------------------------------------------------------ Act 25: burned

	/// <summary>
	/// Act 25 (STORY.md: "past the cabin from Act 1, burned out from the fire in Act 7: its door is shut, and E pushes it in
	/// on a cold, dark room. Inside, the board is charred and the assignment sheet is ash on the table"). Safe before or
	/// after the cabin has built: it builds again in this state.
	/// </summary>
	public void SetBurnedOut()
	{
		if (BurnedOut) return;
		BurnedOut = true;
		_lightOn = false;
		IsOpen = false;
		_hearthLit = false;
		if (_gen != null && IsInsideTree()) Build();
	}

	/// <summary>Build's last step for <see cref="BurnedOut"/>: the char through everything, the papers gone to ash.</summary>
	private void ApplyBurnedOut()
	{
		if (_gen == null) return;
		// the board: dark, empty, charred with the rest
		if (Board != null) { Board.GlowOn = false; Board.ClearPrints(); }
		// the sheet burned where it lay (ash on its folder); the photo in its frame burned black (the frame stays)
		if (_gen.GetNodeOrNull<Node3D>("AssignmentSheet") is { } sheet)
		{
			sheet.Visible = false;
			foreach (var r in sheet.FindChildren("*", nameof(Interactable), true, false)) if (r is Interactable i) i.Enabled = false;
			ItemModels.Add(_gen, "cabin_fire", "AshSheet", sheet.Transform * new Transform3D(Basis.Identity, new Vector3(0, 0.0045f, 0)), "AshSheet", FirePaint, false);
		}
		if (FramedPhoto != null) FramedPhoto.Visible = false;
		// the axe went out with him (Act 7)
		if (HearthAxe != null) HearthAxe.Visible = false;
		// what came down over the door in Act 7, still on the floor inside it, and the beams' chopped ends against the wall
		var heap = new Node3D { Name = "BurnedHeap", Position = new Vector3(0, 0, Hd) };
		_gen.AddChild(heap);
		ItemModels.Add(heap, "cabin_fire", "RoofDebris", Transform3D.Identity, "RoofDebris", FirePaint);
		ItemModels.Add(heap, "cabin_fire", "BeamStubs", Transform3D.Identity, "BeamStubs", FirePaint);
		ColdEmbers(heap);
		// a little cold light from the empty window frames, so the room isn't a black hole
		_gen.AddChild(new OmniLight3D
		{
			Name = "BurnedFill", LightColor = new Color(0.6f, 0.66f, 0.76f), LightEnergy = 0.3f, OmniRange = 4.2f, OmniAttenuation = 1.1f,
			ShadowEnabled = false, Position = new Vector3(-WinCx * 0.5f, 1.6f, 0.2f),
		});
		_char ??= BuildingTextures.NewCharOverlay();
		_char.SetShaderParameter("amount", 0.92f);
		_char.SetShaderParameter("ember", 0f);
		SetOverlay(_gen, _char);
		UpdateLamp();
	}

	/// <summary>Act 25: E on the burned door. It scrapes in on its scorched hinges, onto the cold room (not saved: it's the
	/// last time they pass).</summary>
	public void PushBurnedDoor()
	{
		if (!BurnedOut || BurnedDoorOpen) return;
		BurnedDoorOpen = true;
		IsOpen = true;
		if (_doorCollision != null && IsInstanceValid(_doorCollision)) _doorCollision.Disabled = true;
		if (_door != null && IsInstanceValid(_door))
		{
			if (_door.GetNodeOrNull<Interactable>("PushDoor") is { } use) use.QueueFree();
			_door.CreateTween().TweenProperty(_door, "rotation:y", Mathf.DegToRad(84f), 1.7f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		}
		Audio.AudioDirector.OneShot(this, "door_creak", 1, DoorCenter, -4f);
		GD.Print("[story] Act 25: the burned cabin's door pushed in");
	}

	/// <summary>The burned door: shut, and its E-point (built with the door when <see cref="BurnedOut"/>).</summary>
	private void BuildBurnedDoorUse(float w)
	{
		if (Engine.IsEditorHint() || BurnedDoorOpen || _door == null) return;
		var use = new Interactable { Name = "PushDoor", Prompt = "Push the door", MaxDistance = 2.6f, PickRadius = 0.6f, Position = new Vector3(w * 0.6f, 1.0f, 0.1f), HighlightRoot = new NodePath("NoHighlight") };
		use.Interacted += _ => PushBurnedDoor();
		_door.AddChild(use);
	}

	// ------------------------------------------------------------------ paint

	private static readonly Dictionary<string, Material> _firePaint = new();

	/// <summary>cabin_fire.py's materials: the char, the embers in it (a slow glow), the ash.</summary>
	public static Material FirePaint(string name)
	{
		if (_firePaint.TryGetValue(name, out var m)) return m;
		m = name switch
		{
			"char" => new StandardMaterial3D { ResourceName = "cf_char", AlbedoColor = new Color(0.045f, 0.038f, 0.034f), Roughness = 0.95f, AlbedoTexture = ProcTextures.WoodMat is StandardMaterial3D w ? w.AlbedoTexture : null },
			"ember" => new StandardMaterial3D
			{
				ResourceName = "cf_ember", AlbedoColor = new Color(0.3f, 0.06f, 0.01f), Roughness = 0.8f,
				EmissionEnabled = true, Emission = new Color(1f, 0.36f, 0.07f), EmissionEnergyMultiplier = 2.4f,
			},
			"ash" => new StandardMaterial3D { ResourceName = "cf_ash", AlbedoColor = new Color(0.42f, 0.41f, 0.4f), Roughness = 1f },
			"char_paper" => new StandardMaterial3D { ResourceName = "cf_char_paper", AlbedoColor = new Color(0.07f, 0.065f, 0.06f), Roughness = 1f },
			_ => null,
		};
		_firePaint[name] = m;
		return m;
	}

	/// <summary>Cold embers (Act 25): the same glowing cracks, gone dark.</summary>
	public static void ColdEmbers(Node root)
	{
		var cold = new StandardMaterial3D { ResourceName = "cf_ember_cold", AlbedoColor = new Color(0.08f, 0.06f, 0.05f), Roughness = 0.95f };
		foreach (var n in root.FindChildren("*", nameof(MeshInstance3D), true, false))
			if (n is MeshInstance3D mi && mi.Mesh != null)
				for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
					if (mi.GetSurfaceOverrideMaterial(s) is Material m && m.ResourceName == "cf_ember") mi.SetSurfaceOverrideMaterial(s, cold);
	}
}
