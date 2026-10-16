using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.World.LakeParts;

namespace ProjectDS.World;

/// <summary>
/// (2026-10-16, STORY.md Rev 5, Act 12: the wake) Where the giant's throw came down. He comes to on the ground at the head
/// of the dock; behind him (back the way he came, inland) the long furrow his body dragged through the earth runs up to
/// the crater where he landed. Both modelled in Blender (tools/Blender/rh_trail.py: Furrow, Crater) and laid to the
/// shore's slope here, vertex by vertex. The wake itself is <see cref="Act11Ending"/>'s; the checkpoint-9 respawn is the
/// same spot, facing the dock.
///
/// "Dad's rowboat?": the live wake says it as the boat comes into view (<see cref="RowboatLineFlag"/>); on a Continue
/// into checkpoint 9 the dock's head says it once, a moment after the picture comes up.
/// </summary>
public partial class Lake
{
	/// <summary>The story flag: the player has said "Dad's rowboat?".</summary>
	public const string RowboatLineFlag = "line_dads_rowboat";
	/// <summary>Where he wakes (Lake-local XZ): on the beach at the head of the dock, just short of its first boards.</summary>
	public static readonly Vector2 WakeSpot = new(0f, LakeShape.DockStartZ + 0.9f);
	/// <summary>How far up the furrow runs from where he lies, and the crater's middle beyond its end.</summary>
	public const float FurrowLength = 10f, CraterBeyond = 1.45f;

	/// <summary>For tests: the crater and the furrow are down.</summary>
	public MeshInstance3D Crater { get; private set; }
	public MeshInstance3D Furrow { get; private set; }

	/// <summary>Inland from the wake spot, away from the boat (Lake-local, level, unit): the way the furrow runs.</summary>
	private static Vector2 Inland => (WakeSpot - LakeShape.BoatMooring).Normalized();

	private void BuildWakeGround()
	{
		Vector2 d = Inland;
		var basis = new Basis(Vector3.Up, Mathf.Atan2(d.X, d.Y));
		// the furrow's model runs 10 m along its -Z from the crater's lip and ends in the heap of earth he pushed; the heap
		// lies just past where he lies, toward the water
		Vector2 o = WakeSpot + d * (FurrowLength - 0.35f - 0.5f);
		Vector2 c = o + d * CraterBeyond;
		Furrow = LayToGround("Furrow", new Transform3D(basis, new Vector3(o.X, LakeShape.Ground(o.X, o.Y), o.Y)));
		Crater = LayToGround("Crater", new Transform3D(basis, new Vector3(c.X, LakeShape.Ground(c.X, c.Y), c.Y)));
	}

	private static StandardMaterial3D _soil, _soilDark, _turf;

	/// <summary>One of the wake's ground pieces from rh_trail.glb, each vertex lifted to the shore under it (the model is
	/// made on flat ground; the shore slopes a little up from the water).</summary>
	private MeshInstance3D LayToGround(string node, Transform3D xf)
	{
		_soil ??= ProcTextures.Std("lake_wake_soil", ProcTextures.Dirt(), new Color(0.72f, 0.6f, 0.48f));
		_soilDark ??= ProcTextures.Std("lake_wake_soil_dark", ProcTextures.Dirt(), new Color(0.36f, 0.29f, 0.22f));
		_turf ??= ProcTextures.Std("lake_wake_turf", ProcTextures.GrassGround(), new Color(0.85f, 0.85f, 0.8f));
		var mi = ItemModels.Add(this, "rh_trail", node, xf, node, n => n switch
		{
			"soil" => _soil,
			"soil_dark" => _soilDark,
			"turf" => _turf,
			_ => null,
		}, shadows: false);
		if (mi?.Mesh is not ArrayMesh src) return mi;
		var dst = new ArrayMesh();
		for (int s = 0; s < src.GetSurfaceCount(); s++)
		{
			var arr = src.SurfaceGetArrays(s);
			var verts = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
			for (int i = 0; i < verts.Length; i++)
			{
				Vector3 p = xf * verts[i];
				verts[i].Y += LakeShape.Ground(p.X, p.Z) - xf.Origin.Y + 0.03f;   // (a hair over the shore's own mesh)
			}
			arr[(int)Mesh.ArrayType.Vertex] = verts;
			dst.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
			dst.SurfaceSetMaterial(s, src.SurfaceGetMaterial(s));
		}
		mi.Mesh = dst;
		return mi;
	}

	/// <summary>The dock's head: on a Continue into checkpoint 9 (the wake already gone by), the line once the picture is up.</summary>
	private void BuildRowboatLine()
	{
		StoryBeat.MakeTrigger(this, new SphereShape3D { Radius = 6f }, new Vector3(WakeSpot.X, LakeShape.Ground(WakeSpot.X, WakeSpot.Y) + 1f, WakeSpot.Y - 1f),
			OnDockHead, "RowboatLine");
	}

	private void OnDockHead(PlayerController player)
	{
		var s = StoryManager.Instance;
		if (s == null || s.HasFlag(RowboatLineFlag) || s.Current != Checkpoint.Act11GiantEncounter) return;
		s.SetFlag(RowboatLineFlag);
		_ = Cutscene.Run(this, async ct =>
		{
			while (GameFlow.Instance is { Started: false }) await Cutscene.Frame(this, ct);
			while (StoryBeat.Fader(this) is { BlackAlpha: > 0.3f }) await Cutscene.Frame(this, ct);
			await Cutscene.Wait(this, 1.2, ct);
			await PlayerVoice.Say(this, "dads_rowboat", 2.2f, ct);
		});
	}
}
