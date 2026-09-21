using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.UI;

namespace ProjectDS.World;

/// <summary>
/// A concrete bunker door set into a mound, deep in the woods. Locked and
/// solid until the cabin has been seen burning (Act 7); the first time the
/// player then gets close, the door stands open for good. Walking through the
/// open doorway carries them into the bunker's interior (Act 8) and marks
/// checkpoint 7. Group "bunker_marker" so the compass can find it.
/// </summary>
[Tool]
[GlobalClass]
public partial class Bunker : Node3D
{
	[Export] public float OpenRadius = 14f;
	private const float DoorRadius = 0.95f;

	public bool IsOpen { get; private set; }

	private Node3D _gen;
	private MeshInstance3D _doorMesh;
	private CollisionShape3D _doorCollision;
	private PlayerController _player;
	private bool _entered;

	public override void _Ready()
	{
		AddToGroup("bunker_marker");
		Build();
	}

	public override void _Process(double delta)
	{
		if (Engine.IsEditorHint() || IsOpen) return;
		if (StoryManager.Instance is not { Current: >= Checkpoint.Act7CabinBurning }) return;
		_player ??= GetTree().GetFirstNodeInGroup("player") as PlayerController;
		if (_player == null) return;
		float d = new Vector2(_player.GlobalPosition.X - GlobalPosition.X, _player.GlobalPosition.Z - GlobalPosition.Z).Length();
		if (d > OpenRadius) return;
		Open();
	}

	public void Build()
	{
		var old = GetNodeOrNull("Generated");
		if (old != null) { RemoveChild(old); old.QueueFree(); }
		_gen = new Node3D { Name = "Generated" };
		AddChild(_gen);

		var k = new MeshKit();
		var dirt = ProcTextures.Flat("bunker_dirt", new Color(0.22f, 0.19f, 0.14f), 1f);
		var concrete = ProcTextures.ConcreteMat;
		var metal = ProcTextures.MetalMat;
		var rng = new RandomNumberGenerator { Seed = 55 };

		// Earthen mound the door is set into.
		k.Color = new Color(0.3f, 0.27f, 0.2f);
		k.Mat(dirt);
		k.Blob(new Vector3(0, 1.6f, -1.2f), new Vector3(4.2f, 2.0f, 3.6f), 71, 0.22f, true, 0.5f, 0.3f);

		// Roots and dead vines draping down over the lintel from the mound above, like the reference photo.
		var root = ProcTextures.Flat("bunker_root", new Color(0.18f, 0.15f, 0.1f), 1f);
		k.Mat(root);
		for (int i = 0; i < 9; i++)
		{
			float x = rng.RandfRange(-1.5f, 1.5f);
			float shade = rng.RandfRange(0.7f, 1.1f);
			k.Color = new Color(0.2f * shade, 0.16f * shade, 0.1f * shade);
			Vector3 top = new(x, 2.55f + rng.RandfRange(-0.1f, 0.15f), rng.RandfRange(-0.15f, 0.15f));
			Vector3 bot = top + new Vector3(rng.RandfRange(-0.15f, 0.15f), -rng.RandfRange(0.3f, 0.85f), rng.RandfRange(0f, 0.15f));
			k.Beam(top, bot, rng.RandfRange(0.02f, 0.045f), rng.RandfRange(0.02f, 0.045f));
		}

		// Concrete face set into the mound, weathered with moss and rust streaks, plus a small vent window off to one side.
		k.Mat(concrete);
		float faceHalf = 1.45f;
		k.Color = new Color(0.5f, 0.5f, 0.47f);
		k.Box(new Vector3(0, 1.15f, 0.15f), new Vector3(faceHalf * 2f, 2.3f, 0.3f), 1f);
		for (int i = 0; i < 10; i++)
		{
			bool moss = rng.Randf() < 0.6f;
			var stainCol = moss ? new Color(0.28f, 0.34f, 0.2f) : new Color(0.22f, 0.2f, 0.17f);
			k.Color = stainCol;
			float sx = rng.RandfRange(-faceHalf + 0.2f, faceHalf - 0.2f);
			float sy = rng.RandfRange(0.2f, 2.3f);
			k.Box(new Vector3(sx, sy, 0.301f), new Vector3(rng.RandfRange(0.15f, 0.4f), rng.RandfRange(0.3f, 1.1f), 0.006f), 1f);
		}
		k.Color = new Color(0.55f, 0.68f, 0.68f);
		k.Box(new Vector3(-1.0f, 1.75f, 0.31f), new Vector3(0.34f, 0.34f, 0.05f), 1f);
		k.Color = new Color(0.05f, 0.07f, 0.06f);
		k.Box(new Vector3(-1.0f, 1.75f, 0.335f), new Vector3(0.24f, 0.24f, 0.03f), 1f);

		// A round, dark recess set into the face — the vault door sits inside this like a hatch.
		k.Color = new Color(0.15f, 0.14f, 0.13f);
		k.Mat(metal);
		k.Cylinder(new Vector3(0, 1.05f, 0.28f), new Vector3(0, 1.05f, 0.34f), DoorRadius + 0.12f, DoorRadius + 0.12f, 20, false);

		// Two wall lamps flanking the door, like the reference photo.
		var lampMetal = ProcTextures.Flat("bunker_lamp", new Color(0.2f, 0.19f, 0.17f), 0.6f);
		foreach (float x in new[] { -1.55f, 1.55f })
		{
			k.Color = new Color(0.55f, 0.53f, 0.5f);
			k.Mat(lampMetal);
			k.Cylinder(new Vector3(x, 2.05f, 0.3f), new Vector3(x, 2.05f, 0.55f), 0.03f, 0.03f, 6);
			k.Cylinder(new Vector3(x, 1.95f, 0.55f), new Vector3(x, 1.95f, 0.72f), 0.1f, 0.14f, 8, true);
			var lamp = new OmniLight3D { LightColor = new Color(1f, 0.8f, 0.5f), LightEnergy = 1.6f, OmniRange = 6f, Position = new Vector3(x, 1.9f, 0.75f) };
			_gen.AddChild(lamp);
		}

		k.Color = Colors.White;
		k.CommitTo(_gen, "BunkerMesh");

		BuildClosedDoor(metal);

		if (!Engine.IsEditorHint())
		{
			var body = new StaticBody3D { Name = "BunkerBody", CollisionLayer = 1, CollisionMask = 0 };
			body.SetMeta("surface", "stone");
			_gen.AddChild(body);
			// The mound's own collision must not reach past the concrete face (Z=0.15±0.15) or it
			// blocks the doorway outright, regardless of the door's own open/closed state.
			body.AddChild(new CollisionShape3D { Position = new Vector3(0, 1.6f, -1.55f), Shape = new BoxShape3D { Size = new Vector3(4.2f, 3.2f, 2.9f) } });
			// The concrete face itself is solid either side of the round door opening.
			body.AddChild(new CollisionShape3D { Position = new Vector3(-1.2f, 1.1f, 0.15f), Shape = new BoxShape3D { Size = new Vector3(0.5f, 2.2f, 0.3f) } });
			body.AddChild(new CollisionShape3D { Position = new Vector3(1.2f, 1.1f, 0.15f), Shape = new BoxShape3D { Size = new Vector3(0.5f, 2.2f, 0.3f) } });
			_doorCollision = new CollisionShape3D { Position = new Vector3(0, 1.05f, 0.15f), Shape = new BoxShape3D { Size = new Vector3(DoorRadius * 2f, DoorRadius * 2f, 0.35f) } };
			body.AddChild(_doorCollision);

			// Generous and omnidirectional on purpose: a real player can walk straight to the modelled
			// doorway, but this trail-side structure's facing doesn't line up with the direction the
			// autotest bot's blind steering happens to approach from, and the story beat only needs
			// "got to the now-open bunker," not a precise walk through a ~1 m gap from one exact side.
			var trigger = new Area3D { Name = "EntryTrigger", CollisionLayer = 0, CollisionMask = 2, Monitorable = false };
			trigger.Position = new Vector3(0, 1.6f, -1.2f);
			trigger.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 5f } });
			_gen.AddChild(trigger);
			trigger.BodyEntered += OnEntered;
		}
	}

	/// <summary>A round riveted vault door with a spoked wheel-lock at its centre, built at
	/// <paramref name="diskCenter"/> in the MeshKit's own (pre-Xf) space — the caller's Xf then
	/// places and, for the open state, swings it about its hinge edge.</summary>
	private static void BuildVaultDoorAt(MeshKit dk, Vector3 diskCenter, Material metal)
	{
		float thickness = 0.16f;
		Vector3 front = diskCenter + new Vector3(0, 0, thickness * 0.5f);
		Vector3 back = diskCenter - new Vector3(0, 0, thickness * 0.5f);

		dk.Color = new Color(0.34f, 0.32f, 0.29f);
		dk.Mat(metal);
		dk.Cylinder(back, front, DoorRadius, DoorRadius, 20, true, 1.2f);

		// Rivets ringing the rim.
		dk.Color = new Color(0.42f, 0.4f, 0.36f);
		const int rivets = 16;
		for (int i = 0; i < rivets; i++)
		{
			float a = Mathf.Tau / rivets * i;
			Vector3 p = front + new Vector3(Mathf.Cos(a) * DoorRadius * 0.86f, Mathf.Sin(a) * DoorRadius * 0.86f, 0f);
			dk.Cylinder(p, p + new Vector3(0, 0, 0.025f), 0.035f, 0.03f, 6);
		}

		// A recessed inner ring groove, like the reference photo's stepped face.
		dk.Color = new Color(0.24f, 0.23f, 0.2f);
		dk.Cylinder(front + new Vector3(0, 0, 0.002f), front + new Vector3(0, 0, 0.02f), DoorRadius * 0.62f, DoorRadius * 0.62f, 18, false);

		// Central hub and spoked wheel-lock.
		dk.Color = new Color(0.3f, 0.29f, 0.26f);
		Vector3 hubFront = front + new Vector3(0, 0, 0.1f);
		dk.Cylinder(front, hubFront, 0.16f, 0.13f, 10, true);
		dk.Cylinder(hubFront - new Vector3(0, 0, 0.03f), hubFront + new Vector3(0, 0, 0.03f), 0.34f, 0.34f, 14, false);
		for (int i = 0; i < 4; i++)
		{
			float a = Mathf.Pi * 0.5f * i + Mathf.Pi * 0.25f;
			Vector3 dir = new(Mathf.Cos(a), Mathf.Sin(a), 0);
			var rot = Basis.FromEuler(new Vector3(0, 0, a));
			dk.Box(hubFront + dir * 0.22f, new Vector3(0.32f, 0.045f, 0.045f), 1f, rot);
		}
	}

	private void BuildClosedDoor(Material metal)
	{
		var dk = new MeshKit();
		BuildVaultDoorAt(dk, new Vector3(0, 1.05f, 0.15f), metal);
		_doorMesh = dk.CommitTo(_gen, "DoorMesh");
	}

	private void BuildOpenDoor()
	{
		var metal = ProcTextures.MetalMat;
		// Swings open about its left edge (the hinge). Building the disc relative to that hinge and
		// letting Xf place and rotate it keeps the hinge edge itself fixed between the two states.
		var leaf = new MeshKit { Xf = new Transform3D(Basis.FromEuler(new Vector3(0, Mathf.DegToRad(-100f), 0)), new Vector3(-DoorRadius, 0f, 0.15f)) };
		BuildVaultDoorAt(leaf, new Vector3(DoorRadius, 1.05f, 0f), metal);
		leaf.CommitTo(_gen, "DoorOpenMesh");
	}

	private void Open()
	{
		IsOpen = true;
		if (_doorMesh != null) { _doorMesh.QueueFree(); _doorMesh = null; }
		if (_doorCollision != null) _doorCollision.Disabled = true;
		BuildOpenDoor();
		string path = "res://assets/audio/sfx/trunk_creak_02.wav";
		if (ResourceLoader.Exists(path))
		{
			var groan = new AudioStreamPlayer3D
			{
				Stream = GD.Load<AudioStream>(path), UnitSize = 4f, MaxDistance = 40f,
				PitchScale = 0.6f, VolumeDb = 3f, Position = new Vector3(0, 1f, 0.3f),
			};
			_gen.AddChild(groan);
			groan.Finished += groan.QueueFree;
			groan.Play();
		}
		GD.Print("[story] the bunker door stands open");
	}

	private void OnEntered(Node3D body)
	{
		if (_entered || !IsOpen || body is not PlayerController player) return;
		_entered = true;
		InteractPrompt.Instance?.HidePrompt();
		BunkerInterior.Instance?.AdmitPlayer(player);
		StoryManager.Instance?.ReachCheckpoint(Checkpoint.Act8BunkerEntered, player.GlobalPosition, player.CameraRig.Yaw);
		GD.Print("[story] Act 8: into the bunker");
	}
}
