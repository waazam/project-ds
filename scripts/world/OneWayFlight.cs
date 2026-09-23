using Godot;
using ProjectDS.Player;

namespace ProjectDS.World;

/// <summary>
/// A staircase that will not let you back down. Attached to a <see cref="StaircaseBuilder"/>
/// (<see cref="Attach"/>), it watches the player on the flight and, once they are a couple of
/// treads up, keeps an invisible wall just behind them: it follows them up and never comes
/// back down, so turning round (or backing down while facing up) meets a wall where the way
/// down was. The cheek walls stop the sides. Used by the first staircase (Act 1), the last one
/// (Act 11) and every leg of the clearing's loop (Act 6).
///
/// The wall is a StaticBody3D box across the clear width of the flight plus its walls, thick
/// enough that no stride passes through it, its foot on the tread <see cref="Behind"/> metres
/// below the player's highest tread, placed from that progress every physics frame in the
/// staircase's own frame (the flight runs along -Z, rising <c>Rise</c> per <c>Run</c>). Progress
/// only ever grows, whatever the player faces or presses.
/// </summary>
public partial class OneWayFlight : Node3D
{
	/// <summary>Treads climbed before the wall appears (so stepping on and off the first step still works).</summary>
	[Export] public int ArmAfterTreads = 2;
	/// <summary>How far behind the player's highest tread the wall stands (metres along the flight). About a
	/// stride and a half: backing down a tread or two is not possible (Dan, 2026-09-22).</summary>
	[Export] public float Behind = 0.45f;

	/// <summary>The wall is up (the player has climbed far enough).</summary>
	public bool Armed { get; private set; }
	/// <summary>Highest point reached along the flight, in metres from the first step (for tests). Never decreases.</summary>
	public float MaxProgress { get; private set; }

	private StaircaseBuilder _stairs;
	private PlayerController _player;
	private StaticBody3D _wall;
	private CollisionShape3D _shape;

	/// <summary>Adds a one-way flight to a staircase (once; returns the existing one on a repeat).</summary>
	public static OneWayFlight Attach(StaircaseBuilder stairs)
	{
		if (stairs == null) return null;
		if (stairs.GetNodeOrNull<OneWayFlight>("OneWay") is { } existing) return existing;
		var f = new OneWayFlight { Name = "OneWay" };
		stairs.AddChild(f);
		return f;
	}

	/// <summary>Takes the wall down and frees this (the leg is over; a new Attach starts afresh).</summary>
	public void Detach()
	{
		Name = "OneWayGone";
		if (_shape != null) _shape.Disabled = true;
		QueueFree();
	}

	public override void _Ready()
	{
		_stairs = GetParent<StaircaseBuilder>();
		_wall = new StaticBody3D { Name = "Wall", CollisionLayer = 1, CollisionMask = 0 };
		_wall.SetMeta("surface", "stone");
		AddChild(_wall);
		// The full clear width plus both cheek walls, 3 m tall, 0.6 m thick: nothing steps or slides past it.
		_shape = new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(_stairs.Width + 2f * _stairs.WallThickness + 0.4f, 3f, 0.6f) }, Disabled = true };
		_wall.AddChild(_shape);
	}

	/// <summary>Tread height of the flight at a point z along it (local): the ramp the collider is.</summary>
	private float HeightAt(float z)
	{
		float treads = Mathf.Clamp(-z / Mathf.Max(_stairs.Run, 0.01f), 0f, _stairs.Steps);
		return treads * _stairs.Rise;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_player == null || !IsInstanceValid(_player))
		{
			_player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
			if (_player == null) return;
		}
		Vector3 p = _stairs.ToLocal(_player.GlobalPosition);
		float flightLen = _stairs.Steps * _stairs.Run;
		bool onFlight = Mathf.Abs(p.X) < _stairs.Width * 0.5f + 0.4f && p.Z < 0.4f && p.Z > _stairs.BackZ - 1.5f && p.Y > HeightAt(p.Z) - 1.0f;
		if (onFlight) MaxProgress = Mathf.Max(MaxProgress, -p.Z);
		bool arm = MaxProgress >= ArmAfterTreads * _stairs.Run;
		if (arm != Armed)
		{
			Armed = arm;
			_shape.Disabled = !arm;
		}
		if (!Armed) return;
		// The wall's near face sits Behind metres below the highest tread reached; its foot on that tread,
		// clamped so it never stands past the top landing.
		float z = Mathf.Min(Mathf.Max(-MaxProgress + Behind, -flightLen + 0.2f), 0.2f);
		float y = HeightAt(z);
		_wall.Position = new Vector3(0, y + 1.3f, z + 0.3f);
	}
}
