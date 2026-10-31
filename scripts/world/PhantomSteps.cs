using Godot;
using ProjectDS.Player;

namespace ProjectDS.World;

/// <summary>
/// Footsteps that aren't theirs (2026-10-31, the owner, yes to: "in the tunnels, a second set of steps echoes half a beat
/// behind the player and stops when they stop"). Down in the tunnels, now and then for a spell, each of their footfalls is
/// answered: the same step, a little heavier, a few metres behind them, a moment late. When they stop, it takes one more
/// step, sometimes two, and then it's quiet too. Never all the time (it would be wallpaper): a spell of a dozen-odd steps,
/// then a long while of nothing. Laid with the graffiti, one to an underground area (RenderBudget), only while they're
/// inside it.
/// </summary>
public partial class PhantomSteps : Node3D
{
	private Aabb _bounds;
	private PlayerController _player;
	private PlayerFootsteps _steps;
	private readonly RandomNumberGenerator _rng = new();
	private double _now, _nextSpell, _lastOwnStep = -10;
	private int _left;                 // steps left in this spell
	private int _afterStop;            // steps it still takes once they've stopped
	private double _afterAt;
	private AudioStream _lastStream;
	/// <summary>For tests: answers given.</summary>
	public int Answered { get; private set; }

	public static void Haunt(Node3D root, Aabb bounds, int seed)
	{
		if (root == null || !IsInstanceValid(root) || root.HasMeta("phantom_steps")) return;
		root.SetMeta("phantom_steps", true);
		var p = new PhantomSteps { Name = "PhantomSteps", _bounds = bounds.Grow(2f) };
		p._rng.Seed = (ulong)seed;
		p._nextSpell = p._rng.RandfRange(12f, 30f);
		root.AddChild(p);
	}

	public override void _Process(double delta)
	{
		_now += delta;
		if (_player == null || !IsInstanceValid(_player))
		{
			_player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
			_steps = _player?.GetNodeOrNull<PlayerFootsteps>("Footsteps");
			if (_steps != null) _steps.Stepped += OnStep;
			return;
		}
		if (!_bounds.HasPoint(_player.GlobalPosition)) { _left = 0; _afterStop = 0; return; }
		// a spell begins (while they're walking: it falls in with them)
		if (_left <= 0 && _afterStop <= 0 && _now >= _nextSpell && _now - _lastOwnStep < 0.8)
		{
			_left = _rng.RandiRange(9, 18);
			GD.Print($"[story] footsteps behind them, in step ({GetParent().Name}, {_left} steps)");
		}
		// they've stopped: the last step or two of its own, then nothing
		if (_afterStop > 0 && _now >= _afterAt)
		{
			Answer(_rng.RandfRange(2.5f, 4f));
			_afterStop--;
			_afterAt = _now + _rng.RandfRange(0.55f, 0.75f);
			if (_afterStop == 0) EndSpell();
		}
		else if (_left > 0 && _now - _lastOwnStep > 0.9 && _afterStop == 0)
		{
			_afterStop = _rng.Randf() < 0.35f ? 2 : 1;
			_afterAt = _now + 0.45;
		}
	}

	private void OnStep()
	{
		_lastOwnStep = _now;
		_lastStream = _steps?.LastStep;
		if (_left <= 0 || _afterStop > 0 || _player == null) return;
		float delay = _rng.RandfRange(0.22f, 0.32f);   // (half a beat behind)
		GetTree().CreateTimer(delay).Timeout += () => { if (IsInstanceValid(this)) Answer(_rng.RandfRange(3f, 5.5f)); };
		if (--_left <= 0) EndSpell();
	}

	private void EndSpell()
	{
		_left = 0;
		_nextSpell = _now + _rng.RandfRange(45f, 90f);
	}

	/// <summary>A step on their own ground, <paramref name="back"/> m behind them, heavier.</summary>
	private void Answer(float back)
	{
		if (_lastStream == null || _player == null || !IsInstanceValid(_player)) return;
		var cam = _player.CameraRig?.Camera;
		Vector3 fwd = cam != null ? (-cam.GlobalBasis.Z with { Y = 0 }).Normalized() : -_player.GlobalBasis.Z;
		var at = _player.GlobalPosition - fwd * back + fwd.Cross(Vector3.Up) * _rng.RandfRange(-0.4f, 0.4f);
		var s = new AudioStreamPlayer3D
		{
			Stream = _lastStream, Bus = "Events", VolumeDb = -9f, UnitSize = 2.2f, MaxDistance = 18f, PitchScale = _rng.RandfRange(0.84f, 0.9f),
		};
		AddChild(s);
		s.GlobalPosition = at;
		s.Finished += s.QueueFree;
		s.Play();
		Answered++;
	}
}
