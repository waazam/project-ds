using System.Collections.Generic;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using static ProjectDS.World.BunkerParts.BunkerLayout;

namespace ProjectDS.World.BunkerParts;

/// <summary>
/// The end of the hallway: a weathered plank door in the bulkhead, barely
/// cracked open and overgrown with ivy (real stems and leaf cards) that has
/// crept in over the last stretch of clean floor. "Push through the vines"
/// (an <see cref="Interactable"/>) swings it open into the CRT room.
/// </summary>
public partial class BunkerVineDoor : Node3D
{
	private const float Z = -HallLength - BulkheadThickness * 0.5f;   // the door sits mid-bulkhead
	private const float AjarDegrees = 7f, OpenDegrees = 100f;

	private Node3D _leaf;
	private CollisionShape3D _leafCollision;
	private Interactable _interact;
	private Node3D _tornStrands;

	public bool IsOpen { get; private set; }
	/// <summary>The player is standing in the door's zone (the autotest reads this).</summary>
	public bool PlayerNear { get; private set; }
	/// <summary>Where the E pick volume sits (aim here to use the door).</summary>
	public Vector3 InteractWorld => _interact != null ? _interact.ToGlobal(_interact.PickOffset) : ToGlobal(new Vector3(0, 1.25f, Z));

	public event System.Action Opened;

	public override void _Ready()
	{
		BuildFrame();
		BuildLeaf();
		BuildOvergrowth();

		var area = StoryBeat.MakeTrigger(this, new BoxShape3D { Size = new Vector3(HallHalfWidth * 1.8f, HallHeight, 1.4f) },
			new Vector3(0, HallHeight * 0.5f, -HallLength), _ => PlayerNear = true, "VineDoorArea");
		area.BodyExited += b => { if (b is PlayerController) PlayerNear = false; };
	}

	/// <summary>Push it open (animated, with a creak). No-op if already open.</summary>
	public void Open()
	{
		if (IsOpen) return;
		SetOpenState();
		var tw = CreateTween();
		tw.TweenProperty(_leaf, "rotation:y", Mathf.DegToRad(OpenDegrees), 1.1f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
		BunkerKit.OneShot(this, "res://assets/audio/sfx/trunk_creak_02.wav", new Vector3(0, 1.2f, Z), "Events", -2f, 0.8f, 3f, 25f);
		Opened?.Invoke();
	}

	/// <summary>Restore: already standing open, silently.</summary>
	public void SetOpenInstant()
	{
		if (IsOpen) return;
		SetOpenState();
		_leaf.Rotation = new Vector3(0, Mathf.DegToRad(OpenDegrees), 0);
	}

	private void SetOpenState()
	{
		IsOpen = true;
		if (_leafCollision != null) _leafCollision.Disabled = true;
		if (_interact != null) _interact.Enabled = false;
		if (_tornStrands != null) _tornStrands.Visible = false;
	}

	// ------------------------------------------------------------------ building

	private void BuildFrame()
	{
		var k = new MeshKit();
		k.Mat(ProcTextures.WoodMat);
		k.Color = new Color(0.42f, 0.34f, 0.26f);
		float t = 0.12f, d = BulkheadThickness + 0.06f;
		k.Box(new Vector3(-DoorHalfWidth - t * 0.5f, DoorHeight * 0.5f, Z), new Vector3(t, DoorHeight, d));
		k.Box(new Vector3(DoorHalfWidth + t * 0.5f, DoorHeight * 0.5f, Z), new Vector3(t, DoorHeight, d));
		k.Box(new Vector3(0, DoorHeight + t * 0.5f, Z), new Vector3(DoorHalfWidth * 2f + t * 2f, t, d));
		// A rusted steel threshold plate.
		k.Mat(ProcTextures.MetalMat);
		k.Color = new Color(0.5f, 0.45f, 0.4f);
		k.Box(new Vector3(0, 0.01f, Z), new Vector3(DoorHalfWidth * 2f, 0.02f, d));
		k.CommitTo(this, "DoorFrame");
	}

	private void BuildLeaf()
	{
		float w = DoorHalfWidth * 2f - 0.04f, h = DoorHeight - 0.04f;
		_leaf = new Node3D { Name = "Leaf", Position = new Vector3(-DoorHalfWidth + 0.02f, 0.02f, Z), Rotation = new Vector3(0, Mathf.DegToRad(AjarDegrees), 0) };
		AddChild(_leaf);
		var rng = new RandomNumberGenerator { Seed = 8201 };
		var k = new MeshKit();
		k.Mat(ProcTextures.WoodMat);
		// Vertical planks with dark gaps, two battens and a brace on the far side.
		int planks = 5;
		float pw = w / planks;
		for (int i = 0; i < planks; i++)
		{
			float s = rng.RandfRange(0.75f, 1.0f);
			k.Color = new Color(0.44f * s, 0.37f * s, 0.29f * s);
			float ph = h - rng.RandfRange(0f, 0.05f);
			k.Box(new Vector3(pw * (i + 0.5f), ph * 0.5f, 0), new Vector3(pw - 0.012f, ph, 0.05f), 1.2f);
		}
		k.Color = new Color(0.34f, 0.28f, 0.22f);
		foreach (float y in new[] { 0.35f, h - 0.35f })
			k.Box(new Vector3(w * 0.5f, y, -0.045f), new Vector3(w - 0.04f, 0.14f, 0.04f));
		k.Beam(new Vector3(0.08f, 0.42f, -0.045f), new Vector3(w - 0.08f, h - 0.42f, -0.045f), 0.12f, 0.04f, 1f, Vector3.Back);
		// Black iron strap hinges and a ring pull on the hallway side.
		k.Mat(ProcTextures.MetalMat);
		k.Color = new Color(0.22f, 0.2f, 0.18f);
		foreach (float y in new[] { 0.4f, h - 0.4f })
			k.Box(new Vector3(0.32f, y, 0.03f), new Vector3(0.62f, 0.05f, 0.012f));
		k.Cylinder(new Vector3(w - 0.14f, 1.05f, 0.025f), new Vector3(w - 0.14f, 1.05f, 0.05f), 0.03f, 0.03f, 6);
		k.Cylinder(new Vector3(w - 0.14f, 0.99f, 0.05f), new Vector3(w - 0.14f, 0.93f, 0.06f), 0.012f, 0.012f, 4, false);
		k.CommitTo(_leaf, "LeafMesh");

		// Ivy over the leaf itself (moves with it) and a few strands bridging the gap to the frame,
		// which tear when the door is pushed.
		var ivy = new MeshKit();
		for (int i = 0; i < 7; i++)
		{
			Vector3 start = new(rng.RandfRange(0.05f, w - 0.05f), rng.RandfRange(0f, 0.3f), 0.04f);
			Strand(ivy, rng, start, new Vector3(rng.RandfRange(-0.25f, 0.25f), 1f, 0), rng.RandiRange(6, 11), 0.2f, Vector3.Back, 0.028f, planar: true, minX: 0.03f, maxX: w - 0.03f, maxY: h - 0.05f);
		}
		ivy.CommitTo(_leaf, "LeafIvy", false);

		_tornStrands = new Node3D { Name = "TornStrands" };
		AddChild(_tornStrands);
		var torn = new MeshKit();
		for (int i = 0; i < 4; i++)
		{
			float y = rng.RandfRange(0.4f, 2.0f);
			var a = new Vector3(DoorHalfWidth + 0.08f, y, Z + 0.2f);
			var b = new Vector3(DoorHalfWidth - 0.25f, y + rng.RandfRange(-0.2f, 0.2f), Z + 0.18f);
			torn.Mat(BunkerTextures.BarkMat);
			torn.Color = new Color(0.36f, 0.34f, 0.2f);
			BunkerKit.Cable(torn, a, b, 0.04f, 0.012f, 4);
			Leaf(torn, rng, a.Lerp(b, 0.5f), Vector3.Back, 0.16f);
		}
		torn.CommitTo(_tornStrands, "Mesh", false);

		var body = new StaticBody3D { Name = "LeafBody", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "wood");
		AddChild(body);
		_leafCollision = new CollisionShape3D { Position = new Vector3(0, DoorHeight * 0.5f, Z), Shape = new BoxShape3D { Size = new Vector3(DoorHalfWidth * 2f, DoorHeight, 0.1f) } };
		body.AddChild(_leafCollision);

		// E to push. The pick sphere sits behind the leaf so only a disc of it (centred on the door at
		// head height) stands proud of the collider, where the look ray can reach it.
		_interact = new Interactable
		{
			Name = "Interact",
			Prompt = "Push through the vines",
			MaxDistance = 3f,
			PickRadius = 0.7f,
			PickOffset = new Vector3(0, 0, -0.45f),
			Position = new Vector3(w * 0.5f, 1.43f, 0),
		};
		_leaf.AddChild(_interact);
		_interact.Interacted += _ => Open();
	}

	private void BuildOvergrowth()
	{
		var rng = new RandomNumberGenerator { Seed = 8202 };
		var k = new MeshKit();
		float face = -HallLength + 0.02f;
		// Ivy climbing the bulkhead face around the doorway, and hanging from the vault above it.
		for (int i = 0; i < 12; i++)
		{
			float side = i % 2 == 0 ? -1f : 1f;
			float x = side * rng.RandfRange(DoorHalfWidth + 0.15f, HallHalfWidth - 0.1f);
			Strand(k, rng, new Vector3(x, 0.02f, face), new Vector3(rng.RandfRange(-0.3f, 0.3f), 1f, 0), rng.RandiRange(10, 18), 0.22f, Vector3.Back, 0.03f, planar: true);
		}
		for (int i = 0; i < 9; i++)
		{
			float x = rng.RandfRange(-1.4f, 1.4f);
			float y = Mathf.Min(HallHeight - 0.15f, HallKickHeight + Mathf.Sqrt(Mathf.Max(0f, HallHalfWidth * HallHalfWidth - x * x)) - 0.12f);
			Strand(k, rng, new Vector3(x, y, face + 0.05f), new Vector3(rng.RandfRange(-0.2f, 0.2f), -1f, 0.1f), rng.RandiRange(5, 10), 0.18f, Vector3.Back, 0.025f, planar: false);
		}
		// Creeping in along the floor edges and up the last ribs.
		for (int i = 0; i < 10; i++)
		{
			float side = i % 2 == 0 ? -1f : 1f;
			Vector3 start = new(side * rng.RandfRange(1.3f, 1.9f), 0.02f, face + 0.1f);
			Strand(k, rng, start, new Vector3(side * 0.1f, 0.02f, 1f), rng.RandiRange(18, 30), 0.3f, Vector3.Up, 0.035f, planar: false, floor: true);
		}
		for (int i = 0; i < 6; i++)
		{
			float side = i % 2 == 0 ? -1f : 1f;
			float z = -HallLength + 1.25f + RibSpacing * rng.RandiRange(0, 2);
			Strand(k, rng, new Vector3(side * (HallHalfWidth - 0.05f), 0.02f, z), new Vector3(0, 1f, rng.RandfRange(-0.2f, 0.2f)), rng.RandiRange(6, 12), 0.2f, new Vector3(-side, 0, 0), 0.035f, planar: false);
		}
		k.CommitTo(this, "Overgrowth", false);

		var moss = BunkerTextures.MossPatch();
		for (int i = 0; i < 10; i++)
		{
			float z = -HallLength + rng.RandfRange(0.3f, 9f);
			BunkerKit.AddDecal(this, moss, new Vector3(rng.RandfRange(-2f, 2f), 0f, z), Vector3.Up, Vector3.Forward,
				new Vector2(rng.RandfRange(0.8f, 1.8f), rng.RandfRange(0.8f, 1.8f)), 0.3f, new Color(1, 1, 1, 0.9f));
		}
		for (int i = 0; i < 5; i++)
		{
			float side = i % 2 == 0 ? -1f : 1f;
			BunkerKit.AddDecal(this, moss, new Vector3(side * HallHalfWidth, rng.RandfRange(0.2f, 1.2f), -HallLength + rng.RandfRange(0.5f, 6f)),
				new Vector3(-side, 0, 0), Vector3.Down, new Vector2(1.2f, 1.2f), 0.5f, new Color(1, 1, 1, 0.85f));
		}
	}

	/// <summary>
	/// One ivy stem: a wandering tube from <paramref name="start"/> heading along <paramref name="dir"/>,
	/// with leaf cards facing <paramref name="facing"/>. planar keeps it on the wall plane (Z fixed);
	/// floor keeps it on the floor (Y fixed).
	/// </summary>
	private static void Strand(MeshKit k, RandomNumberGenerator rng, Vector3 start, Vector3 dir, int segs, float segLen,
		Vector3 facing, float radius, bool planar, bool floor = false, float minX = -HallHalfWidth + 0.08f, float maxX = HallHalfWidth - 0.08f, float maxY = HallHeight - 0.1f)
	{
		var pts = new List<Vector3> { start };
		Vector3 p = start, d = dir.Normalized();
		for (int i = 0; i < segs; i++)
		{
			d = (d + new Vector3(rng.RandfRange(-0.45f, 0.45f), floor ? 0f : rng.RandfRange(-0.15f, 0.25f), planar ? 0f : rng.RandfRange(-0.2f, 0.2f))).Normalized();
			p += d * segLen;
			if (planar) p.Z = start.Z + Mathf.Sin(i * 1.7f) * 0.012f;
			if (floor) p.Y = 0.02f + Mathf.Abs(Mathf.Sin(i * 1.3f)) * 0.03f;
			p.X = Mathf.Clamp(p.X, minX, maxX);
			p.Y = Mathf.Clamp(p.Y, 0.01f, maxY);
			pts.Add(p);
		}
		k.Mat(BunkerTextures.BarkMat);
		k.Color = new Color(0.34f, 0.32f, 0.2f);
		BunkerKit.Tube(k, pts, radius, radius * 0.35f, 4);
		for (int i = 1; i < pts.Count; i++)
		{
			int leaves = rng.RandiRange(1, 3);
			for (int l = 0; l < leaves; l++)
				Leaf(k, rng, pts[i - 1].Lerp(pts[i], rng.Randf()), facing, rng.RandfRange(0.14f, 0.24f));
		}
	}

	private static void Leaf(MeshKit k, RandomNumberGenerator rng, Vector3 at, Vector3 facing, float size)
	{
		k.Mat(BunkerTextures.IvyMat);
		float s = rng.RandfRange(0.75f, 1.05f);
		k.Color = new Color(s, s, s * 0.95f);
		Vector3 n = (facing + new Vector3(rng.RandfRange(-0.5f, 0.5f), rng.RandfRange(-0.3f, 0.5f), rng.RandfRange(-0.5f, 0.5f))).Normalized();
		Vector3 u = n.Cross(Mathf.Abs(n.Y) > 0.9f ? Vector3.Right : Vector3.Up).Normalized();
		u = u.Rotated(n, rng.RandfRange(0, Mathf.Tau));
		Vector3 v = n.Cross(u).Normalized();
		Vector3 c = at + n * 0.02f;
		float hs = size * 0.5f;
		k.Quad(c - u * hs - v * hs, c + u * hs - v * hs, c + u * hs + v * hs, c - u * hs + v * hs, n,
			new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0));
	}
}
