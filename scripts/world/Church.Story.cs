using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.UI;

namespace ProjectDS.World;

/// <summary>
/// Act 21's beats in the church (the owner's outline, with the vestry and the font's key added to join
/// them up): the player comes up into the crypt and the hatch falls shut behind them (a save); the great
/// door is locked, with four empty niches in it; the font's lid is chained and locked; lighting the four
/// tall candles round the nave with the lighter unlocks the vestry's door off the left transept, and it
/// swings open; the font's key hangs in the vestry; the font holds the chalice; the chalice goes into the
/// first niche of the great door (<see cref="Checkpoint.Act21Finished"/>): one of four. Act 22 finds the
/// other three. The compass lives again up here: it points at whatever the church wants next.
/// Winter: from Act 20's end, the whole world's trees and the atmosphere are wintry (the global shader
/// value "winter" and <see cref="ForestAtmosphere.Winter"/>); in here, no underground black.
/// </summary>
public partial class Church
{
	public bool VestryOpen { get; private set; }
	public bool FontOpen { get; private set; }
	public bool ChalicePlaced { get; private set; }
	public bool Ending { get; private set; }
	public bool CandleLit(int i) => StoryManager.Instance?.HasFlag(StoryManager.Flag.ChurchCandle(i + 1)) ?? false;
	public int CandlesLit { get { int n = 0; for (int i = 0; i < 4; i++) if (CandleLit(i)) n++; return n; } }
	public Pickup ChalicePickup { get; private set; }

	public Vector3 CandleWorld(int i) => ToGlobal(CandleLocal[i] + Vector3.Up * 2f);
	public Vector3 CandleStandWorld(int i) => ToGlobal(CandleLocal[i] + new Vector3(0, 0.05f, CandleLocal[i].Z < 20f ? 1.4f : -1.4f));
	public Vector3 FontWorld => ToGlobal(FontLocal + Vector3.Up * 1.2f);
	public Vector3 FontStandWorld => ToGlobal(FontLocal + new Vector3(1.4f, 0.05f, 0.6f));
	public Vector3 VestryDoorWorld => ToGlobal(new Vector3(-TransHalf + 1.2f, 0.05f, (NaveEnd + CrossEnd) * 0.5f));
	public Vector3 VestryInsideWorld => ToGlobal(new Vector3((VestryX0 + VestryX1) * 0.5f, 0.05f, (VestryZ0 + VestryZ1) * 0.5f - 1.5f));
	public Vector3 KeyWorld => FontKeyPickup != null && IsInstanceValid(FontKeyPickup) ? FontKeyPickup.GlobalPosition : ToGlobal(new Vector3(VestryX1 + 5.4f, 1.42f, VestryZ0 + 0.14f));
	public Vector3 NicheWorld(int i) => ToGlobal(new Vector3(NicheX[i], NicheY, -0.75f + 0.12f));
	public Vector3 NicheStandWorld => ToGlobal(new Vector3(-1.3f, 0.05f, 1.3f));
	public Vector3 CryptStairTopWorld => ToGlobal(new Vector3((StairOpenings[1].x0 + StairOpenings[1].x1) * 0.5f, 0.05f, StairOpenings[1].z0 - 1.2f));
	public Vector3 CryptStairFootWorld => ToGlobal(new Vector3((StairOpenings[1].x0 + StairOpenings[1].x1) * 0.5f, CryptFloor + 0.05f, StairOpenings[1].z1 + 1.2f));

	private Node3D _objective;
	private bool _silenced, _winterSet;

	/// <summary>Is this world point in the church (the nave and its arms, the vestry, the crypt)?</summary>
	public bool Inside(Vector3 world)
	{
		var l = ToLocal(world);
		if (l.Y < CryptFloor - 1f || l.Y > 40f) return false;
		if (l.X > VestryX1 - 1f && l.X < -TransHalf && l.Z > VestryZ0 - 1f && l.Z < VestryZ1 + 1f) return true;
		return l.X > -TransHalf - 1.5f && l.X < TransHalf + 1.5f && l.Z > -1.6f && l.Z < ChancelEnd + ApseR + 1f;
	}

	private void StartStory()
	{
		var s = StoryManager.Instance;
		_objective = new Node3D { Name = "ChurchObjective" };
		AddChild(_objective);
		_objective.AddToGroup("church_objective_marker");
		for (int i = 0; i < 4; i++) if (CandleLit(i)) SetCandle(i, true, instant: true);
		VestryOpen = s?.HasFlag(StoryManager.Flag.ChurchVestryOpen) ?? false;
		if (VestryOpen) OpenVestryDoor(instant: true);
		FontOpen = s?.HasFlag(StoryManager.Flag.ChurchFontOpen) ?? false;
		if (FontOpen) LiftLid(instant: true);
		ChalicePlaced = s?.HasFlag(StoryManager.Flag.ChurchChalicePlaced) ?? false;
		if (FontOpen) PlaceChaliceInFont();
		if (ChalicePlaced) ShowChaliceInDoor(instant: true);
		if (s != null && s.Current >= Checkpoint.Act21ChurchReached) Stair?.CloseHatch(instant: true);
		SetProcess(true);
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		var s = StoryManager.Instance;
		// the world is in winter from here on: the frost on every tree, the cold light
		bool winter = s != null && s.Current >= Checkpoint.Act20Finished;
		if (winter != _winterSet)
		{
			_winterSet = winter;
			RenderingServer.GlobalShaderParameterSet("winter", winter ? 1f : 0f);
		}
		var atmo = StoryBeat.Atmosphere(this);
		if (atmo != null) atmo.Winter = Mathf.MoveToward(atmo.Winter, winter ? 1f : 0f, dt * 0.5f);
		var player = StoryBeat.Player(this);
		bool inside = player != null && Inside(player.GlobalPosition);
		if (inside && atmo != null) atmo.Underground = Mathf.MoveToward(atmo.Underground, 0f, dt * 0.6f);
		if (atmo != null) atmo.Interior = Mathf.MoveToward(atmo.Interior, inside ? 1f : 0f, dt * 0.8f);
		// the forest's own sounds have no place in here: the church has its wind and its hush
		var amb = ForestAmbienceManager.Instance;
		if (amb != null && inside != _silenced)
		{
			_silenced = inside;
			if (inside) amb.RequestSilence(this, 1f, 5); else amb.ReleaseSilence(this);
		}
		UpdateObjective(player);
	}

	public override void _ExitTree()
	{
		if (_silenced) ForestAmbienceManager.Instance?.ReleaseSilence(this);
		// the next level (a new game's trailhead) is not in winter
		RenderingServer.GlobalShaderParameterSet("winter", 0f);
	}

	/// <summary>Where the compass points: the nearest unlit candle, then the vestry (and its key), the font,
	/// the great door.</summary>
	private void UpdateObjective(PlayerController player)
	{
		if (_objective == null) return;
		Vector3 goal;
		if (CandlesLit < 4)
		{
			goal = CandleWorld(0);
			float best = float.MaxValue;
			for (int i = 0; i < 4; i++)
			{
				if (CandleLit(i)) continue;
				float d = player != null ? player.GlobalPosition.DistanceSquaredTo(CandleWorld(i)) : i;
				if (d < best) { best = d; goal = CandleWorld(i); }
			}
		}
		else if (!FontOpen && !(player?.Inventory?.HasTool(ToolKind.FontKey) ?? false)) goal = KeyWorld;
		else if (!FontOpen || (ChalicePickup != null && IsInstanceValid(ChalicePickup) && !ChalicePickup.Taken)) goal = FontWorld;
		else goal = NicheWorld(0);
		_objective.GlobalPosition = goal;
	}

	// ------------------------------------------------------------------ the candles

	private void OnCandle(int i, PlayerController player)
	{
		if (CandleLit(i) || player?.Inventory is not { } inv || !inv.HasTool(ToolKind.Lighter)) return;
		AudioDirector.OneShot(this, "lighter_flick", 1, CandleWorld(i), -2f);
		SetCandle(i, true);
		StoryManager.Instance?.SetFlag(StoryManager.Flag.ChurchCandle(i + 1));
		GD.Print($"[story] Act 21: candle {i + 1} lit ({CandlesLit} of 4)");
		if (CandlesLit == 4 && !VestryOpen) _ = Cutscene.Run(this, ct => AllFourBurn(player, ct), lockInput: true);
	}

	private void SetCandle(int i, bool lit, bool instant = false)
	{
		var (flame, light, use) = _candles[i];
		flame.Visible = lit;
		light.Visible = lit;
		use.Enabled = !lit;
		if (!lit) return;
		if (instant) { light.LightEnergy = 1.6f; return; }
		AudioDirector.OneShot(this, "candle_ignite", 3, flame.GlobalPosition, -2f, "Events", 4f, 0.1f);
		flame.Scale = Vector3.One * 0.2f;
		var tw = CreateTween().SetParallel();
		tw.TweenProperty(flame, "scale", Vector3.One, 0.5f).SetTrans(Tween.TransitionType.Sine);
		tw.TweenProperty(light, "light_energy", 1.6f, 0.9f);
	}

	/// <summary>The fourth flame: a pause, then far off across the church a lock turns, heavy, and the
	/// vestry's door groans open on its own. The view turns (slowly) to find it.</summary>
	private async Task AllFourBurn(PlayerController player, CancellationToken ct)
	{
		GD.Print("[story] Act 21: all four candles burn - a lock turns somewhere");
		await Cutscene.Wait(this, 1.2, ct);
		var door = ToGlobal(new Vector3(-TransHalf, 1.6f, (NaveEnd + CrossEnd) * 0.5f));
		AudioDirector.OneShot(this, "valve_clunk", 1, door, 6f, "Events", 12f, 0f);
		await Cutscene.Wait(this, 0.9, ct);
		AudioDirector.OneShot(this, "door_creak", 1, door, 4f, "Events", 12f, 0f);
		OpenVestryDoor();
		VestryOpen = true;
		StoryManager.Instance?.SetFlag(StoryManager.Flag.ChurchVestryOpen);
		double t = 0;
		while (t < 2.6)
		{
			await Cutscene.Frame(this, ct);
			float dt = (float)GetProcessDeltaTime();
			t += dt;
			var rig = player?.CameraRig;
			if (rig?.Camera == null) continue;
			Vector3 to = door - rig.Camera.GlobalPosition;
			player.PlayerInput.AddCutsceneLook(new Vector2(Mathf.AngleDifference(rig.Yaw, Mathf.Atan2(-to.X, -to.Z)) * Mathf.Min(1f, dt * 1.2f), 0));
		}
		_ = Subtitle.Instance?.Show("Across the church, a door has opened.", 0.5f, 2.6f, 0.9f);
	}

	private void OpenVestryDoor(bool instant = false)
	{
		if (_vestryDoor == null) return;
		if (_vestryBlock != null) foreach (var c in _vestryBlock.GetChildren()) if (c is CollisionShape3D cs) cs.Disabled = true;
		float open = -1.9f;
		if (instant) { _vestryDoor.Rotation = _vestryDoor.Rotation with { Y = _vestryDoor.Rotation.Y + open }; return; }
		var tw = CreateTween();
		tw.TweenProperty(_vestryDoor, "rotation:y", _vestryDoor.Rotation.Y + open, 3.2f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
	}

	// ------------------------------------------------------------------ the font

	private void OnFont(PlayerController player)
	{
		if (FontOpen || player?.Inventory is not { } inv || !inv.HasTool(ToolKind.FontKey)) return;
		inv.Consume(ToolKind.FontKey);
		FontOpen = true;
		_fontUse.Enabled = false;
		StoryManager.Instance?.SetFlag(StoryManager.Flag.ChurchFontOpen);
		GD.Print("[story] Act 21: the font unlocked - the lid lifted off");
		AudioDirector.OneShot(this, "valve_clunk", 1, FontWorld, -2f);
		_ = Cutscene.Run(this, async ct =>
		{
			await Cutscene.Wait(this, 0.6, ct);
			AudioDirector.OneShot(this, "wood_place", 3, FontWorld, 0f);
			LiftLid();
			await Cutscene.Wait(this, 1.4, ct);
			PlaceChaliceInFont();
		}, lockInput: true);
	}

	private void LiftLid(bool instant = false)
	{
		if (_fontLid == null) return;
		Vector3 to = _fontLid.Position + new Vector3(0.95f, -0.9f, 0.4f);
		Vector3 rot = new(0.25f, 0.6f, 1.25f);
		if (instant) { _fontLid.Position = to; _fontLid.Rotation = rot; return; }
		var tw = CreateTween();
		tw.TweenProperty(_fontLid, "position", _fontLid.Position + Vector3.Up * 0.35f, 0.6f).SetTrans(Tween.TransitionType.Sine);
		tw.TweenProperty(_fontLid, "position", to, 0.7f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
		tw.Parallel().TweenProperty(_fontLid, "rotation", rot, 0.7f);
		tw.TweenCallback(Callable.From(() => AudioDirector.OneShot(this, "wood_bump", 3, _fontLid.GlobalPosition, 0f)));
	}

	/// <summary>The chalice, standing in the black water in the font (to be taken).</summary>
	private void PlaceChaliceInFont()
	{
		if (ChalicePickup != null) return;
		var p = new Pickup { Name = "Chalice", Kind = ToolKind.Chalice, UseSpot = false, SnapToSurface = false, TakenId = "church_chalice", Position = FontLocal + Vector3.Up * 1.03f };
		AddChild(p);
		ChalicePickup = p;
	}

	// ------------------------------------------------------------------ the great door

	private void OnNiche(int i, PlayerController player)
	{
		if (i != 0 || ChalicePlaced || player?.Inventory is not { } inv || !inv.HasTool(ToolKind.Chalice)) return;
		inv.Consume(ToolKind.Chalice);
		ChalicePlaced = true;
		_niches[0].Enabled = false;
		StoryManager.Instance?.SetFlag(StoryManager.Flag.ChurchChalicePlaced);
		ShowChaliceInDoor();
		GD.Print("[story] Act 21: the chalice set in the great door - one of four");
		_ = Cutscene.Run(this, ct => OneOfFour(player, ct), lockInput: true);
	}

	private void ShowChaliceInDoor(bool instant = false)
	{
		if (_chaliceInDoor != null) return;
		_chaliceInDoor = new Node3D { Name = "ChaliceInDoor", Position = new Vector3(NicheX[0], NicheY - 0.2f, -0.75f + 0.14f) };
		AddChild(_chaliceInDoor);
		ItemMeshes.Build(ToolKind.Chalice, _chaliceInDoor);
		_chaliceInDoor.Scale = Vector3.One * 1.9f;
		var glow = new OmniLight3D { Name = "Glow", Position = new Vector3(0, 0.25f, 0.2f), LightColor = new Color(1f, 0.85f, 0.55f), LightEnergy = instant ? 0.6f : 0f, OmniRange = 2.2f, ShadowEnabled = false };
		_chaliceInDoor.AddChild(glow);
		if (!instant) CreateTween().TweenProperty(glow, "light_energy", 0.6f, 2.5f);
	}

	/// <summary>One of four: the chalice settles into its niche with a low tone, the gold outlines of the
	/// other three catch the light for a moment. The save. The first half of the church is done.</summary>
	private async Task OneOfFour(PlayerController player, CancellationToken ct)
	{
		Ending = true;
		AudioDirector.OneShot(this, "puzzle_glow", 1, NicheWorld(0), 0f);
		await Cutscene.Wait(this, 2.0, ct);
		StoryBeat.ReachCheckpoint(player, Checkpoint.Act21Finished);
		GD.Print("[story] Act 21 done: one of the great door's four pieces in place");
		await StoryBeat.Caption(this, "One of four.", 0.8f, 2.6f, 1.2f, ct);
		await Cutscene.Wait(this, 0.8, ct);
		var fader = StoryBeat.Fader(this);
		if (fader != null) await fader.Fade(1f, 2.5f, ct);
		if (GetTree().GetFirstNodeInGroup("act11_ending") is Act11Ending ending) await ending.Credits(fader, ct);
	}
}
