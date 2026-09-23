using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.UI;

namespace ProjectDS.World;

/// <summary>
/// Act 13, Room 2: a four-digit code scratched into the wall by whoever was here before, and a
/// rusted keypad by the door to Room 3. Reuses <see cref="CodeLockOverlay"/> (the bunker's own
/// dial, generic enough for any code) rather than a new UI — E on the keypad holds it up, the
/// right code unlocks Room 3.
///
/// Local space: floor y=0; its doorway back to the lobby is a gap on the wall StationInterior
/// tells it to build (Room 2 sits off the lobby's left side, so its own +X wall carries the gap).
/// </summary>
public partial class StationRoom2 : Node3D
{
	[Export] public float HalfWidth = 2.5f;
	[Export] public float HalfDepth = 2.5f;
	[Export] public float Height = 3.0f;
	[Export] public float DoorGapZ = 0f;
	[Export] public string Code = "4271";

	/// <summary>For tests: the keypad has taken the right code.</summary>
	public bool Solved { get; private set; }

	private Interactable _use;

	public override void _Ready() => Callable.From(Build).CallDeferred();

	private void Build()
	{
		var k = new MeshKit();
		var floorK = new MeshKit();
		var ceilK = new MeshKit();
		var body = new StaticBody3D { Name = "Walls", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "wood");
		AddChild(body);

		var wall = BuildingTextures.BoardsMat;
		k.Mat(wall);
		k.Color = new Color(0.4f, 0.37f, 0.33f);
		StationKit.WallAlongZ(k, body, HalfWidth, -HalfDepth, HalfDepth, Height, 0, (DoorGapZ, 2.2f));
		StationKit.WallAlongX(k, body, HalfDepth, -HalfWidth, HalfWidth, Height, 0, null);
		StationKit.WallAlongX(k, body, -HalfDepth, -HalfWidth, HalfWidth, Height, 0, null);
		StationKit.WallAlongZ(k, body, -HalfWidth, -HalfDepth, HalfDepth, Height, 0, null);
		floorK.Mat(BuildingTextures.FloorMat);
		floorK.Color = new Color(0.48f, 0.44f, 0.38f);
		ceilK.Mat(wall);
		ceilK.Color = new Color(0.28f, 0.26f, 0.23f);
		StationKit.FloorAndCeiling(floorK, ceilK, body, HalfWidth, HalfDepth, Height, 0);
		k.Color = Colors.White;
		k.CommitTo(this, "Walls");
		floorK.Color = Colors.White;
		floorK.CommitTo(this, "Floor");
		ceilK.Color = Colors.White;
		ceilK.CommitTo(this, "Ceiling", false);

		AddChild(new OmniLight3D
		{
			Name = "RoomLamp", LightColor = new Color(0.95f, 0.82f, 0.6f), LightEnergy = 1.8f,
			OmniRange = 6.5f, OmniAttenuation = 1.2f, Position = new Vector3(0, Height - 0.25f, 0),
		});

		// The code, scratched into the back wall - carved so it stays legible in the dark.
		SignKit.Text(this, string.Join("  ", Code.ToCharArray()), new Vector3(0, 1.55f, -HalfDepth + 0.02f), Basis.Identity, 0.3f, new Color(0.6f, 0.58f, 0.52f));

		BuildKeypad(new Vector3(HalfWidth - 0.06f, 1.35f, HalfDepth - 0.6f));
	}

	private void BuildKeypad(Vector3 at)
	{
		var pad = new Node3D { Name = "Keypad", Position = at, Rotation = new Vector3(0, -Mathf.Pi / 2f, 0) };
		AddChild(pad);
		var k = new MeshKit();
		k.Mat(BuildingTextures.IronMat);
		k.Color = new Color(0.35f, 0.33f, 0.3f);
		BuildKit.Box(k, Vector3.Zero, new Vector3(0.24f, 0.32f, 0.03f), 4f);
		k.Color = new Color(0.15f, 0.15f, 0.14f);
		BuildKit.Box(k, new Vector3(0, 0.02f, 0.018f), new Vector3(0.18f, 0.2f, 0.006f), 4f);
		k.Mat(ItemTextures.BrassMat);
		k.Color = new Color(0.6f, 0.5f, 0.3f);
		for (int row = 0; row < 3; row++)
			for (int col = 0; col < 3; col++)
				BuildKit.Box(k, new Vector3((col - 1) * 0.052f, 0.08f - row * 0.052f, 0.023f), new Vector3(0.04f, 0.04f, 0.008f), 8f);
		k.Color = Colors.White;
		k.CommitTo(pad, "PadMesh", true);

		var body = new StaticBody3D { Name = "PadBody", CollisionLayer = 1, CollisionMask = 0 };
		body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.24f, 0.32f, 0.06f) } });
		pad.AddChild(body);

		_use = new Interactable { Name = "Use", Prompt = "Try the keypad", PickRadius = 0.5f, MaxDistance = 2.4f };
		_use.Interacted += OnUsed;
		pad.AddChild(_use);
	}

	private void OnUsed(PlayerController player)
	{
		if (Solved || CodeLockOverlay.Instance == null || CodeLockOverlay.Instance.IsOpen) return;
		CodeLockOverlay.Instance.Open(Code, player, OnSolved);
	}

	private void OnSolved()
	{
		if (Solved) return;
		Solved = true;
		if (_use != null) _use.Enabled = false;
		StoryManager.Instance?.SetFlag(StoryManager.Flag.StationRoom2Solved);
		GD.Print("[story] Act 13, room 2: the keypad takes the code");
	}
}
