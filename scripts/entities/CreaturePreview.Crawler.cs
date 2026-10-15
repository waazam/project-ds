using System.Threading.Tasks;
using Godot;

namespace ProjectDS.Entities;

/// <summary>
/// `--crawler-uv`: the crawler as the player meets it in Act 14's stairwell: no light but the lantern's blacklight (the
/// flame dead), the beam from the eye along the view. Shot from the side, the front, its face, and with the beam off
/// (it should be all but gone: in the dark it's only the blacklight that shows it).
/// </summary>
public partial class CreaturePreview
{
	private async Task CrawlerUvShots()
	{
		env.BackgroundColor = new Color(0.01f, 0.01f, 0.012f);
		env.AmbientLightEnergy = 0.02f;
		foreach (var n in GetChildren()) if (n is Light3D l) l.Visible = false;
		var floor = new StaticBody3D { Name = "Floor" };
		floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(40, 1, 40) }, Position = new Vector3(0, -0.5f, 0) });
		floor.AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(40, 40) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.19f, 0.18f) } });
		AddChild(floor);
		var crawler = new Crawler { Name = "Crawler" };
		AddChild(crawler);
		for (int i = 0; i < 90; i++)
		{
			crawler.Hurry = 0.2f;
			crawler.Place(new Vector3(0, 0, 2f - i * 0.02f), Vector3.Forward);
			await Frames(1);
		}
		Vector3 c = crawler.GlobalPosition;
		var head = crawler.FindChild("Head", true, false) as Node3D;
		Vector3 h = head?.GlobalPosition ?? c;
		async Task UvShot(string name, Vector3 eye, Vector3 at)
		{
			ProjectDS.Player.Lantern.PreviewUv(eye, at - eye, 9f);
			await Frames(3);
			await Shot(name, eye, at);
		}
		await UvShot("crawler_uv_side", new Vector3(2.6f, 1.2f, c.Z + 0.2f), c + new Vector3(0, -0.15f, 0));
		await UvShot("crawler_uv_front", c + new Vector3(0.4f, 0.7f, -2.2f), c + new Vector3(0, -0.2f, 0));
		await UvShot("crawler_uv_face", h + new Vector3(0.12f, -0.2f, -0.7f), h);
		ProjectDS.Player.Lantern.PreviewUv(null, Vector3.Forward);
		await Frames(3);
		await Shot("crawler_dark_no_uv", new Vector3(2.6f, 1.2f, c.Z + 0.2f), c + new Vector3(0, -0.15f, 0));
	}
}
