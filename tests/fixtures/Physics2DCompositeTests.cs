using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

// CompositeCollider2D (welded outlines, GetPath, Outlines / Polygons), Grid + Tilemap (cells, tiles, bounds), TilemapCollider2D
// alone and merged by a composite, TilemapRenderer drawing.
public class Physics2DCompositeTests
{
    static CompositeCollider2D Composite(CompositeCollider2D.GeometryType type, params Rect[] boxes)
    {
        var go = new GameObject("Composite");
        var rb = go.AddComponent<Rigidbody2D>(); rb.bodyType = RigidbodyType2D.Static;
        var comp = go.AddComponent<CompositeCollider2D>(); comp.geometryType = type;
        foreach (var r in boxes)
        {
            var b = go.AddComponent<BoxCollider2D>();
            b.offset = r.center; b.size = r.size; b.usedByComposite = true;
        }
        comp.GenerateGeometry();
        return comp;
    }

    static List<Vector2> Path(CompositeCollider2D c, int i) { var l = new List<Vector2>(); c.GetPath(i, l); return l; }
    static float Area(List<Vector2> p) { float a = 0; for (int i = 0; i < p.Count; i++) { var u = p[i]; var v = p[(i + 1) % p.Count]; a += u.x * v.y - v.x * u.y; } return a * 0.5f; }

    [Test] public void AdjacentBoxesWeldIntoOneRectangle()
    {
        var c = Composite(CompositeCollider2D.GeometryType.Polygons, new Rect(0, 0, 1, 1), new Rect(1, 0, 1, 1), new Rect(2, 0, 1, 1));
        Assert.AreEqual(1, c.pathCount);
        Assert.AreEqual(4, c.GetPathPointCount(0), "collinear seam points are removed");
        Assert.AreEqual(3f, Area(Path(c, 0)), 1e-4f, "counter-clockwise outer boundary");
    }

    [Test] public void OverlapsMergeAndHolesAreClockwise()
    {
        var l = Composite(CompositeCollider2D.GeometryType.Polygons, new Rect(0, 0, 2, 1), new Rect(0, 0, 1, 2));
        Assert.AreEqual(1, l.pathCount); Assert.AreEqual(6, l.pointCount, "L shape");
        Assert.AreEqual(3f, Area(Path(l, 0)), 1e-4f);

        var ring = new List<Rect>();
        for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++) if (x != 1 || y != 1) ring.Add(new Rect(x + 20, y, 1, 1));
        var r = Composite(CompositeCollider2D.GeometryType.Polygons, ring.ToArray());
        Assert.AreEqual(2, r.pathCount, "outer boundary and the hole");
        Assert.AreEqual(9f, Area(Path(r, 0)), 1e-4f);
        Assert.AreEqual(-1f, Area(Path(r, 1)), 1e-4f, "hole is clockwise");
        Assert.IsNull(Physics2D.OverlapPoint(new Vector2(21.5f, 1.5f)), "nothing in the hole");
        Assert.AreSame(r, Physics2D.OverlapPoint(new Vector2(20.5f, 0.5f)));

        var apart = Composite(CompositeCollider2D.GeometryType.Polygons, new Rect(10, 0, 1, 1), new Rect(13, 0, 1, 1));
        Assert.AreEqual(2, apart.pathCount);
    }

    [UnityTest] public IEnumerator OutlinesLetABoxSlideAcrossSeams()
    {
        var tiles = new List<Rect>();
        for (int i = 0; i < 40; i++) tiles.Add(new Rect(i - 5, -1, 1, 1));
        var floor = Composite(CompositeCollider2D.GeometryType.Outlines, tiles.ToArray());
        floor.sharedMaterial = new PhysicsMaterial2D { friction = 0 };
        var box = new GameObject("Slider");
        box.transform.position = new Vector3(0, 0.5f, 0);
        var rb = box.AddComponent<Rigidbody2D>(); rb.freezeRotation = true;
        box.AddComponent<BoxCollider2D>().sharedMaterial = new PhysicsMaterial2D { friction = 0 };
        rb.velocity = new Vector2(8, 0);
        for (int i = 0; i < 120; i++) yield return new WaitForFixedUpdate();
        Assert.Greater(rb.velocity.x, 7.5f, "no catching on the tile seams");
        Assert.AreEqual(0.5f, rb.position.y, 0.05f);
    }

    static Tile GridTile() { var t = ScriptableObject.CreateInstance<Tile>(); t.colliderType = Tile.ColliderType.Grid; return t; }

    static Tilemap Map(out Grid grid)
    {
        grid = new GameObject("Grid").AddComponent<Grid>();
        var go = new GameObject("Tilemap");
        go.transform.SetParent(grid.transform, false);
        return go.AddComponent<Tilemap>();
    }

    [Test] public void TilemapCellsAndBounds()
    {
        var map = Map(out var grid);
        grid.cellSize = new Vector3(2, 1, 0);
        var t = GridTile();
        map.SetTile(new Vector3Int(1, 2, 0), t);
        map.SetTile(new Vector3Int(-1, 0, 0), t);
        Assert.IsTrue(map.HasTile(new Vector3Int(1, 2, 0)));
        Assert.AreSame(t, map.GetTile(new Vector3Int(-1, 0, 0)));
        Assert.IsNull(map.GetTile(Vector3Int.zero));
        Assert.AreEqual(new BoundsInt(-1, 0, 0, 3, 3, 1), map.cellBounds);
        Assert.AreEqual(new Vector3(3, 2.5f, 0), map.GetCellCenterWorld(new Vector3Int(1, 2, 0)));
        Assert.AreEqual(new Vector3Int(1, 2, 0), grid.WorldToCell(new Vector3(3.9f, 2.1f, 0)));
        Assert.AreEqual(2, map.GetTilesRangeCount(new Vector3Int(-5, -5, 0), new Vector3Int(5, 5, 0)));
        map.SetTile(new Vector3Int(1, 2, 0), null);
        map.CompressBounds();
        Assert.AreEqual(new BoundsInt(-1, 0, 0, 1, 1, 1), map.cellBounds);
        map.ClearAllTiles();
        Assert.AreEqual(0, map.GetUsedTilesCount());
    }

    [Test] public void TilemapColliderAloneAndMerged()
    {
        var map = Map(out _);
        var t = GridTile();
        for (int x = 0; x < 4; x++) map.SetTile(new Vector3Int(x, 0, 0), t);
        var col = map.gameObject.AddComponent<TilemapCollider2D>();
        var hit = Physics2D.Raycast(new Vector2(2.5f, 5), Vector2.down);
        Assert.AreSame(col, hit.collider); Assert.AreEqual(1f, hit.point.y, 1e-3f);

        map.gameObject.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
        var comp = map.gameObject.AddComponent<CompositeCollider2D>();
        col.usedByComposite = true;
        comp.GenerateGeometry();
        Assert.AreEqual(1, comp.pathCount, "the four cells weld into one outline");
        Assert.AreEqual(4, comp.GetPathPointCount(0));
        Assert.AreSame(comp, Physics2D.Raycast(new Vector2(0.5f, 5), Vector2.down).collider);

        map.SetTile(new Vector3Int(10, 0, 0), t);
        Assert.AreEqual(2, comp.pathCount, "a new tile is a second outline");
    }

    [Test] public void TilemapRendererDrawsTileSprites()
    {
        var map = Map(out _);
        var tex = new Texture2D(4, 4); var px = new Color[16]; for (int i = 0; i < 16; i++) px[i] = Color.white; tex.SetPixels(px); tex.Apply();
        var tile = ScriptableObject.CreateInstance<Tile>();
        tile.sprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4);
        map.SetTile(Vector3Int.zero, tile);
        map.color = Color.red;
        map.gameObject.AddComponent<TilemapRenderer>();
        var rt = new RenderTexture(32, 32, 24);
        var cam = new GameObject("Cam").AddComponent<Camera>();
        cam.orthographic = true; cam.orthographicSize = 1; cam.transform.position = new Vector3(0.5f, 0.5f, -10);
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; cam.targetTexture = rt;
        cam.Render();
        var t2 = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        RenderTexture.active = rt; t2.ReadPixels(new Rect(0, 0, 32, 32), 0, 0); t2.Apply(); RenderTexture.active = null;
        Color32 c = t2.GetPixel(16, 16), outside = t2.GetPixel(2, 2);
        Assert.AreEqual(255, c.r); Assert.AreEqual(0, c.g, "tile tinted by Tilemap.color");
        Assert.AreEqual(0, outside.r, "outside the cell");
    }
}
