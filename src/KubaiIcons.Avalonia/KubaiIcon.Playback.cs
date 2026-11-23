using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using KubaiIcons.Avalonia.Rendering;

namespace KubaiIcons.Avalonia;

public abstract partial class KubaiIconBase
{
    /// <summary>
    /// Identifies the IsPlaying property.
    /// </summary>
    public static readonly StyledProperty<bool> IsPlayingProperty = AvaloniaProperty.Register<KubaiIconBase, bool>(nameof(IsPlaying));

    /// <summary>
    /// Identifies the LoopMode property.
    /// </summary>
    public static readonly StyledProperty<LoopMode> LoopModeProperty =
        AvaloniaProperty.Register<KubaiIconBase, LoopMode>(nameof(LoopMode), validate: v => v is LoopMode.Once or LoopMode.Repeat);

    /// <summary>
    /// Identifies the SpeedRatio property.
    /// </summary>
    public static readonly StyledProperty<double> SpeedRatioProperty =
        AvaloniaProperty.Register<KubaiIconBase, double>(nameof(SpeedRatio), 1, validate: v => double.IsFinite(v) && v > 0);

    /// <summary>
    /// Identifies the RepeatDelay property.
    /// </summary>
    public static readonly StyledProperty<TimeSpan> RepeatDelayProperty = AvaloniaProperty.Register<KubaiIconBase, TimeSpan>(
        nameof(RepeatDelay),
        TimeSpan.Zero,
        validate: v => v >= TimeSpan.Zero && v.TotalMilliseconds <= int.MaxValue);

    /// <summary>
    /// Identifies the read-only IsAnimating property.
    /// </summary>
    public static readonly DirectProperty<KubaiIconBase, bool> IsAnimatingProperty =
        AvaloniaProperty.RegisterDirect<KubaiIconBase, bool>(nameof(IsAnimating), x => x.IsAnimating);

    /// <summary>
    /// A false-to-true edge requests playback; false stops it. Completion never writes
    /// this property or its binding. Call Play() to retrigger an already-true request.
    /// </summary>
    public bool IsPlaying
    {
        get => GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    /// <summary>
    /// Once plays a complete action. Repeat continues until stopped; changing Repeat to
    /// Once finishes the current cycle. Changing this property does not start playback.
    /// </summary>
    public LoopMode LoopMode
    {
        get => GetValue(LoopModeProperty);
        set => SetValue(LoopModeProperty, value);
    }

    /// <summary>
    /// Positive finite multiplier for motion and active-state transitions. Changes take
    /// effect from the current frame; the inter-cycle delay remains wall-clock time.
    /// </summary>
    public double SpeedRatio
    {
        get => GetValue(SpeedRatioProperty);
        set => SetValue(SpeedRatioProperty, value);
    }

    /// <summary>
    /// Wall-clock rest time between completed cycles. Defaults to zero.
    /// </summary>
    public TimeSpan RepeatDelay
    {
        get => GetValue(RepeatDelayProperty);
        set => SetValue(RepeatDelayProperty, value);
    }

    /// <summary>
    /// Whether a pose or active-state transition is advancing. False at rest, while
    /// waiting between cycles, or while motion is suppressed. Does not mean IsPlaying.
    /// </summary>
    public new bool IsAnimating => _isAnimating;

    private TopLevel? _root;
    private IDisposable? _visibilitySubscription;
    private bool _wasEffectivelyVisible;
    private Action<TimeSpan>? _tick;
    private DispatcherTimer? _repeatTimer;
    private Phase _phase;
    private Pose _from;
    private Pose _pose;
    private Pose _target;
    private double _elapsed;
    private double _duration;
    private double _saved;
    private double _savedFrom;
    private double _savedTarget;
    private double _savedElapsed;
    private double _appliedSpeedRatio = 1;
    private long _lastTick;
    private long _waitStarted;
    private bool _hasPlaybackRequest;
    private bool _hasPendingFrame;
    private bool _isAvailable;
    private bool _isAnimating;

    /// <summary>
    /// Retriggers playback with the current LoopMode from the pose on screen.
    /// Does not change IsPlaying or a binding. Call Stop() to cancel a method-started loop.
    /// </summary>
    public void Play()
    {
        VerifyAccess();
        SampleNow();
        _hasPlaybackRequest = true;
        RefreshAvailability();
        if (_isAvailable)
        {
            BeginSegment(Phase.Outbound, true);
        }

        Schedule();
    }

    /// <summary>
    /// Cancels playback and settles at the latest IsEngaged target, preserving IsActive.
    /// Does not change IsPlaying; another true edge or Play() can start it again.
    /// </summary>
    public void Stop()
    {
        VerifyAccess();
        SampleNow();
        _hasPlaybackRequest = false;
        _repeatTimer?.Stop();
        _phase = Phase.Idle;
        RefreshAvailability();
        if (_isAvailable)
        {
            MoveToTarget();
        }

        Schedule();
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _root = TopLevel.GetTopLevel(this);
        _appliedSpeedRatio = SpeedRatio;
        SnapToTarget();
        RefreshAvailability();
        Schedule();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ObserveVisibility(false);
        _root = null;
        _tick = null;
        _hasPendingFrame = false;
        _isAvailable = false;
        SnapToTarget();
        base.OnDetachedFromVisualTree(e);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsPlayingProperty)
        {
            if (IsPlaying)
            {
                Play();
            }
            else
            {
                Stop();
            }

            return;
        }

        if (IsSelectionProperty(change.Property))
        {
            _duotone = null;
            SnapToTarget();
            _isAvailable = false;
            RefreshAvailability();
        }
        else if (change.Property == IsAnimationEnabledProperty || change.Property == IsVisibleProperty ||
                 change.Property == IsEffectivelyEnabledProperty || change.Property == SizeProperty)
        {
            RefreshAvailability();
        }
        else if (change.Property == IsEngagedProperty || change.Property == IsActiveProperty ||
                 change.Property == SpeedRatioProperty || change.Property == LoopModeProperty || change.Property == RepeatDelayProperty)
        {
            SampleNow();
            _appliedSpeedRatio = SpeedRatio;
            RefreshAvailability();
            if (_isAvailable)
            {
                if (change.Property == IsActiveProperty)
                {
                    SetActiveTarget();
                }

                if (change.Property == IsEngagedProperty && _phase is Phase.Idle or Phase.Target)
                {
                    MoveToTarget();
                }

                if (change.Property == LoopModeProperty && _phase == Phase.Waiting && LoopMode == LoopMode.Once)
                {
                    CompletePlayback();
                }

                if (change.Property == RepeatDelayProperty && _phase == Phase.Waiting)
                {
                    ArmRepeat();
                }
            }
        }
        else
        {
            return;
        }

        Schedule();
    }

    // Avalonia 12 exposes effective visibility but not its change event. Only a player
    // with work to suspend or resume listens for visibility changes; idle artwork does not.
    private void ObserveVisibility(bool shouldObserve)
    {
        if (!shouldObserve)
        {
            _visibilitySubscription?.Dispose();
            _visibilitySubscription = null;
        }
        else if (_visibilitySubscription is null)
        {
            _wasEffectivelyVisible = IsEffectivelyVisible;
            _visibilitySubscription = IsVisibleProperty.Changed.AddClassHandler<Visual>(OnVisibilityChanged);
        }
    }

    private void OnVisibilityChanged(Visual sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender.Dispatcher != Dispatcher || _wasEffectivelyVisible == IsEffectivelyVisible)
        {
            return;
        }

        _wasEffectivelyVisible = IsEffectivelyVisible;
        RefreshAvailability();
        Schedule();
    }

    private bool CanAnimate()
    {
        return _root is not null && Size > 0 && IsEffectivelyVisible && IsEffectivelyEnabled && IsAnimationEnabled &&
            KubaiIconCatalog.Get(Identity)?.SupportsStyle(Family) == true && AllowsSystemAnimation();
    }

    private void RefreshAvailability()
    {
        var isAvailable = CanAnimate();
        if (!isAvailable)
        {
            _isAvailable = false;
            SnapToTarget();
            return;
        }

        if (_isAvailable)
        {
            return;
        }

        _isAvailable = true;
        if (_hasPlaybackRequest)
        {
            BeginSegment(Phase.Outbound, true);
        }
    }

    private void SnapToTarget()
    {
        _repeatTimer?.Stop();
        _phase = Phase.Idle;
        _from = _pose = _target = Pose.Target(IsEngaged, HasActivePose);
        _duotone?.Snap(IsEngaged);
        _saved = _savedTarget = IsActive && KubaiIconCatalog.Get(Identity)?.SupportsActiveState(Family) == true ? 1 : 0;
        _lastTick = Stopwatch.GetTimestamp();
        SetAndRaise(IsAnimatingProperty, ref _isAnimating, false);
        InvalidateVisual();
    }

    private void BeginSegment(Phase phase, bool isEngaged)
    {
        _repeatTimer?.Stop();
        _phase = phase;
        _from = _pose with
        {
            Saved = _saved
        };
        _target = Pose.Target(isEngaged, HasActivePose);
        if (phase == Phase.Target)
        {
            // An engagement or state change retargets every visible component together.
            _savedFrom = _saved;
            _savedTarget = _target.Saved;
            _savedElapsed = 0;
        }

        _elapsed = 0;
        _duration = (Family, isEngaged) switch
        {
            (KubaiIconStyle.Duotone, true) => CatalogData.GetDuotoneDurationMs(Identity),
            (KubaiIconStyle.Duotone, false) => 360,
            _ => 460
        };
        _duotone?.Begin(isEngaged);
        _lastTick = Stopwatch.GetTimestamp();
    }

    private void MoveToTarget()
    {
        var target = Pose.Target(IsEngaged, HasActivePose);
        if (_target == target && _phase == Phase.Target)
        {
            return;
        }

        if (_pose == target)
        {
            _target = target;
            _phase = Phase.Idle;
            return;
        }

        BeginSegment(Phase.Target, IsEngaged);
    }

    private bool HasActivePose => IsActive && KubaiIconCatalog.Get(Identity)?.SupportsActiveState(Family) == true;

    private void SetActiveTarget()
    {
        if (_phase is Phase.Target or Phase.Idle && KubaiIconCatalog.Get(Identity)?.SupportsActiveState(Family) == true)
        {
            BeginSegment(Phase.Target, IsEngaged);
        }

        _savedFrom = _saved;
        _savedTarget = IsActive && KubaiIconCatalog.Get(Identity)?.SupportsActiveState(Family) == true ? 1 : 0;
        _savedElapsed = 0;
        _lastTick = Stopwatch.GetTimestamp();
    }

    // Both channels share one UI-thread clock. Playback owns the motion channel;
    // saved-state changes remain live without restarting or rewinding that motion.
    private void SampleNow()
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = _lastTick == 0 ? 0 : Stopwatch.GetElapsedTime(_lastTick, now).TotalMilliseconds * _appliedSpeedRatio;
        _lastTick = now;
        if (!_isAvailable)
        {
            return;
        }

        // Endpoints are assigned exactly, so equality also marks completion without a tolerance.
        if (!_saved.Equals(_savedTarget))
        {
            _savedElapsed += elapsed;
            var k = Math.Min(1, _savedElapsed / 460);
            _saved = k >= 1 ? _savedTarget : Shape.Mix(_savedFrom, _savedTarget, Pose.Ease(k));
        }

        if (_phase is not (Phase.Target or Phase.Outbound or Phase.Returning))
        {
            return;
        }

        _elapsed += elapsed;
        var t = Math.Min(1, _elapsed / _duration);
        _pose = Pose.Between(_from, _target, t);
        _duotone?.Sample(t);
        if (t < 1)
        {
            return;
        }

        switch (_phase)
        {
            case Phase.Outbound:
                BeginSegment(Phase.Returning, false);
                break;
            case Phase.Returning:
                if (_hasPlaybackRequest && LoopMode == LoopMode.Repeat)
                {
                    _phase = Phase.Waiting;
                    _waitStarted = Stopwatch.GetTimestamp();
                    ArmRepeat();
                }
                else
                {
                    CompletePlayback();
                }

                break;
            case Phase.Target:
                _phase = Phase.Idle;
                break;
        }
    }

    private void CompletePlayback()
    {
        _hasPlaybackRequest = false;
        _repeatTimer?.Stop();
        _phase = Phase.Idle;
        MoveToTarget();
    }

    private void ArmRepeat()
    {
        _repeatTimer?.Stop();
        var remaining = RepeatDelay - Stopwatch.GetElapsedTime(_waitStarted);
        if (remaining <= TimeSpan.Zero)
        {
            BeginSegment(Phase.Outbound, true);
            return;
        }

        if (_repeatTimer is null)
        {
            _repeatTimer = new DispatcherTimer(DispatcherPriority.Render);
            _repeatTimer.Tick += RepeatReady;
        }

        _repeatTimer.Interval = remaining;
        _repeatTimer.Start();
    }

    private void RepeatReady(object? sender, EventArgs e)
    {
        _repeatTimer?.Stop();
        SampleNow();
        RefreshAvailability();
        if (_isAvailable && _phase == Phase.Waiting && _hasPlaybackRequest)
        {
            BeginSegment(Phase.Outbound, true);
        }

        Schedule();
    }

    private void Schedule()
    {
        var isMoving = _isAvailable && (_phase is Phase.Target or Phase.Outbound or Phase.Returning || !_saved.Equals(_savedTarget));
        ObserveVisibility(_root is not null && (isMoving || _hasPlaybackRequest && IsAnimationEnabled && IsEffectivelyEnabled && Size > 0));
        SetAndRaise(IsAnimatingProperty, ref _isAnimating, isMoving);
        InvalidateVisual();
        if (!isMoving || _hasPendingFrame || _root is null)
        {
            return;
        }

        _hasPendingFrame = true;
        _root.RequestAnimationFrame(_tick ??= CreateFrameCallback());
    }

    private Action<TimeSpan> CreateFrameCallback()
    {
        // A callback queued before detach must not advance a new attachment to the same root.
        Action<TimeSpan>? callback = null;
        callback = _ =>
        {
            if (_tick == callback)
            {
                AdvanceFrame();
            }
        };
        return callback;
    }

    private void AdvanceFrame()
    {
        _hasPendingFrame = false;
        RefreshAvailability();
        SampleNow();
        Schedule();
    }

    // Avalonia 12.0 has no portable reduced-motion query. Other platforms can bind
    // IsAnimationEnabled to the host application's accessibility preference.
    private static bool AllowsSystemAnimation()
    {
        return !OperatingSystem.IsWindows() || !SystemParametersInfo(0x1042, 0, out var enabled, 0) || enabled != 0;
    }

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(uint action, uint parameter, out int value, uint flags);

    private enum Phase
    {
        Idle,
        Target,
        Outbound,
        Returning,
        Waiting
    }
}