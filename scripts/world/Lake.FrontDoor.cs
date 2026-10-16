using Godot;
using ProjectDS.Audio;
using ProjectDS.Systems;
using ProjectDS.World.LakeParts;

namespace ProjectDS.World;

/// <summary>
/// (2026-10-15, Dan: "all doors should be closed before entering and they always open to the next area, not a black
/// room") The rescue station's front door, shut. It stood a little open onto a black box behind the doorway. Now it is a
/// plank door in the doorway that has to be opened: it swings out over the porch, and the doorway looks into the
/// station's lobby as it stands (built far off, <see cref="StationInterior"/>, seen through a <see cref="DoorPortal"/>),
/// its lamp, its desk and its fog. Stepping into the doorway is still the act's end (the fade, then the lobby).
/// The station is built in the black of the wake-up on the near shore, so it's there to be seen by the time they land.
/// </summary>
public partial class Lake
{
	/// <summary>The front door has been opened (it stays open).</summary>
	public bool StationDoorOpen { get; private set; }
	/// <summary>E on the shut door.</summary>
	public Interactable StationDoorUse { get; private set; }
	/// <summary>For tests and the shots: the doorway is drawing the lobby.</summary>
	public bool LobbyShowing => _lobbyView is { Rendering: true };

	private Node3D _stationLeaf;
	private DoorPortal _lobbyView;

	private void BuildStationFrontDoor()
	{
		if (Station == null) return;
		float hd = LakeStructures.StationD * 0.5f, w = LakeStructures.StationDoorW, h = LakeStructures.StationDoorH;
		// hinged on the left jamb (seen from the porch), the leaf across the doorway in the logs' thickness
		_stationLeaf = new Node3D { Name = "FrontDoor", Position = new Vector3(-w * 0.5f + 0.02f, 0, hd + 0.03f) };
		Station.AddChild(_stationLeaf);
		float lw = w - 0.06f, lh = h - 0.04f;
		var k = new MeshKit();
		k.Mat(BuildingTextures.BoardsMat);
		// five upright boards, each its own grey, a dark gap between
		var rng = new RandomNumberGenerator { Seed = 1207 };
		const int boards = 5;
		float bw = lw / boards;
		for (int i = 0; i < boards; i++)
		{
			float s = rng.RandfRange(0.3f, 0.4f);
			k.Color = new Color(s, s * 0.93f, s * 0.86f);
			BuildKit.Box(k, new Vector3(bw * (i + 0.5f), lh * 0.5f + 0.02f, rng.RandfRange(-0.004f, 0.004f)), new Vector3(bw - 0.012f, lh, 0.045f), 1.2f);
		}
		// ledges and a brace on the porch side
		k.Color = new Color(0.3f, 0.28f, 0.26f);
		foreach (float y in new[] { 0.32f, lh - 0.32f })
			BuildKit.Box(k, new Vector3(lw * 0.5f, y, 0.035f), new Vector3(lw - 0.12f, 0.13f, 0.03f), 1.2f);
		float a = Mathf.Atan2(lh - 0.64f, lw - 0.2f);
		BuildKit.Box(k, new Vector3(lw * 0.5f, lh * 0.5f, 0.035f), new Vector3(Mathf.Sqrt((lw - 0.2f) * (lw - 0.2f) + (lh - 0.64f) * (lh - 0.64f)), 0.12f, 0.028f), 1.2f,
			BuildKit.Face.None, new Basis(Vector3.Back, a));
		// an iron pull and strap hinges
		k.Mat(BuildingTextures.IronMat);
		k.Color = new Color(0.32f, 0.3f, 0.28f);
		k.Cylinder(new Vector3(lw - 0.16f, 1.05f, 0.03f), new Vector3(lw - 0.16f, 1.05f, 0.1f), 0.022f, 0.022f, 6, true);
		k.Cylinder(new Vector3(lw - 0.16f, 0.9f, 0.1f), new Vector3(lw - 0.16f, 1.2f, 0.1f), 0.016f, 0.016f, 6, true);
		foreach (float y in new[] { 0.32f, lh - 0.32f })
			BuildKit.Box(k, new Vector3(0.3f, y, 0.055f), new Vector3(0.6f, 0.05f, 0.01f), 3f);
		k.CommitTo(_stationLeaf, "Leaf", true);
		var body = new StaticBody3D { Name = "Body", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "wood");
		body.AddChild(new CollisionShape3D { Position = new Vector3(lw * 0.5f, lh * 0.5f, 0), Shape = new BoxShape3D { Size = new Vector3(lw, lh, 0.1f) } });
		_stationLeaf.AddChild(body);
		StationDoorUse = new Interactable { Name = "Use", Prompt = "Open the door", PickRadius = 0.6f, MaxDistance = 2.6f, Position = new Vector3(lw - 0.3f, 1.1f, 0.1f) };
		StationDoorUse.Interacted += _ => OpenStationDoor();
		_stationLeaf.AddChild(StationDoorUse);
		// the doorway, just behind the leaf: the lobby
		_lobbyView = new DoorPortal
		{
			Name = "LobbyView", Position = new Vector3(0, 0, hd - 0.07f), Size = new Vector2(w, h),
			OnWhen = () => StationDoorOpen, TargetWhen = () => StationInterior.Instance is { Built: true } st ? st.LakeView : null,
		};
		Station.AddChild(_lobbyView);
	}

	/// <summary>Opens it (out over the porch, with its creak). (The station is built in the wake-up's black; a station still
	/// unbuilt shows nothing in the doorway, and is built in the arrival's black as before.)</summary>
	public void OpenStationDoor(bool instant = false)
	{
		if (StationDoorOpen || _stationLeaf == null) return;
		StationDoorOpen = true;
		if (StationDoorUse != null) StationDoorUse.Enabled = false;
		const float open = -1.75f;   // out, back against the porch side of the wall
		if (instant) { _stationLeaf.Rotation = new Vector3(0, open, 0); return; }
		AudioDirector.OneShot(this, "door_creak", 1, _stationLeaf.GlobalPosition + Vector3.Up, -2f);
		_stationLeaf.CreateTween().TweenProperty(_stationLeaf, "rotation:y", open, 1.3f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
		GD.Print("[story] Act 12: the station's door opens on its lobby");
	}
}
