using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Nasser.SceneChunks
{
    public enum ChunkState
    {
        None,
        Queued,
        Loading,
        Active,
        Pooled
    }

    /// <summary>
    /// Keeps a moving window of chunks alive around a target transform.
    ///
    /// Multiplayer note: this is a purely local, cosmetic system. Point it at the *local*
    /// player only. World state stays authoritative on the server regardless of which
    /// chunks any given client happens to have resident.
    /// </summary>
    [AddComponentMenu("Scene Chunks/Chunk Streamer")]
    [DefaultExecutionOrder(-50)]
    public class ChunkStreamer : MonoBehaviour
    {
        private class Record
        {
            public ChunkCoord Coord;
            public ChunkState State;
            public GameObject Root;
            public Coroutine Routine;
            public float PooledAt;
        }

        [SerializeField] private ChunkStreamingSettings settings;
        [SerializeField] private ChunkProvider provider;

        [Tooltip("Usually the local player. Leave empty to auto-bind the first ChunkStreamTarget that registers.")]
        [SerializeField] private Transform target;

        [Tooltip("Parent for instantiated chunks. Defaults to this transform.")]
        [SerializeField] private Transform chunkRoot;

        [Tooltip("Run one full load pass before the first frame is shown, so the player never spawns into an empty world.")]
        [SerializeField] private bool warmupOnStart = true;

        [Header("View & bias")]
        [Tooltip("Optional camera transform to stream around, distinct from the target (player). Leave empty to stream around the target only.")]
        [SerializeField] private Transform view;

        [Tooltip("Where the load window centres when a view transform is set. Target = player only (default, backward compatible).")]
        [SerializeField] private StreamingAnchor anchor = StreamingAnchor.Target;

        [Tooltip("Shift the load window this many chunks along the view's forward vector, so you stream more of what you look at. 0 = symmetric (default). Typical 0.5 - 1.5.")]
        [SerializeField] private float forwardBiasChunks = 0f;

        [Tooltip("Seconds to damp the directional bias so spinning the view does not cause a load storm.")]
        [Min(0f)]
        [SerializeField] private float biasSmoothTime = 0.35f;

        /// <summary>Raised when a chunk becomes visible (freshly loaded or restored from the pool).</summary>
        public event Action<ChunkCoord> ChunkActivated;

        /// <summary>Raised when a chunk leaves the active set.</summary>
        public event Action<ChunkCoord> ChunkReleased;

        private readonly Dictionary<ChunkCoord, Record> _records = new Dictionary<ChunkCoord, Record>();
        private readonly List<Record> _pooled = new List<Record>();
        private readonly List<ChunkCoord> _wanted = new List<ChunkCoord>();
        private readonly List<Record> _releaseScratch = new List<Record>();
        private readonly List<float> _poolTimesScratch = new List<float>();

        // Load window centre (target, view, or midpoint, plus directional bias) and the
        // unbiased player centre used for releasing. They coincide under default settings.
        private ChunkCoord _loadCenter;
        private ChunkCoord _unloadCenter;
        private bool _hasCenters;
        private Vector3 _smoothBias;
        private Vector3 _biasVel;
        private ChunkCoord _sortCenter;
        private Comparison<ChunkCoord> _nearestFirst;
        private float _timer;
        private int _loadsInFlight;

        public ChunkStreamingSettings Settings { get { return settings; } set { settings = value; } }
        public ChunkProvider Provider { get { return provider; } set { provider = value; } }
        public Transform Target { get { return target; } }
        public Transform View { get { return view; } set { view = value; } }
        public StreamingAnchor Anchor { get { return anchor; } set { anchor = value; } }

        /// <summary>Chunk the load window is centred on (with anchor and bias applied).</summary>
        public ChunkCoord CurrentCoord { get { return _loadCenter; } }

        /// <summary>Chunk the unbiased player is standing in; releasing is measured from here.</summary>
        public ChunkCoord PlayerCoord { get { return _unloadCenter; } }

        public bool IsReady { get { return settings != null && provider != null && target != null; } }

        public int ActiveCount { get; private set; }
        public int LoadingCount { get { return _loadsInFlight; } }
        public int PooledCount { get { return _pooled.Count; } }
        public int ResidentCount { get { return _records.Count; } }

        public static ChunkStreamer Instance { get; private set; }

        private void Awake()
        {
            if (Instance == null) Instance = this;
            if (chunkRoot == null) chunkRoot = transform;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private IEnumerator Start()
        {
            if (!warmupOnStart) yield break;
            while (target == null) yield return null;

            // Fill the whole radius before the player sees anything, rather than
            // letting the per-tick budget drip chunks in over the first few seconds.
            int guard = 0;
            int expected = settings != null ? settings.ActiveChunkCount : 0;
            while (guard++ < 1000)
            {
                Refresh();
                while (_loadsInFlight > 0) yield return null;
                if (ActiveCount >= expected) break;

                int before = ResidentCount;
                RequestNearby();
                while (_loadsInFlight > 0) yield return null;
                if (ResidentCount == before) break; // nothing left that the provider can supply
            }
        }

        /// <summary>Bind the transform the window follows. Call this when your local player spawns.</summary>
        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
            _hasCenters = false;
            _timer = 0f;
        }

        /// <summary>Bind or clear the separate view (camera) transform the window can centre on.</summary>
        public void SetView(Transform newView)
        {
            view = newView;
        }

        private void Update()
        {
            if (!IsReady) return;

            // Smooth the directional bias every frame so a fast turn ramps in rather than
            // snapping and triggering a load storm. No-op (and no allocation) when bias is off.
            UpdateBias();

            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = settings.RefreshInterval;

            Refresh();
        }

        private void UpdateBias()
        {
            if (forwardBiasChunks == 0f && _smoothBias == Vector3.zero) return;

            Vector3 forward = view != null ? view.forward : target.forward;
            Vector3 targetBias = ChunkWindow.ForwardBias(forward, forwardBiasChunks, settings.ChunkSize);
            _smoothBias = Vector3.SmoothDamp(_smoothBias, targetBias, ref _biasVel, Mathf.Max(0.0001f, biasSmoothTime));
        }

        private void Refresh()
        {
            if (!IsReady) return;

            bool hasView = view != null;
            Vector3 viewPos = hasView ? view.position : target.position;
            Vector3 anchorPos = ChunkWindow.AnchorPosition(anchor, target.position, viewPos, hasView);

            ChunkCoord loadCenter = ChunkGrid.WorldToCoord(anchorPos + _smoothBias, settings.ChunkSize);
            ChunkCoord unloadCenter = ChunkGrid.WorldToCoord(target.position, settings.ChunkSize);

            bool moved = !_hasCenters || loadCenter != _loadCenter || unloadCenter != _unloadCenter;
            _loadCenter = loadCenter;
            _unloadCenter = unloadCenter;
            _hasCenters = true;

            if (moved) ReleaseDistant();
            RequestNearby();
        }

        // ---------------------------------------------------------------- request

        private void RequestNearby()
        {
            int budget = settings.MaxConcurrentLoads - _loadsInFlight;
            if (budget <= 0) return;

            _wanted.Clear();
            int r = settings.LoadRadius;

            for (int dx = -r; dx <= r; dx++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    ChunkCoord coord = new ChunkCoord(_loadCenter.X + dx, _loadCenter.Z + dz);

                    Record existing;
                    if (_records.TryGetValue(coord, out existing))
                    {
                        // A pooled chunk inside the radius comes back instantly - no load cost at all.
                        if (existing.State == ChunkState.Pooled) Restore(existing);
                        continue;
                    }

                    if (!provider.Exists(coord)) continue;
                    _wanted.Add(coord);
                }
            }

            if (_wanted.Count == 0) return;

            // Nearest first, so the ground under the player's feet always wins the budget.
            // Comparator is cached (reads _sortCenter) so the per-refresh sort never allocates.
            _sortCenter = _loadCenter;
            if (_nearestFirst == null) _nearestFirst = CompareByDistance;
            _wanted.Sort(_nearestFirst);

            int count = Mathf.Min(budget, _wanted.Count);
            for (int i = 0; i < count; i++) BeginLoad(_wanted[i]);
        }

        private int CompareByDistance(ChunkCoord a, ChunkCoord b)
        {
            return ChunkCoord.Distance(a, _sortCenter).CompareTo(ChunkCoord.Distance(b, _sortCenter));
        }

        private void BeginLoad(ChunkCoord coord)
        {
            Record record = new Record { Coord = coord, State = ChunkState.Queued };
            _records[coord] = record;
            _loadsInFlight++;
            record.Routine = StartCoroutine(LoadRoutine(record));
        }

        private IEnumerator LoadRoutine(Record record)
        {
            record.State = ChunkState.Loading;
            bool done = false;

            yield return provider.Load(record.Coord, chunkRoot, delegate (GameObject root)
            {
                record.Root = root;
                done = true;
            });

            _loadsInFlight--;
            record.Routine = null;

            // The player may have run past this chunk while it was loading. Keep it if it is
            // still resident relative to either the load window or the player.
            if (!IsResident(record.Coord))
            {
                Discard(record);
                yield break;
            }

            record.State = ChunkState.Active;
            ActiveCount++;
            if (done && ChunkActivated != null) ChunkActivated(record.Coord);
        }

        // ---------------------------------------------------------------- release

        private void ReleaseDistant()
        {
            _releaseScratch.Clear();

            foreach (KeyValuePair<ChunkCoord, Record> kv in _records)
            {
                Record record = kv.Value;
                if (record.State != ChunkState.Active) continue;
                if (IsResident(record.Coord)) continue;
                _releaseScratch.Add(record);
            }

            int count = Mathf.Min(settings.MaxReleasesPerTick, _releaseScratch.Count);
            for (int i = 0; i < count; i++) Release(_releaseScratch[i]);
        }

        // A chunk stays resident while it is within the unload radius of EITHER the load window
        // or the unbiased player. Measuring against the player guarantees spinning the view never
        // releases the ground under your feet; measuring against the load window keeps the extra
        // forward-biased chunks alive while you look their way.
        private bool IsResident(ChunkCoord coord)
        {
            int limit = settings.UnloadRadius;
            return !ChunkWindow.ShouldRelease(coord, _unloadCenter, limit)
                   || !ChunkWindow.ShouldRelease(coord, _loadCenter, limit);
        }

        private void Release(Record record)
        {
            ActiveCount--;
            if (ChunkReleased != null) ChunkReleased(record.Coord);

            bool canPool = settings.EnablePooling
                           && settings.MaxPooledChunks > 0
                           && provider.SupportsPooling
                           && record.Root != null;

            if (!canPool)
            {
                Discard(record);
                return;
            }

            record.Root.SetActive(false);
            record.State = ChunkState.Pooled;
            record.PooledAt = Time.time;
            _pooled.Add(record);

            TrimPool();
        }

        private void Restore(Record record)
        {
            _pooled.Remove(record);
            if (record.Root != null) record.Root.SetActive(true);
            record.State = ChunkState.Active;
            ActiveCount++;
            if (ChunkActivated != null) ChunkActivated(record.Coord);
        }

        private void TrimPool()
        {
            while (_pooled.Count > settings.MaxPooledChunks)
            {
                // Evict the chunk pooled longest ago (LRU).
                _poolTimesScratch.Clear();
                for (int i = 0; i < _pooled.Count; i++) _poolTimesScratch.Add(_pooled[i].PooledAt);

                int oldest = ChunkWindow.OldestIndex(_poolTimesScratch);
                if (oldest < 0) break;

                Record victim = _pooled[oldest];
                _pooled.RemoveAt(oldest);
                Discard(victim);
            }
        }

        private void Discard(Record record)
        {
            _records.Remove(record.Coord);
            provider.Unload(record.Coord, record.Root);
            record.Root = null;
            record.State = ChunkState.None;
        }

        /// <summary>Destroy every resident chunk. Useful when teleporting the player across the map.</summary>
        public void FlushAll()
        {
            foreach (KeyValuePair<ChunkCoord, Record> kv in _records)
            {
                Record record = kv.Value;
                if (record.Routine != null) StopCoroutine(record.Routine);
                provider.Unload(record.Coord, record.Root);
            }
            _records.Clear();
            _pooled.Clear();
            ActiveCount = 0;
            _loadsInFlight = 0;
            _hasCenters = false;
            _smoothBias = Vector3.zero;
            _biasVel = Vector3.zero;
        }

        public ChunkState GetState(ChunkCoord coord)
        {
            Record record;
            return _records.TryGetValue(coord, out record) ? record.State : ChunkState.None;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (settings == null || !settings.DrawGizmos) return;

            Transform t = target != null ? target : transform;
            ChunkCoord center = ChunkGrid.WorldToCoord(t.position, settings.ChunkSize);
            float size = settings.ChunkSize;
            float h = settings.GizmoHeight;

            DrawRing(center, settings.LoadRadius, size, h, new Color(0.36f, 0.79f, 0.65f, 0.9f));
            DrawRing(center, settings.UnloadRadius, size, h, new Color(0.94f, 0.62f, 0.15f, 0.7f));

            Gizmos.color = new Color(0.36f, 0.79f, 0.65f, 0.18f);
            int r = settings.LoadRadius;
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    Bounds b = ChunkGrid.CoordToBounds(new ChunkCoord(center.X + dx, center.Z + dz), size, 0.1f);
                    Gizmos.DrawWireCube(b.center, b.size);
                }
            }
        }

        private static void DrawRing(ChunkCoord center, int radius, float size, float height, Color color)
        {
            Gizmos.color = color;
            float span = (radius * 2 + 1) * size;
            Vector3 c = ChunkGrid.CoordToCenter(center, size);
            c.y = height * 0.5f;
            Gizmos.DrawWireCube(c, new Vector3(span, height, span));
        }
#endif
    }
}
