using System.Collections.Generic;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Entities;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// A loose thing on the floor that goes over when it's walked into (2026-10-10): a bottle, a tin can, a chair. Bumped,
/// it's shoved off the way it was hit, rolls or topples, and clatters (glass clinks, tin rattles, a chair scrapes and
/// knocks), and the noise carries: whatever is hunting nearby may hear it (<see cref="Lures"/>).
/// A tin can (or a bottle) can be <see cref="Takeable"/>: picked up, it's carried in the hand (<see cref="HeldProp"/>) to be
/// thrown (G): where it comes down, it clatters, and draws what's listening to that spot instead.
/// They're real bodies (they fall, roll and come to rest on what's under them) but never in the way: you walk through
/// what you've knocked over rather than catching on it.
/// </summary>
public partial class Knockable : RigidBody3D
{
	public const uint PropLayer = 1u << 5;
	public enum Sound { Glass, Tin, Chair }

	public string Model = "bottle_wine";
	public Dictionary<string, Material> Roles;
	public Sound Kind = Sound.Glass;
	public Vector3 Size = new(0.09f, 0.3f, 0.09f);
	public bool Takeable;
	public string TakePrompt = "Take the tin";
	/// <summary>How far its noise carries (metres, along whatever it passes through).</summary>
	public float NoiseReach = 14f;
	/// <summary>Thrown: its first landing is the loud one (and the lure).</summary>
	public bool Thrown;
	public int Knocks { get; private set; }

	private Area3D _bump;
	private double _coolUntil, _soundUntil;
	private bool _landed;
	private Interactable _take;

	public override void _Ready()
	{
		CollisionLayer = PropLayer;
		CollisionMask = 1 | PropLayer;
		Mass = Kind == Sound.Chair ? 5f : 0.4f;
		ContactMonitor = true;
		MaxContactsReported = 2;
		ContinuousCd = Thrown;
		AngularDamp = Kind == Sound.Chair ? 2.5f : 0.6f;
		LinearDamp = 0.2f;
		PhysicsMaterialOverride = new PhysicsMaterial { Friction = Kind == Sound.Chair ? 0.9f : 0.55f, Bounce = Kind == Sound.Tin ? 0.3f : 0.15f };
		if (Kind == Sound.Chair)
			AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = Size }, Position = Vector3.Up * Size.Y * 0.5f });
		else
			AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = Size.X * 0.5f, Height = Size.Y }, Position = Vector3.Up * Size.Y * 0.5f });
		if (Roles != null) FurnitureKit.Clean(() => FurnitureKit.Place(this, Model, Transform3D.Identity, Roles, "Model"));
		// the bump: the player's body walking into it
		_bump = new Area3D { Name = "Bump", CollisionLayer = 0, CollisionMask = 2, Monitorable = false };
		float r = Mathf.Max(Size.X, Size.Z) * 0.5f + 0.3f;
		_bump.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = r, Height = Mathf.Max(Size.Y, 0.6f) }, Position = Vector3.Up * Mathf.Max(Size.Y, 0.6f) * 0.5f });
		AddChild(_bump);
		BodyEntered += OnContact;
		if (Takeable)
		{
			_take = new Interactable { Name = "Take", Prompt = TakePrompt, PickRadius = 0.3f, MaxDistance = 2.2f, Position = Vector3.Up * Size.Y * 0.5f };
			_take.Interacted += Take;
			AddChild(_take);
		}
		_landed = !Thrown;
	}

	private void Take(PlayerController player)
	{
		var held = HeldProp.For(player);
		if (held == null || held.Has) return;
		held.Carry(Model, Roles, Kind, Size);
		QueueFree();
	}

	public override void _PhysicsProcess(double delta)
	{
		double now = Time.GetTicksMsec() / 1000.0;
		if (now < _coolUntil) return;
		foreach (var b in _bump.GetOverlappingBodies())
		{
			if (b is not PlayerController p || p.GroundSpeed < 0.6f) continue;
			// shoved off the way it was walked into
			var dir = (GlobalPosition - p.GlobalPosition) with { Y = 0 };
			var v = p.Velocity with { Y = 0 };
			var push = (dir.Normalized() * 0.5f + v.Normalized()).Normalized();
			Sleeping = false;
			float k = Kind == Sound.Chair ? 6f : 0.9f;
			ApplyImpulse(push * k * Mathf.Clamp(p.GroundSpeed / 2.7f, 0.6f, 1.8f) + Vector3.Up * k * 0.15f, Vector3.Up * Size.Y * 0.7f);
			_coolUntil = now + 0.8;
			Knocks++;
			Noise(Kind == Sound.Chair ? 1f : 0.8f);
			break;
		}
	}

	private void OnContact(Node body)
	{
		double now = Time.GetTicksMsec() / 1000.0;
		float speed = LinearVelocity.Length();
		if (!_landed)
		{
			// a thrown one coming down: the loud clatter, and what's listening comes to it
			_landed = true;
			Noise(1.4f);
			Lures.Noise(GlobalPosition, NoiseReach * 1.4f, Kind == Sound.Glass ? "a bottle" : "a can");
			return;
		}
		if (speed > 1.2f && now > _soundUntil) Noise(Mathf.Clamp(speed / 3f, 0.3f, 0.8f), lure: false);
	}

	private void Noise(float loud, bool lure = true)
	{
		double now = Time.GetTicksMsec() / 1000.0;
		_soundUntil = now + 0.35;
		string name = Kind switch { Sound.Glass => "bottle_clink", Sound.Tin => "can_clatter", _ => "chair_scrape" };
		int n = Kind == Sound.Chair ? 2 : 3;
		AudioDirector.OneShot(this, name, n, GlobalPosition, -10f + 8f * Mathf.Clamp(loud, 0f, 1.4f), "Events", 3f, 0.08f);
		if (lure) Lures.Noise(GlobalPosition, NoiseReach * loud, Kind switch { Sound.Glass => "a bottle knocked over", Sound.Tin => "a can kicked", _ => "a chair shoved" });
	}

	/// <summary>One put down in the world (local to <paramref name="parent"/>).</summary>
	public static Knockable Put(Node3D parent, string model, Dictionary<string, Material> roles, Sound kind, Vector3 at, float yaw, Vector3? size = null, bool takeable = false)
	{
		var k = new Knockable
		{
			Name = $"Loose_{model}", Model = model, Roles = roles, Kind = kind, Takeable = takeable,
			Size = size ?? kind switch { Sound.Chair => new Vector3(0.45f, 0.95f, 0.45f), Sound.Tin => new Vector3(0.08f, 0.11f, 0.08f), _ => new Vector3(0.08f, 0.3f, 0.08f) },
			Position = at, Rotation = new Vector3(0, yaw, 0),
		};
		parent.AddChild(k);
		return k;
	}
}
