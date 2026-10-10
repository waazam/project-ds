using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.World;
using ProjectDS.World.SnowMaze;

namespace ProjectDS.Entities;

/// <summary>
/// (2026-10-10) What draws it, and where it can't see:
/// <list type="bullet">
/// <item><b>Lures</b>: a thrown flare landing, a can or a bottle clattering down, something knocked over: it goes to look,
/// along the tunnels, if the noise reaches it. At a flare it stays, crouched over the light, sniffing at it, as long as it
/// burns (and a while after).</item>
/// <item><b>The hollows</b> (<see cref="MazeExtras"/>): crouched in one, it can't see you. If it comes by the mouth it
/// stops there and sniffs: hold your breath and it moves on; breathe (or gasp, held too long) and it has you.</item>
/// </list>
/// </summary>
public partial class WendigoHunter
{
	public int Lured { get; private set; }
	public int SniffedHollows { get; private set; }
	private Vector3 _lureAt;
	private float _sniffLeft, _sniffHeard, _hollowCool;
	private bool _sniffingHollow;

	public override void _EnterTree()
	{
		Flare.Landed += OnFlareLanded;
		Lures.Heard += OnNoise;
	}

	public override void _ExitTree()
	{
		Flare.Landed -= OnFlareLanded;
		Lures.Heard -= OnNoise;
	}

	private void OnFlareLanded(Vector3 world, float burn) => Lure(world, 34f, burn + 4f, "a flare");
	private void OnNoise(Vector3 world, float reach, string what) => Lure(world, reach, 3f, what);

	/// <summary>Something at <paramref name="world"/> it might hear (along the tunnels, within <paramref name="reach"/>):
	/// it goes to look, and stays <paramref name="linger"/> seconds sniffing at it.</summary>
	public bool Lure(Vector3 world, float reach, float linger, string what)
	{
		if (_cave == null || Body == null || Dead || Paused) return false;
		if (State is Mode.Chase or Mode.Ambush or Mode.Burning or Mode.Kill or Mode.Flee or Mode.Off) return false;
		var local = _cave.ToLocal(world);
		float along = TunnelDistance(Pos, local);
		if (along > reach) return false;
		Lured++;
		GD.Print($"[wendigo] lured by {what} ({along:0} m along the tunnels) - going to look");
		Pick(Mode.Investigate);
		_lureAt = MazeExtras.KeepOut(local);
		_sniffLeft = linger;
		_sniffingHollow = false;
		RouteTo(_lureAt);
		return true;
	}

	/// <summary>Back to wandering, aware of nothing (tests).</summary>
	public void Calm()
	{
		Awareness = 0f;
		_sniffLeft = 0f;
		_sniffingHollow = false;
		_route.Clear();
		State = Mode.Patrol;
		_modeT = 0f;
	}

	/// <summary>The player in a hollow, crouched, no flare showing.</summary>
	private bool PlayerHidden => MazeExtras.PlayerHidden(_cave, _player);

	/// <summary>Walking by a hollow with them in it: it stops at the mouth and sniffs.</summary>
	private void CheckHollows(float dt)
	{
		_hollowCool -= dt;
		if (_hollowCool > 0f || _sniffingHollow || !PlayerHidden) return;
		if (State is not (Mode.Patrol or Mode.Investigate or Mode.Search or Mode.Stalk)) return;
		if (!MazeExtras.InHollow(PlayerLocal, out var mouth)) return;
		if (Pos.DistanceTo(mouth) > 4.5f) return;
		SniffedHollows++;
		GD.Print("[wendigo] at a hollow's mouth - sniffing");
		Pick(Mode.Investigate);
		_route.Clear();
		_route.Add(mouth);
		_lureAt = mouth;
		_sniffLeft = _rng.RandfRange(4.5f, 6.5f);
		_sniffHeard = 0f;
		_sniffingHollow = true;
		_hollowCool = 25f;
		AudioDirector.OneShot(this, "wendigo_mumble", 4, Body.GlobalPosition + Vector3.Up * 2.5f, -6f, "Voice", 4f, 0.05f);
	}

	/// <summary>At what it came to look at (its route walked): crouched over it, sniffing, its head turning. At a hollow, it
	/// listens for their breath.</summary>
	private bool Sniff(float dt)
	{
		if (_sniffLeft <= 0f) return false;
		_sniffLeft -= dt;
		_vel = _vel.MoveToward(Vector3.Zero, 6f * dt);
		Face((_sniffingHollow ? PlayerLocal : _lureAt) - Pos, dt, 2.5f);
		if (_sniffingHollow)
		{
			bool breathing = !(PlayerBreathing.Of(_player)?.Holding ?? false);
			bool gasp = PlayerBreathing.Of(_player)?.Gasped ?? false;
			if (PlayerHidden && (breathing || gasp)) _sniffHeard += dt * (gasp ? 4f : 1f);
			if (_sniffHeard > 1.6f && !PlayerDeath.Dying)
			{
				GD.Print("[wendigo] heard them breathing in the hollow");
				_sniffLeft = 0f;
				_sniffingHollow = false;
				Shriek();
				_ = Cutscene.Run(this, ct => Catch(ct), lockInput: true, freezeBody: true);
				return true;
			}
			if (!PlayerHidden && CanSee(PlayerLocal)) { _sniffLeft = 0f; _sniffingHollow = false; return false; }
		}
		if (_sniffLeft <= 0f)
		{
			if (_sniffingHollow) GD.Print("[wendigo] heard nothing at the hollow - moving on");
			_sniffingHollow = false;
			Pick(Mode.Search);
			_searchLeft = _rng.RandfRange(6f, 10f);
			_searchStops = 3;
			RouteTo(L.Nodes[RandomNodeNear(Pos, 2)]);
		}
		return true;
	}
}

/// <summary>Noises in the world that something hunting might hear: a can clattering down, a bottle knocked over, a chair
/// shoved (see <see cref="Lure"/>).</summary>
public static class Lures
{
	public static event System.Action<Vector3, float, string> Heard;
	public static void Noise(Vector3 world, float reach, string what) => Heard?.Invoke(world, reach, what);
}
