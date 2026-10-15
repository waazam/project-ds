using System.Threading.Tasks;
using Godot;
using ProjectDS.World;
using ProjectDS.World.ChurchParts;
using ProjectDS.World.LodgeParts;
using ProjectDS.World.StationParts;

namespace ProjectDS.Entities;

/// <summary>`--figures` (2026-10-15): the minor figures, before (the old code-built stand-ins, rebuilt here as they were) and
/// after (the Blender models in tools/Blender/figures.py, painted with the game's own materials): the figure at the stairwell's
/// rail, the lodge's standing sheet (both poses), the crypt's hooded statue, the station's growths on a wall, a moth.</summary>
public partial class CreaturePreview
{
	private async Task FigureShots()
	{
		AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(80, 80) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.32f, 0.31f, 0.3f) } });
		// a pale wall behind each set, so the near-black figure reads
		var wallMat = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.55f, 0.56f) };
		void Wall(Vector3 at) => AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(5f, 3.2f, 0.1f) }, MaterialOverride = wallMat, Position = at + new Vector3(0, 1.6f, -1.2f) });
		var black = StationTextures.Flat("sw_figure", new Color(0.02f, 0.02f, 0.02f), 1f, 0f);

		// ---- the rail figure
		Wall(new Vector3(-20, 0, 0));
		var oldFig = new Node3D { Position = new Vector3(-21, 0, 0) };
		AddChild(oldFig);
		var k1 = new MeshKit();
		k1.Mat(black);
		k1.Color = Colors.White;
		k1.Cylinder(new Vector3(0, 0, 0), new Vector3(0, 1.55f, 0), 0.16f, 0.2f, 8, true);
		k1.Cylinder(new Vector3(0, 1.55f, 0), new Vector3(0, 1.62f, 0), 0.07f, 0.07f, 6, false);
		k1.Cylinder(new Vector3(0, 1.62f, 0), new Vector3(0, 1.9f, 0), 0.1f, 0.08f, 8, true);
		foreach (float s in new[] { -1f, 1f }) k1.Cylinder(new Vector3(s * 0.2f, 1.45f, 0), new Vector3(s * 0.23f, 0.55f, 0), 0.04f, 0.03f, 5, true);
		k1.CommitTo(oldFig, "Body", false);
		oldFig.RotateObjectLocal(Vector3.Right, -0.25f);
		if (FigureModels.Load("rail_figure") is { } rf)
		{
			rf.Position = new Vector3(-19, 0, 0);
			rf.RotationDegrees = new Vector3(0, 180, 0);
			AddChild(rf);
			FigureModels.Paint(FigureModels.Mesh(rf, "Figure"), black, false);
			FigureModels.Paint(FigureModels.Mesh(rf, "Eyes"), new StandardMaterial3D { AlbedoColor = new Color(0.6f, 0.65f, 0.7f), EmissionEnabled = true, Emission = new Color(0.55f, 0.6f, 0.68f), EmissionEnergyMultiplier = 0.6f }, false);
		}

		// ---- the standing sheet
		Wall(new Vector3(0, 0, 0));
		var oldSheet = new Node3D { Position = new Vector3(-1.4f, 0, 0) };
		AddChild(oldSheet);
		var k = new MeshKit();
		k.Mat(LodgeTextures.SheetLinenMat);
		k.Color = Colors.White;
		k.Blob(new Vector3(0, 1.72f, 0), new Vector3(0.13f, 0.15f, 0.13f), 7101, 0.05f, false, 1f, 0f, 2);
		k.Blob(new Vector3(0, 1.45f, 0), new Vector3(0.27f, 0.12f, 0.17f), 7102, 0.06f, false, 1f, 0f, 2);
		for (int i = 0; i < 9; i++)
		{
			float a = i / 9f * Mathf.Tau;
			var d = new Vector3(Mathf.Cos(a) * 0.24f, 0, Mathf.Sin(a) * 0.17f);
			k.Cylinder(new Vector3(0, 1.5f, 0) + d * 0.55f, d * 1.45f + Vector3.Up * 0.01f, 0.07f, 0.11f, 7, false);
		}
		k.Cylinder(new Vector3(0, 1.5f, 0), new Vector3(0, 0.02f, 0), 0.24f, 0.36f, 12, false);
		k.CommitTo(oldSheet, "Sheet", true);
		foreach (var (pose, x) in new[] { ("SheetA", 0.2f), ("SheetB", 1.6f) })
			if (FigureModels.Load("sheet_figure") is { } sf)
			{
				sf.Position = new Vector3(x, 0, 0);
				sf.RotationDegrees = new Vector3(0, 180, 0);
				AddChild(sf);
				foreach (var n in sf.FindChildren("*", "MeshInstance3D", true, false))
					if (n is MeshInstance3D mi) { mi.Visible = mi.Name == pose; FigureModels.Paint(mi, LodgeTextures.SheetLinenMat); }
			}

		// ---- the crypt's statue
		Wall(new Vector3(20, 0, 0));
		var stone = ChurchTextures.AshlarDampMat;
		var oldStat = new MeshKit();
		var fig = new Vector3(19, 0, 0);
		oldStat.Mat(stone);
		oldStat.Color = Colors.White * 0.8f;
		oldStat.Cylinder(fig, fig + Vector3.Up * 0.3f, 0.42f, 0.42f, 8, true);
		oldStat.Cylinder(fig + Vector3.Up * 0.3f, fig + Vector3.Up * 1.5f, 0.36f, 0.24f, 10, false);
		oldStat.Blob(fig + Vector3.Up * 1.7f + new Vector3(0, 0, 0.06f), new Vector3(0.2f, 0.25f, 0.22f), 7, 0.1f);
		oldStat.Blob(fig + Vector3.Up * 1.2f + new Vector3(0, 0, 0.3f), new Vector3(0.14f, 0.08f, 0.1f), 8, 0.1f);
		oldStat.CommitTo(this, "OldStatue", true);
		if (FigureModels.Load("crypt_figure") is { } cf)
		{
			cf.Position = new Vector3(21, 0, 0);
			cf.RotationDegrees = new Vector3(0, 180, 0);
			AddChild(cf);
			FigureModels.Paint(FigureModels.Mesh(cf, "CryptFigure"), stone);
		}

		// ---- the growths, on a wall
		var gwall = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(6f, 3f, 0.1f) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.5f, 0.47f, 0.42f) }, Position = new Vector3(40, 1.5f, -0.05f) };
		AddChild(gwall);
		var gk = new MeshKit();
		for (int i = 0; i < 2; i++) StationProps.Growth(gk, new Vector3(38.2f + i * 0.9f, 1.2f + i * 0.5f, 0f), Vector3.Back, 0.6f, 11 + i * 7);
		gk.CommitTo(this, "OldGrowths", true);
		var holder = new Node3D { Name = "NewGrowths" };
		AddChild(holder);
		for (int i = 0; i < 3; i++) StationProps.GrowthModel(holder, new Vector3(40.6f + i * 0.85f, 0.9f + (i % 2) * 0.7f, 0f), Vector3.Back, 0.6f, i);

		// ---- a moth (and the old card), big and close
		var mothMat = new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.56f, 0.46f), CullMode = BaseMaterial3D.CullModeEnum.Disabled };
		AddChild(new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(0.018f, 0.013f) }, MaterialOverride = mothMat, Position = new Vector3(59.97f, 1.2f, 0) });
		if (FigureModels.Load("moth") is { } moth)
		{
			moth.Position = new Vector3(60.02f, 1.2f, 0);
			moth.RotationDegrees = new Vector3(-55, 160, 0);
			FigureModels.Paint(FigureModels.Mesh(moth, "Body"), new StandardMaterial3D { AlbedoColor = new Color(0.4f, 0.34f, 0.26f), Roughness = 1f }, false);
			AddChild(moth);
		}

		var key = new DirectionalLight3D { RotationDegrees = new Vector3(-35, 30, 0), LightEnergy = 1.4f, ShadowEnabled = true };
		AddChild(key);
		await Seconds(1.0);
		await Shot("fig_rail_before_after", new Vector3(-20, 1.5f, 4.2f), new Vector3(-20, 1.0f, 0));
		await Shot("fig_rail_after_close", new Vector3(-19, 1.75f, 1.6f), new Vector3(-19, 1.3f, 0));
		await Shot("fig_sheet_before_after", new Vector3(0.1f, 1.4f, 4.6f), new Vector3(0.1f, 0.95f, 0));
		await Shot("fig_sheet_after_close", new Vector3(0.6f, 1.6f, 1.8f), new Vector3(0.6f, 1.3f, 0));
		await Shot("fig_crypt_before_after", new Vector3(20, 1.5f, 4.0f), new Vector3(20, 0.95f, 0));
		await Shot("fig_crypt_after_close", new Vector3(21.3f, 1.6f, 1.7f), new Vector3(21, 1.2f, 0));
		await Shot("fig_growths_before_after", new Vector3(40, 1.4f, 3.2f), new Vector3(40, 1.3f, 0));
		await Shot("fig_moth_before_after", new Vector3(60, 1.215f, 0.075f), new Vector3(60, 1.2f, 0));
	}
}
