using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using KubaiIcons.Avalonia;
using IconStudio.Localization;
using TKCode.Arc.Localization;

namespace IconStudio;

/// <summary>
/// One lazy inspector, reused across selections and removed when closed.
/// </summary>
public partial class IconInspector : UserControl
{
    public event EventHandler? CloseRequested;

    private StudioEntry? _entry;
    private double _gallerySize = 32;
    private bool _isAttached;
    private static readonly string[] Rates = ["slow", "", "fast", "faster"];
    private static readonly string[] Delays = ["", "gap-short", "gap-long"];

    public IconInspector()
    {
        InitializeComponent();
        Loops.SelectionChanged += (_, _) => Apply();
        Speeds.SelectionChanged += (_, _) => Apply();
        Gaps.SelectionChanged += (_, _) => Apply();
        Animate.IsCheckedChanged += (_, _) => Apply();
        Hold.IsCheckedChanged += (_, _) => Apply();
        Active.IsCheckedChanged += (_, _) => Apply();
        Playing.IsCheckedChanged += (_, _) => Apply();
        CodeLanguage.SelectionChanged += (_, _) => UpdateCode();
        CloseDetail.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        PlayAgain.Click += (_, _) => Specimen.Play();
        StopPlayback.Click += (_, _) => Specimen.Stop();
        Stage.Tapped += (_, e) =>
        {
            if (e.Pointer.Type == PointerType.Touch)
            {
                Specimen.Play();
            }
        };
    }

    public void ShowIcon(StudioEntry entry, string category)
    {
        if (_isAttached && _entry is not null)
        {
            _entry.PropertyChanged -= EntryChanged;
        }

        _entry = entry;
        if (_isAttached)
        {
            entry.PropertyChanged += EntryChanged;
        }

        EnumCopy.Value = entry.Kind.ToString();
        Specimen.Kind = entry.Kind;
        Specimen.IconStyle = entry.Style;
        Active.IsEnabled = entry.Info.SupportsActiveState(entry.Style);
        RefreshLanguage(category);
        Apply();
    }

    internal void RefreshLanguage(string category)
    {
        if (_entry is null)
        {
            return;
        }

        IconName.Text = _entry.DisplayName;
        CategoryBadge.Content = category;
        EnumCopy.Label = string.Format(ArcLanguage.Culture, StudioLangResources.Get(StudioLangResourceKind.CopyEnumName), _entry.Kind);
    }

    internal void SetGallerySize(double size)
    {
        _gallerySize = size;
        UpdateCode();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        if (_entry is not null)
        {
            _entry.PropertyChanged += EntryChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_entry is not null)
        {
            _entry.PropertyChanged -= EntryChanged;
        }

        _isAttached = false;
        base.OnDetachedFromVisualTree(e);
    }

    private void EntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StudioEntry.Color) or nameof(StudioEntry.ColorVariant))
        {
            Apply();
        }
    }

    private void Apply()
    {
        if (_entry is null)
        {
            return;
        }

        var rate = Rates[Math.Max(0, Speeds.SelectedIndex)];
        var delay = Delays[Math.Max(0, Gaps.SelectedIndex)];
        var isActive = Active.IsEnabled && Active.IsChecked == true;
        var isSoft = _entry.ColorVariant == KubaiColorVariant.Soft;
        Specimen.AccentColor = _entry.Color;
        Specimen.Classes.Set("still", Animate.IsChecked != true);
        Specimen.Classes.Set("held", Hold.IsChecked == true);
        Specimen.Classes.Set("active", isActive);
        Specimen.Classes.Set("soft", isSoft);
        Specimen.Classes.Set("repeat", Loops.SelectedIndex == 1);
        Specimen.Classes.Set("play-request", Playing.IsChecked == true);
        foreach (var item in Rates)
        {
            if (item.Length > 0)
            {
                Specimen.Classes.Set(item, rate == item);
            }
        }

        foreach (var item in Delays)
        {
            if (item.Length > 0)
            {
                Specimen.Classes.Set(item, delay == item);
            }
        }

        UpdateCode();
    }

    private void UpdateCode()
    {
        if (_entry is null)
        {
            return;
        }

        var isCSharp = CodeLanguage.SelectedIndex == 1;
        Snippet.Language = isCSharp ? "cs" : "axaml";
        Snippet.FileName = isCSharp ? "Icon.cs" : "Icon.axaml";
        Snippet.Code = CodeSnippet.Create(Specimen, isCSharp, Hold.IsChecked == true, _gallerySize);
    }
}