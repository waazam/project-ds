using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Entities;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World.SnowMaze;

/// <summary>
/// Act 25 in the trailhead level (the owner: "there is a gradiant right in front of them where the snow turns back into
/// the forest from the first acts of the game. The player will see a trail and follow it back past their cabin and to
/// their vehicle"). Out of the hole in the snow at the trail's far end: the snow thinning away into the autumn woods; the
/// trail back, an old cabin beside it (theirs, if they'd stayed); the lot, and the car with its trunk open. Then the
/// ending they earned (<see cref="Act25Ending.Choose"/>):
/// <list type="number">
/// <item>The camera back in the trunk. Black. The credits.</item>
/// <item>The camera back in the trunk. Black, held five seconds; far off, it howls. The credits.</item>
/// <item>Nearly at the car, it comes down on it from the trees and crushes it flat; burning, it shambles at them; they
/// turn from it, and the stalker is there, lunging. Black.</item>
/// <item>The fog closes in as they near the car, and where it should be: the stairs from the first act. Blood runs down
/// them, step by step to the bottom; and they go up, slowly, then faster and faster, nearly at the top. Black.</item>
/// <item>The fog closes in; but the car really is there. The camera in the trunk; black, five seconds; then behind the
/// wheel, the engine catching, and away.</item>
/// </list>
/// Exists only in the trailhead level once the story's there (Act24Finished, or after the end).
/// </summary>
public partial class EndingDirector : Node3D
{
	public static EndingDirector Instance { get; private set; }
	public int Ending { get; private set; }
	public bool Reached { get; private set; }
	public bool Done { get; private set; }
	/// <summary>For tests: where the walk starts, and the car.</summary>
	public Vector3 Start { get; private set; }
	public Vector3 CarAt { get; private set; }
	public PickupInteractable TrunkUse { get; private set; }

	private Node3D _car, _opening, _stairs, _trunkCam;
	private ForestTerrain _terrain;
	private float _startS;
	private bool _running;

	public override void _EnterTree() => Instance = this;
	public override void _ExitTree() { if (Instance == this) Instance = null; }

	public override void _Ready()
	{
		_terrain = GetTree().GetFirstNodeInGroup("terrain") as ForestTerrain;
		_opening = GetTree().GetFirstNodeInGroup("opening") as Node3D;
		_car = _opening?.GetParent() as Node3D;
		_trunkCam = _opening?.GetNodeOrNull<Node3D>("TrunkCamera");
		_stairs = GetTree().CurrentScene?.FindChild("Stairs", true, false) as Node3D;
		Ending = Act25Ending.Chosen(StoryManager.Instance);
		CarAt = _car?.GlobalPosition ?? Vector3.Zero;
		// the walk starts a good way back up the trail (the trail's last third), out of a hole in a snowbank
		float len = _terrain?.TrailLength ?? 400f;
		_startS = len * 0.62f;
		if (_terrain != null)
		{
			var p = _terrain.TrailPoint(_startS, out var tan);
			Start = p with { Y = _terrain.HeightAt(p.X, p.Z) };
			BuildSnowEdge(Start, tan);
		}
		if (_stairs != null) _stairs.Visible = Ending == 4 && false;   // (the stairs aren't there: only in the fourth, at the car)
		if (_trunkCam != null) _trunkCam.Visible = false;
		BuildCabin();
		BuildTrunkUse();
		GD.Print($"[story] Act 25: the trailhead - ending {Ending}, walking from {_startS:0} m up the trail");
	}

	/// <summary>Where the story puts the player here: at the snow's edge (Act24Finished), or by the car after the end.</summary>
	public (Vector3 pos, float yaw) SpawnFor(Checkpoint cp)
	{
		if (cp == Checkpoint.GameFinished && _car != null)
		{
			var stand = _opening?.GetNodeOrNull<Node3D>("Stand") ?? _car;
			var f = -stand.GlobalBasis.Z;
			return (stand.GlobalPosition + Vector3.Up * 0.1f, Mathf.Atan2(-f.X, -f.Z));
		}
		if (_terrain == null) return (Start + Vector3.Up * 0.2f, 0f);
		var ahead = _terrain.TrailPoint(_startS - 6f, out _);
		var d = ahead - Start;
		return (Start + Vector3.Up * 0.2f, Mathf.Atan2(-d.X, -d.Z));
	}

	// ------------------------------------------------------------------ the snow's edge, the cabin

	/// <summary>Behind them the snowbank and its hole, the cave's light in it; round them, snow thinning to nothing over
	/// twenty metres into the autumn leaves.</summary>
	private void BuildSnowEdge(Vector3 at, Vector3 tan)
	{
		var k = new MeshKit();
		k.Mat(WinterWoods.SoftSnow);
		k.Color = Colors.White;
		var back = tan.Normalized();   // (behind them: up the trail, the way they came; the walk down goes the other way)
		var side = new Vector3(back.Z, 0, -back.X);
		var bank = at + back * 3.5f;
		k.Blob(bank + Vector3.Up * 1.2f, new Vector3(4.5f, 2.6f, 2.4f), 2501, 0.3f, true, 1f);
		k.Blob(bank + side * 3.4f + Vector3.Up * 0.7f, new Vector3(2.4f, 1.5f, 2f), 2502, 0.3f, true, 1f);
		k.Blob(bank - side * 3.2f + Vector3.Up * 0.8f, new Vector3(2.6f, 1.6f, 2.2f), 2503, 0.3f, true, 1f);
		// the snow on the ground, in drifts that thin and break up into patches as they go
		var rng = new RandomNumberGenerator { Seed = 2504 };
		for (int i = 0; i < 46; i++)
		{
			float r = Mathf.Pow(rng.Randf(), 0.8f) * 18f;
			float a = rng.Randf() * Mathf.Tau;
			var p = at + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r;
			if (_terrain != null) p.Y = _terrain.HeightAt(p.X, p.Z);
			float s = Mathf.Lerp(2.2f, 0.5f, r / 18f) * rng.RandfRange(0.6f, 1.2f);
			k.Blob(p, new Vector3(s, 0.06f + 0.1f * (1f - r / 18f), s * rng.RandfRange(0.6f, 1f)), 2510 + i, 0.4f, true, 1f);
		}
		k.CommitTo(this, "SnowEdge", false);
		var hole = new MeshKit();
		hole.Mat(new StandardMaterial3D { ResourceName = "cave_mouth", AlbedoColor = new Color(0.2f, 0.32f, 0.45f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded });
		hole.Color = Colors.White;
		hole.Blob(bank + Vector3.Up * 0.9f - back * 2.2f, new Vector3(1.1f, 1.0f, 0.2f), 2505, 0.2f, false, 1f);
		hole.CommitTo(this, "CaveMouth", false);
		var body = new StaticBody3D { Name = "BankBody", CollisionLayer = 1, CollisionMask = 0 };
		body.AddChild(new CollisionShape3D { Position = bank + Vector3.Up * 1.2f, Shape = new BoxShape3D { Size = new Vector3(8f, 2.4f, 3f) }, Basis = Basis.LookingAt(back, Vector3.Up) });
		AddChild(body);
	}

	/// <summary>An old cabin beside the trail on the way down, its door hanging open (theirs, if they'd stayed).</summary>
	private void BuildCabin()
	{
		if (_terrain == null) return;
		float s = _startS * 0.45f;
		var p = _terrain.TrailPoint(s, out var tan);
		var side = new Vector3(tan.Z, 0, -tan.X).Normalized();
		var at = p + side * 9f;
		at.Y = _terrain.HeightAt(at.X, at.Z);
		var cabin = new Node3D { Name = "EndingCabin" };
		AddChild(cabin);
		cabin.GlobalPosition = at;
		cabin.GlobalBasis = Basis.LookingAt(-side, Vector3.Up);
		var k = new MeshKit();
		k.Mat(BuildingTextures.LogMat);
		k.Color = Colors.White;
		// log walls, a door, a pitched roof sagging, a chimney
		for (int i = 0; i < 9; i++)
		{
			float y = 0.15f + i * 0.28f;
			k.Cylinder(new Vector3(-2.6f, y, -2f), new Vector3(2.6f, y, -2f), 0.15f, 0.15f, 7, true);
			k.Cylinder(new Vector3(-2.6f, y, 2f), new Vector3(2.6f, y, 2f), 0.15f, 0.15f, 7, true);
			k.Cylinder(new Vector3(-2.5f, y + 0.14f, -2.1f), new Vector3(-2.5f, y + 0.14f, 2.1f), 0.15f, 0.15f, 7, true);
			k.Cylinder(new Vector3(2.5f, y + 0.14f, -2.1f), new Vector3(2.5f, y + 0.14f, 2.1f), 0.15f, 0.15f, 7, true);
		}
		k.Mat(BuildingTextures.ShingleMat);
		k.Box(new Vector3(0, 3.3f, -1.1f), new Vector3(6f, 0.12f, 2.6f), 1f, new Basis(Vector3.Right, 0.6f));
		k.Box(new Vector3(0, 3.3f, 1.1f), new Vector3(6f, 0.12f, 2.6f), 1f, new Basis(Vector3.Right, -0.6f));
		k.Mat(BuildingTextures.StoneMat);
		k.Box(new Vector3(2.2f, 3.2f, 0.6f), new Vector3(0.6f, 2.4f, 0.6f), 1f);
		k.Mat(BuildingTextures.BoardsMat);
		k.Box(new Vector3(-0.4f, 1.0f, -2.25f), new Vector3(0.9f, 1.9f, 0.06f), 1f, new Basis(Vector3.Up, 0.9f));
		k.CommitTo(cabin, "Cabin", true);
		var body = new StaticBody3D { Name = "CabinBody", CollisionLayer = 1, CollisionMask = 0 };
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, 1.5f, 0), Shape = new BoxShape3D { Size = new Vector3(5.4f, 3f, 4.3f) } });
		cabin.AddChild(body);
	}

	private void BuildTrunkUse()
	{
		if (_opening == null) return;
		TrunkUse = new PickupInteractable
		{
			Name = "TrunkUse", PickRadius = 0.7f, MaxDistance = 2.8f, Position = new Vector3(0.1f, 0.65f, -0.3f),   // (where the camera lay)
			PromptFor = _ => _running ? "" : "Put the camera back",
			CanUse = _ => !_running && Ending is 1 or 2 or 5 && Reached,
		};
		TrunkUse.Interacted += p => { if (!_running) _ = Cutscene.Run(this, ct => CameraInTrunk(p, ct), lockInput: true); };
		_opening.AddChild(TrunkUse);
	}

	// ------------------------------------------------------------------ the walk down, and the endings

	public override void _Process(double delta)
	{
		var player = StoryBeat.Player(this);
		if (player == null || _car == null || Done) return;
		float d = player.GlobalPosition.DistanceTo(CarAt);
		// the fog (the fourth and the fifth): closing in the nearer they come, until the car's lost in it
		if (Ending is 4 or 5 && StoryBeat.Atmosphere(this) is { } atmo)
			atmo.Act1FogOverride = Mathf.Clamp(1f - (d - 12f) / 80f, 0.4f, 1f);
		if (!Reached && d < 30f)
		{
			Reached = true;
			GD.Print($"[story] Act 25: the lot in sight - ending {Ending}");
			if (Ending == 3 && !_running) _ = Cutscene.Run(this, ct => Crushed(player, ct), lockInput: true);
			if (Ending == 4 && !_running) _ = Cutscene.Run(this, ct => Stairs(player, ct), lockInput: true);
		}
	}

	private async Task Credits(CancellationToken ct, bool fade = true)
	{
		Done = true;
		var player = StoryBeat.Player(this);
		await GameEnding.Play(this, player, ct);
	}

	/// <summary>The first, second and fifth: the camera lifted back into the trunk; black (and the rest).</summary>
	private async Task CameraInTrunk(PlayerController player, CancellationToken ct)
	{
		_running = true;
		GD.Print("[story] Act 25: the camera back in the trunk");
		await StoryBeat.PanTowards(this, player, _opening.GlobalPosition + Vector3.Down * 0.3f, 0.8f, ct, pitch: true);
		player.Inventory?.TakeAwayCamera();
		if (_trunkCam != null)
		{
			_trunkCam.Visible = true;
			var home = _trunkCam.Position;
			var cam = player.CameraRig.Camera;
			_trunkCam.GlobalPosition = cam.GlobalPosition + -cam.GlobalBasis.Z * 0.4f + Vector3.Down * 0.2f;
			var tw = CreateTween();
			tw.TweenProperty(_trunkCam, "position", home, 1.2f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
			await Cutscene.Tween(this, tw, ct);
		}
		AudioDirector.OneShot(this, "wood_place", 3, _opening.GlobalPosition, -4f, "Events", 3f, 0.05f);
		await Cutscene.Wait(this, 1.0, ct);
		var fader = StoryBeat.Fader(this);
		if (fader != null) await fader.Fade(1f, 2.5f, ct);
		if (Ending == 2)
		{
			await Cutscene.Wait(this, 5.0, ct);
			AudioDirector.OneShot(this, "wendigo_howl_far", 3, null, -6f, "Unnatural");
			await Cutscene.Wait(this, 3.5, ct);
		}
		else if (Ending == 5)
		{
			await Cutscene.Wait(this, 5.0, ct);
			// behind the wheel: the view from the driver's seat, the trail's trees through the windscreen, the engine
			var seat = _car.GlobalPosition + _car.GlobalBasis * new Vector3(-0.38f, 1.15f, 0.25f);
			player.GlobalPosition = seat + Vector3.Down * 1.0f;
			var fwd = -_car.GlobalBasis.Z;
			player.CameraRig.SnapBehind(Mathf.Atan2(-fwd.X, -fwd.Z));
			player.CameraRig.SetPitch(-0.05f);
			if (StoryBeat.Atmosphere(this) is { } a2) a2.Act1FogOverride = 0.3f;
			if (fader != null) await fader.Fade(0f, 2.0f, ct);
			AudioDirector.OneShot(this, "car_start", 1, null, -2f, "Events");
			GD.Print("[story] Act 25: behind the wheel - the engine catches, and away");
			await Cutscene.Wait(this, 4.0, ct);
			if (fader != null) await fader.Fade(1f, 2.5f, ct);
		}
		await Credits(ct);
	}

	/// <summary>The third: it comes down on the car and crushes it; burning, it shambles at them; they turn, and the
	/// stalker is there.</summary>
	private async Task Crushed(PlayerController player, CancellationToken ct)
	{
		_running = true;
		GD.Print("[story] Act 25: it comes down on the car");
		var w = new Wendigo { Name = "EndingWendigo" };
		AddChild(w);
		var above = CarAt + Vector3.Up * 14f + (player.GlobalPosition - CarAt).Normalized() * -6f;
		w.StandAt(above, player.GlobalPosition);
		await StoryBeat.PanTowards(this, player, CarAt + Vector3.Up * 1.2f, 0.6f, ct, pitch: true);
		bool down = false;
		w.Leap(CarAt + Vector3.Up * 0.9f, 0.7f, () => down = true, stay: true, arc: 0.5f, ballistic: true, landClip: "land", landSeconds: 0.9f);
		for (int i = 0; i < 40 && !down; i++) await Cutscene.Wait(this, 0.05, ct);
		AudioDirector.OneShot(this, "door_ram_burst", 1, CarAt, 7f, "Events", 12f, 0.02f);
		AudioDirector.OneShot(this, "window_shatter", 2, CarAt, 6f, "Events", 10f, 0.05f);
		// the car crushed flat under it
		if (_car.GetNodeOrNull<Node3D>("Mesh") is { } mesh) mesh.Scale = new Vector3(1.05f, 0.55f, 1f);
		else _car.Scale = new Vector3(1.05f, 0.55f, 1f);
		var fire = new FireVfx { Name = "Burning", Extent = new Vector3(0.7f, 1.4f, 0.6f), FlameScale = 0.8f, LightRange = 7f, LightEnergy = 2f, LightShadows = false, Position = new Vector3(0, 2f, 0) };
		w.AddChild(fire);
		await Cutscene.Wait(this, 1.2, ct);
		// down off the car and at them, slow, burning, dragging itself
		var to = player.GlobalPosition + (CarAt - player.GlobalPosition).Normalized() * 3.5f;
		// (its walk cycle at the pace it really covers the ground: a cycle is 2.55 m of its full stride)
		float pace = Mathf.Max(w.GlobalPosition.DistanceTo(to) / 3.6f, 0.3f);
		w.Play("walk", Mathf.Clamp(2.55f / pace, 0.9f, 3.5f), 0.3);
		var tw = CreateTween();
		tw.TweenProperty(w, "global_position", to with { Y = player.GlobalPosition.Y }, 3.6f);
		await StoryBeat.PanTowards(this, player, to + Vector3.Up * 2.5f, 2.5f, ct, pitch: true);
		// they turn away from it, and the stalker is right there
		var behind = player.GlobalPosition + (player.GlobalPosition - CarAt).Normalized() * 2.2f;
		var st = new StalkerBody { Name = "EndingStalker", Size = 1.1f };
		AddChild(st);
		st.GlobalPosition = behind with { Y = player.GlobalPosition.Y };
		st.LookAt(player.GlobalPosition with { Y = st.GlobalPosition.Y }, Vector3.Up);
		await StoryBeat.PanTowards(this, player, behind + Vector3.Up * 1.7f, 0.6f, ct, pitch: true);
		AudioDirector.OneShot(this, "stalker_seen_01", 1, behind, 6f, "Unnatural", 6f, 0.02f);
		var lunge = CreateTween();
		lunge.TweenProperty(st, "global_position", player.GlobalPosition + (behind - player.GlobalPosition).Normalized() * 0.5f, 0.22f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
		await Cutscene.Tween(this, lunge, ct);
		StoryBeat.Fader(this)?.SetBlack(true);
		await Cutscene.Wait(this, 2.5, ct);
		await Credits(ct);
	}

	/// <summary>The fourth: where the car should be, the stairs. Blood down them, step by step; then up them, faster and
	/// faster, nearly to the top. Black.</summary>
	private async Task Stairs(PlayerController player, CancellationToken ct)
	{
		_running = true;
		GD.Print("[story] Act 25: the stairs, where the car should be");
		_car.Visible = false;
		if (_stairs == null) { await Credits(ct); return; }
		_stairs.Visible = true;
		_stairs.GlobalPosition = CarAt;
		var face = (player.GlobalPosition - CarAt) with { Y = 0 };
		_stairs.GlobalBasis = Basis.LookingAt(-face.Normalized(), Vector3.Up);
		await StoryBeat.PanTowards(this, player, CarAt + Vector3.Up * 2f, 1.5f, ct, pitch: true);
		await Cutscene.Wait(this, 1.5, ct);
		// the blood: down from the top step, a step at a time, to the bottom
		var blood = new StandardMaterial3D { AlbedoColor = new Color(0.32f, 0.02f, 0.02f), Roughness = 0.1f, MetallicSpecular = 0.8f };
		for (int i = 11; i >= 0; i--)
		{
			var step = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.7f, 0.01f, 0.32f) }, MaterialOverride = blood, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
			_stairs.AddChild(step);
			step.Position = new Vector3(0, 0.2f + i * 0.2f, -0.3f - i * 0.3f);
			AudioDirector.OneShot(this, "haunt_drip", 3, step.GlobalPosition, -10f, "Events", 2f, 0.1f);
			await Cutscene.Wait(this, 0.45, ct);
		}
		await Cutscene.Wait(this, 1.0, ct);
		// up them: slowly at first, faster and faster
		var top = _stairs.GlobalPosition + _stairs.GlobalBasis * new Vector3(0, 2.6f, -3.6f);
		var from = player.GlobalPosition;
		double t = 0, total = 6.0;
		while (t < total)
		{
			await Cutscene.Frame(this, ct);
			t += GetProcessDeltaTime();
			float u = (float)(t / total);
			player.GlobalPosition = from.Lerp(top, u * u * u * 0.96f);
		}
		StoryBeat.Fader(this)?.SetBlack(true);
		await Cutscene.Wait(this, 2.0, ct);
		await Credits(ct);
	}
}
