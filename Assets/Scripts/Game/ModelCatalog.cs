using System;
using System.Collections.Generic;
using UnityEngine;

namespace ParcelFactory.Game
{
    /// <summary>Kenney FBX 프리팹을 이름으로 찾는다. 에디터의 CatalogBuilder가 채운다.</summary>
    public sealed class ModelCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string Key;
            public GameObject Prefab;
        }

        public List<Entry> Entries = new List<Entry>();

        private Dictionary<string, GameObject> map;

        public GameObject Get(string key)
        {
            if (map == null)
            {
                map = new Dictionary<string, GameObject>();
                foreach (var e in Entries) if (e.Prefab != null && !map.ContainsKey(e.Key)) map[e.Key] = e.Prefab;
            }
            if (!map.TryGetValue(key, out var go))
                throw new KeyNotFoundException($"ModelCatalog has no '{key}'");
            return go;
        }

        public bool Has(string key)
        {
            try { Get(key); return true; } catch (KeyNotFoundException) { return false; }
        }
    }
}
