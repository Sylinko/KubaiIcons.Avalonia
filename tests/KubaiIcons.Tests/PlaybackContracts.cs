using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using KubaiIcons.Avalonia;
using NUnit.Framework;

namespace KubaiIcons.Tests;

public partial class IconContracts
{
    [Test]
    public async Task Bookmark_WhenHoverInterruptsSaving_RetargetsTheVisibleRibbonAndStarTogether()
    {
        await _session.Dispatch(
            () =>
            {
                if (!SystemAllowsMotion())
                {
                    Assert.Ignore("The system has disabled animation.");
                }

                var icon = new KubaiIcon { Kind = KubaiIconKind.Bookmark };
                var window = new Window
                {
                    Content = icon,
                    Width = 100,
                    Height = 100
                };
                try
                {
                    window.Show();
                    Pump();
                    icon.IsActive = true;
                    Step(icon, 55);
                    var partial = Field<double>(icon, "_saved");
                    Assert.That(partial, Is.InRange(.1, .9));
                    icon.IsEngaged = true;
                    Step(icon, 80);
                    Assert.That(Field<double>(icon, "_saved"), Is.EqualTo(PoseValue(icon, "Saved")).Within(.001));
                    Assert.That(PoseValue(icon, "SavedAtRest"), Is.InRange(.001, partial + .01));
                    icon.IsEngaged = false;
                    Step(icon, 500);
                    Assert.That(PoseValue(icon, "SavedAtRest"), Is.EqualTo(1));
                    Assert.That(PoseValue(icon, "Hover"), Is.Zero);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Test, Order(1)]
    public async Task Once_WhenCompletedOrRetriggered_DoesNotWriteTheBindingAndReturnsToLatestTarget()
    {
        await _session.Dispatch(
            () =>
            {
                if (!SystemAllowsMotion())
                {
                    Assert.Ignore("The system has disabled animation.");
                }

                var request = new PlayRequest();
                var icon = new KubaiIcon
                {
                    Kind = KubaiIconKind.Bookmark,
                    Size = 96,
                    IsEngaged = false
                };
                using var binding = icon.Bind(
                    KubaiIconBase.IsPlayingProperty,
                    new ReflectionBinding(nameof(PlayRequest.IsRequested))
                    {
                        Source = request,
                        Mode = BindingMode.TwoWay,
                    });
                var window = new Window
                {
                    Content = icon,
                    Width = 150,
                    Height = 150
                };
                try
                {
                    window.Show();
                    Pump();
                    request.IsRequested = true;
                    Pump();
                    Assert.That(icon.IsAnimating, Is.True);
                    Step(icon, 500);
                    Assert.That(PhaseOf(icon), Is.EqualTo("Returning"));
                    icon.IsEngaged = true;
                    icon.IsActive = true;
                    Assert.That(PhaseOf(icon), Is.EqualTo("Returning"), "The latest target must not steal the playback channel.");
                    Step(icon, 500);
                    Assert.That(PhaseOf(icon), Is.EqualTo("Target"));
                    Step(icon, 500);
                    Assert.That(icon.IsAnimating, Is.False);
                    Assert.That(PoseValue(icon, "Hover"), Is.EqualTo(1));
                    Assert.That(Field<double>(icon, "_saved"), Is.EqualTo(1));
                    Assert.That(request.IsRequested && icon.IsPlaying, Is.True);
                    Assert.That(request.Writes, Is.EqualTo(1));

                    icon.AccentColor = Colors.Orange;
                    icon.SpeedRatio = 2;
                    Pump();
                    Assert.That(icon.IsAnimating, Is.False, "An already-completed true request must not restart.");
                    icon.Play();
                    Assert.That(icon.IsAnimating, Is.True);
                    Step(icon, 250);
                    Step(icon, 250);
                    Step(icon, 250);
                    Assert.That(icon.IsAnimating, Is.False);
                    Assert.That(request.Writes, Is.EqualTo(1));

                    icon.IsEngaged = false;
                    Step(icon, 250);
                    Assert.That(PoseValue(icon, "Hover"), Is.Zero);
                    request.IsRequested = false;
                    request.IsRequested = true;
                    Pump();
                    Step(icon, 60);
                    Assert.That(PoseValue(icon, "Hover"), Is.InRange(0.1, 0.99));
                    icon.Stop();
                    Step(icon, 250);
                    Assert.That(icon.IsAnimating, Is.False);
                    Assert.That(icon.IsActive, Is.True);
                    Assert.That(PoseValue(icon, "Hover"), Is.Zero);
                    Assert.That(request.IsRequested && icon.IsPlaying, Is.True);
                    Assert.That(request.Writes, Is.EqualTo(3));
                    window.Hide();
                    window.Show();
                    Pump();
                    Assert.That(icon.IsAnimating, Is.False, "An explicit Stop must survive reattachment.");
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Test, Order(2)]
    public async Task Repeat_WhenSpeedDelayAndVisibilityChange_KeepsOnePlaybackOwnerAndSleepsBetweenCycles()
    {
        await _session.Dispatch(
            () =>
            {
                if (!SystemAllowsMotion())
                {
                    Assert.Ignore("The system has disabled animation.");
                }

                var icon = new KubaiIcon
                {
                    Kind = KubaiIconKind.Folder,
                    IconStyle = KubaiIconStyle.Duotone,
                    IsEngaged = false,
                    LoopMode = LoopMode.Repeat,
                    SpeedRatio = 2,
                    RepeatDelay = TimeSpan.FromSeconds(30),
                    IsPlaying = true,
                };
                Assert.That(icon.IsAnimating, Is.False, "An unattached request must not subscribe to frames.");
                var host = new Border { Child = icon };
                var section = new Border { Child = host };
                var window = new Window
                {
                    Content = section,
                    Width = 150,
                    Height = 150
                };
                try
                {
                    window.Show();
                    Pump();
                    Assert.That(icon.IsAnimating, Is.True);
                    Step(icon, 100);
                    var elapsed = Field<double>(icon, "_elapsed");
                    icon.SpeedRatio = 0.5;
                    Assert.That(Field<double>(icon, "_elapsed"), Is.GreaterThanOrEqualTo(elapsed));
                    Assert.That(Field<double>(icon, "_elapsed"), Is.LessThan(elapsed + 20), "A speed change must not rewind elapsed motion.");
                    Step(icon, 400);
                    Assert.That(PhaseOf(icon), Is.EqualTo("Outbound"));
                    Step(icon, 500);
                    Assert.That(PhaseOf(icon), Is.EqualTo("Returning"));
                    Step(icon, 800);
                    Pump();
                    Assert.That(PhaseOf(icon), Is.EqualTo("Waiting"));
                    Assert.That(icon.IsPlaying, Is.True);
                    Assert.That(icon.IsAnimating, Is.False);
                    Assert.That(Field<DispatcherTimer>(icon, "_repeatTimer").IsEnabled, Is.True);
                    Assert.That(Field<bool>(icon, "_hasPendingFrame"), Is.False, "The delay uses one wakeup, not a frame loop.");

                    section.IsVisible = false;
                    host.IsVisible = false;
                    section.IsVisible = true;
                    Pump();
                    Assert.That(Field<DispatcherTimer>(icon, "_repeatTimer").IsEnabled, Is.False);
                    Assert.That(icon.IsAnimating, Is.False);
                    host.IsVisible = true;
                    Pump();
                    Assert.That(PhaseOf(icon), Is.EqualTo("Outbound"));
                    host.Child = null;
                    Assert.That(icon.IsAnimating, Is.False);
                    host.Child = icon; // Same TopLevel: old queued callbacks must be harmless.
                    Pump();
                    Assert.That(icon.IsAnimating, Is.True);

                    icon.IsAnimationEnabled = false;
                    icon.IsEngaged = true;
                    Pump();
                    Assert.That(icon.IsAnimating, Is.False);
                    Assert.That(PoseValue(icon, "Hover"), Is.EqualTo(1));
                    Assert.That(Field<bool>(icon, "_hasPendingFrame"), Is.False);
                    Assert.That(Field<DispatcherTimer>(icon, "_repeatTimer").IsEnabled, Is.False);
                    icon.IsAnimationEnabled = true;
                    icon.SpeedRatio = 4;
                    Step(icon, 160);
                    Step(icon, 100);
                    Pump();
                    Assert.That(PhaseOf(icon), Is.EqualTo("Waiting"));
                    icon.RepeatDelay = TimeSpan.Zero;
                    Assert.That(icon.IsAnimating, Is.True);
                    Step(icon, 160);
                    Step(icon, 100);
                    Assert.That(PhaseOf(icon), Is.EqualTo("Outbound"));
                    icon.IsPlaying = false;
                    Step(icon, 160);
                    Assert.That(icon.IsAnimating, Is.False);
                    Assert.That(PoseValue(icon, "Hover"), Is.EqualTo(1), "Stopping returns to the target changed while the loop owned motion.");

                    icon.IsEngaged = false;
                    Step(icon, 100);
                    icon.RepeatDelay = TimeSpan.FromSeconds(30);
                    icon.Play(); // A method-started loop does not change IsPlaying.
                    Assert.That(icon.IsPlaying, Is.False);
                    Step(icon, 160);
                    Step(icon, 100);
                    icon.LoopMode = LoopMode.Once;
                    Pump();
                    Assert.That(icon.IsAnimating, Is.False);
                    Assert.That(Field<DispatcherTimer>(icon, "_repeatTimer").IsEnabled, Is.False);
                    icon.LoopMode = LoopMode.Repeat;
                    Assert.That(icon.IsAnimating, Is.False, "Configuration alone does not issue a play request.");

                    section.IsVisible = false;
                    icon.IsEngaged = true;
                    Assert.That(PoseValue(icon, "Hover"), Is.EqualTo(1));
                    section.IsVisible = true;
                    icon.IsEngaged = false;
                    Assert.That(icon.IsAnimating, Is.True, "The first new pose after revealing an idle icon still animates.");
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Test, Order(3)]
    public async Task Icon_WhenInputHasNoStyle_StaysAtRestUntilTheCallerOptsIn()
    {
        await _session.Dispatch(
            () =>
            {
                var icon = new KubaiIcon
                {
                    Kind = KubaiIconKind.Folder,
                    Size = 80,
                    Focusable = true
                };
                var window = new Window
                {
                    Content = icon,
                    Width = 120,
                    Height = 120
                };
                try
                {
                    window.Show();
                    Pump();
                    window.MouseMove(new Point(60, 60));
                    Pump();
                    Assert.That(icon.IsPointerOver, Is.True);
                    Assert.That(icon.Focus(NavigationMethod.Tab), Is.True);
                    Assert.That(icon.Classes, Does.Contain(":focus-visible"));
                    Assert.That(icon.IsEngaged, Is.False);
                    Assert.That(icon.IsAnimating, Is.False);

                    var wasTapped = false;
                    icon.Tapped += (_, _) => wasTapped = true;
                    var point = new Point(60, 60);
                    using (var touch = window.TouchBegin(point))
                    {
                        window.TouchEnd(touch, point);
                    }
                    Pump();
                    Assert.That(wasTapped, Is.True);
                    Assert.That(icon.IsAnimating, Is.False);
                    Assert.That(PoseValue(icon, "Hover"), Is.Zero);

                    icon.IsAnimationEnabled = false;
                    window.Styles.Add(new Style(x => x.OfType<KubaiIcon>().Class(":pointerover"))
                    {
                        Setters = { new Setter(KubaiIconBase.IsEngagedProperty, true) }
                    });
                    window.MouseMove(point);
                    Pump();
                    Assert.That(icon.IsEngaged, Is.True);
                    Assert.That(PoseValue(icon, "Hover"), Is.EqualTo(1));
                    icon.IsEngaged = false;
                    Assert.That(PoseValue(icon, "Hover"), Is.Zero, "A caller's local value wins over hover without a control mode.");
                    icon.ClearValue(KubaiIconBase.IsEngagedProperty);
                    Assert.That(PoseValue(icon, "Hover"), Is.EqualTo(1));

                    icon.IsPlaying = true;
                    Assert.That(icon.IsAnimating, Is.False);
                    Assert.That(Field<bool>(icon, "_hasPendingFrame"), Is.False);
                    Assert.Throws<ArgumentException>(() => icon.SpeedRatio = 0);
                    Assert.Throws<ArgumentException>(() => icon.SpeedRatio = double.NaN);
                    Assert.Throws<ArgumentException>(() => icon.RepeatDelay = TimeSpan.FromSeconds(-1));
                    Assert.Throws<ArgumentException>(() => icon.LoopMode = (LoopMode)99);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Test, Order(4)]
    public async Task Repeat_WhenDelayExpires_WakesOnlyTheRequestingInstance()
    {
        await _session.Dispatch(
            async () =>
            {
                if (!SystemAllowsMotion())
                {
                    Assert.Ignore("The system has disabled animation.");
                }

                var moving = new KubaiIcon
                {
                    Kind = KubaiIconKind.Folder,
                    IconStyle = KubaiIconStyle.Duotone,
                    IsEngaged = false,
                    LoopMode = LoopMode.Repeat,
                    SpeedRatio = 4,
                    RepeatDelay = TimeSpan.FromMilliseconds(80),
                    IsPlaying = true,
                };
                var still = new KubaiIcon
                {
                    Kind = KubaiIconKind.Folder,
                    IsEngaged = false
                };
                var window = new Window
                {
                    Content = new StackPanel { Children = { moving, still } },
                    Width = 150,
                    Height = 150
                };
                try
                {
                    window.Show();
                    Pump();
                    Step(moving, 160);
                    Step(moving, 100);
                    Pump();
                    Assert.That(PhaseOf(moving), Is.EqualTo("Waiting"));
                    Assert.That(moving.IsAnimating, Is.False);
                    var deadline = Stopwatch.StartNew();
                    while (!moving.IsAnimating && deadline.Elapsed < TimeSpan.FromSeconds(3))
                    {
                        await Task.Delay(10);
                        Pump();
                    }

                    Assert.That(moving.IsAnimating, Is.True, "The dispatcher timer must start the next cycle.");
                    Assert.That(PhaseOf(moving), Is.EqualTo("Outbound"));
                    Assert.That(still.IsAnimating || still.IsPlaying, Is.False, "Shared art must not share playback.");
                    Assert.That(PoseValue(still, "Hover"), Is.Zero);
                    Assert.That(Field<bool>(still, "_hasPendingFrame"), Is.False);
                    moving.Stop();
                    return true;
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

    private static string? PhaseOf(KubaiIcon icon) => Field<object>(icon, "_phase").ToString();

    private static double PoseValue(KubaiIcon icon, string name)
    {
        var pose = Field<object>(icon, "_pose");
        return pose.GetType().GetProperty(name)?.GetValue(pose) is double value ? value : throw new AssertionException(name);
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