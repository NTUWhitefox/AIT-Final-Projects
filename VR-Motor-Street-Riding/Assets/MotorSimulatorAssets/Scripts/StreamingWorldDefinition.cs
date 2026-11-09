using System;
using UnityEngine;

namespace AIT.Riding
{
    [Serializable] public sealed class StreamingChunkDefinition
    {
        public string id, scenePath;
        public Vector2Int cell;
        public Bounds bounds;
    }
    [Serializable] public sealed class BlockoutRoadDefinition
    {
        public string id;
        public Vector3[] centreline;
        public float width = 14;
    }
    // Small first contract. Chunk lifetime does not depend on road shape or width.
    [CreateAssetMenu(menuName = "AIT/Streaming World Definition")]
    public sealed class StreamingWorldDefinition : ScriptableObject
    {
        public string worldId = "streaming-blockout", contentVersion = "1";
        public Vector3 gridOrigin;
        public float chunkSize = 256;
        public Bounds worldBounds;
        public StreamingChunkDefinition[] chunks;
        public BlockoutRoadDefinition[] roads;
        public Vector3[] routePoints;
        public Vector2Int CellAt(Vector3 position) => new Vector2Int(
            Mathf.FloorToInt((position.x - gridOrigin.x) / chunkSize),
            Mathf.FloorToInt((position.z - gridOrigin.z) / chunkSize));
    }
}
