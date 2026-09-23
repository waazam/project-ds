using Godot;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// R.H.'s camp in the Hollow (Act 3; a stranger, the hiker the stairs took a day before the player): a cold fire ring (a ring of stones, charred
/// logs, ash), a stump beside it with his lit lantern and his compass on top and his note
/// weighted down with a pebble. The tent and his pack are ParkProps placed in the level;
/// the lantern and compass are ordinary <see cref="Pickup"/> children of this node (they
/// move onto the "LanternSpot"/"CompassSpot" markers this builds on the stump top).
///
/// While the lantern still stands on the stump, a warm light around it makes the camp a
/// faint glow in the dark, visible from the wake spot. It goes out when the lantern is
/// taken (or was taken in the saved story).
///
/// Local frame: origin on the ground at the camp's centre, +Z toward the path. Everything
/// grounds itself on the terrain at runtime.
/// </summary>
[GlobalClass]
public partial class Camp : Node3D
{
	[Export] public Vector3 StumpLocal = new(0.9f, 0f, 1.3f);
	[Export] public float StumpHeight = 0.55f;
	[Export] public float StumpRadius = 0.31f;
	[Export] public Vector3 FireRingLocal = new(-0.9f, 0f, 1.9f);
	[Export] public NodePath LanternPath = "LanternPickup";
	[Export] public float GlowEnergy = 3.2f;
	[Export] public float GlowRange = 11f;

	/// <summary>His note, word for word (Act 3).</summary>
	public const string NoteText =
		"If you're reading this, it got you too.\n" +
		"Don't stay out here after dark. There's an old cabin up the hollow. The needle knows the way.\n" +
		"Past it there's a bunker. Steel door, four numbers.\n" +
		"Leave the lantern lit. — R.H.";

	public const string NoteReadFlag = "read_camp_note";

	/// <summary>The note's Readable (null in the editor), for tests and previews.</summary>
	public Readable Note { get; private set; }
	/// <summary>World-space top centre of the stump.</summary>
	public Vector3 StumpTopWorld => ToGlobal(StumpLocal + new Vector3(0, _stumpBase + StumpHeight, 0));

	private ForestTerrain _terrain;
	private float _stumpBase;
	private OmniLight3D _glow;
	private Node3D _lantern;

	/// <summary>
	/// The camp grounds itself on entering the tree, before its children are ready: the tent and the
	/// pack snap themselves (GroundSnap) in their own _Ready, which runs before this node's _Ready, so
	/// moving the camp any later would carry them off the ground with it.
	/// </summary>
	public override void _EnterTree()
	{
		if (Engine.IsEditorHint()) return;
		_terrain = GroundSnap.FindTerrain(this);
		if (_terrain != null) GlobalPosition = GlobalPosition with { Y = _terrain.HeightAt(GlobalPosition.X, GlobalPosition.Z) };
	}

	public override void _Ready()
	{
		_terrain ??= GroundSnap.FindTerrain(this);
		var gen = new Node3D { Name = "Generated" };
		AddChild(gen);
		BuildFireRing(gen);
		BuildStump(gen);
		_lantern = GetNodeOrNull<Node3D>(LanternPath);
		SetProcess(_glow != null);
	}

	private float GroundLocal(Vector3 local)
	{
		if (_terrain == null) return 0f;
		Vector3 w = ToGlobal(local);
		return _terrain.HeightAt(w.X, w.Z) - GlobalPosition.Y;
	}

	// ------------------------------------------------------------------ the fire ring

	private void BuildFireRing(Node3D gen)
	{
		var k = new MeshKit();
		var rng = new RandomNumberGenerator { Seed = 5813 };
		Vector3 c = FireRingLocal;
		float gy = GroundLocal(c);
		// ash: a dark grey disc, a little proud of the ground, and a few pale flecks
		k.Mat(ProcTextures.Flat("camp_ash", new Color(0.16f, 0.15f, 0.14f), 1f, 0.1f));
		k.Color = Colors.White;
		ItemMeshes.Disc(k, c + new Vector3(0, gy + 0.025f, 0), Vector3.Up, 0.42f, 10);
		k.Mat(ProcTextures.Flat("camp_ash_pale", new Color(0.42f, 0.4f, 0.38f), 1f, 0.1f));
		for (int i = 0; i < 5; i++)
		{
			float a = rng.RandfRange(0, Mathf.Tau), r = rng.RandfRange(0.05f, 0.3f);
			Vector3 p = c + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
			ItemMeshes.Disc(k, p + new Vector3(0, GroundLocal(p) + 0.03f, 0), Vector3.Up, rng.RandfRange(0.04f, 0.08f), 6, rng.Randf());
		}
		// the ring of stones
		k.Mat(ProcTextures.RockMat);
		const int stones = 11;
		for (int i = 0; i < stones; i++)
		{
			float a = Mathf.Tau * i / stones + rng.RandfRange(-0.12f, 0.12f);
			float r = 0.56f + rng.RandfRange(-0.04f, 0.05f);
			Vector3 p = c + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
			float sh = rng.RandfRange(0.62f, 0.85f);
			k.Color = new Color(sh, sh, sh * 0.96f);
			var size = new Vector3(rng.RandfRange(0.11f, 0.16f), rng.RandfRange(0.08f, 0.12f), rng.RandfRange(0.1f, 0.14f));
			// on the lowest ground under the stone, so its downhill side meets the slope
			float sg = Mathf.Min(GroundLocal(p), Mathf.Min(Mathf.Min(GroundLocal(p + new Vector3(size.X, 0, 0)), GroundLocal(p - new Vector3(size.X, 0, 0))), Mathf.Min(GroundLocal(p + new Vector3(0, 0, size.Z)), GroundLocal(p - new Vector3(0, 0, size.Z)))));
			k.Blob(p + new Vector3(0, sg + size.Y * 0.45f, 0), size, 7100 + i, 0.2f, true, 3f, 0.4f);
		}
		// charred logs, crossed over the ash, their ends burnt down to points
		k.Mat(ProcTextures.Flat("camp_char", new Color(0.07f, 0.065f, 0.06f), 0.9f, 0.15f));
		for (int i = 0; i < 4; i++)
		{
			float a = i * 0.8f + rng.RandfRange(-0.2f, 0.2f);
			Vector3 dir = new(Mathf.Cos(a), 0, Mathf.Sin(a));
			float len = rng.RandfRange(0.28f, 0.4f);
			Vector3 p0 = c - dir * len * 0.5f, p1 = c + dir * len * 0.5f;
			float y0 = GroundLocal(p0) + 0.06f + i * 0.03f, y1 = GroundLocal(p1) + 0.05f + (i % 2) * 0.05f;
			k.Color = new Color(1f, 1f, 1f) * rng.RandfRange(0.8f, 1.1f);
			k.Cylinder(p0 + new Vector3(0, y0, 0), p1 + new Vector3(0, y1, 0), 0.045f, 0.018f, 5, true, 3f, rng.Randf());
		}
		// one half-burnt log left at the edge, bark still on one end
		k.Mat(ProcTextures.BarkMat);
		k.Color = new Color(0.5f, 0.46f, 0.42f);
		Vector3 l0 = c + new Vector3(0.7f, 0, -0.35f), l1 = c + new Vector3(1.15f, 0, -0.62f);
		k.Cylinder(l0 + new Vector3(0, GroundLocal(l0) + 0.06f, 0), l1 + new Vector3(0, GroundLocal(l1) + 0.06f, 0), 0.06f, 0.065f, 6, true, 2f);
		k.CommitTo(gen, "FireRing");
	}

	// ------------------------------------------------------------------ the stump, the note, the glow

	private void BuildStump(Node3D gen)
	{
		Vector3 s = StumpLocal;
		// the stump's base: the lowest ground under it, so it never floats
		float lo = float.MaxValue;
		for (int i = 0; i < 8; i++)
		{
			float a = Mathf.Tau * i / 8f;
			lo = Mathf.Min(lo, GroundLocal(s + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * StumpRadius));
		}
		if (lo == float.MaxValue) lo = 0f;
		_stumpBase = lo;
		float top = lo + StumpHeight;
		var k = new MeshKit();
		k.Mat(ProcTextures.BarkMat);
		k.Color = new Color(0.66f, 0.6f, 0.54f);
		ItemMeshes.Loft(k, new[]
		{
			new ItemMeshes.Ring(s + new Vector3(0, lo - 0.15f, 0), StumpRadius * 1.3f, StumpRadius * 1.22f),
			new ItemMeshes.Ring(s + new Vector3(0, lo + 0.05f, 0), StumpRadius * 1.12f, StumpRadius * 1.08f),
			new ItemMeshes.Ring(s + new Vector3(0, lo + 0.2f, 0), StumpRadius * 1.02f, StumpRadius),
			new ItemMeshes.Ring(s + new Vector3(0, top, 0), StumpRadius, StumpRadius * 0.97f),
		}, 10, false, false, Vector3.Right, 2.5f);
		// old cut top, grey and weathered (not fresh like the woodpile's)
		k.Mat(ProcTextures.EndGrainMat);
		k.Color = new Color(0.62f, 0.56f, 0.5f);
		ItemMeshes.Disc(k, s + new Vector3(0, top, 0), Vector3.Up, StumpRadius * 0.99f, 10);
		// the pebble on the note
		k.Mat(ProcTextures.RockMat);
		k.Color = new Color(0.72f, 0.71f, 0.68f);
		k.Blob(s + new Vector3(0.06f, top + 0.018f, 0.2f), new Vector3(0.035f, 0.022f, 0.03f), 9127, 0.15f, true, 4f, 0.3f);
		k.CommitTo(gen, "Stump");

		if (!Engine.IsEditorHint())
		{
			var body = new StaticBody3D { Name = "StumpBody", CollisionLayer = 1, CollisionMask = 0 };
			body.SetMeta("surface", "wood");
			gen.AddChild(body);
			body.AddChild(new CollisionShape3D
			{
				Position = s + new Vector3(0, (lo - 0.2f + top) * 0.5f, 0),
				Shape = new CylinderShape3D { Radius = StumpRadius, Height = top - lo + 0.2f },
			});
		}

		// where the pickups settle: lantern at the back left, compass back right (the note lies in front)
		AddChild(new Marker3D { Name = "LanternSpot", Position = s + new Vector3(-0.12f, top, -0.07f) });
		AddChild(new Marker3D { Name = "CompassSpot", Position = s + new Vector3(0.13f, top, -0.05f), Rotation = new Vector3(0, 0.4f, 0) });

		// his note, flat on the stump's front, under the pebble; read from the path side
		if (!Engine.IsEditorHint())
		{
			var root = new Node3D { Name = "Note" };
			Note = PaperKit.Flat(root, s + new Vector3(0.02f, top + 0.002f, 0.14f), 8f, new Vector2(0.14f, 0.18f), PaperKit.Look.Note,
				"", NoteText, Readable.NoteStyle.Handwritten, prompt: "Read the note", seed: 11);
			Note.ReadFlag = NoteReadFlag;
			Note.PickRadius = 0.13f;
			Note.PickOffset = new Vector3(0, 0, 0.04f);   // the sheet's +Z is up
			AddChild(root);
		}

		// the lantern's glow on the stump and the ground around it
		_glow = new OmniLight3D
		{
			Name = "LanternGlow",
			LightColor = new Color(1f, 0.62f, 0.3f),
			LightEnergy = GlowEnergy,
			OmniRange = GlowRange,
			OmniAttenuation = 1.6f,
			ShadowEnabled = false,
			Position = s + new Vector3(-0.35f, top + 1.1f, -0.35f),   // above and behind: lights the camp without blowing out the stump top
		};
		gen.AddChild(_glow);
	}

	private double _t;

	public override void _Process(double delta)
	{
		if (_glow == null) { SetProcess(false); return; }
		// The glow belongs to the lantern standing there: once it is carried off (or never was here), it goes.
		if (_lantern == null || !IsInstanceValid(_lantern) || _lantern.IsQueuedForDeletion() || _lantern is Pickup { Taken: true })
		{
			_glow.QueueFree();
			_glow = null;
			SetProcess(false);
			return;
		}
		_t += delta;
		_glow.LightEnergy = GlowEnergy * (0.9f + 0.07f * Mathf.Sin((float)_t * 1.7f) + 0.03f * Mathf.Sin((float)_t * 11.3f));
	}
}
