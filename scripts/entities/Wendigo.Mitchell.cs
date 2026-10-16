using Godot;

namespace ProjectDS.Entities;

/// <summary>
/// What the wendigo wears of Mitchell Webber (STORY.md Rev 5, 2026-10-16: the stalker in many masks; the wendigo is the
/// stalker wearing Mitchell, his badge, his name plate, his voice): his ranger badge high on its left breast and a name
/// plate like the one on his father's desk across its right, hung on its hide over the tear in its chest. Modelled in
/// Blender (tools/Blender/wendigo_story.py: assets/models/wendigo/wendigo_story.glb, ChestBadge and ChestPlate, placed
/// on the creature's hide in its own model space at rest) and carried on its "chest" bone, so they move with it.
/// They're there whenever it is (Acts 22 to 25); the lodge's firelight is the first time the player is close enough to
/// see them (Act 23).
/// </summary>
public partial class Wendigo
{
	public const string StoryModel = "res://assets/models/wendigo/wendigo_story.glb";
	/// <summary>The badge and the plate on its chest (null until <see cref="WearMitchell"/>).</summary>
	public Node3D Badge { get; private set; }
	public Node3D NamePlate { get; private set; }
	/// <summary>Where the badge is on it, in the world (its chest when it wears none).</summary>
	public Vector3 BadgeWorld => Badge != null && IsInstanceValid(Badge) ? Badge.ToGlobal(_badgeCentre) : ChestWorld;
	public bool WearsMitchell => Badge != null;
	private Vector3 _badgeCentre;

	/// <summary>Hangs Mitchell's badge and name plate on its chest (once; before it's ready, as soon as it is).</summary>
	public void WearMitchell()
	{
		if (Badge != null) return;
		if (!IsNodeReady()) { Ready += WearMitchell; return; }
		if (_skel == null || _chest < 0 || _headOnly || !ResourceLoader.Exists(StoryModel)) return;
		var src = GD.Load<PackedScene>(StoryModel)?.Instantiate<Node3D>();
		if (src == null) return;
		// (the glTF's nodes are in the creature's model space at rest; the skeleton's own place in the model, and the
		// chest bone's rest in the skeleton, take them into the bone's space)
		var skelInModel = Transform3D.Identity;
		for (Node n = _skel; n != null && n != _model; n = n.GetParent())
			if (n is Node3D n3) skelInModel = n3.Transform * skelInModel;
		var toBone = _skel.GetBoneGlobalRest(_chest).AffineInverse() * skelInModel.AffineInverse();
		var att = new BoneAttachment3D { Name = "MitchellsThings", BoneName = "chest" };
		_skel.AddChild(att);
		Badge = Take(src, "ChestBadge", att, toBone);
		NamePlate = Take(src, "ChestPlate", att, toBone);
		src.Free();
		if (Badge is MeshInstance3D bm && bm.Mesh != null) _badgeCentre = bm.Mesh.GetAabb().GetCenter();
	}

	private static Node3D Take(Node3D src, string name, Node3D to, Transform3D toBone)
	{
		if (src.FindChild(name, true, false) is not MeshInstance3D mi) return null;
		var inModel = Transform3D.Identity;
		for (Node n = mi; n != null && n != src; n = n.GetParent())
			if (n is Node3D n3) inModel = n3.Transform * inModel;
		mi.GetParent().RemoveChild(mi);
		mi.Owner = null;
		mi.Transform = toBone * inModel;
		mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		to.AddChild(mi);
		return mi;
	}
}
