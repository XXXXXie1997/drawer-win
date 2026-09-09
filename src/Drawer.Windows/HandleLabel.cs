using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Drawer.Windows;

public sealed class HandleLabel : TextBlock
{
    public HandleLabel()
    {
        Foreground = Brushes.White; FontSize = 12; LineHeight = 14;
        TextAlignment = TextAlignment.Center; TextTrimming = TextTrimming.CharacterEllipsis;
        HorizontalAlignment = HorizontalAlignment.Center; VerticalAlignment = VerticalAlignment.Center;
        IsHitTestVisible = false;
    }
    public void Update(string label, DockEdge edge)
    {
        // Stack complete text elements on side handles, preserving upright glyphs and joined emoji.
        bool side = edge != DockEdge.Top;
        MaxWidth = side ? 22 : 112;
        MaxHeight = side ? 112 : 18;
        var elements = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(label);
        while (enumerator.MoveNext()) elements.Add(enumerator.GetTextElement());
        var visible = elements.Count <= DrawerWorkspace.MaximumLabelCharacters ? elements : elements.Take(DrawerWorkspace.MaximumLabelCharacters - 1).Append("…");
        Text = string.Join(side ? "\n" : "", visible);
    }
}
