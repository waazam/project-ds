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
	/// <summary>It swung shut behind the player once they were in the CRT room (Dan, 2026-09-22): from
	/// then on it is used from the inside, and the way through it is the room of doors.</summary>
	public bool ShutBehind { get; private set; }
	/// <summary>The player is standing in the door's zone (the autotest reads this).</summary>
	public bool PlayerNear { get; private set; }
	/// <summary>Where the E pick volume sits (aim here to use the door).</summary>
	public Vector3 InteractWorld => _interact != null ? _interact.ToGlobal(_interact.PickOffset) : ToGlobal(new Vector3(0, 1.25f, Z));

	public event System.Action Opened;
	/// <summary>E on the shut door from the CRT room side (the flow decides: stuck, or the way out).</summary>
	public event System.Action UsedFromInside;

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

	/// <summary>
	/// The player is through into the CRT room: the door swings shut behind them (a creak, a soft
	/// thud), the collider comes back, and the E-point turns round to the room side ("Push it open").
	/// Once only. <see cref="SetShutBehindInstant"/> is the silent restore of the same state.
	/// </summary>
	public void Close()
	{
		if (!IsOpen || ShutBehind) return;
		ShutBehind = true;
		var tw = CreateTween();
		tw.TweenProperty(_leaf, "rotation:y", 0f, 0.9f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
		tw.TweenCallback(Callable.From(() =>
		{
			BunkerKit.OneShot(this, "res://assets/audio/sfx/wall_knock_02.wav", new Vector3(0, 1.0f, Z), "Events", 2f, 0.7f, 3f, 25f);
			SetShutState();
		}));
		BunkerKit.OneShot(this, "res://assets/audio/sfx/trunk_creak_01.wav", new Vector3(0, 1.2f, Z), "Events", -1f, 0.85f, 3f, 25f);
		GD.Print("[story] Act 9: the vine door swings shut behind them");
	}

	/// <summary>Restore: shut behind the player, silently (a Continue inside the CRT room).</summary>
	public void SetShutBehindInstant()
	{
		if (ShutBehind) return;
		SetOpenInstant();
		ShutBehind = true;
		_leaf.Rotation = Vector3.Zero;
		SetShutState();
	}

	/// <summary>Stuck: E from the inside before the screens are done only rattles it.</summary>
	public void Rattle()
	{
		BunkerKit.OneShot(this, "res://assets/audio/sfx/trunk_creak_03.wav", new Vector3(0, 1.2f, Z), "Events", -3f, 1.05f, 3f, 20f);
		if (_leaf == null) return;
		var tw = CreateTween();
		tw.TweenProperty(_leaf, "rotation:y", Mathf.DegToRad(1.5f), 0.08f);
		tw.TweenProperty(_leaf, "rotation:y", 0f, 0.12f);
	}

	/// <summary>The flow relabels the inside E-point once the way through is open.</summary>
	public void SetInsidePrompt(string prompt) { if (_interact != null) _interact.Prompt = prompt; }

	private void SetShutState()
	{
		if (_leafCollision != null) _leafCollision.Disabled = false;
		if (_interact != null)
		{
			// The pick disc now stands proud on the CRT-room side (local -Z of the leaf when shut).
			_interact.PickOffset = new Vector3(0, 0, 0.45f);
			_interact.Prompt = "It won't move.";   // locked until the screens are done and the walkie is in hand (the flow re-labels it)
			_interact.Enabled = true;
		}
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
		MeshKit.Shrink(k.CommitTo(this, "DoorFrame"));   // its sill and jambs just clear of the floor and walls (the clip audit)
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

		// Ivy over the leaf itself (moves with it): stems up from its foot and across it, and a few strands bridging
		// the gap to the frame, which tear when the door is pushed.
		var ivy = new MeshKit();
		Vector3 LeafClamp(Vector3 p) => new(Mathf.Clamp(p.X, 0.03f, w - 0.03f), Mathf.Clamp(p.Y, 0.02f, h - 0.04f), p.Z);
		for (int i = 0; i < 3; i++)
		{
			float x0 = rng.RandfRange(0.08f, w - 0.08f), lean = rng.RandfRange(-0.35f, 0.35f), top = rng.RandfRange(0.9f, h - 0.3f);
			var path = new List<Vector3> { new(x0, 0.02f, 0.035f) };
			for (int j = 1; j <= 3; j++)
				path.Add(new Vector3(x0 + lean * j / 3f + rng.RandfRange(-0.12f, 0.12f), top * j / 3f, 0.035f));
			Stem(ivy, rng, path, rng.RandfRange(0.01f, 0.016f), Vector3.Back, LeafClamp, 1, 0.45f);
		}
		ivy.CommitTo(_leaf, "LeafIvy", false);

		_tornStrands = new Node3D { Name = "TornStrands" };
		AddChild(_tornStrands);
		var torn = new MeshKit();
		for (int i = 0; i < 5; i++)
		{
			float y = rng.RandfRange(0.4f, 2.0f);
			var a = new Vector3(DoorHalfWidth + 0.1f, y, Z + 0.22f);
			var b = new Vector3(DoorHalfWidth - 0.3f, y + rng.RandfRange(-0.2f, 0.2f), Z + 0.2f);
			Stem(torn, rng, new List<Vector3> { a, a.Lerp(b, 0.5f) + Vector3.Down * 0.05f, b }, 0.01f, Vector3.Back, p => p, 1);
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
		_interact.Interacted += _ => { if (ShutBehind) UsedFromInside?.Invoke(); else Open(); };
	}

	/// <summary>
	/// The ivy (rebuilt 2026-10-03, the owner: the vines "desperately need to look way better and more tangible"): old
	/// woody trunks up either side of the frame and arching over the lintel to meet, thinner climbers spreading over
	/// the bulkhead, a fringe hanging from the vault (clear of the head through the doorway), creepers across the
	/// floor and up the last ribs. Each stem branches, and carries real leaves on stalks (<see cref="Leaf"/>).
	/// </summary>
	private void BuildOvergrowth()
	{
		var rng = new RandomNumberGenerator { Seed = 8202 };
		var k = new MeshKit();
		float face = -HallLength + 0.05f;
		float Roof(float x) => HallKickHeight + Mathf.Sqrt(Mathf.Max(0f, HallHalfWidth * HallHalfWidth - x * x)) - 0.08f;
		Vector3 OnFace(Vector3 p)
		{
			float x = Mathf.Clamp(p.X, -HallHalfWidth + 0.06f, HallHalfWidth - 0.06f);
			return new Vector3(x, Mathf.Clamp(p.Y, 0.02f, Mathf.Min(Roof(x), HallHeight - 0.1f)), p.Z);
		}
		float jamb = DoorHalfWidth + 0.2f, lintel = DoorHeight + 0.2f;
		// the two old trunks framing the door, up the jambs and over the lintel, crossing in the middle
		foreach (float side in new[] { -1f, 1f })
		{
			Stem(k, rng, new List<Vector3>
			{
				new(side * (jamb + 0.06f), 0.02f, face), new(side * (jamb + 0.02f), 0.8f, face), new(side * jamb, 1.7f, face),
				new(side * (jamb - 0.03f), lintel, face), new(side * 0.45f, lintel + 0.16f, face), new(-side * 0.18f, lintel + 0.1f, face),
			}, 0.034f, Vector3.Back, OnFace);
			// a second, thinner, twisting round the first and away up toward the vault
			Stem(k, rng, new List<Vector3>
			{
				new(side * (jamb + 0.3f), 0.02f, face), new(side * (jamb + 0.12f), 0.9f, face), new(side * (jamb + 0.28f), 1.8f, face),
				new(side * (jamb + 0.4f), lintel + 0.5f, face), new(side * 0.6f, Roof(0.6f) - 0.1f, face),
			}, 0.022f, Vector3.Back, OnFace);
		}
		// climbers spreading over the rest of the bulkhead
		for (int i = 0; i < 10; i++)
		{
			float side = i % 2 == 0 ? -1f : 1f;
			float x = side * rng.RandfRange(jamb + 0.35f, HallHalfWidth - 0.1f), top = rng.RandfRange(1.4f, 3.1f);
			var path = new List<Vector3> { new(x, 0.02f, face) };
			for (int j = 1; j <= 4; j++)
				path.Add(new Vector3(x + side * rng.RandfRange(-0.2f, 0.35f) * j * 0.5f, top * j / 4f, face));
			Stem(k, rng, path, rng.RandfRange(0.01f, 0.018f), Vector3.Back, OnFace);
		}
		// the fringe hanging from the vault and the lintel: over the doorway it stops above the head (2.05 m)
		for (int i = 0; i < 16; i++)
		{
			float x = rng.RandfRange(-HallHalfWidth + 0.3f, HallHalfWidth - 0.3f);
			bool overDoor = Mathf.Abs(x) < DoorHalfWidth + 0.1f;
			float y0 = overDoor ? lintel + rng.RandfRange(0f, 0.4f) : Mathf.Min(Roof(x), HallHeight - 0.15f);
			float y1 = overDoor ? rng.RandfRange(2.05f, 2.25f) : rng.RandfRange(0.9f, 2.2f);
			float z = face + rng.RandfRange(0.03f, 0.1f);
			Stem(k, rng, new List<Vector3> { new(x, y0, z), new(x + rng.RandfRange(-0.08f, 0.08f), (y0 + y1) * 0.5f, z + 0.02f), new(x + rng.RandfRange(-0.1f, 0.1f), y1, z) },
				rng.RandfRange(0.008f, 0.014f), Vector3.Back, OnFace, 1);
		}
		// The tunnel before the door, grown over (the owner: "some of the tunnel before the vine door is mossy and viney
		// too"): the last eighteen metres, thicker toward the door. Creepers out along the floor by the walls; climbers
		// up the walls and over onto the vault (following its curve, the leaves turned out from it); a fringe hanging
		// from the vault, kept above the head down the middle.
		const float reach = 18f;
		float Near(float z) => Mathf.Clamp(1f - (z - face) / reach, 0f, 1f);   // 1 at the door, 0 eighteen metres back
		Vector3 OnFloor(Vector3 p) => new(Mathf.Clamp(p.X, -HallHalfWidth + 0.05f, HallHalfWidth - 0.05f), 0.015f, Mathf.Min(p.Z, face + reach + 2f));
		Vector3 OnShell(Vector3 p)
		{
			const float r = HallHalfWidth - 0.03f;
			float y = Mathf.Max(p.Y, 0.02f);
			if (y <= HallKickHeight) return new Vector3(Mathf.Sign(p.X == 0f ? 1f : p.X) * r, y, p.Z);
			var d = new Vector2(p.X, y - HallKickHeight);
			if (d.LengthSquared() < 1e-4f) d = new Vector2(0f, 1f);
			d = d.Normalized() * r;
			return new Vector3(d.X, HallKickHeight + d.Y, p.Z);
		}
		Vector3 ShellFacing(Vector3 p) => p.Y <= HallKickHeight + 0.001f
			? new Vector3(-Mathf.Sign(p.X), 0, 0)
			: -new Vector3(p.X, p.Y - HallKickHeight, 0).Normalized();
		for (int i = 0; i < 16; i++)
		{
			float side = i % 2 == 0 ? -1f : 1f, x = side * rng.RandfRange(1.0f, 1.95f);
			float len = reach * Mathf.Sqrt(rng.Randf());
			Stem(k, rng, new List<Vector3> { new(x, 0.015f, face + 0.05f), new(x + side * rng.RandfRange(-0.3f, 0.2f), 0.015f, face + len * 0.5f), new(x + rng.RandfRange(-0.3f, 0.3f), 0.015f, face + len) },
				rng.RandfRange(0.008f, 0.016f), Vector3.Up, OnFloor);
		}
		for (int i = 0; i < 18; i++)
		{
			float side = i % 2 == 0 ? -1f : 1f;
			float z = face + reach * rng.Randf() * rng.Randf();   // most near the door
			float over = Mathf.Lerp(0.4f, 1.5f, Near(z)) * rng.RandfRange(0.6f, 1f);   // how far over the vault it gets (radians)
			var path = new List<Vector3> { new(side * HallHalfWidth, 0.02f, z), new(side * HallHalfWidth, HallKickHeight * 0.6f, z + rng.RandfRange(-0.4f, 0.4f)) };
			for (int j = 1; j <= 3; j++)
			{
				float ang = over * j / 3f;
				path.Add(new Vector3(side * Mathf.Cos(ang) * HallHalfWidth, HallKickHeight + Mathf.Sin(ang) * HallHalfWidth, z + rng.RandfRange(-0.8f, 0.8f)));
			}
			Stem(k, rng, path, rng.RandfRange(0.01f, 0.02f), ShellFacing, OnShell);
		}
		for (int i = 0; i < 22; i++)
		{
			float z = face + 0.4f + reach * 0.75f * rng.Randf() * rng.Randf();
			float ang = rng.RandfRange(0.25f, Mathf.Pi - 0.25f);
			var top = new Vector3(Mathf.Cos(ang) * HallHalfWidth, HallKickHeight + Mathf.Sin(ang) * HallHalfWidth, z);
			float low = Mathf.Abs(top.X) < 1.2f ? rng.RandfRange(2.05f, 2.4f) : rng.RandfRange(1.2f, 2.2f);
			if (low >= top.Y - 0.2f) continue;
			var inward = -new Vector3(top.X, top.Y - HallKickHeight, 0).Normalized();
			Vector3 Hang(Vector3 p) => p.Y > HallKickHeight && new Vector2(p.X, p.Y - HallKickHeight).Length() > HallHalfWidth - 0.03f ? OnShell(p) : p;
			var start = top + inward * 0.03f;
			Stem(k, rng, new List<Vector3> { start, new(start.X * 0.97f, (start.Y + low) * 0.5f, z + rng.RandfRange(-0.05f, 0.05f)), new(start.X * 0.95f + rng.RandfRange(-0.08f, 0.08f), low, z) },
				rng.RandfRange(0.006f, 0.012f), _ => Vector3.Back, Hang, 1);
		}
		k.CommitTo(this, "Overgrowth", false);

		// moss: thick on the floor by the door, thinning back down the tunnel; up the walls and onto the vault
		var mossTex = BunkerTextures.MossPatch();
		for (int i = 0; i < 26; i++)
		{
			float z = face + reach * rng.Randf() * rng.Randf();
			BunkerKit.AddDecal(this, mossTex, new Vector3(rng.RandfRange(-2f, 2f), 0f, z), Vector3.Up, Vector3.Forward,
				new Vector2(rng.RandfRange(0.8f, 2f), rng.RandfRange(0.8f, 2f)), 0.3f, new Color(1, 1, 1, Mathf.Lerp(0.55f, 0.95f, Near(z))));
		}
		for (int i = 0; i < 16; i++)
		{
			float side = i % 2 == 0 ? -1f : 1f, z = face + reach * rng.Randf() * rng.Randf();
			BunkerKit.AddDecal(this, mossTex, new Vector3(side * HallHalfWidth, rng.RandfRange(0.2f, 1.2f), z), new Vector3(-side, 0, 0), Vector3.Down,
				new Vector2(rng.RandfRange(1f, 1.8f), rng.RandfRange(1f, 1.6f)), 0.5f, new Color(1, 1, 1, Mathf.Lerp(0.5f, 0.9f, Near(z))));
		}
		for (int i = 0; i < 8; i++)
		{
			float z = face + reach * 0.6f * rng.Randf(), ang = rng.RandfRange(0.4f, Mathf.Pi - 0.4f);
			var on = new Vector3(Mathf.Cos(ang) * HallHalfWidth, HallKickHeight + Mathf.Sin(ang) * HallHalfWidth, z);
			BunkerKit.AddDecal(this, mossTex, on, -new Vector3(on.X, on.Y - HallKickHeight, 0).Normalized(), Vector3.Forward,
				new Vector2(rng.RandfRange(0.9f, 1.6f), rng.RandfRange(0.9f, 1.6f)), 0.4f, new Color(1, 1, 1, 0.8f));
		}

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
	/// One ivy stem along <paramref name="path"/>, wandering about it: woody and thick at its foot, thin at its tip,
	/// held a hair off what it climbs (<paramref name="facing"/>: the way that surface faces; <paramref name="clamp"/>
	/// keeps it on it). It branches now and then, and its leaves alternate along it on their stalks, big near the foot
	/// and small at the tip.
	/// </summary>
	private static void Stem(MeshKit k, RandomNumberGenerator rng, List<Vector3> path, float radius, Vector3 facing,
		System.Func<Vector3, Vector3> clamp, int depth = 0, float leafy = 1f) => Stem(k, rng, path, radius, _ => facing, clamp, depth, leafy);

	/// <param name="leafy">How many of its leaves it keeps (the door's own ivy is thinner, so the door still reads).</param>
	private static void Stem(MeshKit k, RandomNumberGenerator rng, List<Vector3> path, float radius, System.Func<Vector3, Vector3> facingAt,
		System.Func<Vector3, Vector3> clamp, int depth = 0, float leafy = 1f)
	{
		const float step = 0.06f;
		var pts = new List<Vector3>();
		var faces = new List<Vector3>();
		Vector3 offset = Vector3.Zero;
		void Add(Vector3 p)
		{
			var on = clamp(p);
			var f = facingAt(on);
			pts.Add(on + f * (radius * 0.8f + 0.004f));
			faces.Add(f);
		}
		for (int i = 0; i < path.Count - 1; i++)
		{
			Vector3 a = path[i], b = path[i + 1];
			int n = Mathf.Max(1, Mathf.CeilToInt(a.DistanceTo(b) / step));
			Vector3 dir = (b - a).Normalized();
			Vector3 across = facingAt(clamp(a)).Cross(dir).Normalized();
			for (int j = 0; j < n; j++)
			{
				offset = (offset + across * rng.RandfRange(-0.014f, 0.014f)) * 0.9f;
				Add(a.Lerp(b, j / (float)n) + offset);
			}
		}
		Add(path[^1]);
		if (pts.Count < 3) return;
		k.Mat(BunkerTextures.BarkMat);
		k.Color = depth == 0 ? new Color(0.27f, 0.23f, 0.18f) : new Color(0.3f, 0.28f, 0.19f);
		BunkerKit.Tube(k, pts, radius, Mathf.Max(radius * 0.25f, 0.004f), 6);
		int count = pts.Count;
		for (int i = 1; i < count; i++)
		{
			float u = i / (float)count;
			Vector3 facing = faces[i];
			Vector3 dir = (pts[i] - pts[i - 1]).Normalized();
			Vector3 across = facing.Cross(dir).Normalized() * (i % 2 == 0 ? 1f : -1f);
			float r = Mathf.Lerp(radius, radius * 0.25f, u);
			// a leaf each side of most nodes (ivy hides its stems), big near the foot, small at the tip
			for (int l = 0; l < 2; l++)
			{
				if (rng.Randf() > (l == 0 ? 0.95f : 0.7f) * leafy) continue;
				var sideways = l == 0 ? across : -across;
				float size = Mathf.Lerp(0.21f, 0.11f, u) * rng.RandfRange(0.8f, 1.15f) * (depth == 0 ? 1f : 0.9f);
				Leaf(k, rng, pts[i] + sideways * r, facing, (dir * 0.3f + sideways * 0.7f + Vector3.Up * 0.3f).Normalized(), size);
			}
			if (u > 0.85f && rng.Randf() < 0.6f)   // the tip's young leaves, crowded
				Leaf(k, rng, pts[i], facing, (dir + across * 0.3f).Normalized(), rng.RandfRange(0.07f, 0.1f));
			if (depth < 1 && i > 2 && count - i > 6 && rng.Randf() < 0.08f)
			{
				// a branch, off at 40-70 degrees, a third to two-thirds as long as what's left
				Vector3 bdir = dir.Rotated(facing, rng.RandfRange(0.7f, 1.2f) * (rng.Randf() < 0.5f ? -1f : 1f));
				float len = (count - i) * step * rng.RandfRange(0.3f, 0.65f);
				var root = clamp(pts[i]);
				Stem(k, rng, new List<Vector3> { root, root + bdir * len * 0.5f, root + (bdir + dir * 0.5f).Normalized() * len },
					Mathf.Max(r * 0.5f, 0.005f), facingAt, clamp, depth + 1, leafy);
			}
		}
	}

	/// <summary>
	/// One leaf: a card out of the ivy atlas, its stalk's end at <paramref name="at"/>, its blade reaching along
	/// <paramref name="up"/> and turned toward <paramref name="facing"/>; folded a little along its midrib (so the
	/// light falls differently on either half), its tip curled back.
	/// </summary>
	private static void Leaf(MeshKit k, RandomNumberGenerator rng, Vector3 at, Vector3 facing, Vector3 up, float size)
	{
		k.Mat(BunkerTextures.IvyMat);
		float s = rng.RandfRange(0.8f, 1.1f);
		k.Color = new Color(s, s * rng.RandfRange(0.95f, 1.06f), s * 0.95f);
		Vector3 n = (facing + new Vector3(rng.RandfRange(-0.35f, 0.35f), rng.RandfRange(-0.1f, 0.45f), rng.RandfRange(-0.35f, 0.35f))).Normalized();
		Vector3 t = up - n * up.Dot(n);
		if (t.LengthSquared() < 1e-4f) t = Vector3.Up - n * n.Y;
		if (t.LengthSquared() < 1e-4f) t = Vector3.Right;
		t = t.Normalized().Rotated(n, rng.RandfRange(-0.45f, 0.45f));
		Vector3 side = n.Cross(t).Normalized();
		float hs = size * 0.5f, fold = rng.RandfRange(0.12f, 0.32f), curl = rng.RandfRange(0.05f, 0.22f);
		Vector3 b = at, top = at + t * size - n * size * curl;
		Vector3 bl = b - side * hs + n * hs * fold, br = b + side * hs + n * hs * fold;
		Vector3 tl = top - side * hs + n * hs * fold, tr = top + side * hs + n * hs * fold;
		float pick = rng.Randf();
		int cell = pick < 0.45f ? 0 : pick < 0.7f ? 1 : pick < 0.8f ? 2 : 3;
		float u0 = (cell % 2) * 0.5f, v0 = (cell / 2) * 0.5f;
		Vector3 Facing(Vector3 nn) => nn.Dot(n) < 0f ? -nn : nn;
		Vector3 nl = Facing((b - bl).Cross(tl - bl).Normalized()), nr = Facing((br - b).Cross(top - b).Normalized());
		k.Quad(bl, b, top, tl, nl, new Vector2(u0, v0 + 0.5f), new Vector2(u0 + 0.25f, v0 + 0.5f), new Vector2(u0 + 0.25f, v0), new Vector2(u0, v0));
		k.Quad(b, br, tr, top, nr, new Vector2(u0 + 0.25f, v0 + 0.5f), new Vector2(u0 + 0.5f, v0 + 0.5f), new Vector2(u0 + 0.5f, v0), new Vector2(u0 + 0.25f, v0));
	}
}
