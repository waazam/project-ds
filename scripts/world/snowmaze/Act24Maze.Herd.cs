using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Entities;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World.SnowMaze;

/// <summary>
/// (2026-10-10b, the owner: "could potentially make the player be forced to go into the tunnels by the wendigo instead of
/// them just following it inside") Out of the splintered doorway and off the porch, there's nowhere to go but the drift:
/// it comes out of the trees behind them, rears and screams, and comes on. Not fast while they keep going (a stalking
/// walk, a few metres back, cutting off the way round), faster if they turn away or dawdle; caught, it's over (and the act
/// starts again at the doorway). As they go down into the hole it comes at the mouth behind them, and they hear it at the
/// top as the dark closes over. Then, inside, it follows them down, after a while (<see cref="StartHunt"/>).
/// </summary>
public partial class Act24Maze
{
	public Wendigo Herder { get; private set; }
	public bool Herding { get; private set; }
	public bool HerdCaught { get; private set; }
	public int HerdShrieks { get; private set; }
	private float _herdStep, _herdStall, _herdGround;
	private Vector3 _herdVel;
	private bool _followedIn;
	public bool FollowedIn => _followedIn;
	private const float HerdScale = 0.85f;

	/// <summary>Off the porch: it's behind them.</summary>
	private void HerdProcess(PlayerController player, float dt)
	{
		if (InMaze || _handing || HerdCaught || S == null || S.Current != Checkpoint.Act23Finished || PlayerDeath.Dying) return;
		var l = _lodge.ToLocal(player.GlobalPosition);
		bool off = l.Z > SkiLodge.Apothem + 7f;   // (into the snow tunnel, past the porch)
		if (Herder == null)
		{
			if (!off || !player.PlayerInput.Enabled) return;
			StartHerd(player);
			return;
		}
		if (!Herding || !IsInstanceValid(Herder)) return;
		var me = Herder.GlobalPosition;
		var them = player.GlobalPosition;
		var toHole = (_holeWorld - them) with { Y = 0 };
		var flatTo = (them - me) with { Y = 0 };
		float d = flatTo.Length();
		// going for the hole (their move toward it) or not
		float toward = player.Velocity with { Y = 0 } is var v && v.LengthSquared() > 0.2f && toHole.LengthSquared() > 0.1f ? v.Normalized().Dot(toHole.Normalized()) : 0f;
		_herdStall = toward > 0.3f ? Mathf.Max(0f, _herdStall - dt) : _herdStall + dt;
		// it keeps a few metres back while they go, comes on faster when they don't (or turn away)
		float speed = d > 11f ? 3.6f : toward > 0.3f ? 2.4f : Mathf.Lerp(2.8f, 4.6f, Mathf.Clamp(_herdStall / 4f, 0f, 1f));
		// at the mouth it holds back: it wants them in, not caught
		bool atMouth = toHole.Length() < 5f;
		if (atMouth) speed = Mathf.Min(speed, 1.0f);
		// aim at them, but from the side away from the hole: it herds rather than chases
		var aim = them - (toHole.LengthSquared() > 0.01f ? toHole.Normalized() * 1.5f : Vector3.Zero);
		var want = ((aim - me) with { Y = 0 }).Normalized() * speed;
		_herdVel = _herdVel.MoveToward(want, 6f * dt);
		var np = me + _herdVel * dt;
		_herdGround -= dt;
		if (_herdGround <= 0f)
		{
			_herdGround = 0.15f;
			var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(np + Vector3.Up * 6f, np + Vector3.Down * 12f, 1));
			if (hit.Count > 0) np.Y = ((Vector3)hit["position"]).Y;
		}
		Herder.GlobalPosition = np;
		if (_herdVel.LengthSquared() > 0.04f) Herder.GlobalBasis = new Basis(Herder.GlobalBasis.GetRotationQuaternion().Slerp(Basis.LookingAt(_herdVel.Normalized(), Vector3.Up).GetRotationQuaternion(), Mathf.Min(1f, dt * 5f)));
		float s = _herdVel.Length();
		Herder.Gait(s > 3.3f ? "run" : s > 0.3f ? "walk" : "idle", s > 3.3f ? s / (3.5f * HerdScale) * Herder.ClipLength("run") : s / (2.55f * HerdScale) * Herder.ClipLength("walk"), 0.3);
		_herdStep -= s * dt;
		if (_herdStep <= 0f)
		{
			_herdStep = 1.15f * HerdScale;
			AudioDirector.OneShot(this, "wendigo_step", 6, np, s > 3.3f ? -2f : -8f, "Events", 5f, 0.08f);
			WinterWoods.Instance?.LayClaws(np, _herdVel, 0.6f);   // (the tunnel's floor is the snow)
		}
		// too slow, or turned back on it: it has them
		if (d < (atMouth ? 1.2f : 2.3f)) _ = Cutscene.Run(this, ct => HerdCatch(player, ct), lockInput: true, freezeBody: true);
	}

	private void StartHerd(PlayerController player)
	{
		// (2026-10-10b) out of the lodge's doorway behind them: it came through the lodge after them
		var them = player.GlobalPosition;
		var at = _lodge.ToGlobal(new Vector3(0, 0.15f, SkiLodge.Apothem + 0.9f));
		Herder = new Wendigo { Name = "Herder" };
		AddChild(Herder);
		Herder.SetSize(HerdScale);
		Herder.StandAt(at, them);
		Herder.Play("crouch", 1.2f, 0.1);
		Herding = true;
		HerdShrieks++;
		_herdVel = Vector3.Zero;
		AudioDirector.OneShot(this, "wendigo_howl_03", 1, at + Vector3.Up * 3f, 6f, "Unnatural", 14f, 0.02f);
		PlayerBreathing.Startle(1f);
		_ = StoryBeat.Caption(this, "Go. Go.", 0.3f, 1.6f, 0.8f);
		GD.Print("[story] Act 24: the wendigo out of the trees behind them - it drives them to the drift");
		// (a moment's rear before it comes)
		var hold = GetTree().CreateTimer(1.4);
		Herding = false;
		hold.Timeout += () => { if (IsInstanceValid(Herder)) Herding = true; };
	}

	private async Task HerdCatch(PlayerController player, CancellationToken ct)
	{
		if (HerdCaught || PlayerDeath.Dying) return;
		HerdCaught = true;
		Herding = false;
		PlayerDeath.Begin();
		GD.Print("[story] Act 24: it caught them before the drift");
		AudioDirector.OneShot(this, "wendigo_howl_03", 1, Herder.GlobalPosition + Vector3.Up * 3f, 6f, "Unnatural", 8f, 0.02f);
		Herder.Play("pounce", 0.5f, 0.05);
		var from = Herder.GlobalPosition;
		var to = player.GlobalPosition + (from - player.GlobalPosition).Normalized() * 0.9f;
		double t = 0;
		while (t < 0.45)
		{
			await Cutscene.Frame(this, ct);
			t += GetProcessDeltaTime();
			Herder.GlobalPosition = from.Lerp(to, (float)Mathf.Min(1.0, t / 0.4));
		}
		StoryBeat.Fader(this)?.SetBlack(true);
		await PlayerDeath.Reload(this, "It drove you down. You were too slow.", ct);
	}

	/// <summary>Going down: it comes at the mouth behind them, and they hear it at the top as the dark closes.</summary>
	private void HerdAtTheMouth()
	{
		if (Herder == null || !IsInstanceValid(Herder)) return;
		Herding = false;
		var mouth = _holeWorld + (_lodge.GlobalPosition - _holeWorld).Normalized() * 2.4f;
		Herder.Leap(mouth, 0.5f, () => { }, stay: true, arc: 0.8f);
		AudioDirector.OneShot(this, "wendigo_howl_04", 1, _holeWorld + Vector3.Up * 2f, 4f, "Unnatural", 10f, 0.02f);
		_followedIn = true;
		var gone = GetTree().CreateTimer(3.0);
		gone.Timeout += () => { if (IsInstanceValid(Herder)) Herder.QueueFree(); Herder = null; };
	}
}
