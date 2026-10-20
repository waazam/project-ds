using System;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World.StationParts;

/// <summary>
/// (2026-10-16, STORY.md Rev 5) The room his father locked (the logbook's December 31st: "I had to put him into one of the
/// rooms and lock the door"): a narrow door in the lobby's back wall, right of the iron door, chained shut from the
/// outside, a footlocker, a crate and a chair stacked against it. Once Room 3 is reached the chain hangs cut, the stack
/// is shoved aside and the door stands ajar (all while the player is out of the lobby). Inside, optional: a bare cot and
/// a blanket, no body, and every wall written over to the ceiling in blood, "Come and see.", in his hand. Nothing says
/// what became of the man locked in it.
///
/// Local space: the doorway's middle at the floor, on the lobby back wall's middle plane; x along the wall, the lobby
/// toward -Z, the room behind toward +Z. (StationInterior puts it at x = <see cref="DoorX"/> on the back wall.)
/// </summary>
public partial class LockedRoom : Node3D
{
	public const float DoorX = 3.45f, DoorW = 1.0f, DoorH = 2.1f;
	public const float RoomHalfW = 1.45f, RoomDepth = 3.1f, RoomH = 2.7f, RoomCX = -0.05f;
	private const float AjarDeg = 38f, OpenDeg = 100f;

	/// <summary>For tests: chained (until Room 3); then the chain cut and the door ajar.</summary>
	public bool Chained { get; private set; } = true;
	/// <summary>For tests: pushed open all the way.</summary>
	public bool PushedOpen { get; private set; }
	public bool Entered { get; private set; }
	public Interactable DoorUse { get; private set; }
	/// <summary>The middle of the room, on its floor (world).</summary>
	public Vector3 InsideWorld => ToGlobal(new Vector3(RoomCX, 0.05f, RoomDepth * 0.5f));

	private Node3D _hinge, _stackUp, _stackDown, _chain, _cut;
	private OmniLight3D _light;
	private Func<bool> _released;
	private StationInterior _st;

	/// <summary>Builds it; <paramref name="released"/> says when the chain is to be found cut (Room 3 reached).</summary>
	public void Build(StationInterior st, Func<bool> released)
	{
		_st = st;
		_released = released;
		BuildDoor();
		BuildStack();
		BuildRoom();
		DoorUse = new Interactable { Name = "Use", Prompt = "Chained shut", PickRadius = 0.6f, MaxDistance = 2.6f, Position = new Vector3(0, 1.1f, -0.2f) };
		DoorUse.Interacted += OnUsed;
		AddChild(DoorUse);
		Set(released?.Invoke() ?? false, false);
		SetProcess(true);
	}

	// ------------------------------------------------------------------ the door, its chain

	private void BuildDoor()
	{
		var frames = new MeshKit();
		frames.Mat(RoomSurface.Of(StationTextures.CasingMat, RoomSurface.Wood, 139));
		frames.Color = Colors.White;
		StationKit.DoorFrame(frames, Vector3.Zero, Vector3.Right, DoorW, DoorH, 0.2f);
		frames.CommitTo(this, "Frame", true);

		_hinge = new Node3D { Name = "Hinge", Position = new Vector3(-DoorW * 0.5f, 0, 0) };
		AddChild(_hinge);
		var k = new MeshKit();
		k.Mat(RoomSurface.Of(StationTextures.DoorPaintMat, RoomSurface.Wood with { Streaks = 0.5f, Mottle = 0.5f, Tide = 0.5f }, 144));
		k.Color = new Color(0.86f, 0.84f, 0.8f);
		StationKit.PanelDoor(k, new Vector3(DoorW * 0.5f, DoorH * 0.5f - 0.005f, 0), DoorW - 0.02f, DoorH - 0.02f, 0.045f, Basis.Identity, 1);
		k.Mat(ItemTextures.BrassMat);
		k.Color = new Color(0.75f, 0.6f, 0.38f);
		foreach (int s in new[] { -1, 1 })
			k.Cylinder(new Vector3(DoorW - 0.1f, 1.0f, 0), new Vector3(DoorW - 0.1f, 1.0f, s * 0.065f), 0.028f, 0.028f, 8, true);
		k.Color = Colors.White;
		k.CommitTo(_hinge, "Leaf", true);
		StationStory.Solid(_hinge, new Vector3(DoorW * 0.5f, DoorH * 0.5f, 0), new Vector3(DoorW, DoorH, 0.06f), "wood", "LeafBody");
		// the staple on the leaf the chain runs from (it goes with the leaf)
		StationStory.Place(_hinge, "ss_hasp", new Transform3D(Basis.Identity, new Vector3(DoorW - 0.09f, 1.04f, -0.0225f)));
		// the chain, from the leaf's staple to an eye in the casing (the latch side's jamb at x = DoorW / 2)
		_chain = new Node3D { Name = "Chain", Position = new Vector3(DoorW * 0.5f, 0, 0) };
		AddChild(_chain);
		StationStory.Place(_chain, "ss_door_chain", Transform3D.Identity);
		_cut = new Node3D { Name = "ChainCut", Position = new Vector3(DoorW * 0.5f, 0, 0), Visible = false };
		AddChild(_cut);
		StationStory.Place(_cut, "ss_chain_cut", Transform3D.Identity);
	}

	/// <summary>The stack against it (his: "Im locking all the doors"), and where it ends up shoved aside.</summary>
	private void BuildStack()
	{
		var roles = LobbyDecor.Roles;
		_stackUp = new Node3D { Name = "Stack" };
		AddChild(_stackUp);
		StationStory.Place(_stackUp, "footlocker", new Transform3D(new Basis(Vector3.Up, 0.04f), new Vector3(-0.02f, 0, -0.3f)), roles);
		StationStory.Place(_stackUp, "crate", new Transform3D(new Basis(Vector3.Up, -0.12f), new Vector3(-0.08f, 0.395f, -0.36f)), roles);
		StationStory.Place(_stackUp, "dining_chair", new Transform3D(new Basis(Vector3.Up, 0.3f) * new Basis(Vector3.Back, Mathf.Pi), new Vector3(-0.1f, 0.995f + 0.95f, -0.36f)), roles);
		StationStory.Solid(_stackUp, new Vector3(-0.02f, 0.2f, -0.3f), new Vector3(0.82f, 0.4f, 0.44f));
		StationStory.Solid(_stackUp, new Vector3(-0.08f, 0.7f, -0.36f), new Vector3(0.62f, 0.62f, 0.62f), "wood", "CrateBody");
		// (2026-10-19, the clip audit: walked through) the chair upended on the crate, at head height
		StationStory.Solid(_stackUp, new Vector3(-0.1f, 1.47f, -0.36f), new Vector3(0.5f, 0.92f, 0.5f), "wood", "ChairBody");

		_stackDown = new Node3D { Name = "StackShoved", Visible = false };
		AddChild(_stackDown);
		StationStory.Place(_stackDown, "footlocker", new Transform3D(new Basis(Vector3.Up, 0.35f), new Vector3(-1.1f, 0, -0.5f)), roles);
		StationStory.Place(_stackDown, "crate", new Transform3D(new Basis(Vector3.Up, 0.6f), new Vector3(-1.55f, 0, -1.15f)), roles);
		StationStory.Place(_stackDown, "dining_chair", new Transform3D(new Basis(Vector3.Up, 2.2f) * new Basis(Vector3.Right, -1.5f), new Vector3(-0.45f, 0.24f, -1.2f)), roles);
		StationStory.Solid(_stackDown, new Vector3(-1.1f, 0.2f, -0.5f), new Vector3(0.82f, 0.4f, 0.44f));
		StationStory.Solid(_stackDown, new Vector3(-1.55f, 0.3f, -1.15f), new Vector3(0.62f, 0.6f, 0.62f), "wood", "CrateBody");
		StationStory.Solid(_stackDown, new Vector3(-0.45f, 0.24f, -1.2f), new Vector3(0.9f, 0.48f, 0.5f), "wood", "ChairBody");   // (on its back on the floor)
	}

	// ------------------------------------------------------------------ the room

	private void BuildRoom()
	{
		var room = new Node3D { Name = "Room", Position = new Vector3(RoomCX, 0, RoomDepth * 0.5f) };
		AddChild(room);
		var body = new StaticBody3D { Name = "Shell", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "wood");
		room.AddChild(body);
		var k = new MeshKit { Tess = 0.6f };
		k.Mat(RoomSurface.Of(StationTextures.WallpaperStainedMat, RoomSurface.Paper with { Streaks = 0.9f, Tide = 0.8f, Grime = 1.4f }, 145));
		k.Color = new Color(0.72f, 0.68f, 0.62f);
		float hd = RoomDepth * 0.5f;
		StationKit.WallAlongX(k, body, hd, -RoomHalfW - 0.07f, RoomHalfW + 0.07f, RoomH, 0, null, crown: false);
		StationKit.WallAlongZ(k, body, RoomHalfW, -hd, hd, RoomH, 0, null, crown: false);
		StationKit.WallAlongZ(k, body, -RoomHalfW, -hd, hd, RoomH, 0, null, crown: false);
		k.Color = Colors.White;
		k.CommitTo(room, "Walls", true);
		var floorK = new MeshKit { Tess = 0.6f };
		var ceilK = new MeshKit { Tess = 0.6f };
		floorK.Mat(RoomSurface.Of(StationTextures.PlankFloorMat, RoomSurface.Wood with { FloorDirt = 0.8f }, 146));
		floorK.Color = new Color(0.62f, 0.58f, 0.54f);
		ceilK.Mat(RoomSurface.Of(StationTextures.CeilingMat, RoomSurface.Plaster with { CeilingStains = 1f }, 147));
		ceilK.Color = new Color(0.5f, 0.48f, 0.45f);
		StationKit.FloorAndCeiling(floorK, ceilK, body, RoomHalfW, hd, RoomH, 0);
		floorK.Color = Colors.White;
		ceilK.Color = Colors.White;
		floorK.CommitTo(room, "Floor", false);
		ceilK.CommitTo(room, "Ceiling", false);

		// the bare cot along the right wall, the blanket bunched at its foot; nobody on it
		StationStory.Place(room, "ss_bare_cot", new Transform3D(Basis.Identity, new Vector3(RoomHalfW - 0.5f, 0, 0.35f)));
		StationStory.Solid(room, new Vector3(RoomHalfW - 0.5f, 0.22f, 0.35f), new Vector3(0.78f, 0.44f, 1.95f), "wood", "CotBody");

		// every wall to the ceiling: "Come and see.", in his hand, in blood
		const float em = 0.085f;
		float ix = RoomHalfW - 0.078f, iz = hd - 0.078f, mid = RoomH * 0.5f;   // (8 mm off the walls' faces at 0.07)
		void Wall(string name, Vector3 at, Basis b, float w, float h, int seed)
			=> StationStory.HandText(room, StationStory.ComeAndSeeWall(w, h, em, seed), at, b, em, StationStory.Blood, name, HorizontalAlignment.Left, -6f);
		Wall("WritingBack", new Vector3(0, mid, iz), new Basis(Vector3.Up, Mathf.Pi), ix * 2f - 0.1f, RoomH - 0.12f, 31);
		Wall("WritingRight", new Vector3(ix, mid, 0), new Basis(Vector3.Up, -Mathf.Pi * 0.5f), hd * 2f - 0.1f, RoomH - 0.12f, 32);
		Wall("WritingLeft", new Vector3(-ix, mid, 0), new Basis(Vector3.Up, Mathf.Pi * 0.5f), hd * 2f - 0.1f, RoomH - 0.12f, 33);
		// the front, round the door (its gap from x = -RoomCX - DoorW/2 - the casing, in the room's space)
		float gap0 = -RoomCX - DoorW * 0.5f - 0.1f, gap1 = -RoomCX + DoorW * 0.5f + 0.1f;
		float fz = -hd + 0.078f;
		Wall("WritingFrontL", new Vector3((-ix + gap0) * 0.5f, mid, fz), Basis.Identity, gap0 + ix - 0.06f, RoomH - 0.12f, 34);
		Wall("WritingFrontR", new Vector3((gap1 + ix) * 0.5f, mid, fz), Basis.Identity, ix - gap1 - 0.06f, RoomH - 0.12f, 35);
		Wall("WritingOver", new Vector3(-RoomCX, (DoorH + 0.15f + RoomH) * 0.5f, fz), Basis.Identity, gap1 - gap0, RoomH - DoorH - 0.2f, 36);
		// the floor under it, and the walls, stained
		StationProps.Decal(room, StationTextures.StainMat, new Vector3(0.2f, 0.008f, -0.2f), Vector3.Up, new Vector2(1.6f, 1.2f), 0.4f);

		// a cold, very dim light (there is no lamp: what comes in round the door), so the walls read
		_light = new OmniLight3D
		{
			Name = "Dim", LightColor = new Color(0.72f, 0.76f, 0.86f), LightEnergy = 0.35f, OmniRange = 3.6f, ShadowEnabled = false,
			Position = new Vector3(-0.3f, RoomH - 0.4f, -0.4f),
		};
		room.AddChild(_light);
		Audio.AudioDirector.Zone(room, new Vector3(0, RoomH * 0.5f, 0), new Vector3(RoomHalfW * 2f, RoomH, RoomDepth), Audio.AudioDirector.Space.Room, "LockedVerb");
	}

	// ------------------------------------------------------------------ the state

	/// <summary>Chained (the stack against it), or the chain cut, the stack shoved aside, the door ajar.</summary>
	private void Set(bool released, bool live)
	{
		Chained = !released;
		_chain.Visible = !released;
		_cut.Visible = released;
		_stackUp.Visible = !released;
		_stackDown.Visible = released;
		Collide(_stackUp, !released);
		Collide(_stackDown, released);
		_light.Visible = released;
		if (released && !PushedOpen) _hinge.Rotation = new Vector3(0, -Mathf.DegToRad(AjarDeg), 0);
		DoorUse.Prompt = released ? "Push the door" : "Chained shut";
		if (released && live) GD.Print("[story] Act 13: the chain on the locked room's door hangs cut; the door stands ajar");
	}

	private static void Collide(Node n, bool on)
	{
		foreach (var c in n.GetChildren())
		{
			if (c is CollisionShape3D cs) cs.SetDeferred(CollisionShape3D.PropertyName.Disabled, !on);
			Collide(c, on);
		}
	}

	private void OnUsed(PlayerController player)
	{
		if (Chained)
		{
			Audio.AudioDirector.OneShot(this, "door_locked", 2, ToGlobal(new Vector3(0.4f, 1.0f, -0.1f)), -4f, "Events", 3f);
			return;
		}
		if (PushedOpen) return;
		PushedOpen = true;
		DoorUse.Enabled = false;
		var tw = CreateTween();
		tw.TweenProperty(_hinge, "rotation:y", -Mathf.DegToRad(OpenDeg), 1.4f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
		Audio.AudioDirector.OneShot(this, "door_creak", 1, ToGlobal(new Vector3(-0.4f, 1.8f, 0.1f)), -6f, "Events", 3f, 0.12f);
	}

	public override void _Process(double delta)
	{
		var p = StoryBeat.Player(this);
		if (p == null) return;
		// found cut only while they're away from the lobby (Room 3)
		if (Chained && (_released?.Invoke() ?? false) && !(_st?.InLobby(p.GlobalPosition) ?? false)) Set(true, true);
		if (!Chained && !Entered)
		{
			Vector3 l = ToLocal(p.GlobalPosition);
			if (l.Z > 0.6f && l.Z < RoomDepth && Mathf.Abs(l.X - RoomCX) < RoomHalfW && l.Y > -0.5f && l.Y < RoomH)
			{
				Entered = true;
				StoryManager.Instance?.SetFlag(StationStory.LockedRoomEntered);
				GD.Print("[story] Act 13: in the room he locked - no one on the cot, and the walls");
			}
		}
	}
}
