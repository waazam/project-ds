using System.Collections.Generic;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.UI;
using ProjectDS.World.BunkerParts;

namespace ProjectDS.World;

/// <summary>
/// Act 13, Room 1: three walls of hurried, scrawled red writing (DO NOT LOOK AT THEM / DO NOT
/// TOUCH THEM / NEVER GO UP THEM) and a duct-taped cigar box on the floor. Cutting the box's three
/// sides (left, right, then the front - <see cref="TapeCutOverlay"/>) opens it on a button; pressing
/// the button (E) melts the writing into a blood puddle at each wall's foot and unlocks Room 2.
///
/// Local space: floor y=0; its doorway back to the lobby is a gap on the wall StationInterior
/// tells it to build (Room 1 sits off the lobby's right side in this layout, so its own -X wall
/// carries the gap — see StationInterior's layout comment).
/// </summary>
public partial class StationRoom1 : Node3D
{
	[Export] public float HalfWidth = 2.5f;
	[Export] public float HalfDepth = 2.5f;
	[Export] public float Height = 3.0f;
	[Export] public float DoorGapZ = 0f;

	public const string Wall1 = "DO NOT LOOK AT THEM";
	public const string Wall2 = "DO NOT TOUCH THEM";
	public const string Wall3 = "NEVER GO UP THEM";

	/// <summary>For tests: how many of the box's three sides have been cut (0..3).</summary>
	public int BoxCutsDone { get; private set; }
	/// <summary>For tests: the box is open and the button showing.</summary>
	public bool BoxOpen { get; private set; }
	/// <summary>For tests: the button has been pressed and the room is solved.</summary>
	public bool Solved { get; private set; }

	private readonly List<Label3D> _wallText = new();
	private Node3D _box, _lid, _button;
	private Interactable _boxUse, _buttonUse;

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
		k.Color = new Color(0.42f, 0.38f, 0.34f);
		StationKit.WallAlongZ(k, body, -HalfWidth, -HalfDepth, HalfDepth, Height, 0, (DoorGapZ, 2.2f));
		StationKit.WallAlongX(k, body, HalfDepth, -HalfWidth, HalfWidth, Height, 0, null);
		StationKit.WallAlongX(k, body, -HalfDepth, -HalfWidth, HalfWidth, Height, 0, null);
		StationKit.WallAlongZ(k, body, HalfWidth, -HalfDepth, HalfDepth, Height, 0, null);
		floorK.Mat(BuildingTextures.FloorMat);
		floorK.Color = new Color(0.5f, 0.46f, 0.4f);
		ceilK.Mat(wall);
		ceilK.Color = new Color(0.3f, 0.28f, 0.25f);
		StationKit.FloorAndCeiling(floorK, ceilK, body, HalfWidth, HalfDepth, Height, 0);
		k.Color = Colors.White;
		k.CommitTo(this, "Walls");
		floorK.Color = Colors.White;
		floorK.CommitTo(this, "Floor");
		ceilK.Color = Colors.White;
		ceilK.CommitTo(this, "Ceiling", false);

		AddChild(new OmniLight3D
		{
			Name = "RoomLamp", LightColor = new Color(0.85f, 0.7f, 0.62f), LightEnergy = 1.5f,
			OmniRange = 6.5f, OmniAttenuation = 1.2f, Position = new Vector3(0, Height - 0.25f, 0),
		});

		float eye = Height * 0.5f + 0.1f;
		var textColor = new Color(0.62f, 0.04f, 0.03f);
		_wallText.Add(SignKit.Text(this, Wall1, new Vector3(0, eye, HalfDepth - 0.02f), new Basis(Vector3.Up, Mathf.Pi), 0.42f, textColor, shadow: false));
		_wallText.Add(SignKit.Text(this, Wall2, new Vector3(HalfWidth - 0.02f, eye, 0), new Basis(Vector3.Up, -Mathf.Pi / 2f), 0.38f, textColor, shadow: false));
		_wallText.Add(SignKit.Text(this, Wall3, new Vector3(0, eye, -HalfDepth + 0.02f), Basis.Identity, 0.4f, textColor, shadow: false));

		BuildBox(new Vector3(0, 0, 0.3f));
	}

	// ------------------------------------------------------------------ the cigar box

	private void BuildBox(Vector3 at)
	{
		_box = new Node3D { Name = "CigarBox", Position = at };
		AddChild(_box);
		const float hw = 0.22f, hd = 0.14f, hh = 0.075f;
		var k = new MeshKit();
		k.Mat(BuildingTextures.BoardsMat);
		k.Color = new Color(0.35f, 0.24f, 0.14f);
		BuildKit.Box(k, new Vector3(0, hh, 0), new Vector3(hw * 2f, hh * 2f, hd * 2f), 3f, BuildKit.Face.PY);
		k.Color = Colors.White;
		k.CommitTo(_box, "BoxMesh", true);

		_lid = new Node3D { Name = "Lid", Position = new Vector3(0, hh * 2f, 0) };
		_box.AddChild(_lid);
		var lk = new MeshKit();
		lk.Mat(BuildingTextures.BoardsMat);
		lk.Color = new Color(0.38f, 0.26f, 0.15f);
		BuildKit.Box(lk, Vector3.Zero, new Vector3(hw * 2f + 0.01f, 0.015f, hd * 2f + 0.01f), 3f);
		lk.Color = Colors.White;
		lk.CommitTo(_lid, "LidMesh", true);

		// three taped seams: left (vertical), right (vertical), front (horizontal).
		var tapeMat = BuildingTextures.Plain("s_tape2", new Color(0.72f, 0.62f, 0.36f), 0.7f);
		var tapes = new MeshInstance3D[3];
		void Tape(int i, Vector3 pos, Vector3 size, Basis b)
		{
			var tk = new MeshKit();
			tk.Mat(tapeMat);
			tk.Color = Colors.White;
			tk.Box(pos, size, 3f, b);
			tapes[i] = tk.CommitTo(_box, $"Tape{i}", false);
		}
		Tape(0, new Vector3(-hw, hh, 0), new Vector3(0.02f, hh * 2.1f, hd * 1.6f), Basis.Identity);
		Tape(1, new Vector3(hw, hh, 0), new Vector3(0.02f, hh * 2.1f, hd * 1.6f), Basis.Identity);
		Tape(2, new Vector3(0, hh, hd), new Vector3(hw * 1.6f, hh * 2.1f, 0.02f), Basis.Identity);

		var body = new StaticBody3D { Name = "BoxBody", CollisionLayer = 1, CollisionMask = 0 };
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, hh, 0), Shape = new BoxShape3D { Size = new Vector3(hw * 2f, hh * 2f, hd * 2f) } });
		_box.AddChild(body);

		_boxUse = new Interactable { Name = "Use", Prompt = "Cut the tape", PickRadius = 0.5f, MaxDistance = 2.2f, Position = new Vector3(0, hh, 0) };
		_boxUse.Interacted += OnBoxUsed;
		_box.AddChild(_boxUse);

		Vector3 boxWorld = _box.GlobalPosition;
		Transform3D Look(Vector3 focus, Vector3 eyeOffset)
		{
			Vector3 eye = focus + eyeOffset;
			return new Transform3D(Basis.LookingAt(focus - eye, Vector3.Up), eye);
		}
		_leftCam = Look(boxWorld + new Vector3(-hw, hh, 0), new Vector3(-0.35f, 0.15f, 0.35f));
		_rightCam = Look(boxWorld + new Vector3(hw, hh, 0), new Vector3(0.35f, 0.15f, 0.35f));
		_frontCam = Look(boxWorld + new Vector3(0, hh, hd), new Vector3(0, 0.2f, 0.5f));
		_tapes = tapes;
	}

	private Transform3D _leftCam, _rightCam, _frontCam;
	private MeshInstance3D[] _tapes;

	private void OnBoxUsed(PlayerController player)
	{
		if (BoxOpen || TapeCutOverlay.Instance == null) return;
		var cuts = new List<TapeCutOverlay.CutSpec>
		{
			new() { CameraView = _leftCam, Direction = Vector2.Down, PixelsNeeded = 220f,
				OnProgress = p => { if (_tapes[0] != null && IsInstanceValid(_tapes[0])) _tapes[0].Scale = new Vector3(1f, 1f - p, 1f); },
				OnCut = () => CutSide(0) },
			new() { CameraView = _rightCam, Direction = Vector2.Down, PixelsNeeded = 220f,
				OnProgress = p => { if (_tapes[1] != null && IsInstanceValid(_tapes[1])) _tapes[1].Scale = new Vector3(1f, 1f - p, 1f); },
				OnCut = () => CutSide(1) },
			new() { CameraView = _frontCam, Direction = Vector2.Right, PixelsNeeded = 220f,
				OnProgress = p => { if (_tapes[2] != null && IsInstanceValid(_tapes[2])) _tapes[2].Scale = new Vector3(1f - p, 1f, 1f); },
				OnCut = () => CutSide(2) },
		};
		TapeCutOverlay.Instance.Open(player, cuts, OpenBox);
	}

	private void CutSide(int i)
	{
		BoxCutsDone++;
		if (_tapes[i] != null && IsInstanceValid(_tapes[i])) _tapes[i].QueueFree();
	}

	private void OpenBox()
	{
		BoxOpen = true;
		if (_boxUse != null) _boxUse.Enabled = false;
		var tween = CreateTween();
		tween.TweenProperty(_lid, "position:y", _lid.Position.Y + 0.16f, 0.4f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
		tween.Parallel().TweenProperty(_lid, "rotation:x", Mathf.DegToRad(-70f), 0.4f);

		_button = new Node3D { Name = "Button", Position = new Vector3(0, 0.16f, 0) };
		_box.AddChild(_button);
		var bk = new MeshKit();
		bk.Mat(BuildingTextures.IronMat);
		bk.Color = new Color(0.2f, 0.2f, 0.2f);
		bk.Cylinder(new Vector3(0, -0.01f, 0), new Vector3(0, 0.01f, 0), 0.045f, 0.045f, 8, true);
		bk.Color = new Color(0.55f, 0.05f, 0.04f);
		bk.Cylinder(new Vector3(0, 0.01f, 0), new Vector3(0, 0.025f, 0), 0.035f, 0.035f, 8, true);
		bk.Color = Colors.White;
		bk.CommitTo(_button, "ButtonMesh", true);

		_buttonUse = new Interactable { Name = "Press", Prompt = "Press the button", PickRadius = 0.35f, MaxDistance = 1.8f, Position = new Vector3(0, 0.03f, 0) };
		_buttonUse.Interacted += OnButtonPressed;
		_button.AddChild(_buttonUse);
		GD.Print("[story] Act 13, room 1: the box is open on a button");
	}

	private void OnButtonPressed(PlayerController player)
	{
		if (Solved) return;
		Solved = true;
		if (_buttonUse != null) _buttonUse.Enabled = false;
		if (_button != null) _button.Position = _button.Position with { Y = _button.Position.Y - 0.015f };
		PlayOneShot("res://assets/audio/sfx/step_stone_01.wav", 4f, 1.6f);
		MeltWalls();
		StoryManager.Instance?.SetFlag(StoryManager.Flag.StationRoom1Solved);
		GD.Print("[story] Act 13, room 1: solved - the writing melts");
	}

	private void MeltWalls()
	{
		Vector3[] bases = { new(0, 0.02f, HalfDepth - 0.05f), new(HalfWidth - 0.05f, 0.02f, 0), new(0, 0.02f, -HalfDepth + 0.05f) };
		Vector3[] normals = { Vector3.Back, Vector3.Left, Vector3.Forward };
		for (int i = 0; i < _wallText.Count; i++)
		{
			var label = _wallText[i];
			if (label == null || !IsInstanceValid(label)) continue;
			var tween = CreateTween();
			tween.TweenProperty(label, "modulate:a", 0f, 2.2f).SetDelay(i * 0.3f);
			tween.TweenCallback(Callable.From(() => { if (IsInstanceValid(label)) label.QueueFree(); }));
			BunkerKit.AddDecal(this, BunkerTextures.Puddle(), bases[i], Vector3.Up, normals[i],
				new Vector2(0.9f, 0.9f), 0.3f, new Color(0.35f, 0.02f, 0.02f, 0.85f));
		}
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
