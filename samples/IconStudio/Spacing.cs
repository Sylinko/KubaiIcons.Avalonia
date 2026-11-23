using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace IconStudio;

/// <summary>
/// Applies Arc's scalar spacing tokens to uniform Thickness properties without taking a snapshot.
/// Dynamic resources keep updating these attached properties when the theme changes.
/// </summary>
public sealed partial class Spacing : AvaloniaObject
{
    static Spacing()
    {
        MarginProperty.Changed.AddClassHandler<Control>((control, change) =>
            control.Margin = new Thickness(change.GetNewValue<double>()));
        PaddingProperty.Changed.AddClassHandler<Control>((control, change) =>
        {
            var padding = new Thickness(change.GetNewValue<double>());
            if (control is Border border)
            {
                border.Padding = padding;
            }
            else if (control is TemplatedControl templated)
            {
                templated.Padding = padding;
            }
        });
    }

    private Spacing()
    {
    }

    /// <summary>
    /// Gets the uniform margin in device-independent pixels.
    /// </summary>
    [GeneratedAttachedProperty]
    public static partial double GetMargin(Control control);

    /// <summary>
    /// Gets the uniform padding for Border and TemplatedControl hosts.
    /// </summary>
    [GeneratedAttachedProperty]
    public static partial double GetPadding(Control control);
}