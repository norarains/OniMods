using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    // Native placement offsets are relative to the object origin, not its lower-left cell.
    // Accept already rotated offsets so the same conversion serves previews and writes.
    internal sealed class BuildingFootprintLayout
    {
        internal readonly List<System.Tuple<int, int>> Offsets;
        internal readonly int MinX, MinY, Width, Height;

        internal BuildingFootprintLayout(IEnumerable<System.Tuple<int, int>> offsets)
        {
            Offsets = offsets.Distinct().ToList();
            if (Offsets.Count == 0) throw new ArgumentException("Building placement offsets are unavailable.");
            MinX = Offsets.Min(p => p.Item1);
            MinY = Offsets.Min(p => p.Item2);
            Width = Offsets.Max(p => p.Item1) - MinX + 1;
            Height = Offsets.Max(p => p.Item2) - MinY + 1;
        }

        internal int OriginX(int lowerLeftX) => lowerLeftX - MinX;
        internal int OriginY(int lowerLeftY) => lowerLeftY - MinY;
        internal IEnumerable<System.Tuple<int, int>> Cells(int lowerLeftX, int lowerLeftY)
            => Offsets.Select(p => System.Tuple.Create(OriginX(lowerLeftX) + p.Item1, OriginY(lowerLeftY) + p.Item2));
    }
}
