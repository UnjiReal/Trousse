using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SkyrimOutfit
{
    internal sealed partial class App
    {
        private PackageConflict packageConflict;
        private bool conflictOnDraft;
        private static readonly Brush ConflictText = new SolidColorBrush(Color.FromRgb(255, 158, 154));

        private PackageConflict CurrentPackageConflict()
        {
            if (packageConflict == null) return null;
            if (conflictOnDraft) return packageConflict;
            return editing >= 0 && editing < pack.Outfits.Count && packageConflict.Outfits.Contains(pack.Outfits[editing])
                ? packageConflict : null;
        }

        private void ShowPackageConflict(PackageConflict error)
        {
            ClearPackageConflict();
            packageConflict = error;
            foreach (var outfit in error.Outfits) outfit.BuildError = error.Message;
            Recipe first = error.Outfits.FirstOrDefault();
            int index = first == null ? -1 : pack.Outfits.IndexOf(first);
            if (index >= 0 && index != editing && !draftDirty) LoadDraft(index);
            else conflictOnDraft = index < 0;
            Switch("HomePage");
            int piece = draft.FindIndex(row => error.PieceIds.Contains(row.Id));
            RefreshDraft(piece >= 0 ? piece : draftList.SelectedIndex);
            if (piece >= 0) draftList.ScrollIntoView(draft[piece]);
            RefreshPack(index >= 0 ? index : editing);
        }

        private void ClearPackageConflict()
        {
            if (packageConflict == null) return;
            foreach (var outfit in packageConflict.Outfits) outfit.BuildError = null;
            packageConflict = null;
            conflictOnDraft = false;
            RefreshDraft(draftList.SelectedIndex);
            packList.Items.Refresh();
        }

        private void RefreshPackageConflictHint(PackageConflict error)
        {
            var banner = Find<Border>("ComposeConflict");
            var message = Find<TextBlock>("ComposeConflictMessage");
            banner.Visibility = error == null ? Visibility.Collapsed : Visibility.Visible;
            banner.ToolTip = error == null ? null : error.Message;
            message.Text = error == null ? "" : error.Message.Split('\n')[0].TrimEnd('\r');
            MarkConflictField(Find<TextBlock>("OutfitNameLabel"), error != null && error.Area == ConflictArea.Name);
            MarkConflictField(Find<TextBlock>("MeshLabel"), error != null && error.Area == ConflictArea.Destination);
            MarkConflictField(meshPath, error != null && error.Area == ConflictArea.Destination);
        }

        private static void MarkConflictField(TextBlock field, bool marked)
        {
            if (marked) field.Foreground = ConflictText;
            else field.ClearValue(TextBlock.ForegroundProperty);
        }
    }
}
