using System.Collections.Generic;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.World.LodgeParts;

namespace ProjectDS.World;

/// <summary>
/// The lodge burns (2026-10-10c, the owner: "the wendigo doesnt kill the player in this cutscene and he smashes out the
/// door and leaves. The player raises his lantern towards the wendigo in this cutscene and it smacks it away causing the
/// ski lodge to go up in flames. The wendigo flees through the door as planned then the player now has urgency to follow
/// him because of the raging fire.").
///
/// The lantern bursts against the foot of the old Christmas tree by the front doors. Its oil runs burning across the
/// boards; the tree (years dry) goes up in a column; and from it the fire walks on through the hall on a schedule: the
/// presents, the banner over the doors, the buffet's cloth, the gallery's garlanded rail above, the wall under the
/// gallery, the walk itself, the rugs, toward the stair. Every patch is a <see cref="FireVfx"/> (the flames' new
/// volumes, embers, smoke, a warm light; only the tree's light throws shadows), each growing over a few seconds from
/// nothing. A roar and a crackle swell with it.
///
/// Once burning, it stays burning: loaded at the doorway (Act 23's end) or in the snow tunnel (Act 24), the hall is
/// already full of fire, its light pouring out of the doorway into the tunnel; and the doorway's heat turns them back
/// if they try to go back in.
/// </summary>
public partial class SkiLodge
{
	public const string FireFlag = "lodge_on_fire";
	public bool OnFire { get; private set; }
	public int FiresBurning => _fires.Count;
	private readonly List<(FireVfx fx, float grow, float target)> _fires = new();
	private double _fireT;
	private int _fireNext;
	private AudioStreamPlayer3D _fireRoar, _fireCrackle;
	private StaticBody3D _heatWall;
	private bool _heatWarned;

	/// <summary>The fire's walk through the hall: when (seconds from the lantern's burst), where (lodge-local, its
	/// base), how big a patch (FireVfx.Extent), and whether its light throws shadows.</summary>
	private static (float t, Vector3 at, Vector3 ext, bool shadows)[] Spread => new[]
	{
		(0.0f, TreeAt + new Vector3(0.6f, 0, -0.9f), new Vector3(1.1f, 0.5f, 1.0f), false),              // the oil across the boards
		(1.1f, TreeAt, new Vector3(2.4f, 4.6f, 2.4f), true),                                            // the tree (wider than it: its needles hid flames inside them)
		(3.4f, TreeAt + new Vector3(1.2f, 0, -1.4f), new Vector3(1.6f, 0.8f, 1.3f), false),             // the presents, the rug's edge
		(5.0f, new Vector3(-1.4f, 3.3f, HexIn - 0.45f), new Vector3(2.4f, 1.5f, 0.5f), false),          // the banner over the doors
		(6.6f, (HexAt(300f, BalconyIn) * 1f) with { Y = UpperY - 0.7f }, new Vector3(3.0f, 1.3f, 0.8f), false),   // the gallery's garlanded rail
		(8.2f, new Vector3(-4.0f, FloorY, 9.2f), new Vector3(2.4f, 1.0f, 0.9f), false),                 // the buffet's cloth
		(10.5f, new Vector3(-8.4f, FloorY, 3.6f), new Vector3(2.4f, 2.2f, 1.6f), false),                // the wall under the gallery
		(12.5f, (HexAt(300f, (HexIn + BalconyIn) * 0.5f)) with { Y = UpperY }, new Vector3(3.0f, 1.6f, 1.6f), false),   // up on the walk
		(15.0f, new Vector3(-2.6f, FloorY, 1.6f), new Vector3(2.6f, 0.6f, 2.4f), false),                // the great rug
		(18.0f, new Vector3(-6.4f, FloorY, -2.3f), new Vector3(2.2f, 1.2f, 2.0f), false),               // the sofas by the west wall
	};

	/// <summary>The lantern bursts here (lodge-local): the fire begins, and walks on.</summary>
	public void Ignite(Vector3 local)
	{
		if (OnFire) return;
		OnFire = true;
		StoryManager.Instance?.SetFlag(FireFlag);
		_fireT = 0;
		_fireNext = 0;
		AudioDirector.OneShot(this, "flame_ignite", 1, ToGlobal(local + Vector3.Up * 0.3f), 6f, "Events", 8f, 0.03f);
		StartFireSound();
		GD.Print("[story] Act 23: the lantern bursts against the Christmas tree - the lodge catches");
	}

	/// <summary>Already burning (a load at Act 23's end or in Act 24): every patch at full at once.</summary>
	private void IgniteInstant()
	{
		if (OnFire) return;
		OnFire = true;
		foreach (var _ in Spread) SpawnFire(full: true);
		StartFireSound();
		BuildHeatWall();
	}

	private void StartFireSound()
	{
		var at = ToGlobal(TreeAt + Vector3.Up * 2f);
		_fireRoar = new AudioStreamPlayer3D { Name = "FireRoar", Stream = GD.Load<AudioStream>("res://assets/audio/ambient/flame_roar_loop.wav"), Bus = "Events", VolumeDb = -30f, UnitSize = 9f, MaxDistance = 90f };
		_fireCrackle = new AudioStreamPlayer3D { Name = "FireCrackle", Stream = GD.Load<AudioStream>("res://assets/audio/ambient/fire_crackle_loop.wav"), Bus = "Events", VolumeDb = -24f, UnitSize = 7f, MaxDistance = 60f };
		foreach (var p in new[] { _fireRoar, _fireCrackle })
		{
			AddChild(p);
			p.GlobalPosition = at;
			p.Play();
		}
	}

	private void SpawnFire(bool full)
	{
		if (_fireNext >= Spread.Length) return;
		var (_, at, ext, shadows) = Spread[_fireNext];
		var fx = new FireVfx
		{
			Name = $"LodgeFire{_fireNext}", Extent = ext, Seed = 2300 + _fireNext, LightRange = 12f, LightEnergy = 3.4f, FlameScale = _fireNext == 1 ? 1.5f : 1f,
			LightShadows = shadows, SmokeAmount = 1.3f, Haze = _fireNext < 3,
		};
		fx.Position = at;
		AddChild(fx);
		fx.Intensity = full ? 1f : 0.02f;
		_fires.Add((fx, full ? 1f : 0.02f, 1f));
		_fireNext++;
	}

	/// <summary>Each frame while it burns: the next patches catch on time, each grows over a few seconds; the sound swells.</summary>
	private void FireTick(double delta)
	{
		if (!OnFire) return;
		_fireT += delta;
		while (_fireNext < Spread.Length && _fireT >= Spread[_fireNext].t) SpawnFire(full: false);
		for (int i = 0; i < _fires.Count; i++)
		{
			var (fx, grow, target) = _fires[i];
			if (!IsInstanceValid(fx)) continue;
			if (grow < target)
			{
				grow = Mathf.Min(target, grow + (float)delta / 3.2f);
				fx.Intensity = Mathf.SmoothStep(0f, 1f, grow);
				_fires[i] = (fx, grow, target);
			}
		}
		float size = Mathf.Clamp(_fires.Count / (float)Spread.Length, 0f, 1f);
		if (_fireRoar != null) _fireRoar.VolumeDb = Mathf.MoveToward(_fireRoar.VolumeDb, Mathf.Lerp(-14f, 2f, size), (float)delta * 6f);
		if (_fireCrackle != null) _fireCrackle.VolumeDb = Mathf.MoveToward(_fireCrackle.VolumeDb, Mathf.Lerp(-10f, 0f, size), (float)delta * 6f);
		// back in through the doorway, in Act 24: the heat turns them round
		if (_heatWall != null && !_heatWarned && FinaleDone == false && StoryManager.Instance is { Current: >= Checkpoint.Act23Finished } && StoryBeat.Player(this) is { } p && ToLocal(p.GlobalPosition) is var l && l.Z < Apothem + 0.2f && Mathf.Abs(l.X) < 2f && l.Z > Apothem - 1.2f)
		{
			_heatWarned = true;
			_ = StoryBeat.Caption(this, "The heat drives them back.", 0.3f, 2f, 0.8f);
			PlayerBreathing.Startle(0.5f);
		}
	}

	/// <summary>An invisible wall inside the doorway, once the hall is burning and the act has moved on: no going back in.</summary>
	private void BuildHeatWall()
	{
		if (_heatWall != null) return;
		_heatWall = new StaticBody3D { Name = "HeatWall", CollisionLayer = 1, CollisionMask = 0 };
		_heatWall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(4f, 4f, 0.4f) } });
		AddChild(_heatWall);
		_heatWall.Position = new Vector3(0, 2f, HexIn - 0.6f);
	}

	/// <summary>Called as the lodge is built and each act begins: burning from Act 23's end on, until the end of the story.</summary>
	private void FireForStory()
	{
		var s = StoryManager.Instance;
		if (s == null) return;
		if (s.Current >= Checkpoint.Act23Finished && s.Current < Checkpoint.Act24Finished) IgniteInstant();
	}
}
