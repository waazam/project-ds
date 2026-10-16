using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.World.BunkerParts;

namespace ProjectDS.World;

/// <summary>
/// The bunker's outside: a round vault door in a stepped concrete face, set
/// into an earth mound deep in the woods. Locked until the cabin has been seen
/// burning (Act 7); the first time the player then gets close, the door swings
/// open for good. Walking in through the doorway (a trigger just
/// inside the hatch) carries them into the bunker's interior (Act 8) and marks
/// checkpoint 7. A laminated station card bolted beside the door can be read at
/// any time, locked or not. (2026-10-15, the owner: the door code became two halves of a key) The door has a keyhole in
/// its face: with both halves of R.H.'s key found (<see cref="SurveyLot"/>, <see cref="KeyPieceCache"/>) using it joins
/// them in the hand and turns the key (<see cref="UseKey"/>); without them it says so. The old keypad
/// (<see cref="BunkerKeypad"/>) is no longer built.
/// (2026-10-16, STORY.md Rev 5) The approach is the stairs': near the door the woods go silent and the hum rises
/// (<see cref="BuildApproachHush"/>), the third chapter's title comes up over the door, and roots and ivy grow through
/// its frame (Blender's DoorRoots).
/// Group "bunker_marker" so the compass can find it.
///
/// Restore: in _Ready the door is open (silently, no caption) if the story is at
/// Act 8 or later. Entry works every time the open doorway is walked into (a
/// Continue respawns the player outside), until the walkie has been found.
/// Geometry lives in <see cref="BunkerExterior"/>.
/// </summary>
[Tool]
[GlobalClass]
public partial class Bunker : Node3D
{
	[Export] public float OpenRadius = 14f;

	/// <summary>The station card beside the hatch (story-gaps P10). Its last lines say the door takes a key (R.H. had it:
	/// see <see cref="SurveyLot"/>).</summary>
	public const string StationCardText =
		"OVERLOOK PARK\n" +
		"REMOTE MONITORING STATION 3\n" +
		"AUTHORISED PERSONNEL ONLY\n" +
		"Unstaffed since 09/98.\n" +
		"Door: keyed lock. Key held by the duty ranger.\n" +
		"No spare on site.";

	/// <summary>The line at the locked door without the whole key (the flag's name is kept from the old dial: saves carry it).</summary>
	public const string DialLine = "Locked. The key's in two pieces somewhere.";
	public const string DialLineFlag = "line_bunker_dial";
	/// <summary>With one half only.</summary>
	public const string HalfLine = "Half a key won't turn it. The other half's still out there.";

	public bool IsOpen { get; private set; }
	/// <summary>The middle of the vault door's face (world).</summary>
	public Vector3 HatchWorld => ToGlobal(new Vector3(0, BunkerExterior.DoorCenterY, BunkerExterior.DoorZ + 0.1f));
	/// <summary>The keyhole in the vault door's face, low and right of the wheel (local, closed): aim here to use it.</summary>
	public static Vector3 KeyholeLocal => new(0.45f, 0.95f, BunkerExterior.DoorZ + 0.1f);
	public Vector3 KeyholeWorld => ToGlobal(KeyholeLocal + new Vector3(0, 0, 0.01f));
	/// <summary>The key's turning in the door right now (between the use and the door swinging).</summary>
	public bool Unlocking { get; private set; }

	/// <summary>A spot on the apron in front of the door, on the ground (the Act 8 checkpoint's respawn point).</summary>
	public Vector3 ApproachPointWorld
	{
		get
		{
			// On the apron's far end (or its ramp down to lower ground), or on the ground if that is higher.
			float y = 0.3f;
			if (_rampEndTop is { } end)
			{
				float t = Mathf.Clamp((3.2f - BunkerExterior.FaceZ) / (end.Z - BunkerExterior.FaceZ), 0f, 1f);
				y = Mathf.Lerp(BunkerExterior.FloorY, end.Y, t) + 0.1f;
			}
			var p = ToGlobal(new Vector3(0, y, 3.2f));
			if (GetTree()?.GetFirstNodeInGroup("terrain") is ForestTerrain terrain) p.Y = Mathf.Max(p.Y, terrain.HeightAt(p.X, p.Z) + 0.1f);
			return p;
		}
	}
	/// <summary>Camera yaw that faces the door from the apron.</summary>
	public float ApproachYaw => Mathf.Atan2(GlobalBasis.Z.X, GlobalBasis.Z.Z);
	/// <summary>A point just inside the doorway, in the entry trigger (walk here to enter).</summary>
	public Vector3 EntryPointWorld => ToGlobal(new Vector3(0, BunkerExterior.FloorY, -1.05f));
	/// <summary>Where the station card hangs (local): right of the frame, at chest height.</summary>
	public static Vector3 StationCardLocal => new(1.62f, 1.42f, BunkerExterior.FaceZ);

	private Node3D _gen;
	private Node3D _doorNode;
	private CollisionShape3D _closedCollision, _openCollision;
	private Area3D _openTrigger, _entryTrigger;
	private bool _entryRetrying;
	/// <summary>Where the apron's ramp meets lower ground in front (top surface, local), or null when the apron is flat.</summary>
	private Vector3? _rampEndTop;

	public override void _Ready()
	{
		AddToGroup("bunker_marker");
		Build();
		if (Engine.IsEditorHint()) return;
		SetOpen(StoryManager.Instance is { Current: >= Checkpoint.Act8BunkerEntered } || StoryManager.Instance?.HasFlag(StoryManager.Flag.BunkerUnlocked) == true);
		if (StoryManager.Instance is { } s) s.CheckpointReached += OnCheckpoint;
	}

	public override void _ExitTree()
	{
		if (!Engine.IsEditorHint() && StoryManager.Instance is { } s) s.CheckpointReached -= OnCheckpoint;
	}

	/// <summary>Instantly and silently open or shut the door (restore, previews).</summary>
	public void SetOpen(bool open)
	{
		if (_gen == null) return;
		IsOpen = open;
		if (_doorNode != null) { _doorNode.QueueFree(); _doorNode = null; }
		_doorNode = open ? BuildOpenDoor() : BuildClosedDoor();
		if (_closedCollision != null) _closedCollision.Disabled = open;
		if (_openCollision != null) _openCollision.Disabled = !open;
	}

	private static bool StoryAllowsOpening => StoryManager.Instance is { Current: >= Checkpoint.Act7CabinBurning };

	/// <summary>The player walked up to the locked hatch after the fire: the one line about the dial, once.</summary>
	private void OnApproach(PlayerController player)
	{
		// (2026-10-11, playtest: the bunker reached by another way, the lookout above the burning cabin never stood on, so
		// Act 7 never came and the dial stayed "Locked") after the stairs' loop, the bunker itself is Act 7's checkpoint
		// too: the cabin's been burning all along (it's lit as already seen); before the loop, the door stays shut and the
		// voice across the creek is still calling
		var s = StoryManager.Instance;
		if (!IsOpen && s != null && s.Current < Checkpoint.Act7CabinBurning && s.Current >= Checkpoint.Act6BridgeCrossed)
		{
			if (s.HasFlag(StoryManager.Flag.ClearingLoopDone))
			{
				GD.Print("[story] Act 7: reached at the bunker (the lookout was walked round)");
				StoryBeat.ReachCheckpoint(player, Checkpoint.Act7CabinBurning);
			}
			else if (Time.GetTicksMsec() - _nudgedAt > 20000)
			{
				_nudgedAt = Time.GetTicksMsec();
				_ = StoryBeat.Caption(this, "Not yet. Something is still calling, back across the creek.", 0.4f, 3.2f, 1f);
			}
		}
		// (2026-10-16, STORY.md Rev 5) the third chapter's title comes up over the vault door on the approach, once (it was
		// on walking in); not on the way back out after the walkie
		if (s != null && StoryAllowsOpening && s.Current < Checkpoint.Act10WalkieFound) UI.ChapterCard.Show(this, 3, 0.8f);
		if (IsOpen || !StoryAllowsOpening) return;
		SayDialLine();
		NudgeMissingPieces();
	}

	// ------------------------------------------------------------------ the approach: the silence and the hum

	/// <summary>(2026-10-16, STORY.md Rev 5: "Near it the ambience cuts to silence and a hum rises, as at the stairs") A
	/// <see cref="Audio.SilenceZone"/> before the door hushes the woods as the stairs' does, and the stairs' hum
	/// (<see cref="Audio.StairsHum"/>'s loop and curve) rises with the hush, from <see cref="HumEdgeDb"/> at its edge to
	/// <see cref="HumCloseDb"/> at the door. Only while the door is the way on: from the fire (or the loop done) until the
	/// walkie (after it, the thing from the rooms comes out of this door, and a silence here would send it away).</summary>
	[Export] public float HumEdgeDb = -30f;
	[Export] public float HumCloseDb = -13f;
	private Audio.SilenceZone _hush;
	private Audio.AmbienceLoop _hum;
	private float _humDb = -80f;
	private double _hushCheck;
	private Node3D _listener;

	/// <summary>For tests: the approach's hush is live, and the hum's level now (dB).</summary>
	public bool HushActive => _hush is { Active: true };
	public float HumDb => _humDb;

	private static bool HushWanted => StoryManager.Instance is { } s && s.Current < Checkpoint.Act10WalkieFound
		&& (s.Current >= Checkpoint.Act7CabinBurning || s.HasFlag(StoryManager.Flag.ClearingLoopDone));

	private void BuildApproachHush()
	{
		_hush = new Audio.SilenceZone { Name = "ApproachSilence", InnerRadius = 7f, OuterRadius = 34f, Falloff = 1.2f, Position = new Vector3(0, 1f, 4f), Active = HushWanted };
		_gen.AddChild(_hush);
		var player = new AudioStreamPlayer { Name = "ApproachHum", Bus = "Unnatural" };
		_gen.AddChild(player);
		_hum = new Audio.AmbienceLoop { Name = "Loop", StreamPath = "res://assets/audio/ambient/stairs_hum_loop.wav", BaseVolumeDb = 0f, Gain = 0f };
		player.AddChild(_hum);
	}

	public override void _Process(double delta)
	{
		if (Engine.IsEditorHint() || _hush == null || !IsInstanceValid(_hush)) return;
		_hushCheck -= delta;
		if (_hushCheck <= 0) { _hushCheck = 0.5; _hush.Active = HushWanted; }
		if (_listener == null || !IsInstanceValid(_listener)) _listener = GetTree().GetFirstNodeInGroup("player") as Node3D;
		float target = -80f;
		if (_listener != null && _hush.Active)
		{
			float prox = _hush.SilenceAt(_listener.GlobalPosition) / Mathf.Max(_hush.Strength, 0.01f);
			if (prox > 0.001f) target = Mathf.Lerp(HumEdgeDb, HumCloseDb, prox);
		}
		float k = 1f - Mathf.Exp(-(float)delta / 0.6f);
		_humDb = Mathf.Lerp(_humDb, target, k);
		if (_hum == null) return;
		// as the stairs' hum: Gain carries the fade to silence, ExtraDb the level while audible
		_hum.Gain = Mathf.Clamp(Mathf.DbToLinear(_humDb - HumEdgeDb + 6f), 0f, 1f);
		_hum.ExtraDb = Mathf.Max(_humDb, HumEdgeDb - 6f);
	}
	private ulong _nudgedAt, _piecesNudgedAt;

	/// <summary>
	/// (2026-10-13, the skippable-events audit; 2026-10-15 for the key) The second half's trail leaves the path between
	/// the lookout and here, but the compass points straight across the woods to the bunker, so a player who follows it can
	/// reach the door without ever passing its start. At the door with a half still missing: one short line under the
	/// picture saying where to look (at most once a minute).
	/// </summary>
	private void NudgeMissingPieces()
	{
		var s = StoryManager.Instance;
		if (s == null || s.HasFlag(StoryManager.Flag.BunkerUnlocked)) return;
		bool has1 = s.HasFlag(StoryManager.Flag.KeyPiece(1)), has2 = s.HasFlag(StoryManager.Flag.KeyPiece(2));
		if (has1 && has2) return;
		if (_piecesNudgedAt != 0 && Time.GetTicksMsec() - _piecesNudgedAt < 60000) return;
		if (UI.Subtitle.Instance is not { } sub) return;
		_piecesNudgedAt = Time.GetTicksMsec();
		string where = !has2
			? "Old paint on a tree by the path, back toward the lookout. His arrows go on from there."
			: "The other half's off the path between his camp and the cabin. His arrows go on from the old paint.";
		_ = sub.Show($"{where}   {UI.KeyHint.Blacklight}: the blacklight.", 0.8f, 5.5f, 1.2f);
		GD.Print($"[story] Act 7: at the door without half {(has2 ? 1 : 2)}: the nudge back");
	}

	/// <summary>The keyhole used: with both halves, the key's joined in the hand and turned (the pinch, <see cref="HeldUse"/>)
	/// and the door unlocks as it turns; without them, a line says what's missing.</summary>
	public void UseKey(PlayerController player)
	{
		var s = StoryManager.Instance;
		if (IsOpen || Unlocking || s == null || !StoryAllowsOpening) return;
		bool has1 = s.HasFlag(StoryManager.Flag.KeyPiece(1)), has2 = s.HasFlag(StoryManager.Flag.KeyPiece(2));
		if (!(has1 && has2))
		{
			SayDialLine();
			BunkerKit.OneShot(_gen, $"res://assets/audio/sfx/door_locked_0{1 + (int)(Time.GetTicksMsec() % 2)}.wav", KeyholeLocal, "Events", -4f, 0.8f, 2f, 14f);
			if (UI.Subtitle.Instance is { } sub && !sub.CurrentlyShowing) _ = sub.Show(has1 || has2 ? HalfLine : DialLine, 0.4f, 3.2f, 0.8f);
			NudgeMissingPieces();
			GD.Print($"[story] the bunker's keyhole tried with {(has1 ? 1 : 0) + (has2 ? 1 : 0)} of 2 halves");
			return;
		}
		_ = TurnKey(player);
	}

	private async System.Threading.Tasks.Task TurnKey(PlayerController player)
	{
		Unlocking = true;
		const float seconds = 3.2f;
		HeldUse.Do(player, ToolKind.BunkerKey, KeyholeWorld, seconds);
		// the halves home (the sleeve screwed down onto the stub): a click; then the key in, and the bolts going back
		await ToSignal(GetTree().CreateTimer(seconds * 0.47f), SceneTreeTimer.SignalName.Timeout);
		if (!IsInstanceValid(this)) return;
		StoryManager.Instance?.SetFlag(StoryManager.Flag.KeyAssembled);
		BunkerKit.OneShot(_gen, "res://assets/audio/sfx/cryptex_click_02.wav", KeyholeLocal, "Events", -6f, 0.9f, 1.4f, 8f);
		GD.Print("[story] the two halves of R.H.'s key joined");
		await ToSignal(GetTree().CreateTimer(seconds * 0.45f), SceneTreeTimer.SignalName.Timeout);
		if (!IsInstanceValid(this)) return;
		BunkerKit.OneShot(_gen, "res://assets/audio/sfx/door_locked_02.wav", KeyholeLocal, "Events", 0f, 0.62f, 3f, 20f);
		Unlocking = false;
		Unlock();
	}

	/// <summary>Checkpoint 6 could conceivably fire with the player already inside the radius: re-check.</summary>
	private void OnCheckpoint(Checkpoint _)
	{
		if (IsOpen || !StoryAllowsOpening || StoryBeat.PlayerInside(_openTrigger) == null) return;
		SayDialLine();
	}

	private void SayDialLine()
	{
		var s = StoryManager.Instance;
		if (s == null || s.HasFlag(DialLineFlag)) return;
		s.SetFlag(DialLineFlag);
		// No spoken line any more (Dan, 2026-09-22: less self-talk); the flag still marks the first look at the dial.
	}

	/// <summary>The key turned: saved, so the door stays open on Continue, then the door swings.</summary>
	private void Unlock()
	{
		if (IsOpen || !StoryAllowsOpening) return;
		StoryManager.Instance?.SetFlag(StoryManager.Flag.BunkerUnlocked);
		Open();
	}

	/// <summary>The story beat: the door grinds open (with its groan) and stays open; two words follow.</summary>
	private void Open()
	{
		SetOpen(true);
		BunkerKit.OneShot(_gen, Audio.AudioDirector.Take("bunker_door_grind", 4), new Vector3(0, 1f, 0.3f), "Events", 2f, (float)GD.RandRange(0.94, 1.04), 4f, 40f);   // its own rusted-steel grind (was a forest trunk's creak)
		GD.Print("[story] the bunker door stands open");
		// Only ever from here (a restore opens it silently through SetOpen), so the line plays once.
		// No "It's open." line (Dan, 2026-09-22: less self-talk); the creak and the swinging door say it.
	}

	public void Build()
	{
		var old = GetNodeOrNull("Generated");
		if (old != null) { RemoveChild(old); old.QueueFree(); }
		_gen = new Node3D { Name = "Generated" };
		AddChild(_gen);
		_doorNode = null;
		_openTrigger = null;

		var terrain = Engine.IsEditorHint() ? null : GetTree()?.GetFirstNodeInGroup("terrain") as ForestTerrain;
		if (terrain != null)
		{
			// Sit the door at the level of the ground just in front of it. (GroundSnap uses the lowest point
			// under the footprint, which on a slope left the door half buried by the ground before it.)
			// The mound follows the terrain and the concrete reaches well below grade, so nothing floats.
			var front = ToGlobal(new Vector3(0, 0, 1.2f));
			var p = GlobalPosition;
			p.Y = Mathf.Max(p.Y, terrain.HeightAt(front.X, front.Z) - 0.05f);
			// ...and never so low that the hill runs through the vestibule: the walkway from the sill to
			// the back must clear the ground (the placement follows the trail, so the slope can go either way).
			foreach (var (x, z) in new[] { (0f, 0.5f), (-0.45f, -0.3f), (0.45f, -0.3f), (0f, -1.2f), (0f, -2.2f) })
			{
				var w = ToGlobal(new Vector3(x, 0, z));
				p.Y = Mathf.Max(p.Y, terrain.HeightAt(w.X, w.Z) - BunkerExterior.FloorY + 0.04f);
			}
			GlobalPosition = p;
		}
		float TerrainY(float x, float z)
		{
			if (terrain == null) return 0f;
			var w = ToGlobal(new Vector3(x, 0, z));
			return terrain.HeightAt(w.X, w.Z) - GlobalPosition.Y;
		}
		var moundTris = BunkerExterior.BuildMound(_gen, TerrainY, true);
		// The placement follows the trail, so the ground in front can fall away from the sill: then the
		// apron ramps down to meet it instead of ending in a ledge. Higher ground buries it as before.
		_rampEndTop = null;
		if (terrain != null)
		{
			// Short and steep (about 37 degrees, well under the walkable limit) so it catches a falling
			// hillside within a few metres and ends just under the ground there.
			float farZ = 3.6f;
			for (int i = 0; i < 8; i++)
			{
				float drop = BunkerExterior.FloorY - (TerrainY(0, farZ) + 0.03f);
				float need = BunkerExterior.FaceZ + drop * 1.33f;
				if (need <= farZ + 0.05f) break;
				farZ = Mathf.Min(need, 6f);
			}
			float groundFar = TerrainY(0, farZ);
			if (groundFar < BunkerExterior.ApronTo.Y + 0.5f - 0.15f) _rampEndTop = new Vector3(0, groundFar + 0.03f, farZ);
		}
		BunkerExterior.BuildConcrete(_gen, _gen, _rampEndTop);
		BuildStationCardPlate();
		// (2026-10-16, STORY.md Rev 5) roots and ivy grown through the vault door's frame (Blender's: tools/Blender/rh_trail.py)
		ItemModels.Add(_gen, "rh_trail", "DoorRoots", Transform3D.Identity, "DoorRoots", n => n switch
		{
			"bark" => BunkerTextures.BarkMat,
			"ivy" => IvyMat,
			_ => null,
		});
		// (2026-10-15) no keypad any more: the door's own keyhole (BuildClosedDoor) takes R.H.'s key

		if (Engine.IsEditorHint()) return;
		var body = new StaticBody3D { Name = "BunkerBody", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "stone");
		_gen.AddChild(body);
		void Box(Vector3 c, Vector3 s) => body.AddChild(new CollisionShape3D { Position = c, Shape = new BoxShape3D { Size = s } });
		void BeamBox(Vector3 from, Vector3 to, float width, float height)
		{
			Vector3 z = (to - from).Normalized();
			Vector3 x = Vector3.Up.Cross(z).Normalized();
			Vector3 y = z.Cross(x).Normalized();
			body.AddChild(new CollisionShape3D
			{
				Transform = new Transform3D(new Basis(x, y, z), (from + to) * 0.5f),
				Shape = new BoxShape3D { Size = new Vector3(width, height, from.DistanceTo(to)) },
			});
		}
		const float gap = 0.55f, fy = BunkerExterior.FloorY, back = BunkerExterior.VestibuleBackZ;
		// The face, solid either side of and above the doorway.
		Box(new Vector3(-(gap + 2.75f) * 0.5f, (3.05f + BunkerExterior.Base) * 0.5f, 0.05f), new Vector3(2.75f - gap, 3.05f - BunkerExterior.Base, 0.7f));
		Box(new Vector3((gap + 2.75f) * 0.5f, (3.05f + BunkerExterior.Base) * 0.5f, 0.05f), new Vector3(2.75f - gap, 3.05f - BunkerExterior.Base, 0.7f));
		// The lintel and the vestibule ceiling sit at the round opening's top (2.37 m), not below it: a
		// 1.75 m capsule standing a little high on the sill still fits through.
		Box(new Vector3(0, 2.7f, 0.05f), new Vector3(gap * 2f, 0.7f, 0.7f));
		// The vestibule: floor, walls, ceiling and back, a corridor just wider than a person.
		Box(new Vector3(0, fy - 0.26f, (0.5f + back) * 0.5f), new Vector3(1.2f, 0.52f, 0.5f - back));
		Box(new Vector3(-0.65f, 1.2f, (-0.3f + back) * 0.5f), new Vector3(0.3f, 2.0f, -0.3f - back));
		Box(new Vector3(0.65f, 1.2f, (-0.3f + back) * 0.5f), new Vector3(0.3f, 2.0f, -0.3f - back));
		Box(new Vector3(0, 2.5f, (-0.3f + back) * 0.5f), new Vector3(1.6f, 0.3f, -0.3f - back));
		Box(new Vector3(0, 1.2f, back - 0.1f), new Vector3(1.6f, 2.4f, 0.2f));
		// Apron (ramped if the ground in front is lower) and wing walls. A ramp's collider is a flat-ended
		// wedge whose top edge is the sill itself, so the surface runs on from the vestibule floor without a lip.
		if (_rampEndTop is { } rampEnd)
		{
			Vector3 sill = new(0, fy, 0.5f);   // where the vestibule floor box ends
			Vector3 mid = sill.Lerp(rampEnd, (BunkerExterior.ApronTo.Z - sill.Z) / (rampEnd.Z - sill.Z));
			body.AddChild(new CollisionShape3D { Shape = BunkerExterior.RampShape(sill, mid, 2.4f) });
			body.AddChild(new CollisionShape3D { Shape = BunkerExterior.RampShape(mid, rampEnd, 1.4f) });
		}
		else BeamBox(BunkerExterior.ApronFrom, BunkerExterior.ApronTo, 4.8f, 1.0f);
		foreach (float s in new[] { -1f, 1f })
		{
			Vector3 a = BunkerExterior.WingPoint(s, false), b = BunkerExterior.WingPoint(s, true), m = a.Lerp(b, 0.5f);
			BeamBox(a + Vector3.Up * BunkerExterior.WingMidA, m + Vector3.Up * BunkerExterior.WingMidA, 0.3f, BunkerExterior.WingTopA - BunkerExterior.Base);
			BeamBox(m + Vector3.Up * BunkerExterior.WingMidB, b + Vector3.Up * BunkerExterior.WingMidB, 0.3f, BunkerExterior.WingTopB - BunkerExterior.Base);
		}
		// The mound itself, walkable.
		var mound = new ConcavePolygonShape3D { BackfaceCollision = true };
		mound.SetFaces(moundTris);
		body.AddChild(new CollisionShape3D { Name = "Mound", Shape = mound });
		// The door: closed it fills the gap; open, the swung leaf is solid where it stands.
		_closedCollision = new CollisionShape3D { Position = new Vector3(0, 1.2f, 0.2f), Shape = new BoxShape3D { Size = new Vector3(1.2f, 2.0f, 0.2f) } };
		body.AddChild(_closedCollision);
		Vector3 hinge = new(-BunkerExterior.DoorRadius, BunkerExterior.DoorCenterY, BunkerExterior.DoorZ);
		Vector3 leafDir = Basis.FromEuler(new Vector3(0, Mathf.DegToRad(-100f), 0)) * Vector3.Right;
		_openCollision = new CollisionShape3D
		{
			Transform = new Transform3D(Basis.LookingAt(leafDir, Vector3.Up), hinge + leafDir * BunkerExterior.DoorRadius),
			Shape = new BoxShape3D { Size = new Vector3(0.2f, 2.0f, BunkerExterior.DoorRadius * 2f) },
			Disabled = true,
		};
		body.AddChild(_openCollision);

		// Coming within OpenRadius after the fire opens the door (a trigger, not a per-frame poll).
		_openTrigger = StoryBeat.MakeTrigger(_gen, new CylinderShape3D { Radius = OpenRadius, Height = 30f }, Vector3.Zero, OnApproach, "OpenTrigger");
		BuildApproachHush();
		// A proper doorway trigger: just inside the hatch, so only walking in through the door counts.
		_entryTrigger = StoryBeat.MakeTrigger(_gen, new BoxShape3D { Size = new Vector3(0.9f, 1.7f, 1.1f) }, new Vector3(0, fy + 0.95f, -1.05f), OnEntered, "EntryTrigger");
		// (2026-10-15, Dan: doors open onto the next area, never a black room) the vestibule's back opens onto the hall
		// it leads to (the round door's inside once the hall has become the rooms), its lamps and all; it was a black wall
		if (!Engine.IsEditorHint())
			_gen.AddChild(new DoorPortal
			{
				Name = "InsideView", Position = new Vector3(0, fy, back + 0.06f), Size = new Vector2(2.3f, 2.2f),
				OnWhen = () => IsOpen, TargetWhen = () => BunkerInterior.Instance?.EntranceView,
			});

		// The station card: laminated, bolted to the face right of the frame, readable while the hatch is still locked.
		PaperKit.Pinned(_gen, StationCardLocal + Vector3.Back * 0.012f, Vector3.Back, new Vector2(0.16f, 0.11f), PaperKit.Look.Card,
			"", StationCardText, Readable.NoteStyle.Printed, 1.5f, "Read the card", 7);

		_doorNode = BuildClosedDoor();
	}

	private static StandardMaterial3D _ivy;
	/// <summary>The ivy's leaves on the door's roots: a dark waxy green, both faces drawn.</summary>
	private static StandardMaterial3D IvyMat => _ivy ??= new StandardMaterial3D
	{
		ResourceName = "bk_door_ivy", AlbedoColor = new Color(0.11f, 0.17f, 0.07f), Roughness = 0.62f,
		CullMode = BaseMaterial3D.CullModeEnum.Disabled,
	};

	/// <summary>The steel plate and four bolts the station card sits on (built in the editor too; the card itself is runtime).</summary>
	private void BuildStationCardPlate()
	{
		var k = new MeshKit();
		k.Mat(ProcTextures.MetalMat);
		k.Color = new Color(0.34f, 0.33f, 0.3f);
		Vector3 c = StationCardLocal;
		k.Box(c + Vector3.Back * 0.006f, new Vector3(0.2f, 0.15f, 0.012f), 1f, Basis.FromEuler(new Vector3(0, 0, Mathf.DegToRad(1.5f))));
		k.Color = new Color(0.5f, 0.48f, 0.42f);
		foreach (float sx in new[] { -0.085f, 0.085f })
			foreach (float sy in new[] { -0.06f, 0.06f })
			{
				Vector3 p = c + new Vector3(sx, sy, 0.012f);
				k.Cylinder(p, p + Vector3.Back * 0.012f, 0.009f, 0.007f, 6);
			}
		k.CommitTo(_gen, "StationCardPlate");
	}

	private Node3D BuildClosedDoor()
	{
		var dk = new MeshKit();
		BunkerExterior.VaultDoor(dk, new Vector3(0, BunkerExterior.DoorCenterY, BunkerExterior.DoorZ), ProcTextures.MetalMat);
		var mesh = dk.CommitTo(_gen, "DoorMesh");
		// the keyhole's brass escutcheon on its face (Blender's: tools/Blender/rh_key.py)
		KeyPieceCache.AddModel(mesh, "Keyhole", new Transform3D(Basis.Identity, KeyholeLocal), "Keyhole");
		if (!Engine.IsEditorHint())
		{
			// The door: locked; from the fire on, R.H.'s key goes in its keyhole (UseKey).
			var hatch = new BunkerLockedHatch
			{
				Name = "Hatch", Prompt = "Locked", MaxDistance = 3f, PickRadius = 1.0f, Door = this,
				Position = new Vector3(0, BunkerExterior.DoorCenterY, BunkerExterior.DoorZ), PickOffset = new Vector3(0, 0, -0.4f),
			};
			mesh.AddChild(hatch);
		}
		return mesh;
	}

	private Node3D BuildOpenDoor()
	{
		// Swings open about its left edge (the hinge). Building the disc relative to that hinge and
		// letting Xf place and rotate it keeps the hinge edge itself fixed between the two states.
		var leaf = new MeshKit
		{
			Xf = new Transform3D(Basis.FromEuler(new Vector3(0, Mathf.DegToRad(-100f), 0)),
				new Vector3(-BunkerExterior.DoorRadius, 0f, BunkerExterior.DoorZ)),
		};
		BunkerExterior.VaultDoor(leaf, new Vector3(BunkerExterior.DoorRadius, BunkerExterior.DoorCenterY, 0f), ProcTextures.MetalMat);
		var mesh = leaf.CommitTo(_gen, "DoorOpenMesh");
		// the keyhole swung round with the leaf (its place on the disc, about the hinge)
		var k = KeyholeLocal;
		KeyPieceCache.AddModel(mesh, "Keyhole", leaf.Xf * new Transform3D(Basis.Identity, new Vector3(BunkerExterior.DoorRadius + k.X, k.Y, k.Z - BunkerExterior.DoorZ)), "Keyhole");
		return mesh;
	}

	private void OnEntered(PlayerController player)
	{
		if (!IsOpen) return;
		var story = StoryManager.Instance;
		// After the walkie the interior has nothing left and no way back out: the doorway stays dark.
		if (story is { Current: >= Checkpoint.Act10WalkieFound }) return;
		var interior = BunkerInterior.Instance;
		if (interior == null) return;
		// (2026-10-13) stepped in while an admission was still running: it used to wait for them to step out and back
		// in; now it tries again as soon as that's over, as long as they're still in the doorway
		if (interior.IsAdmitting) { _ = RetryEntry(player); return; }
		interior.AdmitPlayer(player);
		story?.ReachCheckpoint(Checkpoint.Act8BunkerEntered, ApproachPointWorld, ApproachYaw);
		GD.Print("[story] Act 8: into the bunker");
		// (2026-10-16) the third chapter's title is the approach's now (OnApproach); this only catches a way in that missed it
		UI.ChapterCard.Show(interior, 3, 2.5f);
	}

	/// <summary>Waits out a running admission, then enters again if the player is still standing in the doorway.</summary>
	private async System.Threading.Tasks.Task RetryEntry(PlayerController player)
	{
		if (_entryRetrying) return;
		_entryRetrying = true;
		try
		{
			while (IsInstanceValid(this) && IsInstanceValid(player) && BunkerInterior.Instance is { IsAdmitting: true })
				await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
			if (!IsInstanceValid(this) || !IsInstanceValid(player)) return;
			if (ReferenceEquals(StoryBeat.PlayerInside(_entryTrigger), player)) OnEntered(player);
		}
		finally { if (IsInstanceValid(this)) _entryRetrying = false; }
	}
}
