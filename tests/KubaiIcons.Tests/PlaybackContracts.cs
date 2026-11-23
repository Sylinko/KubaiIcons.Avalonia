using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Threading;
using KubaiIcons.Avalonia;
using NUnit.Framework;

namespace KubaiIcons.Tests;

public partial class IconContracts
{
    [Test]
    public Task Once_WhenCompletedOrStopped_DoesNotWriteBackToThePlayBinding()
    {
        return _session.Dispatch(
            () =>
            {
                if (!SystemAllowsMotion())
                {
                    Assert.Ignore("The system has disabled animation.");
                }

                var request = new PlayRequest();
                var icon = new KubaiIcon { Kind = KubaiIconKind.Bookmark };
                using var binding = icon.Bind(
                    KubaiIconBase.IsPlayingProperty,
                    new ReflectionBinding(nameof(PlayRequest.IsRequested)) { Source = request, Mode = BindingMode.TwoWay });
                var window = new Window { Content = icon };
                try
                {
                    window.Show();
                    request.IsRequested = true;
                    Assert.That(icon.IsAnimating, Is.True);
                    Step(icon, 500);
                    Step(icon, 500);
                    Assert.That(icon.IsAnimating, Is.False);
                    Assert.That(icon.IsPlaying && request.IsRequested, Is.True);

                    icon.Play();
                    Assert.That(icon.IsAnimating, Is.True);
                    icon.Stop();
                    Step(icon, 500);
                    Assert.That(icon.IsAnimating, Is.False);
                    Assert.That(request.Writes, Is.EqualTo(1));
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Test]
    public Task Repeat_WhenTheIconIsHiddenOrDetached_PausesWithoutClearingTheRequest()
    {
        return _session.Dispatch(
            () =>
            {
                if (!SystemAllowsMotion())
                {
                    Assert.Ignore("The system has disabled animation.");
                }

                var icon = new KubaiIcon { Kind = KubaiIconKind.Folder, LoopMode = LoopMode.Repeat, IsPlaying = true };
                var host = new Border { Child = icon };
                var window = new Window { Content = host };
                try
                {
                    window.Show();
                    Assert.That(icon.IsAnimating, Is.True);
                    host.IsVisible = false;
                    Assert.That(icon.IsAnimating, Is.False);
                    host.IsVisible = true;
                    Assert.That(icon.IsAnimating, Is.True);
                    host.Child = null;
                    Assert.That(icon.IsAnimating, Is.False);
                    Assert.That(icon.IsPlaying, Is.True);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        Dispatcher.UIThread.RunJobs();
    }

    // Advance the existing wall clock to make timing contracts deterministic. No
    // test-only clock or extra production entry point is needed for the tiny player.
    private static void Step(KubaiIcon icon, double milliseconds)
    {
        var time = typeof(KubaiIconBase).GetField("_lastTick", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertionException("Missing playback clock.");
        time.SetValue(icon, Stopwatch.GetTimestamp() - (long)(milliseconds * Stopwatch.Frequency / 1000));
        var tick = typeof(KubaiIconBase).GetMethod("AdvanceFrame", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertionException("Missing frame callback.");
        tick.Invoke(icon, null);
    }

    private static T Field<T>(KubaiIcon icon, string name)
    {
        var field = typeof(KubaiIconBase).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        return field?.GetValue(icon) is T value ? value : throw new AssertionException($"Missing {name}.");
    }

    private sealed class PlayRequest : INotifyPropertyChanged
    {
        public bool IsRequested
        {
            get => _isRequested;
            set
            {
                _isRequested = value;
                Writes++;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRequested)));
            }
        }

        public int Writes { get; private set; }

        public event PropertyChangedEventHandler? PropertyChanged;

        private bool _isRequested;
    }
}