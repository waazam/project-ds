using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Act 16: the janitor's closet at the end of the long hallway (its save is <see cref="Checkpoint.Act15Finished"/>).
/// It is dark, and the handle can't be found in the dark: first the light switch by the door. The
/// light comes on with the hallway's low siren, red, then green, then settles to an ordinary
/// yellowish white over about six seconds, as if it had to remember what it was. Then the door, which
/// they came in by, opens onto nothing: pitch black where the hallway was. Stepping into it is
/// Act 16's end, and the black lifts on the sewer (<see cref="Sewer"/>, <see cref="Checkpoint.Act16Finished"/>).
/// </summary>
public partial class Act15Hallway
{
	public bool LightsOn { get; private set; }
	public bool LightsSettled { get; private set; }
	public bool ClosetDoorOpen { get; private set; }
	public bool Through { get; private set; }
	public Interactable SwitchUse => _switchUse;
	public Interactable InsideDoorUse => _insideUse;

	private OmniLight3D _closetLight;
	private StandardMaterial3D _closetBulb;
	private Interactable _switchUse, _insideUse;
	private Node3D _switchLever;
	private MeshInstance3D _void;
	private bool _warned;

	private void BuildClosetAct16()
	{
		float z0 = End + 0.2f, z1 = End + 0.2f + ClosetDepth;
		// a bare bulb on the ceiling
		_closetBulb = new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.19f, 0.16f), EmissionEnabled = true, Emission = Colors.Black };
		AddChild(new MeshInstance3D { Name = "ClosetBulb", Mesh = new SphereMesh { Radius = 0.05f, Height = 0.11f }, Position = new Vector3(0, 2.5f, (z0 + z1) * 0.5f), MaterialOverride = _closetBulb });
		_closetLight = new OmniLight3D
		{
			Name = "ClosetLight", Position = new Vector3(0, 2.35f, (z0 + z1) * 0.5f), LightEnergy = 0f, OmniRange = 4.5f,
			OmniAttenuation = 1.1f, ShadowEnabled = true, Visible = false,
		};
		AddChild(_closetLight);
		// the switch: its lever (the plate is in BuildCloset)
		Vector3 sw = new(DoorWidth * 0.5f + 0.25f, 1.25f, z0 + 0.035f);
		_switchLever = new Node3D { Name = "SwitchLever", Position = sw, Rotation = new Vector3(0.35f, 0, 0) };
		AddChild(_switchLever);
		_switchLever.AddChild(new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = new Vector3(0.015f, 0.04f, 0.015f) }, Position = new Vector3(0, 0, 0.01f),
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.84f, 0.8f) },
		});
		_switchUse = new Interactable { Name = "Switch", Prompt = "Flip the light switch", PickRadius = 0.25f, MaxDistance = 2f, Position = sw + Vector3.Back * 0.02f, Enabled = false };
		_switchUse.Interacted += OnSwitch;
		AddChild(_switchUse);
		// the door from inside
		_insideUse = new Interactable { Name = "InsideDoor", Prompt = "Open the door", PickRadius = 0.45f, MaxDistance = 2f, Position = new Vector3(0.25f, 1.1f, End + 0.2f), Enabled = false };
		_insideUse.Interacted += OnInsideDoor;
		AddChild(_insideUse);
		// what is on the other side now: nothing
		_void = new MeshInstance3D
		{
			Name = "Void", Mesh = new BoxMesh { Size = new Vector3(DoorWidth + 0.4f, 2.4f, 1.2f) }, Position = new Vector3(0, 1.1f, End - 0.55f),
			MaterialOverride = new StandardMaterial3D { AlbedoColor = Colors.Black, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, CullMode = BaseMaterial3D.CullModeEnum.Disabled },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false,
		};
		AddChild(_void);
		StoryBeat.MakeTrigger(this, new BoxShape3D { Size = new Vector3(DoorWidth, 2f, 0.4f) }, new Vector3(0, 1f, End - 0.15f), OnThreshold, "Threshold");
	}

	/// <summary>Called once they are shut in (or Continue puts them here): the switch and the door can be found.</summary>
	private void ArmCloset()
	{
		_switchUse.Enabled = !LightsOn;
		_insideUse.Enabled = true;
		_doorUse.Enabled = false;
	}

	private void OnSwitch(PlayerController player)
	{
		if (LightsOn) return;
		LightsOn = true;
		_switchUse.Enabled = false;
		_switchLever.Rotation = new Vector3(-0.35f, 0, 0);
		Sfx("relay_clunk", 1, _switchLever.GlobalPosition, -8f, 2f);
		_ = Cutscene.Run(this, LightCycle);
		GD.Print("[story] Act 16: the light switch");
	}

	/// <summary>Red, green, then an ordinary light: six seconds, eased, never a flash.</summary>
	private async Task LightCycle(CancellationToken ct)
	{
		Sfx("siren_low", 1, ToGlobal(ClosetCentre + Vector3.Up * 2f), -2f, 3f);
		_closetLight.Visible = true;
		var red = new Color(1f, 0.12f, 0.08f);
		var green = new Color(0.35f, 1f, 0.45f);
		var white = new Color(1f, 0.9f, 0.72f);
		double t = 0;
		const double total = 6.0;
		while (t < total)
		{
			await Cutscene.Frame(this, ct);
			t += GetProcessDeltaTime();
			float u = (float)(t / total);
			Color c = u < 0.33f ? red : u < 0.4f ? red.Lerp(green, (u - 0.33f) / 0.07f)
				: u < 0.66f ? green : u < 0.8f ? green.Lerp(white, (u - 0.66f) / 0.14f) : white;
			float e = Mathf.Min(1f, (float)t / 0.4f) * 1.6f;
			_closetLight.LightColor = c;
			_closetLight.LightEnergy = e;
			_closetBulb.Emission = c;
			_closetBulb.EmissionEnergyMultiplier = 2f * e;
		}
		LightsSettled = true;
		GD.Print("[story] Act 16: the light has settled");
	}

	private void OnInsideDoor(PlayerController player)
	{
		if (ClosetDoorOpen) return;
		if (!LightsOn)
		{
			if (!_warned) { _warned = true; }
			_ = StoryBeat.Caption(this, "Can't find the handle in the dark. There was a switch by the door.", 0.3f, 2.4f, 0.8f);
			return;
		}
		ClosetDoorOpen = true;
		_insideUse.Enabled = false;
		_void.Visible = true;
		Sfx("door_creak", 1, _doorHinge.GlobalPosition + Vector3.Up, -2f, 3f);
		var tw = CreateTween();
		tw.TweenProperty(_doorHinge, "rotation", new Vector3(0, -1.65f, 0), 1.4f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
		GD.Print("[story] Act 16: the door opens on pitch black");
	}

	private void OnThreshold(PlayerController player)
	{
		if (!ClosetDoorOpen || Through) return;
		Through = true;
		_ = Cutscene.Run(this, ct => IntoTheBlack(player, ct), lockInput: true, freezeBody: true);
	}

	/// <summary>Into the black, and out of it somewhere else: the sewer.</summary>
	private async Task IntoTheBlack(PlayerController player, CancellationToken ct)
	{
		var fader = StoryBeat.Fader(this);
		fader?.SetBlack(true);
		GD.Print("[story] Act 16 done: through the door into the black");
		await Cutscene.Wait(this, 1.2, ct);
		var sewer = StationInterior.Instance?.Sewer;
		if (sewer != null)
		{
			player.Teleport(sewer.EntranceWorld, sewer.EntranceYaw);
			player.CameraRig.SetPitch(0f);
		}
		StoryBeat.ReachCheckpoint(player, Checkpoint.Act16Finished);
		await Cutscene.Wait(this, 0.6, ct);
		// the black goes all at once
		fader?.SetBlack(false);
	}
}
