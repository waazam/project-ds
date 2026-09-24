using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>Act 15's red light, green light (see <see cref="Act15Hallway"/>).</summary>
public partial class Act15Hallway
{
	/// <summary>Seconds of green between reds (random in this range).</summary>
	[Export] public Vector2 GreenSeconds = new(20f, 30f);
	/// <summary>Seconds of red (random in this range).</summary>
	[Export] public Vector2 RedSeconds = new(4f, 6f);
	/// <summary>After the red comes on, how long before moving counts (a first red is kinder).</summary>
	[Export] public float Grace = 0.35f, FirstGrace = 1.0f;
	/// <summary>Faster than this (m/s, across the floor) is moving.</summary>
	[Export] public float MoveThreshold = 0.35f;

	// the flicker: off, on, off, on, off, on - slow blinks, never a strobe
	private static readonly float[] FlickerSteps = { 0.24f, 0.3f, 0.24f, 0.3f, 0.24f, 0.36f };

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		var player = StoryBeat.Player(this);
		if (player == null) return;
		Vector3 l = ToLocal(player.GlobalPosition);
		PlayerZ = l.Z;
		bool inside = l.Z > -0.5f && l.Z < End + ClosetDepth + 1f && Mathf.Abs(l.X) < W2 && l.Y > -3f && l.Y < H2;

		// the air in here: green-black fog, going dark red with the lights
		if (inside && StoryBeat.Atmosphere(this) is { } atmo)
		{
			atmo.Underground = Mathf.MoveToward(atmo.Underground, 1f, dt);
			bool red = State == Phase.Red;
			atmo.UndergroundFogColor = atmo.UndergroundFogColor.Lerp(InCloset ? new Color(0.004f, 0.004f, 0.005f) : red ? FogRed : FogGreen, Mathf.Min(1f, dt * 4f));
			atmo.UndergroundFogDensity = Mathf.MoveToward(atmo.UndergroundFogDensity, l.Z < Part1 ? 0.032f : 0.022f, dt * 0.05f);
		}
		float hum = inside && !InCloset ? -14f : -80f;
		if (_hum != null && _hum.Stream != null)
		{
			_hum.VolumeDb = Mathf.MoveToward(_hum.VolumeDb, hum, dt * 20f);
			if (_hum.VolumeDb > -79f && !_hum.Playing) _hum.Play();
			else if (_hum.VolumeDb <= -79f && _hum.Playing) _hum.Stop();
		}

		if (_dying || Finished) return;
		switch (State)
		{
			case Phase.Waiting:
				// frozen in the green; the game starts once they are past him
				if (l.Z > ShadowZ + 1.2f) { State = Phase.Green; _timer = _rng.RandfRange(GreenSeconds.X, GreenSeconds.Y); GD.Print("[story] Act 15: past the shadow man - red light, green light"); }
				break;
			case Phase.Green:
				_timer -= dt;
				if (_timer <= 0f) { State = Phase.Flicker; _flickStep = 0; _flickT = 0f; Sfx("bulb_sputter", 3, player.GlobalPosition + Vector3.Up * 5f, -4f); }
				break;
			case Phase.Flicker:
				_flickT += dt;
				bool reduce = GameSettings.Instance?.ReduceFlashing ?? false;
				float stepLen = FlickerSteps[_flickStep];
				float u = Mathf.Clamp(_flickT / stepLen, 0f, 1f);
				bool offStep = _flickStep % 2 == 0;
				// a soft dip (Reduce Flashing), or a slow blink down to a glow and back
				float level = reduce ? (offStep ? Mathf.Lerp(1f, 0.45f, Mathf.Sin(u * Mathf.Pi)) : 1f) : (offStep ? 0.08f : 1f);
				SetLights(Green, level);
				if (_flickT >= stepLen) { _flickT = 0f; _flickStep++; if (_flickStep >= FlickerSteps.Length) GoRed(player); }
				break;
			case Phase.Red:
				_redT += dt;
				bool moving = new Vector2(player.Velocity.X, player.Velocity.Z).Length() > MoveThreshold || !player.IsOnFloor();
				if (_redT > (_firstRed ? FirstGrace : Grace) && moving && player.PlayerInput.Enabled) { _dying = true; _ = Cutscene.Run(this, ct => Taken(player, ct), lockInput: true, freezeBody: true); break; }
				if (_redT >= _timer) GoGreen(player);
				break;
		}
	}

	private void GoRed(PlayerController player)
	{
		State = Phase.Red;
		Reds++;
		_redT = 0f;
		_timer = _rng.RandfRange(RedSeconds.X, RedSeconds.Y);
		SetLights(Red, 1f);
		Sfx("relay_clunk", 1, player.GlobalPosition + Vector3.Up * 6f, 0f);
		Sfx("siren_low", 1, player.GlobalPosition + new Vector3(0, 8f, 30f), 2f, 30f);
		// he is right behind them
		var cam = player.CameraRig;
		float yaw = cam?.Yaw ?? 0f;
		Vector3 back = new(Mathf.Sin(yaw), 0, Mathf.Cos(yaw));   // the camera looks along -Z of its yaw: behind is +Z
		Vector3 at = player.GlobalPosition + back * 1.3f;
		Vector3 la = ToLocal(at);
		la.X = Mathf.Clamp(la.X, -W2 * 0.5f + 0.4f, W2 * 0.5f - 0.4f);
		la.Z = Mathf.Max(la.Z, Part1 + Taper + 0.5f);
		la.Y = 0f;
		_shadow.StandAt(ToGlobal(la), player.GlobalPosition);
		_shadow.Glare = 0.35f;
		Sfx("shadow_breath", 2, _shadow.GlobalPosition + Vector3.Up * 1.8f, -6f, 2f);
		GD.Print($"[story] Act 15: red light #{Reds} - he is behind you");
	}

	private void GoGreen(PlayerController player)
	{
		State = Phase.Green;
		_firstRed = false;
		_timer = _rng.RandfRange(GreenSeconds.X, GreenSeconds.Y);
		SetLights(Green, 1f);
		Sfx("relay_clunk", 1, player.GlobalPosition + Vector3.Up * 6f, -2f);
	}

	private void SetLights(Color c, float level)
	{
		foreach (var l in _lamps) { l.LightColor = c; l.LightEnergy = 1.5f * level; }
		_lampMat.AlbedoColor = c * Mathf.Max(0.1f, level);
		_lampMat.Emission = c;
		_lampMat.EmissionEnergyMultiplier = 3f * level;
		_stripMat2.AlbedoColor = c * Mathf.Max(0.15f, level);
		_stripMat2.Emission = c;
		_stripMat2.EmissionEnergyMultiplier = 1.3f * Mathf.Max(0.15f, level);
	}

	/// <summary>They moved. The view is turned round onto him, his eyes flare, he comes in, and it's
	/// back to Act 15's start.</summary>
	private async Task Taken(PlayerController player, CancellationToken ct)
	{
		PlayerDeath.Begin();
		GD.Print("[story] Act 15: moved in the red - the shadow man takes them");
		var rig = player.CameraRig;
		Vector3 head = _shadow.GlobalPosition + Vector3.Up * 1.9f;
		Sfx("shadow_strike", 1, head, 2f, 4f);
		double t = 0;
		while (t < 0.45)
		{
			await Cutscene.Frame(this, ct);
			float dt = (float)GetProcessDeltaTime();
			t += dt;
			Vector3 to = head - rig.Camera.GlobalPosition;
			player.PlayerInput.AddCutsceneLook(new Vector2(Mathf.AngleDifference(rig.Yaw, Mathf.Atan2(-to.X, -to.Z)) * Mathf.Min(1f, dt * 12f), 0));
			rig.SetPitch(Mathf.Lerp(rig.Pitch, Mathf.Atan2(to.Y, new Vector2(to.X, to.Z).Length()), Mathf.Min(1f, dt * 12f)));
		}
		Vector3 from = _shadow.GlobalPosition, toward = player.GlobalPosition;
		t = 0;
		while (t < 0.5)
		{
			await Cutscene.Frame(this, ct);
			t += GetProcessDeltaTime();
			float u = (float)(t / 0.5);
			_shadow.Glare = Mathf.Lerp(0.35f, 1f, u);
			_shadow.GlobalPosition = from.Lerp(toward, u * 0.6f);
		}
		StoryBeat.Fader(this)?.SetBlack(true);
		await PlayerDeath.Reload(this, "You moved.", ct);
	}

	private void OnDoorUsed(PlayerController player)
	{
		if (DoorOpen) return;
		DoorOpen = true;
		_doorUse.Enabled = false;
		Sfx("door_creak", 1, _doorHinge.GlobalPosition + Vector3.Up * 1f, -2f);
		var tw = CreateTween();
		tw.TweenProperty(_doorHinge, "rotation", new Vector3(0, -1.65f, 0), 1.3f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
		GD.Print("[story] Act 15: the closet door opens");
	}

	private void OnInCloset(PlayerController player)
	{
		if (!DoorOpen || _closing || _dying) return;
		_closing = true;
		InCloset = true;
		_ = Cutscene.Run(this, ct => Shut(player, ct));
	}

	/// <summary>In: the door swings shut behind them, the hall's lights and its game are gone, and it is
	/// Act 16's save.</summary>
	private async Task Shut(PlayerController player, CancellationToken ct)
	{
		State = Phase.Done;
		var tw = CreateTween();
		tw.TweenProperty(_doorHinge, "rotation", Vector3.Zero, 0.35f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
		await Cutscene.Wait(this, 0.35, ct);
		Sfx("door_slam", 2, _doorHinge.GlobalPosition + Vector3.Up * 1f, 2f);
		SetLights(Green, 0f);
		foreach (var l in _lamps) l.Visible = false;
		Finished = true;
		StoryBeat.ReachCheckpoint(player, Checkpoint.Act15Finished);
		GD.Print("[story] Act 15 done: into the janitor's closet, the door shut behind - Act 16 starts here");
		// Act 16 isn't built yet: a few seconds in the dark, then the credits
		await Cutscene.Wait(this, 5.0, ct);
		var fader = StoryBeat.Fader(this);
		if (fader != null) await fader.Fade(1f, 2.5f, ct);
		if (GetTree().GetFirstNodeInGroup("act11_ending") is Act11Ending ending) await ending.Credits(fader, ct);
	}

	private void Sfx(string name, int variants, Vector3 at, float db, float unit = 5f)
	{
		string path = $"res://assets/audio/sfx/{name}_{_rng.RandiRange(1, variants):00}.wav";
		if (!ResourceLoader.Exists(path)) path = $"res://assets/audio/sfx/{name}.wav";
		if (!ResourceLoader.Exists(path)) return;
		var s = new AudioStreamPlayer3D { Stream = GD.Load<AudioStream>(path), Bus = "Events", VolumeDb = db, UnitSize = unit, MaxDistance = 80f, PitchScale = _rng.RandfRange(0.95f, 1.04f) };
		Cutscene.SceneRoot(this).AddChild(s);
		s.GlobalPosition = at;
		s.Finished += s.QueueFree;
		s.Play();
	}
}
