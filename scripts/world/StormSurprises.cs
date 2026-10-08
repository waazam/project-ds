using System.Collections.Generic;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Now and then on the storm walk (the camp to the cabin), something in the dark you only hear (2026-10-07, for replay:
/// rare, grounded, never the same one twice in a walk). A minute or two apart at least; most walks hear two or three of
/// the pool, a different few each time:
/// <list type="bullet">
/// <item>a branch cracking somewhere far off, and the rush of it coming down;</item>
/// <item>something stepping in the mud off to one side, keeping pace, and stopping when they stop;</item>
/// <item>a trunk groaning nearby, though there's no wind to bend it;</item>
/// <item>something heavy dropping out of a tree onto the leaves behind them;</item>
/// <item>a twig snapping, close, just behind;</item>
/// <item>a knock on wood, three times, from somewhere in the trees.</item>
/// </list>
/// None of them is ever seen; none of them comes again. Indoors, in the silence round a stair, or out of the storm,
/// nothing.
/// </summary>
public partial class StormSurprises : Node3D
{
	public enum Kind { BranchFalls, MudSteps, TrunkGroan, HeavyDrop, TwigBehind, Knocks }
	public readonly List<Kind> Heard = new();

	private readonly RandomNumberGenerator _rng = new();
	private readonly List<Kind> _left = new();
	private float _wait;
	private PlayerController _player;
	// the mud steps: keeping pace off to one side, stopping when they stop
	private int _mudLeft;
	private float _mudT;
	private Vector3 _mudSide;

	public override void _Ready()
	{
		_rng.Randomize();
		foreach (Kind k in System.Enum.GetValues(typeof(Kind))) _left.Add(k);
		// (shuffled: each walk its own order)
		for (int i = _left.Count - 1; i > 0; i--) { int j = _rng.RandiRange(0, i); (_left[i], _left[j]) = (_left[j], _left[i]); }
		_wait = _rng.RandfRange(45f, 80f);
	}

	private static bool OnTheWalk => StoryManager.Instance is { } s && s.Current >= Checkpoint.Act2StairsClimbed && s.Current < Checkpoint.Act5CabinEntered;

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		_player ??= StoryBeat.Player(this);
		if (_player == null || !OnTheWalk || (Weather.Instance?.Rain ?? 0f) < 0.2f) return;
		if (_mudLeft > 0) { MudPace(dt); return; }
		if (GetTree().GetFirstNodeInGroup("atmosphere") is ForestAtmosphere { Interior: > 0.3f }) return;
		_wait -= dt;
		if (_wait > 0f || _left.Count == 0) return;
		_wait = _rng.RandfRange(70f, 140f);
		if (_rng.Randf() > 0.7f) return;   // (and sometimes, nothing)
		var k = _left[0];
		_left.RemoveAt(0);
		Heard.Add(k);
		Play(k);
	}

	private Vector3 Around(float dist, float deg)
	{
		var cam = GetViewport()?.GetCamera3D();
		var fwd = cam != null ? (-cam.GlobalBasis.Z) with { Y = 0 } : Vector3.Forward;
		if (fwd.LengthSquared() < 1e-4f) fwd = Vector3.Forward;
		return _player.GlobalPosition + fwd.Normalized().Rotated(Vector3.Up, Mathf.DegToRad(deg)) * dist + Vector3.Up * 1.2f;
	}

	private void Play(Kind k)
	{
		GD.Print($"[story] the storm walk: something in the dark - {k}");
		float side = _rng.Randf() < 0.5f ? -1f : 1f;
		switch (k)
		{
			case Kind.BranchFalls:
			{
				var at = Around(_rng.RandfRange(30f, 45f), side * _rng.RandfRange(40f, 140f)) + Vector3.Up * 6f;
				AudioDirector.OneShot(this, "tree_fall_crack", 2, at, -4f, "Events", 14f, 0.05f);
				var tw = CreateTween();
				tw.TweenInterval(0.5);
				tw.TweenCallback(Callable.From(() => AudioDirector.OneShot(this, "branch_drop", 3, at + Vector3.Down * 5f, -6f, "Events", 12f, 0.05f)));
				break;
			}
			case Kind.MudSteps:
				_mudLeft = _rng.RandiRange(6, 10);
				_mudT = 0.6f;
				_mudSide = Vector3.Up.Cross(((-GetViewport().GetCamera3D().GlobalBasis.Z) with { Y = 0 }).Normalized()) * side;
				break;
			case Kind.TrunkGroan:
				AudioDirector.OneShot(this, "trunk_creak", 3, Around(_rng.RandfRange(6f, 10f), side * _rng.RandfRange(60f, 120f)) + Vector3.Up * 2f, -6f, "Events", 6f, 0.04f);
				break;
			case Kind.HeavyDrop:
				AudioDirector.OneShot(this, "body_thump", 2, Around(_rng.RandfRange(8f, 12f), 180f + side * _rng.RandfRange(0f, 40f)), -4f, "Events", 6f, 0.05f);
				break;
			case Kind.TwigBehind:
				AudioDirector.OneShot(this, "twig_snap", 4, Around(_rng.RandfRange(3f, 5f), 180f + side * _rng.RandfRange(10f, 40f)) + Vector3.Down * 1f, -8f, "Events", 4f, 0.06f);
				break;
			case Kind.Knocks:
			{
				var at = Around(_rng.RandfRange(18f, 28f), side * _rng.RandfRange(30f, 150f));
				var tw = CreateTween();
				for (int i = 0; i < 3; i++)
				{
					tw.TweenCallback(Callable.From(() => AudioDirector.OneShot(this, "wall_knock", 3, at, -6f, "Events", 10f, 0.02f)));
					tw.TweenInterval(0.7);
				}
				break;
			}
		}
	}

	/// <summary>Steps in the mud off to one side, a stride for theirs; they stop, it stops (and, after a breath, goes).</summary>
	private void MudPace(float dt)
	{
		float speed = _player.GroundSpeed;
		if (speed < 0.3f) { _mudT += dt; if (_mudT > 4f) _mudLeft = 0; return; }   // (they stood still: it stopped; and then it's gone)
		_mudT -= dt * speed / 1.4f;
		if (_mudT > 0f) return;
		_mudT = 1f;
		_mudLeft--;
		AudioDirector.OneShot(this, "step_mud", 6, _player.GlobalPosition + _mudSide * _rng.RandfRange(7f, 10f) + Vector3.Down * 0.2f, -4f, "Events", 5f, 0.05f);
	}
}
