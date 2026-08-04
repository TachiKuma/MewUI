using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.MewvalonEdit.Rendering;

/// <summary>
/// Stroke described by a colour, a thickness and a stroke style. Takes the place of the pens the
/// original editor exposes, in the colour terms the rest of this extension uses.
/// </summary>
public readonly record struct ColorPen(Color Color, double Thickness, StrokeStyle StrokeStyle)
{
    public ColorPen(Color color, double thickness = 1) : this(color, thickness, StrokeStyle.Default)
    {
    }

    /// <summary>Renderer-side stroke for this pen.</summary>
    internal Pen ToPen() => new(Color, Thickness, StrokeStyle);
}
