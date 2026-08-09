using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

/// <summary>
/// Reusable icon presentation that creates an independent visual for the size requested by a
/// menu, toolbar or other command presenter.
/// </summary>
/// <remarks>
/// The size is expressed in DIPs. Each invocation must return a new, parentless element; visual
/// instances cannot be shared by simultaneous presenters. Non-visual resources such as image
/// sources and frozen geometries may be shared by the returned elements.
/// </remarks>
public sealed class IconTemplate
{
    private readonly Func<double, FrameworkElement> _build;

    public IconTemplate(Func<double, FrameworkElement> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        _build = build;
    }

    /// <summary>
    /// Builds one icon visual for the requested square presentation size in DIPs.
    /// </summary>
    public FrameworkElement Build(double size)
    {
        if (!double.IsFinite(size) || size <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "Icon size must be a finite positive value.");
        }

        var element = _build(size)
            ?? throw new InvalidOperationException("The icon template returned null.");
        if (element.Parent != null)
        {
            throw new InvalidOperationException("The icon template must return a new parentless element.");
        }

        return element;
    }
}
