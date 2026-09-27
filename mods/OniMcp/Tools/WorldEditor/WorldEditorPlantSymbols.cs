using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static readonly Dictionary<string, SymbolGlyphEntry> RuntimePlantGlyphs =
            new Dictionary<string, SymbolGlyphEntry>(StringComparer.OrdinalIgnoreCase);
        private static bool RuntimePlantGlyphsLoaded;

        private static void EnsureRuntimePlantGlyphs()
        {
            if (RuntimePlantGlyphsLoaded || !RuntimeDatabaseReady || Assets.Prefabs == null) return;
            var used = new HashSet<char>(ResolvedGlyphById.Values);
            // Register the prefab catalog once, in stable order, including non-harvestable plants such as Oxyfern.
            foreach (var prefab in Assets.Prefabs.Where(item => item != null && item.GetComponent<Uprootable>() != null)
                .OrderBy(item => item.PrefabTag.Name, StringComparer.Ordinal))
            {
                string id = prefab.PrefabTag.Name;
                if (ResolvedGlyphById.ContainsKey(id)) continue;
                var entry = new SymbolGlyphEntry { Id = id, Kind = "Plant",
                    Name = ToolUtil.CleanName(prefab.gameObject.GetProperName()), Glyph = '?' };
                char glyph = ChooseGlyph(entry, used);
                used.Add(glyph);
                ResolvedGlyphById[id] = UniqueCharMap[id] = glyph;
                RuntimePlantGlyphs[id] = entry;
            }
            RuntimePlantGlyphsLoaded = true;
        }

        private static void AddRuntimePlantSymbolRows(List<JObject> rows)
        {
            EnsureRuntimePlantGlyphs();
            foreach (var entry in RuntimePlantGlyphs.Values)
                rows.Add(GlyphRow(entry.Kind, entry.Id, entry.Name, ResolvedGlyphById[entry.Id].ToString(), entry.Name));
        }
    }
}
