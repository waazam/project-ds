using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Act 7: Safety (2026-10-16, STORY.md Revision 5, the hybrid). The beat's logic; the cabin's own states are in
/// Cabin.Act7.cs.
/// <list type="number">
/// <item><b>The cabin ahead.</b> After the clearing's fall (<see cref="StoryManager.Flag.ClearingLoopDone"/>, behind the
/// black) the cabin is carried to its Act 7 place ahead on the trail past the clearing: window lit, door open, no rain,
/// the hearth lit, the axe over it, the sheet and the photo on the table; on the board, R.H.'s pictures gone and one print
/// left, of the stairs.</item>
/// <item><b>The cabin door.</b> Stepping in saves (<see cref="StoryManager.Flag.Act7CabinDoor"/>, within checkpoint 5).</item>
/// <item><b>The fire.</b> Looking at the print (or standing in the room a long while), it animates: its edges curl, the
/// stairs in it begin to burn, and it burns away; the fire jumps to the wall and spreads up it to the roof; smoke fills
/// the room from the ceiling down; the beams groan.</item>
/// <item><b>Trapped.</b> <see cref="CaveInAfter"/> seconds after the jump part of the roof caves in over the door: burning
/// beams across the doorway, no other way out.</item>
/// <item><b>The way out</b> (2026-10-24; CabinFireEvent.Escape.cs): a second beam cuts the room in two, the table shoved
/// back opens a way past it, the stove's fire round the axe put out with the bucket, a wet rag, the smoke layer stayed
/// under, the window smashed in on boards, then the beams chopped through in time with their flames dropping.</item>
/// <item><b>The axe.</b> Held E on the beams, three blows landed while the flames are down, each knocking one away; then
/// the doorway is clear.</item>
/// <item><b>Too slow.</b> <see cref="SecondsToEscape"/> from the cave-in the smoke closes the view to black: "Burned
/// Alive", and back to the cabin door (<see cref="PlayerDeath"/>).</item>
/// <item><b>Outside.</b> Out of the door the whole cabin goes up, sealed by the fire; turning to look back at it is
/// checkpoint 6 (<see cref="Checkpoint.Act7CabinBurning"/>; the giant and the falling trees follow it:
/// <see cref="Entities.GiantStalkerEvent"/>, <see cref="Act7TreeFalls"/>).</item>
/// </list>
/// Outside, burning, it keeps the old beat's touches: the crackle bed, a tall glow over the roof that lights the smoke and
/// the trees, the odd structural groan, and the screen's shadows warmed the closer the player stands.
///
/// Restore: from checkpoint 6 on the cabin stands at its Act 7 place burning (the story never shows it burning out); at
/// checkpoint 5 after the fall it stands there whole, the print waiting.
/// </summary>
public partial class CabinFireEvent : Node
{
	/// <summary>The death's line (STORY.md: Deaths).</summary>
	public const string DeathLine = "Burned Alive";

	[Export] public NodePath CabinPath = "..";
	/// <summary>Outside heat: the tint and the groans reach this far (horizontal metres) times <see cref="HeatReach"/>.</summary>
	[Export] public float Radius = 30f;
	[Export] public float HeatReach = 3f;
	[ExportGroup("The fire inside")]
	/// <summary>Looking at the print from this close (metres) for <see cref="LookSeconds"/> starts it.</summary>
	[Export] public float LookRange = 3.4f;
	[Export] public float LookSeconds = 0.8f;
	/// <summary>In the room this long without looking at it, it starts all the same.</summary>
	[Export] public float InsideFallbackSeconds = 35f;
	/// <summary>From the fire jumping to the wall to the roof caving in over the door.</summary>
	[Export] public float CaveInAfter = 13f;
	/// <summary>From the cave-in to the smoke closing the view (STORY.md: about forty-five seconds).</summary>
	[Export] public float SecondsToEscape = 120f;
	/// <summary>Each blow of the axe is a hold of E this long (short: it's timed to the flames dropping).</summary>
	[Export] public float BlowSeconds = 0.4f;
	/// <summary>Out of the door, looking back: or this long after, or this far away, it is checkpoint 6 anyway.</summary>
	[Export] public float LookBackFallbackSeconds = 12f;
	[Export] public float LookBackFallbackMetres = 22f;
	[ExportGroup("Glow seen from afar")]
	[Export] public Color GlowColor = new(1f, 0.45f, 0.16f);
	[Export] public float GlowEnergy = 9f;
	[Export] public float GlowRange = 60f;
	[Export] public float GlowHeight = 5.5f;

	public enum Stage { Before, Waiting, PrintBurning, Spreading, CavedIn, Cleared, Out, Seen }

	private Cabin _cabin;
	private PlayerController _player;
	private Stage _stage = Stage.Before;
	private double _clock;
	private readonly RandomNumberGenerator _rng = new();

	/// <summary>For tests: where the beat is.</summary>
	public Stage Now => _stage;
	/// <summary>For tests: the cabin burning outside (out of the door, or restored past checkpoint 6).</summary>
	public bool Burning { get; private set; }
	/// <summary>For tests: the fire has started inside (the print).</summary>
	public bool FireStarted => _stage >= Stage.PrintBurning;
	public bool CavedIn => _stage >= Stage.CavedIn;
	public int Blows { get; private set; }
	public bool BrokeOut => _stage >= Stage.Out;
	/// <summary>For tests: checkpoint 6, the burning cabin looked back at.</summary>
	public bool Seen => _stage >= Stage.Seen;
	/// <summary>For tests: seconds left before the smoke takes them (after the cave-in), else -1.</summary>
	public float SecondsLeft => _stage == Stage.CavedIn || _stage == Stage.Cleared ? Mathf.Max(0f, (float)(_deadline - _clock)) : -1f;
	/// <summary>For tests: the axe's E-point over the hearth (live after the cave-in) and the beams' (the doorway).</summary>
	public Interactable AxeUse => _axeUse;
	public Interactable BeamUse => _chop;
	/// <summary>For tests: the cabin the beat runs in.</summary>
	public Cabin Cabin => _cabin;

	public override void _Ready()
	{
		_cabin = GetNode<Cabin>(CabinPath);
		SetProcess(false);
		Callable.From(Restore).CallDeferred();
		if (StoryManager.Instance is { } s) { s.FlagSet += OnFlag; s.CheckpointReached += OnCheckpoint; }
	}

	public override void _ExitTree()
	{
		if (StoryManager.Instance is { } s) { s.FlagSet -= OnFlag; s.CheckpointReached -= OnCheckpoint; }
		// the post material is a shared resource: never leave the heat tint baked into it
		_postMat?.SetShaderParameter("shadow_tint", _postBaseTint);
		if (_caveAt >= 0) UI.StyleFx.SmokeEyes(0f);   // (an autoload: it outlives the level)
	}

	// ------------------------------------------------------------------ restore, and the cabin carried ahead

	private void Restore()
	{
		var s = StoryManager.Instance;
		if (s == null || !IsInstanceValid(_cabin)) return;
		if (s.Current >= Checkpoint.Act7CabinBurning)
		{
			_cabin.MoveToAct7Spot();
			_cabin.BuildFallenBeams(false);
			_cabin.ClearFallenBeams();
			IgniteOutside(instant: true);
			_stage = Stage.Seen;
			GD.Print($"[story] Act 7: the cabin restored burning (checkpoint {s.Current})");
		}
		else if (s.HasFlag(StoryManager.Flag.ClearingLoopDone)) Prepare();
	}

	private void OnFlag(string flag)
	{
		if (flag == StoryManager.Flag.ClearingLoopDone) Prepare();
	}

	/// <summary>Act 7 reached some other way (the bunker found without the cabin): it burns there all the same.</summary>
	private void OnCheckpoint(Checkpoint cp)
	{
		if (cp < Checkpoint.Act7CabinBurning || _stage >= Stage.Out || !IsInsideTree()) return;
		_cabin.MoveToAct7Spot();
		_cabin.BuildFallenBeams(false);
		_cabin.ClearFallenBeams();
		EndInside();
		IgniteOutside(instant: true);
		_stage = Stage.Seen;
		GD.Print("[story] Act 7: the cabin burning (Act 7 reached away from it)");
	}

	/// <summary>The cabin ahead on the trail, whole and warm, the print waiting on the board.</summary>
	private void Prepare()
	{
		if (_stage != Stage.Before) return;
		var s = StoryManager.Instance;
		if (s == null || s.Current >= Checkpoint.Act7CabinBurning) return;
		_cabin.MoveToAct7Spot();
		_cabin.SetOpen(true);
		_cabin.InteriorLightOn = true;
		_cabin.SetHearthLit(true);
		_cabin.Board?.ShowOnlyStairs();
		_stage = Stage.Waiting;
		_player = StoryBeat.Player(this);
		SetProcess(true);
		GD.Print("[story] Act 7: the cabin ahead, lit, its door open; one print left on the board, the stairs");
	}

	// ------------------------------------------------------------------ the frame

	private double _lookFor, _insideFor, _deadline = -1, _outAt = -1;
	private bool _savedDoor;

	/// <summary>Where the player is in the cabin's frame (the floor at y = 0), and whether that's in the room.</summary>
	private bool Inside(out Vector3 local)
	{
		local = Vector3.Zero;
		if (_player == null || !IsInstanceValid(_player)) return false;
		local = _cabin.ToLocal(_player.GlobalPosition);
		var room = _cabin.InteriorBounds;
		return local.X > room.Position.X - 0.1f && local.X < room.End.X + 0.1f && local.Z > room.Position.Z - 0.1f && local.Z < room.End.Z + 0.05f
			&& local.Y > -0.6f && local.Y < 2.5f;
	}

	public override void _Process(double delta)
	{
		_clock += delta;
		if (_player == null || !IsInstanceValid(_player)) _player = StoryBeat.Player(this);
		if (_player == null) return;
		bool inside = Inside(out var local);
		switch (_stage)
		{
			case Stage.Waiting:
				if (!inside) { _lookFor = 0; break; }
				SaveAtDoor();
				_insideFor += delta;
				_lookFor = LooksAtPrint() ? _lookFor + delta : 0;
				if (_lookFor >= LookSeconds || _insideFor >= InsideFallbackSeconds)
				{
					_stage = Stage.PrintBurning;
					GD.Print($"[story] Act 7: the print {(_lookFor >= LookSeconds ? "looked at" : "waited for")}: it animates");
					_ = Cutscene.Run(this, Fire);
				}
				break;
			case Stage.PrintBurning:
			case Stage.Spreading:
			{
				// (no softlock, no way round it) out at the door before the roof came down: they got out ahead of it; making
				// for the door once the wall's alight: it comes down in front of them
				float hd = _cabin.Depth * 0.5f;
				if (local.Z > hd - 0.9f && Mathf.Abs(local.X) < _cabin.DoorWidth) BreakOut();
				else if (_stage == Stage.Spreading && inside && local.Z > hd - 1.8f) CaveIn();
				break;
			}
			case Stage.CavedIn:
			case Stage.Cleared:
				if (_stage == Stage.Cleared && local.Z > _cabin.Depth * 0.5f + 0.5f) { BreakOut(); break; }
				UpdateEscape((float)delta);
				if (_clock >= _deadline && !PlayerDeath.Dying) { _ = Cutscene.Run(this, Die, lockInput: true); _deadline = double.MaxValue; }
				break;
			case Stage.Out:
				// clear of the doorway: it's sealed behind them
				if (!_cabin.Sealed && local.Z > _cabin.Depth * 0.5f + 1.6f) _cabin.SealByFire();
				float away = new Vector2(_player.GlobalPosition.X - _cabin.GlobalPosition.X, _player.GlobalPosition.Z - _cabin.GlobalPosition.Z).Length();
				if ((away > 4.5f && CabinInView()) || _clock - _outAt > LookBackFallbackSeconds || away > LookBackFallbackMetres) LookedBack();
				break;
		}
		if (_stage >= Stage.Spreading && _stage < Stage.Out) UpdateSmoke();
		if (Burning) TickOutside(delta);
	}

	/// <summary>The cabin door: stepping in saves (once).</summary>
	private void SaveAtDoor()
	{
		if (_savedDoor) return;
		_savedDoor = true;
		var s = StoryManager.Instance;
		if (s != null && !s.HasFlag(StoryManager.Flag.Act7CabinDoor) && s.Current < Checkpoint.Act7CabinBurning)
		{
			s.SetFlag(StoryManager.Flag.Act7CabinDoor);
			GD.Print("[story] Act 7: in at the cabin door (saved)");
		}
	}

	private bool LooksAtPrint()
	{
		var print = _cabin.Board?.StairsPrint;
		if (print == null || !IsInstanceValid(print)) return false;
		return Act1TreeFalls.InSight(_player, print.GlobalPosition, LookRange, 20f, _cabin);
	}

	/// <summary>The cabin in front of the camera (within ~50 degrees), from outside.</summary>
	private bool CabinInView()
	{
		var cam = _player?.CameraRig?.Camera;
		if (cam == null) return false;
		Vector3 to = _cabin.GlobalPosition + Vector3.Up * 2f - cam.GlobalPosition;
		float d = to.Length();
		return d < 160f && (-cam.GlobalBasis.Z).Dot(to / Mathf.Max(d, 0.01f)) > 0.64f;
	}

	// ------------------------------------------------------------------ the fire inside

	private readonly List<FireVfx> _fires = new();
	private readonly Dictionary<Node3D, FireVfx> _beamFires = new();
	private Node3D _inside;
	private AudioStreamPlayer3D _crackle;
	private double _fireAt = -1, _caveAt = -1;

	private FireVfx AddFire(Vector3 local, Vector3 extent, float flames, float smoke, bool light, float range, float energy, int seed, Node3D parent = null)
	{
		var f = new FireVfx
		{
			Name = $"Fire{seed}", Extent = extent, FlameScale = flames, SmokeAmount = smoke, Dark = false, EmitLight = light,
			LightRange = range, LightEnergy = energy, LightShadows = false, Embers = flames >= 0.6f, Seed = 700 + seed, Position = local, Haze = false,
		};
		(parent ?? _inside).AddChild(f);
		f.Intensity = 0f;
		_fires.Add(f);
		return f;
	}

	private void Ramp(FireVfx f, float to, float seconds)
	{
		if (f == null || !IsInstanceValid(f)) return;
		f.CreateTween().TweenProperty(f, "Intensity", to, seconds).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
	}

	private void Sound(string name, int variants, Vector3 local, float db, float unit = 4f, float pitchJitter = 0.06f)
	{
		string path = Audio.AudioDirector.Take(name, variants);
		if (!ResourceLoader.Exists(path)) path = $"res://assets/audio/sfx/{name}.wav";
		StoryBeat.PlayAt(_cabin, path, "Events", local, volumeDb: db, unitSize: unit, maxDistance: 40f, pitch: 1f + _rng.RandfRange(-pitchJitter, pitchJitter));
	}

	/// <summary>The print, the wall, the roof; the cave-in; then the clock to get out by.</summary>
	private async Task Fire(CancellationToken ct)
	{
		var print = _cabin.Board?.StairsPrint;
		Vector3 printLocal = print != null ? _cabin.ToLocal(print.GlobalPosition) : new Vector3(-0.85f, 1.5f, -2.4f);
		ShaderMaterial mat = null;
		if (print != null && IsInstanceValid(print))
		{
			if (print.Mesh is QuadMesh q) { q = (QuadMesh)q.Duplicate(); q.SubdivideWidth = 14; q.SubdivideDepth = 14; print.Mesh = q; }
			mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/print_burn.gdshader") };
			mat.SetShaderParameter("tex", PhotoBoard.StairsPrintTexture);
			print.MaterialOverride = mat;
		}
		void Param(string p, float v) { if (mat != null) mat.SetShaderParameter(p, v); }
		// it animates: the edges curl, the stairs in it begin to burn
		Sound("paper_rustle", 3, printLocal, -10f, 2f);
		await Grow(v => { Param("curl", v); Param("stairs", Mathf.Clamp(v * 1.2f - 0.2f, 0f, 1f)); }, 3.0f, ct);
		Sound("web_burn", 1, printLocal, -6f, 2.5f);
		// then it burns away from its edges in, and the fire is on the wall
		_inside = new Node3D { Name = "Act7Fire" };
		_cabin.AddChild(_inside);
		float wallZ = _cabin.InteriorBounds.Position.Z;
		var f0 = AddFire(new Vector3(printLocal.X, printLocal.Y - 0.45f, wallZ + 0.12f), new Vector3(0.5f, 0.7f, 0.2f), 0.7f, 0.35f, true, 6f, 2.2f, 0);
		_ = Grow(v => Param("burn", v), 2.6f, ct);
		await Cutscene.Wait(this, 1.2, ct);
		if (Gone()) return;
		_stage = Stage.Spreading;
		_fireAt = _clock;
		Sound("flame_ignite", 2, printLocal, -4f, 3f);
		Ramp(f0, 0.8f, 2.5f);
		StartCrackle();
		await Cutscene.Wait(this, 1.6, ct);
		if (Gone()) return;
		if (print != null && IsInstanceValid(print)) print.Visible = false;
		// along the back wall and up it to the roof
		var f1 = AddFire(new Vector3(0.55f, 0.75f, wallZ + 0.15f), new Vector3(1.1f, 1.3f, 0.3f), 0.85f, 0.5f, false, 0f, 0f, 1);
		Ramp(f1, 0.85f, 4f);
		await Cutscene.Wait(this, 2.5, ct);
		if (Gone()) return;
		var f2 = AddFire(new Vector3(-0.2f, 1.7f, wallZ + 0.35f), new Vector3(2.6f, 1.0f, 0.6f), 0.9f, 0.7f, true, 7f, 2.6f, 2);
		Ramp(f2, 0.9f, 4f);
		Groan();
		await Cutscene.Wait(this, 3.0, ct);
		if (Gone()) return;
		var f3 = AddFire(new Vector3(0f, _cabin.WallHeight + 0.2f, -0.9f), new Vector3(2.8f, 0.7f, 1.8f), 0.8f, 0.9f, false, 0f, 0f, 3);
		Ramp(f3, 0.8f, 5f);
		Groan();
		while (_stage == Stage.Spreading && _clock - _fireAt < CaveInAfter) { await Cutscene.Frame(this, ct); if (_clock >= _nextInsideGroan) Groan(); }
		if (_stage == Stage.Spreading) CaveIn();
		while (_stage is Stage.CavedIn or Stage.Cleared)
		{
			if (_clock >= _nextInsideGroan) Groan();
			await Cutscene.Frame(this, ct);
		}
	}

	/// <summary>The beat has moved past the fire inside (they got out ahead of it, or Act 7 was reached some other way).</summary>
	private bool Gone() => _stage >= Stage.Out || !IsInstanceValid(_cabin);

	private async Task Grow(System.Action<float> set, float seconds, CancellationToken ct)
	{
		double t = 0;
		while (t < seconds)
		{
			t += GetProcessDeltaTime();
			float u = Mathf.Clamp((float)(t / seconds), 0f, 1f);
			set(u * u * (3f - 2f * u));
			await Cutscene.Frame(this, ct);
		}
		set(1f);
	}

	private double _nextInsideGroan;

	/// <summary>A roof beam groaning in the heat, somewhere overhead (never the same take twice running).</summary>
	private void Groan()
	{
		_nextInsideGroan = _clock + _rng.RandfRange(3.5f, 7.5f);
		Sound("fire_timber_groan", 10, new Vector3(_rng.RandfRange(-1.4f, 1.4f), _cabin.WallHeight + 0.6f, _rng.RandfRange(-1.8f, 1.8f)), _rng.RandfRange(-2f, 3f), 5f, 0.08f);
	}

	private void StartCrackle()
	{
		if (_crackle != null) return;
		_crackle = new AudioStreamPlayer3D { Name = "FireCrackle", Bus = "Events", UnitSize = 7f, MaxDistance = 55f, Position = new Vector3(0, 1.4f, -0.8f), VolumeDb = -40f };
		_cabin.AddChild(_crackle);
		_crackle.AddChild(new Audio.AmbienceLoop { StreamPath = "res://assets/audio/ambient/fire_crackle_loop.wav", BaseVolumeDb = 3f });
		StoryBeat.FadeIn(_crackle, 0f, 3f);
	}

	/// <summary>Part of the roof comes down over the door: the beams across the doorway, burning. The axe can be taken.</summary>
	private void CaveIn()
	{
		if (_stage >= Stage.CavedIn) return;
		_inside ??= new Node3D { Name = "Act7Fire" };
		if (_inside.GetParent() == null) _cabin.AddChild(_inside);
		_stage = Stage.CavedIn;
		_caveAt = _clock;
		_deadline = _clock + SecondsToEscape;
		var door = new Vector3(0, 1.4f, _cabin.Depth * 0.5f - 0.3f);
		Sound("wall_burst", 3, door, 4f, 6f);
		Sound("log_wall_shudder", 4, door + Vector3.Up * 0.8f, 0f, 6f);
		_ = Cutscene.Run(this, async ct => { await Cutscene.Wait(this, 0.7, ct); Sound("dust_trickle", 3, door, -4f, 4f); });
		_cabin.BuildFallenBeams(drop: true);
		_cabin.InteriorLightOn = false;   // the lamp knocked out: the fire is the light now
		var over = AddFire(new Vector3(0, _cabin.WallHeight - 0.1f, _cabin.Depth * 0.5f - 0.45f), new Vector3(1.6f, 0.8f, 0.6f), 0.9f, 0.8f, true, 6f, 2.4f, 4);
		Ramp(over, 0.9f, 1.5f);
		int i = 0;
		foreach (var b in _cabin.Beams)
		{
			var box = BeamBox(b);
			var f = AddFire(box.GetCenter(),new Vector3(Mathf.Min(box.Size.X, 1.4f), 0.5f, 0.35f), 0.55f, 0.25f, false, 0f, 0f, 10 + i++, b);
			_beamFires[b] = f;
			Ramp(f, 0.75f, 1.2f);
		}
		// the way out (the axe over the hearth is behind the stove's fire now: CabinFireEvent.Escape.cs)
		BuildEscape();
		_chop = new BeamChop { Name = "ChopBeams", Owner_ = this, MaxDistance = 2.6f, PickRadius = 0.6f, Position = new Vector3(0, 1.0f, _cabin.Depth * 0.5f - 0.8f), HighlightRoot = new NodePath("NoHighlight") };
		_cabin.AddChild(_chop);
		GD.Print($"[story] Act 7: the roof caves in over the door ({SecondsToEscape:0} s to get out)");
	}

	/// <summary>A beam's bounds in its own pivot's space (where it lies, at rest).</summary>
	private static Aabb BeamBox(Node3D beam)
	{
		Aabb? box = null;
		foreach (var n in beam.FindChildren("*", nameof(MeshInstance3D), true, false))
			if (n is MeshInstance3D mi) { var b = mi.Transform * mi.GetAabb(); box = box?.Merge(b) ?? b; }
		return box ?? new Aabb(new Vector3(-0.5f, 0.6f, -0.3f), new Vector3(1f, 0.4f, 0.2f));
	}

	private PickupInteractable _axeUse;
	private BeamChop _chop;

	private void TakeAxe(PlayerController p)
	{
		if (_axeUse == null) return;
		if (_cabin.HearthAxe != null) _cabin.HearthAxe.Visible = false;
		p.Inventory?.TryPickup(ToolKind.Axe);
		Audio.AudioDirector.OneShot(this, "item_take", 3, _cabin.HearthAxe?.GlobalPosition, -6f);
		_axeUse.QueueFree();
		_axeUse = null;
		GD.Print("[story] Act 7: the axe taken from over the hearth");
	}

	/// <summary>The blade in the wood: the thud, the chips, the beam knocked away; the third clears the doorway.</summary>
	private void Bite(PlayerController p)
	{
		var at = _cabin.ToLocal(_cabin.DoorwayInside);
		Chips(_cabin.DoorwayInside + Vector3.Up * 0.3f);
		Sound(Blows % 2 == 1 ? "door_frame_thud" : "wood_bump", Blows % 2 == 1 ? 4 : 3, at, 2f, 4f, 0.08f);
		Sound("dust_trickle", 3, at + Vector3.Up * 0.6f, -8f, 3f);
		Node3D gone = null;
		foreach (var b in _cabin.Beams) if (IsInstanceValid(b) && !b.HasMeta("chopped")) { gone = b; break; }
		bool clear = _cabin.ChopBeam();
		if (gone != null && _beamFires.TryGetValue(gone, out var f)) Ramp(f, 0f, 0.6f);
		GD.Print($"[story] Act 7: blow {Blows} through the beams{(clear ? ": the doorway is clear" : "")}");
		if (!clear) return;
		_stage = Stage.Cleared;
		p.Inventory?.Consume(ToolKind.Axe);
		if (_chop != null && IsInstanceValid(_chop)) _chop.QueueFree();
		_chop = null;
	}

	/// <summary>Chips of charred wood knocked off the beam by a blow, and a puff of ash.</summary>
	private void Chips(Vector3 world)
	{
		var pm = new ParticleProcessMaterial
		{
			Direction = Vector3.Up, Spread = 75f, InitialVelocityMin = 1.5f, InitialVelocityMax = 4.5f, Gravity = new Vector3(0, -9.8f, 0),
			AngularVelocityMin = -720f, AngularVelocityMax = 720f, ScaleMin = 0.5f, ScaleMax = 1.4f,
		};
		var p = new GpuParticles3D
		{
			Name = "Chips", Amount = 26, Lifetime = 0.9, OneShot = true, Explosiveness = 0.95f, ProcessMaterial = pm,
			DrawPass1 = new BoxMesh { Size = new Vector3(0.04f, 0.012f, 0.025f), Material = new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.13f, 0.08f), Roughness = 0.95f } },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, LocalCoords = false,
		};
		AddChild(p);
		p.GlobalPosition = world;
		p.Emitting = true;
		GetTree().CreateTimer(1.6).Timeout += () => { if (IsInstanceValid(p)) p.QueueFree(); };
	}

	/// <summary>The doorway's E-point: the beams. With the axe, held E is a blow; without, it only says what's there.</summary>
	private sealed partial class BeamChop : Interactable
	{
		public CabinFireEvent Owner_;
		public override string GetPrompt(PlayerController player)
		{
			bool axe = player?.Inventory?.HasTool(ToolKind.Axe) == true && Owner_.BeamsChoppable;
			HoldSeconds = axe ? Owner_.BlowSeconds : 0f;
			if (!axe) return "Burning beams across the door.";
			if (HoldProgress > 0f) return "Swing...";
			return Owner_.FlamesLow ? "Chop now - the flames are down" : "Wait for the flames to drop";
		}
		public override void Interact(PlayerController player)
		{
			if (player?.Inventory?.HasTool(ToolKind.Axe) == true && Owner_.BeamsChoppable) Owner_.Blow(player);
			base.Interact(player);
		}
	}

	// ------------------------------------------------------------------ the smoke

	private readonly List<(MeshInstance3D sheet, ShaderMaterial mat)> _smoke = new();
	private const int SmokeSheets = 9;
	private float _fade;

	private void UpdateSmoke()
	{
		if (_smoke.Count == 0)
		{
			var room = _cabin.InteriorBounds;
			for (int i = 0; i < SmokeSheets; i++)
			{
				var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/room_smoke.gdshader") };
				m.SetShaderParameter("seed", i * 3.1f);
				var mi = new MeshInstance3D
				{
					Name = $"Smoke{i}", Mesh = new QuadMesh { Size = new Vector2(room.Size.X, room.Size.Z) }, MaterialOverride = m,
					Rotation = new Vector3(-Mathf.Pi * 0.5f, 0, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				};
				_inside.AddChild(mi);
				_smoke.Add((mi, m));
			}
		}
		float top = _cabin.WallHeight + 1.25f;
		// down from the roof to above the head by the cave-in, then down to the floor by the time they run out
		// (2026-10-24) after the cave-in it settles as a layer at head height (crouch under it), and only in the last quarter
		// of the time comes on down to the floor
		float since = (float)(_clock - _caveAt), last = Mathf.Clamp((since / SecondsToEscape - 0.75f) / 0.25f, 0f, 1f);
		float level = _caveAt < 0
			? Mathf.Lerp(top, 1.75f, Mathf.Clamp((float)((_clock - _fireAt) / CaveInAfter), 0f, 1f))
			: Mathf.Lerp(Mathf.Lerp(1.75f, SmokeLayer, Mathf.Clamp(since / 6f, 0f, 1f)), 0.3f, last);
		SmokeLevelLocal = level;
		float thick = _caveAt < 0 ? Mathf.Clamp((float)((_clock - _fireAt) / CaveInAfter), 0.2f, 1f) : 1f + Mathf.Clamp((float)((_clock - _caveAt) / SecondsToEscape), 0f, 1f);
		for (int i = 0; i < _smoke.Count; i++)
		{
			float u = i / (float)(SmokeSheets - 1);
			var (sheet, m) = _smoke[i];
			sheet.Position = new Vector3(_cabin.InteriorBounds.GetCenter().X, Mathf.Lerp(level, top, u), _cabin.InteriorBounds.GetCenter().Z);
			m.SetShaderParameter("density", (0.06f + 0.16f * u) * thick);
			m.SetShaderParameter("warm", 0.25f + 0.25f * (1f - u));
		}
		// the view closing in: the smoke at the eyes darkens it over the last of the time (soft, never a flash)
		if (_caveAt >= 0 && StoryBeat.Fader(this) is { } fader && !PlayerDeath.Dying)
		{
			float u = Mathf.Clamp((float)((_clock - _caveAt) / SecondsToEscape), 0f, 1f);
			float want = 0.08f * u + 0.6f * Mathf.Pow(Mathf.Clamp((u - 0.75f) / 0.25f, 0f, 1f), 1.6f);
			_fade = Mathf.MoveToward(_fade, want, (float)GetProcessDeltaTime() * 0.4f);
			fader.BlackAlpha = _fade;
		}
	}

	/// <summary>Too slow: the smoke closes the view to black, "Burned Alive", and back to the cabin door.</summary>
	private async Task Die(CancellationToken ct)
	{
		GD.Print("[story] Act 7: the smoke took them");
		PlayerDeath.Begin();
		if (StoryBeat.Fader(this) is { } fader) await fader.Fade(1f, 1.6f, ct);
		await PlayerDeath.Reload(this, DeathLine, ct);
	}

	/// <summary>The fire inside comes down to the outside fire's own (the cabin's SetBurning), the smoke goes.</summary>
	private void EndInside()
	{
		foreach (var (sheet, _) in _smoke) if (IsInstanceValid(sheet)) sheet.QueueFree();
		_smoke.Clear();
		foreach (var f in _fires) if (IsInstanceValid(f)) Ramp(f, 0f, 2.5f);
		if (_inside != null && IsInstanceValid(_inside))
		{
			var gone = _inside;
			GetTree().CreateTimer(3.0).Timeout += () => { if (IsInstanceValid(gone)) gone.QueueFree(); };
		}
		_inside = null;
		_fires.Clear();
		if (_axeUse != null && IsInstanceValid(_axeUse)) _axeUse.QueueFree();
		if (_chop != null && IsInstanceValid(_chop)) _chop.QueueFree();
		_axeUse = null; _chop = null;
		EndEscape();
	}

	/// <summary>Out through the door: the clock stops, the view clears, and the whole cabin goes up behind them.</summary>
	private void BreakOut()
	{
		_stage = Stage.Out;
		_outAt = _clock;
		_deadline = -1;
		if (StoryBeat.Fader(this) is { } fader) _ = fader.Fade(0f, 1.5f);
		_fade = 0f;
		EndInside();
		IgniteOutside(instant: false);
		GD.Print("[story] Act 7: out of the burning cabin");
	}

	/// <summary>Checkpoint 6: they've turned to look back at it burning.</summary>
	private void LookedBack()
	{
		if (_stage >= Stage.Seen) return;
		_stage = Stage.Seen;
		_cabin.SealByFire();
		if (StoryManager.Instance is { Current: < Checkpoint.Act7CabinBurning }) StoryBeat.ReachCheckpoint(_player, Checkpoint.Act7CabinBurning);
		GD.Print("[story] Act 7: they look back at the cabin burning (checkpoint 6)");
	}

	// ------------------------------------------------------------------ outside: the cabin burning

	private OmniLight3D _glow;
	private ShaderMaterial _postMat;
	private Color _postBaseTint;
	private double _nextGroan, _ignitedAt;

	private void IgniteOutside(bool instant)
	{
		if (Burning) return;
		Burning = true;
		_player ??= StoryBeat.Player(this);
		_cabin.SetHearthLit(false);
		if (instant) { _cabin.SetBurning(1f); _cabin.SealByFire(); }
		else
		{
			_cabin.SetBurning(0.3f);
			var ramp = CreateTween();
			ramp.TweenMethod(Callable.From<float>(v => _cabin.SetBurning(v)), 0.3f, 1f, 4f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
		}
		StartCrackle();
		// a big warm glow over the roof: lights the smoke column and the trees round the cabin, so the fire reads as a fire
		// from the lookout
		_glow = new OmniLight3D
		{
			Name = "FireGlow", LightColor = GlowColor, LightEnergy = instant ? GlowEnergy : 0f, OmniRange = GlowRange, OmniAttenuation = 0.9f,
			ShadowEnabled = false, Position = new Vector3(0, GlowHeight, 0),
		};
		_cabin.AddChild(_glow);
		_ignitedAt = _clock;
		_postMat = StoryBeat.PostMaterial(this);
		if (_postMat != null) _postBaseTint = (Color)_postMat.GetShaderParameter("shadow_tint");
		_nextGroan = _clock + _rng.RandfRange(2f, 6f);
		SetProcess(true);
	}

	private void TickOutside(double delta)
	{
		float d = -1f;
		if (_player != null && IsInstanceValid(_player))
			d = new Vector2(_player.GlobalPosition.X - _cabin.GlobalPosition.X, _player.GlobalPosition.Z - _cabin.GlobalPosition.Z).Length();
		if (d > Radius * HeatReach + 1f)
		{
			_postMat?.SetShaderParameter("shadow_tint", _postBaseTint);
			return;
		}
		float catching = Mathf.Clamp((float)(_clock - _ignitedAt) / 3f, 0f, 1f);
		if (_glow != null && IsInstanceValid(_glow))
			_glow.LightEnergy = GlowEnergy * catching * (0.85f + 0.1f * Mathf.Sin((float)_clock * 2.3f) + 0.05f * Mathf.Sin((float)_clock * 9.1f));
		if (_clock >= _nextGroan)
		{
			_nextGroan = _clock + _rng.RandfRange(5.0f, 11.0f);
			string path = Audio.AudioDirector.Take("fire_timber_groan", 10);
			StoryBeat.PlayAt(_cabin, path, "Events", new Vector3(_rng.RandfRange(-1.5f, 1.5f), 1.2f, _rng.RandfRange(-1.5f, 1.5f)),
				volumeDb: _rng.RandfRange(0f, 4f), unitSize: 6f, maxDistance: 45f, pitch: _rng.RandfRange(0.88f, 1.05f));
		}
		if (_postMat != null && d >= 0f)
		{
			float near = 1f - Mathf.Clamp((d - Radius * 0.5f) / (Radius * 2.5f), 0f, 1f);
			_postMat.SetShaderParameter("shadow_tint", _postBaseTint.Lerp(new Color(0.16f, 0.03f, 0.0f), near * 0.8f));
		}
	}
}
