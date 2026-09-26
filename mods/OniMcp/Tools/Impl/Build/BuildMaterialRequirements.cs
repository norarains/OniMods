namespace OniMcp.Tools
{
    internal static class BuildMaterialRequirements
    {
        internal static float SingleMaterialKg(float[] masses, int materialCategories)
        {
            // The current selection report has one stock quantity. A multi-ingredient
            // recipe cannot be compared against that one stock, even if selection
            // happened to return one tag. Leave its requirement explicitly unknown.
            if (materialCategories != 1 || masses == null || masses.Length != 1
                || float.IsNaN(masses[0]) || float.IsInfinity(masses[0]) || masses[0] <= 0)
                return 0f;
            return masses[0];
        }
    }
}
