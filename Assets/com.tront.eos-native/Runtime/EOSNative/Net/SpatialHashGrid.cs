using System;
using System.Collections.Generic;
using UnityEngine;

namespace EOSNative.Net
{
    /// <summary>
    /// 2D uniform spatial hash grid for interest management.
    /// Projects 3D positions onto a 2D plane (XZ by default) and assigns
    /// objects to grid cells. Visibility is determined by 9-neighbor lookup
    /// (current cell + 8 adjacent), same approach as Mirror.
    ///
    /// Cell size is automatically derived: visRange / 2, so 2 cells = visRange.
    /// </summary>
    public class SpatialHashGrid
    {
        /// <summary>Which axes to project onto for the 2D grid.</summary>
        public enum GridAxes { XZ, XY }

        private readonly Dictionary<long, HashSet<uint>> _cells = new();
        private readonly Dictionary<uint, long> _objectCells = new();
        private readonly int _cellSize;
        private readonly GridAxes _axes;

        /// <summary>
        /// Create a spatial hash grid.
        /// </summary>
        /// <param name="visRange">Visibility range in world units.</param>
        /// <param name="axes">Which 2D plane to project onto.</param>
        public SpatialHashGrid(int visRange, GridAxes axes = GridAxes.XZ)
        {
            _cellSize = Math.Max(1, visRange / 2);
            _axes = axes;
        }

        /// <summary>The cell size (visRange / 2).</summary>
        public int CellSize => _cellSize;

        /// <summary>
        /// Insert or update an object's position in the grid.
        /// </summary>
        public void UpdateObject(uint networkId, Vector3 worldPosition)
        {
            long newCell = PositionToCell(worldPosition);

            if (_objectCells.TryGetValue(networkId, out long oldCell))
            {
                if (oldCell == newCell) return; // no change

                // Remove from old cell
                if (_cells.TryGetValue(oldCell, out var oldSet))
                {
                    oldSet.Remove(networkId);
                    if (oldSet.Count == 0) _cells.Remove(oldCell);
                }
            }

            // Add to new cell
            if (!_cells.TryGetValue(newCell, out var set))
            {
                set = new HashSet<uint>();
                _cells[newCell] = set;
            }
            set.Add(networkId);
            _objectCells[networkId] = newCell;
        }

        /// <summary>Remove an object from the grid.</summary>
        public void RemoveObject(uint networkId)
        {
            if (_objectCells.TryGetValue(networkId, out long cell))
            {
                _objectCells.Remove(networkId);
                if (_cells.TryGetValue(cell, out var set))
                {
                    set.Remove(networkId);
                    if (set.Count == 0) _cells.Remove(cell);
                }
            }
        }

        /// <summary>
        /// Get all object IDs visible from a position (current cell + 8 neighbors).
        /// </summary>
        public void GetNearbyObjects(Vector3 worldPosition, HashSet<uint> results)
        {
            results.Clear();
            var center = PositionToCellXY(worldPosition);

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    long key = PackCell(center.x + dx, center.y + dy);
                    if (_cells.TryGetValue(key, out var set))
                    {
                        foreach (var id in set)
                            results.Add(id);
                    }
                }
            }
        }

        /// <summary>
        /// Check if two positions are in the same or neighboring cells (within interest range).
        /// </summary>
        public bool AreNeighbors(Vector3 a, Vector3 b)
        {
            var cellA = PositionToCellXY(a);
            var cellB = PositionToCellXY(b);
            int dx = Math.Abs(cellA.x - cellB.x);
            int dy = Math.Abs(cellA.y - cellB.y);
            return dx <= 1 && dy <= 1;
        }

        /// <summary>Clear all objects from the grid.</summary>
        public void Clear()
        {
            _cells.Clear();
            _objectCells.Clear();
        }

        private long PositionToCell(Vector3 pos)
        {
            var xy = PositionToCellXY(pos);
            return PackCell(xy.x, xy.y);
        }

        private Vector2Int PositionToCellXY(Vector3 pos)
        {
            if (_axes == GridAxes.XZ)
                return new Vector2Int(
                    Mathf.FloorToInt(pos.x / _cellSize),
                    Mathf.FloorToInt(pos.z / _cellSize));
            else
                return new Vector2Int(
                    Mathf.FloorToInt(pos.x / _cellSize),
                    Mathf.FloorToInt(pos.y / _cellSize));
        }

        private static long PackCell(int x, int y)
        {
            return ((long)x << 32) | (uint)y;
        }
    }
}
