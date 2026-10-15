// Grid, Tilemap and their companions for MiniUnity: rectangular cell layout (gap, swizzle), tiles kept per cell with their sprite,
// colour, transform, flags and collider type (from TileBase.GetTileData, refreshed like Unity), the serialized tile arrays of
// scenes and prefabs (m_Tiles / m_TileAssetArray / …) read and written back, TilemapRenderer drawing every tile's sprite, and
// TilemapCollider2D shapes (cell boxes for Grid tiles, the sprite's physics shape for Sprite tiles; merged by a
// CompositeCollider2D). Hexagon and isometric layouts are placed as rectangles.
using System;
using System.Collections.Generic;
using System.Linq;
using MiniUnity;
using MiniUnity.Box2D;
using MiniUnity.Yaml;
using UnityEngine.Tilemaps;

namespace UnityEngine
{
    public partial class GridLayout
    {
        internal virtual Grid OwnerGrid => this as Grid ?? GetComponentInParent<Grid>();
        public Vector3 cellSize => OwnerGrid?.NGet("m_CellSize") != null ? OwnerGrid.NVec3("m_CellSize") : new Vector3(1, 1, 0);
        public Vector3 cellGap => OwnerGrid != null ? OwnerGrid.NVec3("m_CellGap") : Vector3.zero;
        public CellLayout cellLayout => OwnerGrid != null ? (CellLayout)OwnerGrid.NInt("m_CellLayout") : CellLayout.Rectangle;
        public CellSwizzle cellSwizzle => OwnerGrid != null ? (CellSwizzle)OwnerGrid.NInt("m_CellSwizzle") : CellSwizzle.XYZ;

        Vector3 Pitch() { var s = cellSize; var g = cellGap; return new Vector3(s.x + g.x, s.y + g.y, s.z + g.z); }

        public Vector3 CellToLocalInterpolated(Vector3 cellPosition) => Grid.Swizzle(cellSwizzle, Vector3.Scale(cellPosition, Pitch()));
        public Vector3 LocalToCellInterpolated(Vector3 localPosition)
        {
            var p = Grid.InverseSwizzle(cellSwizzle, localPosition); var d = Pitch();
            return new Vector3(d.x != 0 ? p.x / d.x : 0, d.y != 0 ? p.y / d.y : 0, d.z != 0 ? p.z / d.z : 0);
        }
        public Vector3 CellToLocal(Vector3Int cellPosition) => CellToLocalInterpolated(cellPosition);
        public Vector3Int LocalToCell(Vector3 localPosition)
        {
            var c = LocalToCellInterpolated(localPosition);
            return new Vector3Int(Mathf.FloorToInt(c.x), Mathf.FloorToInt(c.y), Mathf.FloorToInt(c.z));
        }
        public Vector3 LocalToWorld(Vector3 localPosition) => transform.TransformPoint(localPosition);
        public Vector3 WorldToLocal(Vector3 worldPosition) => transform.InverseTransformPoint(worldPosition);
        public Vector3 CellToWorld(Vector3Int cellPosition) => LocalToWorld(CellToLocal(cellPosition));
        public Vector3Int WorldToCell(Vector3 worldPosition) => LocalToCell(WorldToLocal(worldPosition));
        public Bounds GetBoundsLocal(Vector3Int cellPosition)
        {
            var size = Grid.Swizzle(cellSwizzle, cellSize);
            return new Bounds(CellToLocal(cellPosition) + size * 0.5f, new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)));
        }
        public Bounds GetBoundsLocal(Vector3 origin, Vector3 size)
        {
            var a = CellToLocalInterpolated(origin); var b = CellToLocalInterpolated(origin + size);
            var bnd = new Bounds(a, Vector3.zero); bnd.Encapsulate(b); return bnd;
        }
        public Vector3 GetLayoutCellCenter() => new Vector3(0.5f, 0.5f, 0.5f);
    }

    public sealed partial class Grid
    {
        public new Vector3 cellSize { get => NGet("m_CellSize") != null ? NVec3("m_CellSize") : new Vector3(1, 1, 0); set => NSetVec3("m_CellSize", value); }
        public new Vector3 cellGap { get => NVec3("m_CellGap"); set => NSetVec3("m_CellGap", value); }
        public new CellLayout cellLayout { get => (CellLayout)NInt("m_CellLayout"); set => NSetI("m_CellLayout", (int)value); }
        public new CellSwizzle cellSwizzle { get => (CellSwizzle)NInt("m_CellSwizzle"); set => NSetI("m_CellSwizzle", (int)value); }
        public Vector3 GetCellCenterLocal(Vector3Int position) => CellToLocalInterpolated(position + GetLayoutCellCenter());
        public Vector3 GetCellCenterWorld(Vector3Int position) => LocalToWorld(GetCellCenterLocal(position));
        public static Vector3 Swizzle(CellSwizzle swizzle, Vector3 position) => swizzle switch
        {
            CellSwizzle.XZY => new Vector3(position.x, position.z, position.y),
            CellSwizzle.YXZ => new Vector3(position.y, position.x, position.z),
            CellSwizzle.YZX => new Vector3(position.y, position.z, position.x),
            CellSwizzle.ZXY => new Vector3(position.z, position.x, position.y),
            CellSwizzle.ZYX => new Vector3(position.z, position.y, position.x),
            _ => position,
        };
        public static Vector3 InverseSwizzle(CellSwizzle swizzle, Vector3 position) => swizzle switch
        {
            CellSwizzle.XZY => new Vector3(position.x, position.z, position.y),
            CellSwizzle.YXZ => new Vector3(position.y, position.x, position.z),
            CellSwizzle.YZX => new Vector3(position.z, position.x, position.y),
            CellSwizzle.ZXY => new Vector3(position.y, position.z, position.x),
            CellSwizzle.ZYX => new Vector3(position.z, position.y, position.x),
            _ => position,
        };
    }

    public partial class Sprite
    {
        // custom physics shapes (OverridePhysicsShape); without them the sprite's rectangle is its shape
        internal List<Vector2[]> m_PhysicsShapes;
        public int GetPhysicsShapeCount() => m_PhysicsShapes?.Count ?? 1;
        public int GetPhysicsShapePointCount(int shapeIdx) => Shape(shapeIdx).Length;
        public int GetPhysicsShape(int shapeIdx, List<Vector2> physicsShape)
        {
            if (physicsShape == null) throw new ArgumentNullException(nameof(physicsShape));
            physicsShape.Clear(); physicsShape.AddRange(Shape(shapeIdx)); return physicsShape.Count;
        }
        public void OverridePhysicsShape(IList<Vector2[]> physicsShapes) => m_PhysicsShapes = physicsShapes?.Select(s => (Vector2[])s.Clone()).ToList();
        Vector2[] Shape(int i)
        {
            if (m_PhysicsShapes != null)
            {
                if (i < 0 || i >= m_PhysicsShapes.Count) throw new ArgumentOutOfRangeException(nameof(i));
                return m_PhysicsShapes[i];
            }
            if (i != 0) throw new ArgumentOutOfRangeException(nameof(i));
            float x0 = -m_Pivot.x / m_PPU, y0 = -m_Pivot.y / m_PPU, x1 = x0 + m_Rect.width / m_PPU, y1 = y0 + m_Rect.height / m_PPU;
            return new[] { new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1) };
        }
    }
}

namespace UnityEngine.Tilemaps
{
    public partial struct TileData
    {
        Sprite m_Sprite; Color m_Color; Matrix4x4 m_Transform; GameObject m_GameObject; TileFlags m_Flags; Tile.ColliderType m_ColliderType;
        internal static TileData Default => new TileData { m_Color = Color.white, m_Transform = Matrix4x4.identity, m_Flags = TileFlags.None, m_ColliderType = Tile.ColliderType.None };
        public Sprite sprite { get => m_Sprite; set => m_Sprite = value; }
        public Color color { get => m_Color; set => m_Color = value; }
        public Matrix4x4 transform { get => m_Transform; set => m_Transform = value; }
        public GameObject gameObject { get => m_GameObject; set => m_GameObject = value; }
        public TileFlags flags { get => m_Flags; set => m_Flags = value; }
        public Tile.ColliderType colliderType { get => m_ColliderType; set => m_ColliderType = value; }
    }

    public partial struct TileChangeData
    {
        Vector3Int m_Position; TileBase m_Tile; Color m_Color; Matrix4x4 m_Transform;
        public TileChangeData(Vector3Int position, TileBase tile, Color color, Matrix4x4 transform) { m_Position = position; m_Tile = tile; m_Color = color; m_Transform = transform; }
        public Vector3Int position { get => m_Position; set => m_Position = value; }
        public TileBase tile { get => m_Tile; set => m_Tile = value; }
        public Color color { get => m_Color; set => m_Color = value; }
        public Matrix4x4 transform { get => m_Transform; set => m_Transform = value; }
    }

    public partial struct TileAnimationData
    {
        Sprite[] m_AnimatedSprites; float m_AnimationSpeed, m_AnimationStartTime; TileAnimationFlags m_Flags;
        public Sprite[] animatedSprites { get => m_AnimatedSprites; set => m_AnimatedSprites = value; }
        public float animationSpeed { get => m_AnimationSpeed; set => m_AnimationSpeed = value; }
        public float animationStartTime { get => m_AnimationStartTime; set => m_AnimationStartTime = value; }
        public TileAnimationFlags flags { get => m_Flags; set => m_Flags = value; }
    }

    public abstract partial class TileBase
    {
        public virtual void RefreshTile(Vector3Int position, ITilemap tilemap) => tilemap.RefreshTile(position);
        public virtual void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData) { }
        public virtual bool GetTileAnimationData(Vector3Int position, ITilemap tilemap, ref TileAnimationData tileAnimationData) => false;
        public virtual bool StartUp(Vector3Int position, ITilemap tilemap, GameObject go) => true;
    }

    public partial class Tile
    {
        [SerializeField] Sprite m_Sprite;
        [SerializeField] Color m_Color = Color.white;
        [SerializeField] Matrix4x4 m_Transform = Matrix4x4.identity;
        [SerializeField] GameObject m_InstancedGameObject;
        [SerializeField] TileFlags m_Flags = TileFlags.LockColor;
        [SerializeField] ColliderType m_ColliderType = ColliderType.Sprite;
        public Sprite sprite { get => m_Sprite; set => m_Sprite = value; }
        public Color color { get => m_Color; set => m_Color = value; }
        public Matrix4x4 transform { get => m_Transform; set => m_Transform = value; }
        public GameObject gameObject { get => m_InstancedGameObject; set => m_InstancedGameObject = value; }
        public TileFlags flags { get => m_Flags; set => m_Flags = value; }
        public ColliderType colliderType { get => m_ColliderType; set => m_ColliderType = value; }
        public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
        {
            tileData.sprite = m_Sprite; tileData.color = m_Color; tileData.transform = m_Transform;
            tileData.gameObject = m_InstancedGameObject; tileData.flags = m_Flags; tileData.colliderType = m_ColliderType;
        }
    }

    public partial class ITilemap
    {
        internal ITilemap() { }
        public ITilemap(Tilemap tilemap) { m_Tilemap = tilemap; }
        public static implicit operator ITilemap(Tilemap tilemap) => new ITilemap(tilemap);
        internal void SetTilemapInstance(Tilemap tilemap) => m_Tilemap = tilemap;
        public virtual Sprite GetSprite(Vector3Int position) => m_Tilemap.GetSprite(position);
        public virtual Color GetColor(Vector3Int position) => m_Tilemap.GetColor(position);
        public virtual Matrix4x4 GetTransformMatrix(Vector3Int position) => m_Tilemap.GetTransformMatrix(position);
        public virtual TileFlags GetTileFlags(Vector3Int position) => m_Tilemap.GetTileFlags(position);
        public virtual TileBase GetTile(Vector3Int position) => m_Tilemap.GetTile(position);
        public virtual T GetTile<T>(Vector3Int position) where T : TileBase => m_Tilemap.GetTile<T>(position);
        public void RefreshTile(Vector3Int position) => m_Tilemap.RefreshOne(position);
        public T GetComponent<T>() => m_Tilemap.GetComponent<T>();
        public Vector3Int origin => m_Tilemap.origin;
        public Vector3Int size => m_Tilemap.size;
        public Bounds localBounds => m_Tilemap.localBounds;
        public BoundsInt cellBounds => m_Tilemap.cellBounds;
    }

    public sealed partial class Tilemap : INativeSync
    {
        internal sealed class Cell
        {
            public TileBase tile; public Sprite sprite; public Color color = Color.white; public Matrix4x4 transform = Matrix4x4.identity;
            public TileFlags flags; public Tile.ColliderType collider; public GameObject objectToInstantiate, instantiated;
        }
        internal readonly SortedDictionary<Vector3Int, Cell> m_Cells = new(Comparer<Vector3Int>.Create((a, b) => a.z != b.z ? a.z.CompareTo(b.z) : a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x)));
        Vector3Int m_OriginCache, m_SizeCache;
        /// <summary>Bumped on every tile change (TilemapCollider2D / CompositeCollider2D rebuild their shapes).</summary>
        internal int m_TileVersion;

        public Grid layoutGrid => GetComponentInParent<Grid>();
        public Vector3Int origin { get => m_OriginCache; set { m_OriginCache = value; } }
        public Vector3Int size { get => m_SizeCache; set { m_SizeCache = value; } }
        public BoundsInt cellBounds => new BoundsInt(m_OriginCache, m_SizeCache);
        public Bounds localBounds { get { var b = cellBounds; return GetBoundsLocal(b.position, b.size); } }
        public float animationFrameRate { get => NGet("m_AnimationFrameRate") != null ? NFloat("m_AnimationFrameRate") : 1; set => NSetF("m_AnimationFrameRate", value); }
        public Color color { get => NGet("m_Color") != null ? NColor("m_Color") : Color.white; set => NSetColor("m_Color", value); }
        public Vector3 tileAnchor { get => NGet("m_TileAnchor") != null ? NVec3("m_TileAnchor") : new Vector3(0.5f, 0.5f, 0); set => NSetVec3("m_TileAnchor", value); }
        public Orientation orientation { get => (Orientation)NInt("m_TileOrientation"); set => NSetI("m_TileOrientation", (int)value); }
        public Matrix4x4 orientationMatrix
        {
            get => orientation switch
            {
                Orientation.XZ => Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(90, 0, 0), Vector3.one),
                Orientation.YX => Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(180, 0, 90), Vector3.one),
                Orientation.YZ => Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(90, 90, 0), Vector3.one),
                Orientation.ZX => Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(270, -90, 0), Vector3.one),
                Orientation.ZY => Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0, 90, 0), Vector3.one),
                Orientation.Custom => NGet("m_TileOrientationMatrix") is YMap ? ReadMatrix(NGet("m_TileOrientationMatrix") as YMap) : Matrix4x4.identity,
                _ => Matrix4x4.identity,
            };
            set { NSetI("m_TileOrientation", (int)Orientation.Custom); NSet("m_TileOrientationMatrix", WriteMatrix(value)); }
        }

        public Vector3 GetCellCenterLocal(Vector3Int position) => CellToLocalInterpolated(position + tileAnchor);
        public Vector3 GetCellCenterWorld(Vector3Int position) => LocalToWorld(GetCellCenterLocal(position));

        // ---------------------------------------------------------------- tiles
        Cell At(Vector3Int p) => m_Cells.TryGetValue(p, out var c) ? c : null;
        public TileBase GetTile(Vector3Int position) => At(position)?.tile;
        public T GetTile<T>(Vector3Int position) where T : TileBase => GetTile(position) as T;
        public bool HasTile(Vector3Int position) => m_Cells.ContainsKey(position);
        public void SetTile(Vector3Int position, TileBase tile)
        {
            SetTileNoRefresh(position, tile);
            if (tile != null) tile.RefreshTile(position, this); else RefreshNeighbours(position);
        }
        void SetTileNoRefresh(Vector3Int position, TileBase tile)
        {
            m_TileVersion++; Object.SceneEdited();
            if (m_Cells.TryGetValue(position, out var old) && old.instantiated != null) { Object.DestroyImmediate(old.instantiated); old.instantiated = null; }
            if (tile == null) { m_Cells.Remove(position); return; }
            var c = new Cell { tile = tile };
            m_Cells[position] = c;
            Grow(position);
            ApplyTileData(position, c);
        }
        void RefreshNeighbours(Vector3Int position)
        {
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
            {
                var p = new Vector3Int(position.x + dx, position.y + dy, position.z);
                if (At(p) is Cell c) c.tile.RefreshTile(p, this);
            }
        }
        void Grow(Vector3Int p)
        {
            if (m_SizeCache.x <= 0 || m_SizeCache.y <= 0 || m_SizeCache.z <= 0) { m_OriginCache = p; m_SizeCache = Vector3Int.one; return; }
            var min = Vector3Int.Min(m_OriginCache, p); var max = Vector3Int.Max(m_OriginCache + m_SizeCache, p + Vector3Int.one);
            m_OriginCache = min; m_SizeCache = max - min;
        }
        void ApplyTileData(Vector3Int position, Cell c)
        {
            var data = TileData.Default;
            c.tile.GetTileData(position, this, ref data);
            c.sprite = data.sprite; c.color = data.color; c.transform = data.transform; c.flags = data.flags; c.collider = data.colliderType;
            c.objectToInstantiate = data.gameObject;
            if (c.objectToInstantiate != null && c.instantiated == null && ((data.flags & TileFlags.InstantiateGameObjectRuntimeOnly) == 0 || Play.IsPlaying))
            {
                c.instantiated = Object.Instantiate(c.objectToInstantiate, transform);
                c.instantiated.transform.localPosition = GetCellCenterLocal(position);
            }
            c.tile.StartUp(position, this, c.instantiated);
        }
        internal void RefreshOne(Vector3Int position) { if (At(position) is Cell c) { m_TileVersion++; ApplyTileData(position, c); } }
        public void RefreshTile(Vector3Int position) => RefreshOne(position);
        public void RefreshAllTiles() { foreach (var kv in m_Cells.ToList()) kv.Value.tile.RefreshTile(kv.Key, this); }
        public void SetTiles(Vector3Int[] positionArray, TileBase[] tileArray)
        {
            if (positionArray == null || tileArray == null) throw new ArgumentNullException(positionArray == null ? nameof(positionArray) : nameof(tileArray));
            if (positionArray.Length != tileArray.Length) throw new ArgumentException("Length of positionArray does not match length of tileArray");
            for (int i = 0; i < positionArray.Length; i++) SetTileNoRefresh(positionArray[i], tileArray[i]);
            for (int i = 0; i < positionArray.Length; i++) if (tileArray[i] != null) tileArray[i].RefreshTile(positionArray[i], this); else RefreshNeighbours(positionArray[i]);
        }
        public void SetTilesBlock(BoundsInt position, TileBase[] tileArray)
        {
            var ps = new List<Vector3Int>();
            foreach (var p in position.allPositionsWithin) ps.Add(p);
            if (tileArray.Length != ps.Count) throw new ArgumentException("Length of tileArray does not match the size of the block");
            SetTiles(ps.ToArray(), tileArray);
        }
        public void SetTile(TileChangeData tileChangeData, bool ignoreLockFlags)
        {
            SetTile(tileChangeData.position, tileChangeData.tile);
            if (At(tileChangeData.position) is Cell c)
            {
                if (ignoreLockFlags || (c.flags & TileFlags.LockColor) == 0) c.color = tileChangeData.color;
                if (ignoreLockFlags || (c.flags & TileFlags.LockTransform) == 0) c.transform = tileChangeData.transform;
            }
        }
        public void SetTiles(TileChangeData[] tileChangeDataArray, bool ignoreLockFlags) { foreach (var d in tileChangeDataArray) SetTile(d, ignoreLockFlags); }
        public TileBase[] GetTilesBlock(BoundsInt bounds)
        {
            var res = new List<TileBase>();
            foreach (var p in bounds.allPositionsWithin) res.Add(GetTile(p));
            return res.ToArray();
        }
        public int GetTilesBlockNonAlloc(BoundsInt bounds, TileBase[] tiles)
        {
            int i = 0;
            foreach (var p in bounds.allPositionsWithin) { if (i >= tiles.Length) break; tiles[i++] = GetTile(p); }
            return i;
        }
        public int GetTilesRangeCount(Vector3Int startPosition, Vector3Int endPosition) => Range(startPosition, endPosition).Count();
        public int GetTilesRangeNonAlloc(Vector3Int startPosition, Vector3Int endPosition, Vector3Int[] positions, TileBase[] tiles)
        {
            int i = 0;
            foreach (var kv in Range(startPosition, endPosition)) { if (i >= positions.Length || i >= tiles.Length) break; positions[i] = kv.Key; tiles[i] = kv.Value.tile; i++; }
            return i;
        }
        IEnumerable<KeyValuePair<Vector3Int, Cell>> Range(Vector3Int a, Vector3Int b)
        {
            var min = Vector3Int.Min(a, b); var max = Vector3Int.Max(a, b);
            return m_Cells.Where(kv => kv.Key.x >= min.x && kv.Key.y >= min.y && kv.Key.z >= min.z && kv.Key.x <= max.x && kv.Key.y <= max.y && kv.Key.z <= max.z);
        }
        public void SwapTile(TileBase changeTile, TileBase newTile) { foreach (var kv in m_Cells.Where(kv => kv.Value.tile == changeTile).ToList()) SetTile(kv.Key, newTile); }
        public bool ContainsTile(TileBase tileAsset) => m_Cells.Values.Any(c => c.tile == tileAsset);
        public int GetUsedTilesCount() => m_Cells.Values.Select(c => c.tile).Distinct().Count();
        public int GetUsedSpritesCount() => m_Cells.Values.Select(c => c.sprite).Where(s => s != null).Distinct().Count();
        public int GetUsedTilesNonAlloc(TileBase[] usedTiles) { int i = 0; foreach (var t in m_Cells.Values.Select(c => c.tile).Distinct()) { if (i >= usedTiles.Length) break; usedTiles[i++] = t; } return i; }
        public int GetUsedSpritesNonAlloc(Sprite[] usedSprites) { int i = 0; foreach (var s in m_Cells.Values.Select(c => c.sprite).Where(s => s != null).Distinct()) { if (i >= usedSprites.Length) break; usedSprites[i++] = s; } return i; }
        public Sprite GetSprite(Vector3Int position) => At(position)?.sprite;
        public Matrix4x4 GetTransformMatrix(Vector3Int position) => At(position)?.transform ?? Matrix4x4.identity;
        public void SetTransformMatrix(Vector3Int position, Matrix4x4 transform) { if (At(position) is Cell c && (c.flags & TileFlags.LockTransform) == 0) { c.transform = transform; m_TileVersion++; } }
        public Color GetColor(Vector3Int position) => At(position)?.color ?? Color.white;
        public void SetColor(Vector3Int position, Color color) { if (At(position) is Cell c && (c.flags & TileFlags.LockColor) == 0) c.color = color; }
        public TileFlags GetTileFlags(Vector3Int position) => At(position)?.flags ?? TileFlags.None;
        public void SetTileFlags(Vector3Int position, TileFlags flags) { if (At(position) is Cell c) c.flags = flags; }
        public void AddTileFlags(Vector3Int position, TileFlags flags) { if (At(position) is Cell c) c.flags |= flags; }
        public void RemoveTileFlags(Vector3Int position, TileFlags flags) { if (At(position) is Cell c) c.flags &= ~flags; }
        public GameObject GetInstantiatedObject(Vector3Int position) => At(position)?.instantiated;
        public GameObject GetObjectToInstantiate(Vector3Int position) => At(position)?.objectToInstantiate;
        public void SetColliderType(Vector3Int position, Tile.ColliderType colliderType) { if (At(position) is Cell c) { c.collider = colliderType; m_TileVersion++; } }
        public Tile.ColliderType GetColliderType(Vector3Int position) => At(position)?.collider ?? Tile.ColliderType.None;
        public void FloodFill(Vector3Int position, TileBase tile)
        {
            var target = GetTile(position);
            if (target == tile) return;
            var b = cellBounds; if (b.size.x <= 0) { SetTile(position, tile); return; }
            var todo = new Stack<Vector3Int>(); todo.Push(position);
            var seen = new HashSet<Vector3Int>();
            while (todo.Count > 0)
            {
                var p = todo.Pop();
                if (!seen.Add(p) || GetTile(p) != target || p.x < b.xMin || p.y < b.yMin || p.x >= b.xMax || p.y >= b.yMax) continue;
                SetTileNoRefresh(p, tile);
                todo.Push(p + Vector3Int.right); todo.Push(p + Vector3Int.left); todo.Push(p + Vector3Int.up); todo.Push(p + Vector3Int.down);
            }
            foreach (var p in seen) if (At(p) is Cell c) c.tile.RefreshTile(p, this);
        }
        public void BoxFill(Vector3Int position, TileBase tile, int startX, int startY, int endX, int endY)
        {
            for (int y = Math.Min(startY, endY); y <= Math.Max(startY, endY); y++)
                for (int x = Math.Min(startX, endX); x <= Math.Max(startX, endX); x++) SetTile(new Vector3Int(x, y, position.z), tile);
        }
        public void ClearAllTiles()
        {
            foreach (var c in m_Cells.Values) if (c.instantiated != null) Object.DestroyImmediate(c.instantiated);
            m_Cells.Clear(); m_OriginCache = Vector3Int.zero; m_SizeCache = Vector3Int.zero; m_TileVersion++; Object.SceneEdited();
        }
        public void ResizeBounds() => CompressBounds();
        public void CompressBounds()
        {
            if (m_Cells.Count == 0) { m_OriginCache = Vector3Int.zero; m_SizeCache = Vector3Int.zero; return; }
            var min = m_Cells.Keys.Aggregate(Vector3Int.Min); var max = m_Cells.Keys.Aggregate(Vector3Int.Max);
            m_OriginCache = min; m_SizeCache = max - min + Vector3Int.one;
        }

        // ---------------------------------------------------------------- serialized data (Unity's m_Tiles + shared arrays)
        public void SyncFromNative()
        {
            m_Cells.Clear();
            var tileAssets = Seq("m_TileAssetArray", "m_Data"); var sprites = Seq("m_TileSpriteArray", "m_Data");
            var matrices = (NGet("m_TileMatrixArray") as YSeq)?.Items.Select(i => (i as YMap)?["m_Data"] as YMap).ToList() ?? new();
            var colors = (NGet("m_TileColorArray") as YSeq)?.Items.Select(i => Serializer.ReadColor((i as YMap)?["m_Data"])).ToList() ?? new();
            var objects = Seq("m_TileObjectToInstantiateArray", "m_Data");
            if (NGet("m_Tiles") is YSeq tiles)
                foreach (var it in tiles.Items.OfType<YMap>())
                {
                    var f = it["first"] as YMap; var s = it["second"] as YMap;
                    if (f == null || s == null) continue;
                    var p = new Vector3Int(Serializer.Int(f["x"], 0), Serializer.Int(f["y"], 0), Serializer.Int(f["z"], 0));
                    int Idx(string k) => Serializer.Int(s[k], -1);
                    var c = new Cell
                    {
                        tile = Pick(tileAssets, Idx("m_TileIndex")) as TileBase, sprite = Pick(sprites, Idx("m_TileSpriteIndex")) as Sprite,
                        transform = Idx("m_TileMatrixIndex") is int mi && mi >= 0 && mi < matrices.Count && matrices[mi] != null ? ReadMatrix(matrices[mi]) : Matrix4x4.identity,
                        color = Idx("m_TileColorIndex") is int ci && ci >= 0 && ci < colors.Count ? colors[ci] : Color.white,
                        flags = (TileFlags)Serializer.Int(s["m_AllTileFlags"], 0), collider = (Tile.ColliderType)Serializer.Int(s["m_ColliderType"], 0),
                        objectToInstantiate = Pick(objects, Idx("m_TileObjectToInstantiateIndex")) as GameObject,
                    };
                    if (c.tile != null) m_Cells[p] = c;
                }
            m_OriginCache = NGet("m_Origin") != null ? V3I(NGet("m_Origin")) : Vector3Int.zero;
            m_SizeCache = NGet("m_Size") != null ? V3I(NGet("m_Size")) : Vector3Int.zero;
            m_TileVersion++;
        }
        List<YNode> Seq(string key, string data) => (NGet(key) as YSeq)?.Items.Select(i => (i as YMap)?[data]).ToList() ?? new List<YNode>();
        static Object Pick(List<YNode> list, int i) => i >= 0 && i < list.Count && list[i] is YRef r ? (r.Raw ? AssetLoader.ResolveExternal(r) : r.Target) : null;
        static Vector3Int V3I(YNode n) => n is YMap m ? new Vector3Int(Serializer.Int(m["x"], 0), Serializer.Int(m["y"], 0), Serializer.Int(m["z"], 0)) : Vector3Int.zero;

        public void SyncToNative()
        {
            var tiles = new YSeq(); var tileAssets = new List<(Object o, int refs)>(); var sprites = new List<(Object o, int refs)>();
            var matrices = new List<(Matrix4x4 m, int refs)>(); var colors = new List<(Color c, int refs)>(); var objects = new List<(Object o, int refs)>();
            int Index<T>(List<(T v, int refs)> l, T v, Func<T, T, bool> eq)
            {
                for (int i = 0; i < l.Count; i++) if (eq(l[i].v, v)) { l[i] = (l[i].v, l[i].refs + 1); return i; }
                l.Add((v, 1)); return l.Count - 1;
            }
            foreach (var (p, c) in m_Cells)
            {
                var second = new YMap();
                second.Add("serializedVersion", "2");
                second.Add("m_TileIndex", YScalar.Of(Index(tileAssets, (Object)c.tile, (a, b) => a == b)));
                second.Add("m_TileSpriteIndex", YScalar.Of(c.sprite != null ? Index(sprites, (Object)c.sprite, (a, b) => a == b) : 65535));
                second.Add("m_TileMatrixIndex", YScalar.Of(Index(matrices, c.transform, (a, b) => a == b)));
                second.Add("m_TileColorIndex", YScalar.Of(Index(colors, c.color, (a, b) => a == b)));
                second.Add("m_TileObjectToInstantiateIndex", YScalar.Of(c.objectToInstantiate != null ? Index(objects, (Object)c.objectToInstantiate, (a, b) => a == b) : 65535));
                second.Add("dummyAlignment", "0");
                second.Add("m_AllTileFlags", YScalar.Of((int)c.flags | (int)c.collider << 28));
                var first = new YMap { Flow = true }.Add("x", YScalar.Of(p.x)).Add("y", YScalar.Of(p.y)).Add("z", YScalar.Of(p.z));
                tiles.Items.Add(new YMap().Add("first", first).Add("second", second));
            }
            YSeq Refs(List<(Object o, int refs)> l) { var s = new YSeq(); foreach (var (o, r) in l) s.Items.Add(new YMap().Add("m_RefCount", YScalar.Of(r)).Add("m_Data", new YRef(o))); return s; }
            NSet("m_Tiles", tiles);
            NSet("m_TileAssetArray", Refs(tileAssets));
            NSet("m_TileSpriteArray", Refs(sprites));
            var ms = new YSeq(); foreach (var (m, r) in matrices) ms.Items.Add(new YMap().Add("m_RefCount", YScalar.Of(r)).Add("m_Data", WriteMatrix(m))); NSet("m_TileMatrixArray", ms);
            var cs = new YSeq(); foreach (var (c, r) in colors) cs.Items.Add(new YMap().Add("m_RefCount", YScalar.Of(r)).Add("m_Data", Serializer.Col(c))); NSet("m_TileColorArray", cs);
            NSet("m_TileObjectToInstantiateArray", Refs(objects));
            NSet("m_Origin", new YMap { Flow = true }.Add("x", YScalar.Of(m_OriginCache.x)).Add("y", YScalar.Of(m_OriginCache.y)).Add("z", YScalar.Of(m_OriginCache.z)));
            NSet("m_Size", new YMap { Flow = true }.Add("x", YScalar.Of(m_SizeCache.x)).Add("y", YScalar.Of(m_SizeCache.y)).Add("z", YScalar.Of(m_SizeCache.z)));
        }

        static readonly string[] MatrixKeys = { "e00", "e01", "e02", "e03", "e10", "e11", "e12", "e13", "e20", "e21", "e22", "e23", "e30", "e31", "e32", "e33" };
        static Matrix4x4 ReadMatrix(YMap m)
        {
            var r = new Matrix4x4();
            for (int i = 0; i < 16; i++) r[i / 4, i % 4] = m[MatrixKeys[i]] is YScalar s && s.Value.Length > 0 ? s.F : (i % 5 == 0 ? 1 : 0);
            return r;
        }
        static YMap WriteMatrix(Matrix4x4 m)
        {
            var y = new YMap { Flow = false }; y.Add("serializedVersion", "2");
            for (int i = 0; i < 16; i++) y.Add(MatrixKeys[i], YScalar.Of(m[i / 4, i % 4]));
            return y;
        }

        /// <summary>World matrix of a tile's quad: cell anchor (tileAnchor), the tile's transform and the orientation.</summary>
        internal Matrix4x4 TileMatrix(Vector3Int p, Cell c) => transform.localToWorldMatrix * Matrix4x4.Translate(GetCellCenterLocal(p)) * orientationMatrix * c.transform;
    }

    public sealed partial class TilemapRenderer
    {
        public SortOrder sortOrder { get => (SortOrder)NInt("m_SortOrder"); set => NSetI("m_SortOrder", (int)value); }
        public Mode mode { get => (Mode)NInt("m_Mode"); set => NSetI("m_Mode", (int)value); }
        public Vector3Int chunkSize { get => NGet("m_ChunkSize") is YMap m ? new Vector3Int(Serializer.Int(m["x"], 32), Serializer.Int(m["y"], 32), Serializer.Int(m["z"], 1)) : new Vector3Int(32, 32, 1); set => NSet("m_ChunkSize", new YMap { Flow = true }.Add("x", YScalar.Of(value.x)).Add("y", YScalar.Of(value.y)).Add("z", YScalar.Of(value.z))); }
        public Vector3 chunkCullingBounds { get => NVec3("m_ChunkCullingBounds"); set => NSetVec3("m_ChunkCullingBounds", value); }
        public int maxChunkCount { get => NGet("m_MaxChunkCount") != null ? NInt("m_MaxChunkCount") : 16; set => NSetI("m_MaxChunkCount", value); }
        public int maxFrameAge { get => NGet("m_MaxFrameAge") != null ? NInt("m_MaxFrameAge") : 16; set => NSetI("m_MaxFrameAge", value); }
        public DetectChunkCullingBounds detectChunkCullingBounds { get => (DetectChunkCullingBounds)NInt("m_DetectChunkCullingBounds"); set => NSetI("m_DetectChunkCullingBounds", (int)value); }
        public SpriteMaskInteraction maskInteraction { get => (SpriteMaskInteraction)NInt("m_MaskInteraction"); set => NSetI("m_MaskInteraction", (int)value); }

        /// <summary>Tiles in drawing order (sortOrder: the corner the drawing starts from).</summary>
        internal IEnumerable<(Vector3Int pos, Tilemap.Cell cell)> TilesInOrder(Tilemap map)
        {
            var so = sortOrder;
            var cells = map.m_Cells.Select(kv => (kv.Key, kv.Value));
            return so switch
            {
                SortOrder.BottomRight => cells.OrderBy(c => c.Key.z).ThenBy(c => c.Key.y).ThenByDescending(c => c.Key.x),
                SortOrder.TopLeft => cells.OrderBy(c => c.Key.z).ThenByDescending(c => c.Key.y).ThenBy(c => c.Key.x),
                SortOrder.TopRight => cells.OrderBy(c => c.Key.z).ThenByDescending(c => c.Key.y).ThenByDescending(c => c.Key.x),
                _ => cells.OrderBy(c => c.Key.z).ThenBy(c => c.Key.y).ThenBy(c => c.Key.x),
            };
        }
    }
}

namespace UnityEngine.Tilemaps
{
    public sealed partial class TilemapCollider2D
    {
        public bool useDelaunayMesh { get => NBool("m_UseDelaunayMesh"); set => NSetB("m_UseDelaunayMesh", value); }
        public uint maximumTileChangeCount { get => NGet("m_MaximumTileChangeCount") != null ? (uint)NLong("m_MaximumTileChangeCount") : 1000; set => NSetI("m_MaximumTileChangeCount", value); }
        public float extrusionFactor { get => NGet("m_ExtrusionFactor") != null ? NFloat("m_ExtrusionFactor") : 0.00001f; set => NSetF("m_ExtrusionFactor", value); }
        public bool hasTilemapChanges => false;
        public void ProcessTilemapChanges() => Physics2DWorld.Sync();

        Tilemap Map => GetComponent<Tilemaps.Tilemap>();

        /// <summary>Collision outlines of the tiles in this collider's space: the cell for Grid tiles, the sprite's physics shapes for
        /// Sprite tiles (through the tile's transform), grown by extrusionFactor.</summary>
        internal List<List<Vector2>> CellOutlines()
        {
            var res = new List<List<Vector2>>();
            var map = Map;
            if (map == null) return res;
            float ex = extrusionFactor;
            var tmp = new List<Vector2>();
            foreach (var (p, c) in map.m_Cells)
            {
                if (c.collider == Tile.ColliderType.None) continue;
                var local = Matrix4x4.Translate(map.GetCellCenterLocal(p)) * map.orientationMatrix * c.transform;
                if (c.collider == Tile.ColliderType.Grid || c.sprite == null)
                {
                    var b = map.GetBoundsLocal(p);
                    res.Add(new List<Vector2> { new(b.min.x - ex, b.min.y - ex), new(b.max.x + ex, b.min.y - ex), new(b.max.x + ex, b.max.y + ex), new(b.min.x - ex, b.max.y + ex) });
                    continue;
                }
                for (int i = 0; i < c.sprite.GetPhysicsShapeCount(); i++)
                {
                    c.sprite.GetPhysicsShape(i, tmp);
                    var ctr = Vector2.zero; foreach (var v in tmp) ctr += v; ctr /= Math.Max(1, tmp.Count);
                    res.Add(tmp.Select(v => { var g = v + (v - ctr).normalized * ex; var w = local.MultiplyPoint3x4(g); return new Vector2(w.x, w.y); }).ToList());
                }
            }
            return res;
        }

        internal override string ShapeKey() => $"tilemap{Map?.m_TileVersion}{Map?.GetInstanceID()}{offset}{extrusionFactor}";
        internal override IEnumerable<Shape2> BuildShapes(Vector2 scale, float contactOffset)
        {
            foreach (var outline in CellOutlines())
            {
                var ps = outline.Select(p => Vector2.Scale(p + offset, scale)).ToList();
                if (PolygonUnion.SignedArea(ps) < 0) ps.Reverse();
                foreach (var piece in Decompose.ConvexPieces(ps))
                {
                    var s = Shape2.Polygon(piece, contactOffset);
                    if (s != null) yield return s;
                }
            }
        }
    }
}
