using System;
using System.Collections.Generic;
using System.Linq;

namespace SkyrimOutfit
{
    internal enum ConflictArea { Outfit, Name, Destination, Parts }

    internal sealed class PackageConflict : InvalidOperationException
    {
        internal readonly Recipe[] Outfits;
        internal readonly HashSet<string> PieceIds;
        internal readonly ConflictArea Area;

        internal PackageConflict(string message, ConflictArea area, IEnumerable<Recipe> outfits,
            IEnumerable<string> pieces = null, Exception inner = null) : base(message, inner)
        {
            Outfits = outfits.Where(outfit => outfit != null).Distinct().ToArray();
            PieceIds = new HashSet<string>(pieces ?? Enumerable.Empty<string>(), Files.Comparer);
            Area = area;
        }

        internal static PackageConflict At(Exception error, Recipe outfit, ConflictArea area = ConflictArea.Outfit,
            IEnumerable<string> pieces = null)
        {
            return error as PackageConflict ?? new PackageConflict(error.Message, area, new[] { outfit }, pieces, error);
        }
    }
}
