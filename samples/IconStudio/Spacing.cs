using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace IconStudio;

/// <summary>
/// Applies Arc's scalar spacing tokens to uniform Thickness properties without taking a snapshot.
/// Dynamic resources keep updating these attached properties when the theme changes.
/// </summary>
public sealed class Spacing : AvaloniaObject
{
    /// <summary>
    /// Identifies a uniform margin in device-independent pixels.
    /// </summary>
    public static readonly AttachedProperty<double> MarginProperty =
        AvaloniaProperty.RegisterAttached<Spacing, Control, double>("Margin");

    /// <summary>
    /// Identifies uniform padding for Border and TemplatedControl hosts.
    /// </summary>
    public static readonly AttachedProperty<double> PaddingProperty =
        AvaloniaProperty.RegisterAttached<Spacing, Control, double>("Padding");

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
    /// Reads the scalar margin.
    /// </summary>
    public static double GetMargin(Control control) => control.GetValue(MarginProperty);

    /// <summary>
    /// Applies the scalar margin uniformly.
    /// </summary>
    public static void SetMargin(Control control, double value) => control.SetValue(MarginProperty, value);

    /// <summary>
    /// Reads the scalar padding.
    /// </summary>
    public static double GetPadding(Control control) => control.GetValue(PaddingProperty);

    /// <summary>
    /// Applies scalar padding to Border and TemplatedControl hosts.
    /// </summary>
    public static void SetPadding(Control control, double value) => control.SetValue(PaddingProperty, value);
}