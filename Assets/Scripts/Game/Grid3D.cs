using ParcelFactory.Core;
using UnityEngine;

namespace ParcelFactory.Game
{
    /// <summary>격자 (gx, gy) ↔ 월드. 1칸 = 1m, 북쪽(gy+) = +z.</summary>
    public static class Grid3D
    {
        public const float Half = 5.5f;

        public static Vector3 Tile(int gx, int gy) => new Vector3(gx - Half, 0f, gy - Half);
        public static Vector3 TileF(float gx, float gy) => new Vector3(gx - Half, 0f, gy - Half);

        public static Vector3 Forward(Direction d) => new Vector3(d.Dx(), 0f, d.Dy());
        public static Quaternion Facing(Direction d) => Quaternion.LookRotation(Forward(d), Vector3.up);

        /// <summary>월드 좌표를 격자 칸으로. 범위 밖이면 false.</summary>
        public static bool ToTile(Vector3 world, out int gx, out int gy)
        {
            gx = Mathf.FloorToInt(world.x + Half + 0.5f);
            gy = Mathf.FloorToInt(world.z + Half + 0.5f);
            return gx >= 0 && gy >= 0 && gx < FactoryGrid.Width && gy < FactoryGrid.Height;
        }
    }
}
