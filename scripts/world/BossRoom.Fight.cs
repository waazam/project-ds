using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Entities;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.World.BossParts;

namespace ProjectDS.World;

/// <summary>Act 18's fight (see <see cref="BossRoom"/>).</summary>
public partial class BossRoom
{
	public enum Phase { Waiting, Intro, Fight, Draining, Finale, Open, Done }
	public Phase State { get; private set; } = Phase.Waiting;
	public int ValvesTurned { get; private set; }
	public Valve ActiveValve { get; private set; }
	public int Slams { get; private set; }
	public bool Finished { get; private set; }
	public bool DoorOpen { get; private set; }
	/// <summary>Seconds of the fight so far (from the first valve lit to the last turned).</summary>
	public double FightSeconds { get; private set; }
	/// <summary>For tests (and the bot): where limbs are about to come down, and how soon.</summary>
	public IEnumerable<(Vector3 world, float timeLeft, float radius)> Threats
	{
		get
		{
			foreach (var (t, marker, left) in _telegraphs) yield return (marker.GlobalPosition, left, (float)marker.GetMeta("radius"));
		}
	}
	public IReadOnlyList<int> Order => _order;

	private readonly List<int> _order = new();
	private float _barrageTimer = 3f;
	public int Barrages { get; private set; }
	private int _next;
	private float _slamTimer = 4f;
	private readonly List<(Leviathan.Tentacle t, Decal marker, float left)> _telegraphs = new();
	private bool _dying;
	private int _litFixture = -1;
	private float _squeakT;

	private void RestoreOrWait()
	{
		var s = StoryManager.Instance;
		if (s == null) return;
		if (s.Current >= Checkpoint.Act18Finished)
		{
			// long over: the pit empty, the thing dead, the door open
			State = Phase.Done;
			Finished = DoorOpen = true;
			SetBlood(PitFloor);
			Beast.Rot = 1f;
			foreach (var v in Valves) v.Finish();
			foreach (var e in Beast.EyesInBurstOrder()) e.Visible = false;
			Beast.Die();
			_door.Position += Vector3.Up * 2.5f;
			_tidyLight.LightEnergy = 1.6f;
		}
		// otherwise it waits: the fight starts (in _Process) once they are standing on the catwalk
	}

	// ------------------------------------------------------------------ arriving

	/// <summary>Down the shaft from the sewer's hole (the screen is black when this is called): out of the
	/// ceiling, a long drop, and onto the catwalk. That's Act 18's save.</summary>
	public async Task Arrive(PlayerController player, CancellationToken ct)
	{
		var rig = player.CameraRig;
		var fader = StoryBeat.Fader(this);
		Vector3 top = ToGlobal(LandingLocal with { Y = Ceil + 3f });
		player.Teleport(top, GlobalRotation.Y + Mathf.Pi);
		rig.SetPitch(Mathf.DegToRad(-70f));
		await Cutscene.Wait(this, 0.4, ct);
		fader?.SetBlack(false);
		float v = 0f, fallen = 0f, drop = Ceil + 3f - LandingLocal.Y;
		while (fallen < drop)
		{
			await Cutscene.Frame(this, ct);
			float dt = (float)GetProcessDeltaTime();
			v = Mathf.Min(v + 9.8f * dt, 16f);
			fallen = Mathf.Min(drop, fallen + v * dt);
			player.GlobalPosition = top + Vector3.Down * fallen;
			rig.SetPitch(Mathf.Lerp(rig.Pitch, Mathf.DegToRad(-40f), Mathf.Min(1f, dt)));
		}
		player.GlobalPosition = LandingWorld;
		Sfx("step_metal", 6, LandingWorld, 4f, 5f);
		Sfx("body_thump", 2, LandingWorld, 0f, 4f);
		rig.EyeHeight = 0.9f;
		var up = player.CreateTween();
		up.TweenProperty(rig, "EyeHeight", 1.62f, 0.9f).SetTrans(Tween.TransitionType.Sine);
		StoryBeat.ReachCheckpoint(player, Checkpoint.Act17Finished);
		GD.Print("[story] Act 18: onto the catwalk over the blood");
		await Cutscene.Wait(this, 0.9, ct);
	}

	/// <summary>The fight begins: the first time with the full introduction, after a death with a short one.</summary>
	public void Begin(PlayerController player, bool quick)
	{
		if (State is not Phase.Waiting) return;
		State = Phase.Intro;
		_ = Cutscene.Run(this, ct => Intro(player, quick, ct), lockInput: true, freezeBody: true);
	}

	private async Task Intro(PlayerController player, bool quick, CancellationToken ct)
	{
		var rig = player.CameraRig;
		MakeOrder();
		if (!quick)
		{
			// the blood below; something moves in it; a limb comes up out of it and down on the catwalk, not far off
			await Look(player, ToGlobal(new Vector3(0, BloodY, 0)), 1.6f, ct);
			Sfx("creature_giant_bellow", 1, ToGlobal(new Vector3(0, BloodY, 0)), 6f, 30f);
			await Cutscene.Wait(this, 1.2, ct);
			Vector3 demo = ToGlobal(RingLocal(RingOf(LandingLocal) + 9f));
			await Look(player, demo + Vector3.Up * 4f, 1.0f, ct);
			Telegraph(demo, 1.6f, demonstration: true);
			await Cutscene.Wait(this, 2.8, ct);
			StoryManager.Instance?.SetFlag(StoryManager.Flag.Act18IntroSeen);
		}
		else
		{
			Sfx("creature_giant_bellow", 1, ToGlobal(new Vector3(0, BloodY, 0)), 2f, 30f);
			await Cutscene.Wait(this, 0.8, ct);
		}
		LightNext();
		await Cutscene.Wait(this, 0.3, ct);
		if (ActiveValve != null) await Look(player, ActiveValve.WheelWorld, quick ? 0.8f : 1.4f, ct);
		if (!quick) _ = StoryBeat.Caption(this, "The valves. Turn them. Drain it.", 0.4f, 2.6f, 1f);
		await Cutscene.Wait(this, quick ? 0.3 : 1.2, ct);
		State = Phase.Fight;
		_slamTimer = quick ? 3f : 5f;
		GD.Print($"[story] Act 18: the fight - valve order {string.Join(",", _order.ConvertAll(i => i + 1))}");
	}

	private async Task Look(PlayerController player, Vector3 at, float seconds, CancellationToken ct)
	{
		var rig = player.CameraRig;
		double t = 0;
		while (t < seconds)
		{
			await Cutscene.Frame(this, ct);
			float dt = (float)GetProcessDeltaTime();
			t += dt;
			Vector3 to = at - rig.Camera.GlobalPosition;
			player.PlayerInput.AddCutsceneLook(new Vector2(Mathf.AngleDifference(rig.Yaw, Mathf.Atan2(-to.X, -to.Z)) * Mathf.Min(1f, dt * 3f), 0));
			rig.SetPitch(Mathf.Lerp(rig.Pitch, Mathf.Atan2(to.Y, new Vector2(to.X, to.Z).Length()), Mathf.Min(1f, dt * 3f)));
		}
	}

	/// <summary>Ten of the sixteen valves, in a random order that never goes to a neighbour: each next one
	/// is at least four places round the room, often on the far side, so it's back and forth, never a walk round.</summary>
	private void MakeOrder()
	{
		var rng = new RandomNumberGenerator();
		rng.Randomize();
		for (int attempt = 0; attempt < 200; attempt++)
		{
			_order.Clear();
			var left = new List<int>();
			for (int i = 0; i < ValveCount; i++) left.Add(i);
			// the first is well away from where they land (between valves 2 and 3)
			int cur = rng.RandiRange(6, 11);
			_order.Add(cur);
			left.Remove(cur);
			bool ok = true;
			while (_order.Count < ValvesToTurn)
			{
				var cands = left.FindAll(i => RingGap(i, cur) >= 4);
				if (cands.Count == 0) { ok = false; break; }
				cur = cands[rng.RandiRange(0, cands.Count - 1)];
				_order.Add(cur);
				left.Remove(cur);
			}
			if (ok) break;
		}
		_next = 0;
	}

	private static int RingGap(int a, int b) { int d = Mathf.Abs(a - b); return Mathf.Min(d, ValveCount - d); }

	// ------------------------------------------------------------------ per frame

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		var player = StoryBeat.Player(this);
		if (player == null) return;
		bool inside = Inside(player.GlobalPosition);
		if (inside && StoryBeat.Atmosphere(this) is { } atmo)
		{
			atmo.Underground = Mathf.MoveToward(atmo.Underground, 1f, dt * 2f);
			atmo.UndergroundFogColor = atmo.UndergroundFogColor.Lerp(new Color(0.03f, 0.02f, 0.025f), Mathf.Min(1f, dt));
			atmo.UndergroundFogDensity = Mathf.MoveToward(atmo.UndergroundFogDensity, 0.012f, dt * 0.05f);
		}
		if (_air?.Stream != null)
		{
			_air.VolumeDb = Mathf.MoveToward(_air.VolumeDb, inside && State != Phase.Done && !Finished ? -10f : -80f, dt * 15f);
			if (_air.VolumeDb > -79f && !_air.Playing) _air.Play();
			else if (_air.VolumeDb <= -79f && _air.Playing) _air.Stop();
		}
		// on the catwalk, with control, at Act 18's save: the fight begins (after the drop, or on Continue / after a death)
		if (State == Phase.Waiting && StoryManager.Instance is { Current: Checkpoint.Act17Finished } sm && player.PlayerInput.Enabled
			&& player.GlobalPosition.DistanceTo(LandingWorld) < 6f)
			Begin(player, sm.HasFlag(StoryManager.Flag.Act18IntroSeen));
		UpdateTelegraphs(dt);
		if (State is Phase.Fight or Phase.Draining && !_dying)
		{
			FightSeconds += dt;
			Turning(player, dt);
			ScheduleSlams(player, dt);
		}
	}

	public bool Inside(Vector3 world)
	{
		Vector3 l = ToLocal(world);
		return Mathf.Abs(l.X) < Half + 1f && l.Z > -Half - 1f && l.Z < Half + 8f && l.Y > PitFloor - 2f && l.Y < Ceil + 8f;
	}

	/// <summary>Held E on the lit valve: it turns, a little at a time; let go and it stays where it got to.</summary>
	private void Turning(PlayerController player, float dt)
	{
		var v = ActiveValve;
		if (v == null || !v.Active) return;
		bool turning = player.Interaction?.Focused == v.Use && player.PlayerInput.InteractHeld && player.PlayerInput.Enabled;
		if (turning)
		{
			v.Progress = Mathf.Min(1f, v.Progress + dt / Valve.TurnSeconds);
			v.Apply();
			// a squeaky hinge: a short squeal for every bit of the turn
			_squeakT -= dt;
			if (_squeakT <= 0f) { _squeakT = _rng.RandfRange(0.45f, 0.7f); Sfx("valve_squeak", 5, v.WheelWorld, -4f, 3f); }
			if (v.Progress >= 1f) Turned(player, v);
		}
		else _squeakT = 0f;
	}

	// ------------------------------------------------------------------ the valves

	private void LightNext()
	{
		if (_next >= _order.Count) return;
		ActiveValve = Valves[_order[_next]];
		ActiveValve.SetActive(true);
		// the spotlight across the room from it
		int wall = _order[_next] / 4;
		int opposite = (wall + 2) % 4;
		_litFixture = opposite;
		Sfx("relay_clunk", 1, ToGlobal(_fixtures[opposite].light.Position), 4f, 20f);
		var target = ActiveValve.Position + (ActiveValve.Basis * Vector3.Forward) * 0.3f;
		var tw = CreateTween();
		tw.TweenMethod(Callable.From<float>(s => Aim(opposite, target, s)), 0f, 1f, 0.35f);
		GD.Print($"[story] Act 18: the spotlight is on valve {ActiveValve.Number} ({_next + 1} of {ValvesToTurn})");
	}

	private void Turned(PlayerController player, Valve v)
	{
		v.Finish();
		ValvesTurned++;
		_next++;
		ActiveValve = null;
		// home into place, and the siren goes: it knows
		Sfx("valve_clunk", 1, v.WheelWorld, 0f, 4f);
		Sfx("valve_siren", 1, ToGlobal(new Vector3(0, Ceil - 3f, 0)), 2f, 40f);
		// the spot goes out
		int f = _litFixture;
		var tw = CreateTween();
		tw.TweenMethod(Callable.From<float>(s => Aim(f, v.Position + (v.Basis * Vector3.Forward) * 0.3f, s)), 1f, 0f, 0.4f);
		// it hurts: the blood drops, it thrashes and screams, and it rots a little more
		Beast.Rot = ValvesTurned / (float)ValvesToTurn;
		Beast.Convulse(1.4f);
		string[] cries = { "creature_roar_near", "creature_screech", "creature_giant_moan" };
		Sfx(cries[ValvesTurned % cries.Length], 1, ToGlobal(new Vector3(0, BloodY + 4f, 0)), 6f, 30f);
		GD.Print($"[story] Act 18: valve {v.Number} turned ({ValvesTurned} of {ValvesToTurn}) - it rots ({Beast.Rot:0.00})");
		float from = BloodY, to = Mathf.Lerp(BloodStart, PitFloor, ValvesTurned / (float)ValvesToTurn);
		_ = Cutscene.Run(this, ct => Drain(from, to, ValvesTurned >= ValvesToTurn ? 5f : 3.5f, ct));
		if (ValvesTurned >= ValvesToTurn) { State = Phase.Finale; _ = Cutscene.Run(this, ct => Finale(player, ct), lockInput: true, freezeBody: true); }
		else GetTree().CreateTimer(1.6).Timeout += () => { if (State == Phase.Fight) LightNext(); };
	}

	private async Task Drain(float from, float to, float seconds, CancellationToken ct)
	{
		Sfx("blood_drain", 1, ToGlobal(new Vector3(0, from, 0)), 4f, 30f);
		double t = 0;
		while (t < seconds)
		{
			await Cutscene.Frame(this, ct);
			t += GetProcessDeltaTime();
			float u = Mathf.SmoothStep(0f, 1f, (float)(t / seconds));
			SetBlood(Mathf.Lerp(from, to, u));
			_bloodMat.SetShaderParameter("swirl", Mathf.Sin(u * Mathf.Pi) * 0.6f);
		}
		SetBlood(to);
		_bloodMat.SetShaderParameter("swirl", 0f);
	}

	// ------------------------------------------------------------------ the limbs

	/// <summary>How far through the fight (0..1), for how hard it comes at them.</summary>
	private float Progress01 => ValvesTurned / (float)ValvesToTurn;

	private void ScheduleSlams(PlayerController player, float dt)
	{
		_slamTimer -= dt;
		if (_slamTimer > 0f) return;
		float p = Progress01;
		int maxAtOnce = 2 + Mathf.FloorToInt(p * 3.6f);
		if (_telegraphs.Count >= maxAtOnce) { _slamTimer = 0.25f; return; }
		// it starts busy and goes wild: as it rots it comes faster, more at once, with less warning
		_slamTimer = Mathf.Lerp(5f, 1.6f, p) * _rng.RandfRange(0.8f, 1.2f);
		float tele = Mathf.Lerp(2.1f, 1.5f, p);
		// now and then (more as it goes) a barrage: a run of limbs down one after another along the
		// catwalk, a wave to time a way through
		_barrageTimer -= 1f;
		if (p > 0.34f && _barrageTimer <= 0f && _rng.Randf() < Mathf.Lerp(0.25f, 0.45f, p))
		{
			_barrageTimer = Mathf.Lerp(5f, 2f, p);
			_ = Barrage(player, tele, Mathf.RoundToInt(Mathf.Lerp(3f, 6f, p)));
			return;
		}
		// at them, often; at the lit valve they need to get to; and anywhere on the catwalk, raging
		Vector3 at;
		float roll = _rng.Randf();
		if (ActiveValve != null && roll < 0.3f)
			at = ToGlobal(ActiveValve.Position + (ActiveValve.Basis * Vector3.Forward) * _rng.RandfRange(1.2f, 2.4f) + ActiveValve.Basis.X * _rng.RandfRange(-1.5f, 1.5f));
		else if (roll < 0.75f)
			at = player.GlobalPosition + new Vector3(_rng.RandfRange(-0.8f, 0.8f), 0, _rng.RandfRange(-0.8f, 0.8f));
		else
			at = ToGlobal(RingLocal(_rng.RandfRange(0f, Perimeter)));
		Vector3 l = ToLocal(at);
		// on the deck, not in the wall or out over the pit
		Vector3 onRing = RingLocal(RingOf(l));
		Vector3 off = l - onRing;
		off.Y = 0f;
		l = onRing + off * 0.3f;
		l.Y = 0.02f;
		if (!Fair(l, SlamRadius, player)) { _slamTimer = 0.2f; return; }
		Telegraph(ToGlobal(l), tele, false);
	}

	/// <summary>Whether a slam of radius r at local point l is fair: no overlapping another (there's
	/// always catwalk between two), and if it would catch the player, they aren't already running from
	/// one and there is clear catwalk to run to on at least one side.</summary>
	private bool Fair(Vector3 l, float r, PlayerController player, bool barrage = false)
	{
		foreach (var (_, m, _) in _telegraphs)
			if (new Vector2(m.Position.X - l.X, m.Position.Z - l.Z).Length() < r + (float)m.GetMeta("radius") + 0.9f) return false;
		if (player == null) return true;
		Vector3 pl = ToLocal(player.GlobalPosition);
		float dp = new Vector2(pl.X - l.X, pl.Z - l.Z).Length();
		// a barrage is a wall to find a way round, never dropped right on them
		if (barrage && dp < r + 3.5f) return false;
		// while they're running from one, nothing new lands near them: one problem at a time
		foreach (var (_, m, _) in _telegraphs)
			if (new Vector2(m.Position.X - pl.X, m.Position.Z - pl.Z).Length() < (float)m.GetMeta("radius") + 0.8f && dp < r + 8f) return false;
		if (new Vector2(pl.X - l.X, pl.Z - l.Z).Length() > r + 0.5f) return true;
		foreach (var (_, m, _) in _telegraphs)
			if (new Vector2(m.Position.X - pl.X, m.Position.Z - pl.Z).Length() < (float)m.GetMeta("radius") + 0.5f) return false;
		float sp = RingOf(pl);
		foreach (float dir in new[] { -1f, 1f })
		{
			Vector3 esc = RingLocal(sp + dir * (r + 1.5f));
			bool clear = true;
			foreach (var (_, m, _) in _telegraphs)
				if (new Vector2(m.Position.X - esc.X, m.Position.Z - esc.Z).Length() < (float)m.GetMeta("radius") + 1.0f) clear = false;
			if (clear) return true;
		}
		return false;
	}

	private static Texture2D _ring;
	/// <summary>The telegraph's glow: a soft-edged disc with a brighter rim.</summary>
	private static Texture2D Ring()
	{
		if (_ring != null) return _ring;
		const int n = 64;
		var img = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
		for (int y = 0; y < n; y++)
			for (int x = 0; x < n; x++)
			{
				float r = new Vector2(x - n * 0.5f + 0.5f, y - n * 0.5f + 0.5f).Length() / (n * 0.5f);
				float a = r > 1f ? 0f : Mathf.Lerp(0.35f, 1f, Mathf.SmoothStep(0.6f, 0.95f, r)) * Mathf.SmoothStep(1f, 0.93f, r);
				img.SetPixel(x, y, new Color(1f, 1f, 1f, a));
			}
		img.GenerateMipmaps();
		return _ring = ImageTexture.CreateFromImage(img);
	}

	/// <summary>A run of slams stepping along the catwalk from near the player, a limb at a time.</summary>
	private async Task Barrage(PlayerController player, float tele, int count)
	{
		Barrages++;
		float s0 = RingOf(ToLocal(player.GlobalPosition)) + (_rng.Randf() < 0.5f ? -1f : 1f) * _rng.RandfRange(6f, 10f);
		float dir = _rng.Randf() < 0.5f ? -1f : 1f;
		for (int i = 0; i < count && State is Phase.Fight or Phase.Draining && !_dying; i++)
		{
			Vector3 l = RingLocal(s0 + dir * i * (SlamRadius * 2f + 1.3f));
			l.Y = 0.02f;
			if (Fair(l, SlamRadius, player, barrage: true)) Telegraph(ToGlobal(l), tele, false);
			await ToSignal(GetTree().CreateTimer(0.5, false), SceneTreeTimer.SignalName.Timeout);
		}
	}

	/// <summary>A limb rears up over a spot on the catwalk; the grating under it glows red; it comes down.</summary>
	private void Telegraph(Vector3 world, float seconds, bool demonstration)
	{
		var t = Beast.Slam(world, seconds);
		if (t == null) return;
		// a red glow thrown onto whatever is under it (the grating, the pipes), never out over the pit
		float r = SlamRadius;
		var marker = new Decal
		{
			Name = "Telegraph", Size = new Vector3(r * 2f, 3f, r * 2f), TextureAlbedo = Ring(), TextureEmission = Ring(),
			EmissionEnergy = 2f, Modulate = new Color(1f, 0.1f, 0.05f, 0f), UpperFade = 0.1f, LowerFade = 0.1f,
		};
		AddChild(marker);
		marker.GlobalPosition = world + Vector3.Up * 0.2f;
		marker.SetMeta("demo", demonstration);
		marker.SetMeta("total", seconds);
		marker.SetMeta("radius", r);
		_telegraphs.Add((t, marker, seconds + Leviathan.StrikeSeconds));
		Sfx("limb_rise", 3, world + Vector3.Down * 2f, -2f, 10f);
	}

	private void UpdateTelegraphs(float dt)
	{
		for (int i = _telegraphs.Count - 1; i >= 0; i--)
		{
			var (t, m, left) = _telegraphs[i];
			left -= dt;
			_telegraphs[i] = (t, m, left);
			float total = (float)m.GetMeta("total").AsDouble() + Leviathan.StrikeSeconds;
			// the glow builds steadily as it comes (no blinking)
			float u = Mathf.Clamp(1f - left / total, 0f, 1f);
			m.Modulate = new Color(1f, 0.1f, 0.05f, 0.25f + 0.7f * u);
			if (t.State is not (Leviathan.Tentacle.S.Raise or Leviathan.Tentacle.S.Hold or Leviathan.Tentacle.S.Strike)) { m.QueueFree(); _telegraphs.RemoveAt(i); }
		}
	}

	private void OnImpact(Vector3 at, Leviathan.Tentacle t)
	{
		Slams++;
		Sfx("catwalk_slam", 3, at, 6f, 10f);
		Splash(at);
		var player = StoryBeat.Player(this);
		if (player == null || _dying || State is Phase.Finale or Phase.Open or Phase.Done) return;
		float d = new Vector2(player.GlobalPosition.X - at.X, player.GlobalPosition.Z - at.Z).Length();
		if (d < 7f) _ = Shake(player, Mathf.Lerp(0.03f, 0.008f, d / 7f));
		bool demo = false;
		foreach (var (tt, m, _) in _telegraphs) if (tt == t && (bool)m.GetMeta("demo")) demo = true;
		// only what the red circle showed: it has to be fair
		float radius = SlamRadius;
		foreach (var (tt, m, _) in _telegraphs) if (tt == t) radius = (float)m.GetMeta("radius");
		bool hit = d < radius;
		if (hit && !demo && State is Phase.Fight or Phase.Draining)
		{
			GD.Print($"[act18dbg] crushed: {d:0.00} m from the centre (radius {radius:0.00}), {_telegraphs.Count} coming down, running {player.IsRunning}, speed {new Vector2(player.Velocity.X, player.Velocity.Z).Length():0.0}, stamina {player.Stamina?.CanRun}");
			_dying = true;
			_ = Cutscene.Run(this, ct => Crushed(player, ct), lockInput: true, freezeBody: true);
		}
	}

	private async Task Shake(PlayerController player, float amount)
	{
		var rig = player.CameraRig;
		double t = 0;
		while (t < 0.35)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			t += GetProcessDeltaTime();
			float k = amount * (1f - (float)(t / 0.35));
			rig.Shake = new Vector3(_rng.RandfRange(-1, 1), _rng.RandfRange(-1, 1), 0) * k;
		}
		rig.Shake = Vector3.Zero;
	}

	private void Splash(Vector3 at)
	{
		var pm = new ParticleProcessMaterial
		{
			Direction = Vector3.Up, Spread = 70f, InitialVelocityMin = 2f, InitialVelocityMax = 6f, Gravity = new Vector3(0, -9.8f, 0),
			ScaleMin = 0.8f, ScaleMax = 1.8f,
		};
		var p = new GpuParticles3D
		{
			Amount = 50, Lifetime = 1.3, OneShot = true, Explosiveness = 0.95f, ProcessMaterial = pm,
			DrawPass1 = new QuadMesh
			{
				Size = Vector2.One * 0.3f,
				Material = new StandardMaterial3D
				{
					AlbedoTexture = LakeParts.LakeFx.SoftDot(), AlbedoColor = new Color(0.4f, 0.02f, 0.02f, 0.95f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
					BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				},
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(p);
		p.GlobalPosition = at + Vector3.Up * 0.2f;
		p.Emitting = true;
		GetTree().CreateTimer(2.0).Timeout += p.QueueFree;
	}

	/// <summary>Hit: the view goes down with it, black, and back to Act 18's start.</summary>
	private async Task Crushed(PlayerController player, CancellationToken ct)
	{
		PlayerDeath.Begin();
		GD.Print("[story] Act 18: crushed on the catwalk");
		var rig = player.CameraRig;
		var tw = player.CreateTween();
		tw.TweenProperty(rig, "EyeHeight", 0.3f, 0.25f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
		await Cutscene.Wait(this, 0.25, ct);
		StoryBeat.Fader(this)?.SetBlack(true);
		rig.EyeHeight = 1.62f;
		await PlayerDeath.Reload(this, "It crushed you.", ct);
	}

	// ------------------------------------------------------------------ the end

	/// <summary>The last valve: the pit empties, every eye it has bursts in turn from the crown down, it
	/// shrieks and dies; then the far door opens on a lit room.</summary>
	private async Task Finale(PlayerController player, CancellationToken ct)
	{
		GD.Print($"[story] Act 18: the last valve - the fight took {FightSeconds / 60.0:0.0} min");
		foreach (var (_, m, _) in _telegraphs) m.QueueFree();
		_telegraphs.Clear();
		// to the rail, to look down into it
		Vector3 pl = ToLocal(player.GlobalPosition);
		Vector3 rail = RingLocal(RingOf(pl));
		Vector3 inward = new Vector3(-rail.X, 0, -rail.Z);
		rail += (Mathf.Abs(rail.X) > Mathf.Abs(rail.Z) ? new Vector3(Mathf.Sign(inward.X), 0, 0) : new Vector3(0, 0, Mathf.Sign(inward.Z))) * ((Half - CatIn) * 0.5f - 0.55f);
		Vector3 from = player.GlobalPosition, toRail = ToGlobal(rail with { Y = pl.Y });
		double tt = 0;
		while (tt < 1.4)
		{
			await Cutscene.Frame(this, ct);
			tt += GetProcessDeltaTime();
			player.GlobalPosition = from.Lerp(toRail, Mathf.SmoothStep(0f, 1f, (float)(tt / 1.4)));
		}
		await Look(player, Beast.GlobalPosition + Vector3.Up * 10f, 2.0f, ct);
		await Cutscene.Wait(this, 3.2, ct);   // the last of the blood going
		Sfx("creature_giant_rattle_loop", 1, Beast.GlobalPosition + Vector3.Up * 10f, 2f, 30f);
		// the eyes, one after another
		var eyes = Beast.EyesInBurstOrder();
		for (int i = 0; i < eyes.Count; i++)
		{
			Beast.Burst(eyes[i]);
			if (i % 3 == 0) Sfx(i % 2 == 0 ? "squelch_open" : "squelch_close", 2, eyes[i].GlobalPosition, 2f, 12f);
			await Cutscene.Wait(this, Mathf.Lerp(0.11f, 0.035f, i / (float)eyes.Count), ct);
		}
		await Cutscene.Wait(this, 0.4, ct);
		// it shrieks, and dies
		Sfx("creature_screech", 1, Beast.GlobalPosition + Vector3.Up * 10f, 8f, 40f);
		Sfx("creature_giant_bellow", 1, Beast.GlobalPosition + Vector3.Up * 10f, 6f, 40f);
		Beast.Die();
		await Cutscene.Wait(this, 5.0, ct);
		// quiet; and across the pit the door opens, light coming out of it
		State = Phase.Open;
		await Look(player, DoorWorld + Vector3.Up * 1.2f, 1.8f, ct);
		Sfx("steel_door_open", 1, DoorWorld, 4f, 20f);
		var open = CreateTween().SetParallel();
		open.TweenProperty(_door, "position", _door.Position + Vector3.Up * 2.5f, 2.4f).SetTrans(Tween.TransitionType.Sine);
		open.TweenProperty(_tidyLight, "light_energy", 1.6f, 1.5f);
		await Cutscene.Wait(this, 2.6, ct);
		DoorOpen = true;
		GD.Print("[story] Act 18: it is dead; the door on the far side is open");
	}

	private void OnTidyRoom(PlayerController player)
	{
		if (!DoorOpen || Finished) return;
		Finished = true;
		State = Phase.Done;
		StoryBeat.ReachCheckpoint(player, Checkpoint.Act18Finished);
		GD.Print("[story] Act 18 done: into the clean, lit room - Act 19 starts here");
		// Act 19 isn't built yet: a moment in the light, then the credits
		_ = Cutscene.Run(this, async ct =>
		{
			await Cutscene.Wait(this, 5.0, ct);
			var fader = StoryBeat.Fader(this);
			if (fader != null) await fader.Fade(1f, 2.5f, ct);
			if (GetTree().GetFirstNodeInGroup("act11_ending") is Act11Ending ending) await ending.Credits(fader, ct);
		});
	}

	// ------------------------------------------------------------------ for tests

	/// <summary>Brings a limb down on a spot (tests: the death).</summary>
	public void TestSlamAt(Vector3 world, float telegraph) => Telegraph(world, telegraph, false);

	// ------------------------------------------------------------------ sound

	private void Sfx(string name, int variants, Vector3 at, float db, float unit = 5f) => SfxPlayer(name, variants, at, db, unit);

	private AudioStreamPlayer3D SfxPlayer(string name, int variants, Vector3 at, float db, float unit)
	{
		string path = $"res://assets/audio/sfx/{name}_{_rng.RandiRange(1, variants):00}.wav";
		if (!ResourceLoader.Exists(path)) path = $"res://assets/audio/sfx/{name}.wav";
		if (!ResourceLoader.Exists(path)) path = $"res://assets/audio/ambient/{name}.wav";
		if (!ResourceLoader.Exists(path)) return null;
		var s = new AudioStreamPlayer3D { Stream = GD.Load<AudioStream>(path), Bus = "Events", VolumeDb = db, UnitSize = unit, MaxDistance = 120f, PitchScale = _rng.RandfRange(0.94f, 1.05f) };
		Cutscene.SceneRoot(this).AddChild(s);
		s.GlobalPosition = at;
		s.Finished += s.QueueFree;
		s.Play();
		return s;
	}
}
