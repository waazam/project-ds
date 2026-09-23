using System.Collections.Generic;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Act 13, Room 3: three old coins, each a different metal and face, sitting in plain sight around
/// the room; a small stone pedestal with three coin-sized sockets waits by the far wall. Collecting
/// all three (E) and using the pedestal (E) finishes the room - and Act 13's whole chain of
/// puzzles, at which point <see cref="StationInterior"/> reaches the final checkpoint.
///
/// Local space: floor y=0; its doorway back to the lobby is a gap on its -Z wall (Room 3 sits off
/// the lobby's back wall, by the desk - see StationInterior's layout comment).
/// </summary>
public partial class StationRoom3 : Node3D
{
	[Export] public float HalfWidth = 2.5f;
	[Export] public float HalfDepth = 2.5f;
	[Export] public float Height = 3.0f;
	[Export] public float DoorGapX = 0f;

	/// <summary>For tests: how many of the three coins have been picked up.</summary>
	public int CoinsCollected { get; private set; }
	/// <summary>For tests: the pedestal has taken all three coins and the room (and Act 13) is done.</summary>
	public bool Solved { get; private set; }

	private Interactable _pedestalUse;
	private readonly List<Color> _coinColors = new()
	{
		new Color(0.75f, 0.76f, 0.78f), // silver
		new Color(0.75f, 0.62f, 0.25f), // gold
		new Color(0.55f, 0.38f, 0.22f), // copper
	};

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
		k.Color = new Color(0.39f, 0.36f, 0.32f);
		StationKit.WallAlongX(k, body, -HalfDepth, -HalfWidth, HalfWidth, Height, 0, (DoorGapX, 2.2f));
		StationKit.WallAlongX(k, body, HalfDepth, -HalfWidth, HalfWidth, Height, 0, null);
		StationKit.WallAlongZ(k, body, -HalfWidth, -HalfDepth, HalfDepth, Height, 0, null);
		StationKit.WallAlongZ(k, body, HalfWidth, -HalfDepth, HalfDepth, Height, 0, null);
		floorK.Mat(BuildingTextures.FloorMat);
		floorK.Color = new Color(0.47f, 0.43f, 0.37f);
		ceilK.Mat(wall);
		ceilK.Color = new Color(0.27f, 0.25f, 0.22f);
		StationKit.FloorAndCeiling(floorK, ceilK, body, HalfWidth, HalfDepth, Height, 0);
		k.Color = Colors.White;
		k.CommitTo(this, "Walls");
		floorK.Color = Colors.White;
		floorK.CommitTo(this, "Floor");
		ceilK.Color = Colors.White;
		ceilK.CommitTo(this, "Ceiling", false);

		AddChild(new OmniLight3D
		{
			Name = "RoomLamp", LightColor = new Color(0.95f, 0.85f, 0.65f), LightEnergy = 1.9f,
			OmniRange = 7f, OmniAttenuation = 1.2f, Position = new Vector3(0, Height - 0.25f, 0),
		});

		BuildCoin(0, new Vector3(-1.5f, 0.02f, -1.2f));
		BuildCoin(1, new Vector3(1.6f, 0.75f, -1.6f));
		BuildCoin(2, new Vector3(0.2f, 0.02f, 1.4f));
		BuildPedestal(new Vector3(-1.7f, 0, 1.7f));
	}

	private void BuildCoin(int index, Vector3 at)
	{
		var coin = new Node3D { Name = $"Coin{index}", Position = at };
		AddChild(coin);
		var k = new MeshKit();
		k.Mat(ItemTextures.BrassMat);
		k.Color = _coinColors[index];
		k.Cylinder(new Vector3(0, -0.004f, 0), new Vector3(0, 0.004f, 0), 0.055f, 0.055f, 14, true);
		k.Color = _coinColors[index] * 0.85f;
		k.Cylinder(new Vector3(0, 0.004f, 0), new Vector3(0, 0.008f, 0), 0.03f, 0.03f, 10, true);
		k.Color = Colors.White;
		k.CommitTo(coin, "CoinMesh", true);

		var use = new Interactable { Name = "Use", Prompt = "Take the coin", PickRadius = 0.4f, MaxDistance = 2.0f };
		use.Interacted += _ => OnCoinTaken(coin, use);
		coin.AddChild(use);
	}

	private void OnCoinTaken(Node3D coin, Interactable use)
	{
		if (!use.Enabled) return;
		use.Enabled = false;
		CoinsCollected++;
		PlayOneShot("res://assets/audio/sfx/radio_tick_01.wav", -2f, 1.3f);
		coin.QueueFree();
		GD.Print($"[story] Act 13, room 3: coin {CoinsCollected}/3 collected");
	}

	private void BuildPedestal(Vector3 at)
	{
		var ped = new Node3D { Name = "Pedestal", Position = at };
		AddChild(ped);
		var k = new MeshKit();
		k.Mat(ProcTextures.RockMat);
		k.Color = new Color(0.45f, 0.43f, 0.4f);
		k.Cylinder(new Vector3(0, 0, 0), new Vector3(0, 0.75f, 0), 0.28f, 0.22f, 10, true);
		k.Mat(ItemTextures.BrassMat);
		k.Color = new Color(0.55f, 0.46f, 0.28f);
		for (int i = 0; i < 3; i++)
		{
			float a = Mathf.Tau * i / 3f;
			Vector3 slot = new Vector3(Mathf.Cos(a) * 0.12f, 0.755f, Mathf.Sin(a) * 0.12f);
			k.Cylinder(slot + Vector3.Up * 0.002f, slot, 0.06f, 0.06f, 12, false);
		}
		k.Color = Colors.White;
		k.CommitTo(ped, "PedestalMesh", true);

		var body = new StaticBody3D { Name = "PedestalBody", CollisionLayer = 1, CollisionMask = 0 };
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, 0.38f, 0), Shape = new CylinderShape3D { Radius = 0.26f, Height = 0.76f } });
		ped.AddChild(body);

		_pedestalUse = new Interactable { Name = "Use", Prompt = "Set the coins in place", PickRadius = 0.55f, MaxDistance = 2.2f, Position = new Vector3(0, 0.8f, 0) };
		_pedestalUse.Interacted += OnPedestalUsed;
		ped.AddChild(_pedestalUse);
	}

	private void OnPedestalUsed(PlayerController player)
	{
		if (Solved || CoinsCollected < 3) return;
		Solved = true;
		if (_pedestalUse != null) _pedestalUse.Enabled = false;
		PlayOneShot("res://assets/audio/sfx/newel_seat.wav", 2f, 1f);
		StoryBeat.ReachCheckpoint(player, Checkpoint.Act13StationSolved);
		GD.Print("[story] Act 13: the pedestal takes all three coins - the station is solved");
		if (GetTree().GetFirstNodeInGroup("act11_ending") is Act11Ending ending)
			_ = Cutscene.Run(this, ct => ending.Credits(StoryBeat.Fader(this), ct), lockInput: true);
	}

	private void PlayOneShot(string path, float volumeDb, float pitch)
	{
		if (!ResourceLoader.Exists(path)) return;
		var s = new AudioStreamPlayer3D { Stream = GD.Load<AudioStream>(path), Bus = "Events", VolumeDb = volumeDb, PitchScale = pitch, UnitSize = 2f, MaxDistance = 15f };
		AddChild(s);
		s.Finished += s.QueueFree;
		s.Play();
	}
}
