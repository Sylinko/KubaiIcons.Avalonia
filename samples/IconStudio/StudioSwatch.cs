using System.ComponentModel;
using Avalonia.Media;
using IconStudio.Localization;

namespace IconStudio;

/// <summary>
/// An artwork palette choice, independent of the interface's Arc accent and appearance.
/// </summary>
public sealed class StudioSwatch : INotifyPropertyChanged
{
    public string Name => StudioLangResources.Get(_label);
    public string Hex { get; }
    public Color Color { get; }
    public IBrush Brush { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly StudioLangResourceKind _label;

    public StudioSwatch(StudioLangResourceKind label, string hex)
    {
        _label = label;
        Hex = hex;
        Color = Color.Parse(hex);
        Brush = new SolidColorBrush(Color);
    }

    internal void RefreshLanguage() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
}