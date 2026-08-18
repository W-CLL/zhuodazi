using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ZhuoDazi.Controls;
using ZhuoDazi.Interop;
using Forms = System.Windows.Forms;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace ZhuoDazi;

public partial class PetWindow : Window
{
    private const int HotkeyId = 0xDA21;
    private const int WmHotkey = 0x0312;
    private const double CollapsedBubbleHeight = 120;
    private const double MinimumInteractionBubbleHeight = 220;
    private const double MinimumScrollableMessageHeight = 96;
    private const double WindowEdgeGap = 16;
    private readonly AppController _controller;
    private readonly AnimatedGifPlayer _gifPlayer;
    private readonly bool _isCompanion;
    private readonly DispatcherTimer _speechTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _motionTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Queue<DateTime> _hardThrows = new();
    private HwndSource? _source;
    private string? _currentPetPath;
    private bool _petLoaded;
    private int _appearanceVersion;
    private bool _isDragging;
    private Point _lastDragPoint;
    private long _lastDragTicks;
    private double _dragDistance;
    private Vector _dragVelocity;
    private Vector _velocity;
    private bool _inertiaActive;
    private long _lastFrameTicks;
    private Activity _activity = Activity.Idle;
    private DateTime _nextBehaviorDecision = DateTime.MinValue;
    private DateTime _lastBehaviorSpeech = DateTime.MinValue;
    private DateTime _lastBounceSpeech = DateTime.MinValue;
    private DateTime _dockedUntil = DateTime.MinValue;
    private Point _wanderTarget;
    private double _facing = 1;
    private bool _scripted;
    private Point? _scriptTarget;
    private double _scriptSpeed;
    private TaskCompletionSource<bool>? _scriptCompletion;
    private CancellationTokenRegistration _scriptCancellation;
    private bool _cornerSecretFound;
    private bool _throwSecretFound;
    private double _baseWindowHeight = 380;
    private Action<PetInteractionChoice?>? _interactionCallback;

    private enum Activity
    {
        Idle,
        Wander,
        Chase,
        Avoid,
        Dock
    }

    public PetWindow(AppController controller, bool isCompanion = false)
    {
        InitializeComponent();
        _controller = controller;
        _isCompanion = isCompanion;
        ShowActivated = !isCompanion;
        _gifPlayer = new AnimatedGifPlayer(PetImage);
        _speechTimer.Tick += (_, _) =>
        {
            _speechTimer.Stop();
            SpeechBubble.Visibility = Visibility.Collapsed;
        };
        _motionTimer.Tick += OnMotionTick;
        Loaded += OnLoaded;
        Closed += OnClosed;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseMove += OnMouseMove;
        if (!isCompanion) MouseRightButtonUp += (_, _) => _controller.ShowTrayMenu();
    }

    public void RefreshAppearance(string? petPath)
    {
        _appearanceVersion++;
        var size = _controller.Settings.Size;
        var bottom = IsLoaded && double.IsFinite(Top) ? Top + Height : double.NaN;
        Width = Math.Max(250, size + 70);
        _baseWindowHeight = Math.Max(320, size + 150);
        PetStage.Width = size + 20;
        PetStage.Height = size + 20;
        Opacity = _controller.Settings.Opacity / 100d;
        Topmost = _controller.Settings.AlwaysOnTop;
        UpdateFacing();
        if (!string.Equals(_currentPetPath, petPath, StringComparison.OrdinalIgnoreCase))
        {
            _currentPetPath = petPath;
            _petLoaded = _gifPlayer.Load(petPath);
        }
        PetImage.Visibility = _petLoaded ? Visibility.Visible : Visibility.Collapsed;
        DefaultPet.Visibility = _petLoaded ? Visibility.Collapsed : Visibility.Visible;
        NativeMethods.SetClickThrough(this, _isCompanion || _controller.Settings.ClickThrough);
        SetInteractionExpanded(IsInteractionVisible, bottom);
        if (!_isCompanion && _controller.Settings.ClickThrough)
            ShowReaction("鼠标穿透开着，Ctrl+Shift+P 可关掉。");
    }

    public void RefreshBehavior()
    {
        _nextBehaviorDecision = DateTime.MinValue;
        _activity = Activity.Idle;
        _dockedUntil = DateTime.MinValue;
        if (!_scripted) _velocity = default;
    }

    public void ShowReaction(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        if (IsInteractionVisible) return;
        SpeechText.Text = message;
        SpeechBubble.Visibility = Visibility.Visible;
        _speechTimer.Stop();
        _speechTimer.Start();
        if (!IsVisible) Show();
    }

    public async void ShowReminder(string message, string? expressionPath)
    {
        ShowReaction(message);
        if (string.IsNullOrWhiteSpace(expressionPath) || !File.Exists(expressionPath)) return;

        var version = ++_appearanceVersion;
        _currentPetPath = expressionPath;
        _petLoaded = _gifPlayer.Load(expressionPath);
        PetImage.Visibility = _petLoaded ? Visibility.Visible : Visibility.Collapsed;
        DefaultPet.Visibility = _petLoaded ? Visibility.Collapsed : Visibility.Visible;
        await Task.Delay(TimeSpan.FromSeconds(6));
        if (IsLoaded && version == _appearanceVersion) RefreshAppearance(_controller.CurrentPetPath());
    }

    public bool IsInteractionVisible => InteractionCard.Visibility == Visibility.Visible;

    public void ShowInteraction(
        string title,
        string message,
        IReadOnlyList<PetInteractionChoice> choices,
        Action<PetInteractionChoice?> callback)
    {
        if (_isCompanion) return;
        if (IsInteractionVisible) CompleteInteraction(null);

        _speechTimer.Stop();
        SpeechBubble.Visibility = Visibility.Collapsed;
        InteractionTitle.Text = title;
        InteractionMessage.Text = message;
        InteractionChoicePanel.Children.Clear();
        _interactionCallback = callback;

        foreach (var choice in choices)
        {
            var button = new WpfButton
            {
                Content = choice.Label,
                Tag = choice,
                Style = (Style)FindResource("InteractionChoiceButton")
            };
            if (choice.IsPrimary)
            {
                button.Background = new SolidColorBrush(WpfColor.FromRgb(22, 125, 108));
                button.BorderBrush = button.Background;
                button.Foreground = WpfBrushes.White;
            }
            button.Click += (_, _) => CompleteInteraction(choice);
            InteractionChoicePanel.Children.Add(button);
        }

        InteractionCard.Visibility = Visibility.Visible;
        InteractionMessageScroll.ScrollToHome();
        SetInteractionExpanded(true);
        if (!IsVisible) Show();
    }

    private void DismissInteraction_Click(object sender, RoutedEventArgs e)
        => CompleteInteraction(null);

    private void CompleteInteraction(PetInteractionChoice? choice)
    {
        if (!IsInteractionVisible && _interactionCallback is null) return;
        var callback = _interactionCallback;
        _interactionCallback = null;
        InteractionCard.Visibility = Visibility.Collapsed;
        InteractionChoicePanel.Children.Clear();
        SetInteractionExpanded(false);
        callback?.Invoke(choice);
    }

    private void SetInteractionExpanded(bool expanded, double? anchoredBottom = null)
    {
        var bottom = anchoredBottom ?? (IsLoaded && double.IsFinite(Top) ? Top + Height : double.NaN);
        if (!expanded)
        {
            BubbleRow.Height = new GridLength(CollapsedBubbleHeight);
            Height = _baseWindowHeight;
            if (double.IsFinite(bottom)) AnchorBottomWithinWorkingArea(bottom);
            return;
        }

        var area = GetWorkingArea();
        var maximumBubbleHeight = Math.Max(
            MinimumInteractionBubbleHeight,
            CollapsedBubbleHeight + area.Height - _baseWindowHeight - WindowEdgeGap);

        InteractionMessageScroll.ClearValue(MaxHeightProperty);
        InteractionCard.Measure(new System.Windows.Size(Width, double.PositiveInfinity));

        if (InteractionCard.DesiredSize.Height > maximumBubbleHeight)
        {
            var overflow = InteractionCard.DesiredSize.Height - maximumBubbleHeight;
            InteractionMessageScroll.MaxHeight = Math.Max(
                MinimumScrollableMessageHeight,
                InteractionMessageScroll.DesiredSize.Height - overflow);
            InteractionCard.Measure(new System.Windows.Size(Width, double.PositiveInfinity));
        }

        var bubbleHeight = Math.Clamp(
            Math.Ceiling(InteractionCard.DesiredSize.Height),
            MinimumInteractionBubbleHeight,
            maximumBubbleHeight);
        BubbleRow.Height = new GridLength(bubbleHeight);
        Height = _baseWindowHeight + bubbleHeight - CollapsedBubbleHeight;
        if (double.IsFinite(bottom)) AnchorBottomWithinWorkingArea(bottom);
    }

    private void AnchorBottomWithinWorkingArea(double bottom)
    {
        var area = GetWorkingArea();
        Top = Math.Clamp(bottom - Height, area.Top, Math.Max(area.Top, area.Bottom - Height));
    }

    public void Place(Point position)
    {
        Left = position.X;
        Top = position.Y;
        _velocity = default;
        _inertiaActive = false;
    }

    public Rect GetWorkingArea()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var area = handle == nint.Zero ? Forms.Screen.PrimaryScreen?.WorkingArea : Forms.Screen.FromHandle(handle).WorkingArea;
        if (area is null) return SystemParameters.WorkArea;
        var topLeft = DeviceToDip(new Point(area.Value.Left, area.Value.Top));
        var bottomRight = DeviceToDip(new Point(area.Value.Right, area.Value.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    public void EnterScriptedMode()
    {
        _scripted = true;
        _inertiaActive = false;
        _dockedUntil = DateTime.MinValue;
        _velocity = default;
    }

    public void LeaveScriptedMode()
    {
        _scripted = false;
        CancelScriptMotion();
        RefreshBehavior();
    }

    public Task MoveToAsync(Point target, double speed, CancellationToken cancellationToken)
    {
        CancelScriptMotion();
        _scripted = true;
        _scriptTarget = target;
        _scriptSpeed = Math.Clamp(speed, 80, 900);
        _scriptCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (cancellationToken.CanBeCanceled)
        {
            _scriptCancellation = cancellationToken.Register(() => Dispatcher.BeginInvoke(CancelScriptMotion));
        }
        return _scriptCompletion.Task;
    }

    public void PlaySecretAnimation()
    {
        var animation = new DoubleAnimation
        {
            From = 0,
            To = 360,
            Duration = TimeSpan.FromMilliseconds(720),
            EasingFunction = new BackEase { Amplitude = 0.32, EasingMode = EasingMode.EaseOut }
        };
        SecretRotateTransform.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, animation);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        if (!_isCompanion)
        {
            _source?.AddHook(WindowHook);
            NativeMethods.RegisterHotKey(_source?.Handle ?? nint.Zero, HotkeyId, 0x0002 | 0x0004, 0x50);
        }
        NativeMethods.SetClickThrough(this, _isCompanion || _controller.Settings.ClickThrough);
        _lastFrameTicks = _clock.ElapsedTicks;
        _motionTimer.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _motionTimer.Stop();
        _speechTimer.Stop();
        CancelScriptMotion();
        if (_source is not null && !_isCompanion)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
            _source.RemoveHook(WindowHook);
        }
        var callback = _interactionCallback;
        _interactionCallback = null;
        callback?.Invoke(null);
        _gifPlayer.Dispose();
    }

    private nint WindowHook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmHotkey && wParam == HotkeyId)
        {
            _controller.SetClickThrough(!_controller.Settings.ClickThrough);
            handled = true;
        }
        return nint.Zero;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isCompanion || _controller.Settings.ClickThrough || _scripted) return;
        if (IsInteractionVisible && InteractionCard.IsMouseOver) return;
        if (e.ClickCount == 2)
        {
            _controller.ShowSettings();
            return;
        }
        if (e.LeftButton != MouseButtonState.Pressed) return;

        _isDragging = true;
        _inertiaActive = false;
        _dockedUntil = DateTime.MinValue;
        _velocity = default;
        _dragVelocity = default;
        _dragDistance = 0;
        _lastDragPoint = GetCursorPositionInDips();
        _lastDragTicks = _clock.ElapsedTicks;
        CaptureMouse();
        ShowReaction(_controller.GetInteractionWord("grab", "轻点，我的像素会掉渣。"));
        PetStage.Cursor = Cursors.SizeAll;
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            FinishDrag();
            return;
        }

        var nowTicks = _clock.ElapsedTicks;
        var point = GetCursorPositionInDips();
        var delta = point - _lastDragPoint;
        var seconds = Math.Max(0.001, (nowTicks - _lastDragTicks) / (double)Stopwatch.Frequency);
        Left += delta.X;
        Top += delta.Y;
        _dragDistance += delta.Length;
        var instantVelocity = delta / seconds;
        _dragVelocity = (_dragVelocity * 0.58) + (instantVelocity * 0.42);
        _lastDragPoint = point;
        _lastDragTicks = nowTicks;
        UpdateFacing(_dragVelocity.X);
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        FinishDrag();
        e.Handled = true;
    }

    private void FinishDrag()
    {
        if (!_isDragging) return;
        _isDragging = false;
        ReleaseMouseCapture();
        PetStage.Cursor = Cursors.Hand;

        var speed = _dragVelocity.Length;
        if (_dragDistance > 14 && speed > 115)
        {
            _velocity = ClampVector(_dragVelocity * 0.9, 1650);
            _inertiaActive = true;
            if (speed > 900) RegisterHardThrow();
        }
        else
        {
            _velocity = default;
            TrySnapToEdge(34);
            SavePosition();
        }
        TryCornerSecret();
    }

    private void OnMotionTick(object? sender, EventArgs e)
    {
        var nowTicks = _clock.ElapsedTicks;
        var elapsed = (nowTicks - _lastFrameTicks) / (double)Stopwatch.Frequency;
        _lastFrameTicks = nowTicks;
        var dt = Math.Clamp(elapsed, 0.001, 0.05);
        if (!IsVisible || _isDragging) return;
        if (IsInteractionVisible)
        {
            _inertiaActive = false;
            _velocity = default;
            return;
        }

        if (_scripted && _scriptTarget is not null)
        {
            UpdateScriptMotion(dt);
            return;
        }

        if (_inertiaActive)
        {
            _velocity.Y += 420 * dt;
            _velocity *= Math.Pow(0.986, dt * 60);
            MoveAndBounce(dt);
            if (_velocity.Length < 34)
            {
                _inertiaActive = false;
                _velocity = default;
                TrySnapToEdge(38);
                SavePosition();
            }
            return;
        }

        if (!_isCompanion && !_scripted) UpdateAutonomousMotion(dt);
    }

    private void UpdateScriptMotion(double dt)
    {
        if (_scriptTarget is not { } target) return;
        var delta = target - new Point(Left, Top);
        if (delta.Length < 3)
        {
            Left = target.X;
            Top = target.Y;
            _velocity = default;
            _scriptTarget = null;
            _scriptCancellation.Dispose();
            _scriptCompletion?.TrySetResult(true);
            _scriptCompletion = null;
            return;
        }

        var desired = delta;
        desired.Normalize();
        desired *= Math.Min(_scriptSpeed, Math.Max(70, delta.Length * 4.5));
        ApproachVelocity(desired, 1250, dt);
        MoveAndBounce(dt, false);
    }

    private void UpdateAutonomousMotion(double dt)
    {
        var now = DateTime.UtcNow;
        if (now < _dockedUntil)
        {
            _velocity = default;
            return;
        }
        if (now >= _nextBehaviorDecision) DecideActivity();

        var cursor = GetCursorPositionInDips();
        var center = new Point(Left + Width / 2, Top + Height * 0.67);
        var cursorDelta = cursor - center;
        var cursorDistance = cursorDelta.Length;
        var personality = _controller.Settings.Personality;
        var active = _activity;

        if (_controller.Settings.MouseInteractionEnabled)
        {
            if (personality == "shy" && cursorDistance < 350) active = Activity.Avoid;
            if (personality == "clingy") active = cursorDistance > 115 ? Activity.Chase : Activity.Idle;
            if (personality == "chaotic" && cursorDistance < 120 && Random.Shared.NextDouble() < 0.035)
                active = Activity.Avoid;
        }
        else if (active is Activity.Chase or Activity.Avoid)
        {
            active = Activity.Idle;
        }

        if (!_controller.Settings.RandomMovementEnabled && active is Activity.Wander or Activity.Dock)
            active = Activity.Idle;

        switch (active)
        {
            case Activity.Chase:
                if (cursorDistance > 82)
                    Steer(cursorDelta, personality == "clingy" ? 175 : 225, 620, dt);
                else
                    DampVelocity(0.78, dt);
                break;
            case Activity.Avoid:
                if (cursorDistance < 470)
                    Steer(-cursorDelta, personality == "shy" ? 270 : 245, 820, dt);
                else
                    DampVelocity(0.72, dt);
                break;
            case Activity.Wander:
            case Activity.Dock:
                var targetDelta = _wanderTarget - new Point(Left, Top);
                if (targetDelta.Length > 12)
                    Steer(targetDelta, personality == "chaotic" ? 310 : 120, 430, dt);
                else
                {
                    _velocity = default;
                    if (active == Activity.Dock) TrySnapToEdge(80);
                }
                break;
            default:
                DampVelocity(0.66, dt);
                break;
        }

        MoveAndBounce(dt);
    }

    private void DecideActivity()
    {
        var personality = _controller.Settings.Personality;
        var roll = Random.Shared.Next(100);
        _activity = personality switch
        {
            "shy" => roll < 28 ? Activity.Wander : roll < 72 ? Activity.Dock : Activity.Idle,
            "clingy" => roll < 72 ? Activity.Chase : roll < 87 ? Activity.Wander : Activity.Dock,
            "chaotic" => roll < 30 ? Activity.Chase : roll < 57 ? Activity.Avoid : roll < 90 ? Activity.Wander : Activity.Dock,
            _ => roll < 42 ? Activity.Chase : roll < 72 ? Activity.Wander : roll < 88 ? Activity.Avoid : Activity.Dock
        };

        if (!_controller.Settings.MouseInteractionEnabled && _activity is Activity.Chase or Activity.Avoid)
            _activity = _controller.Settings.RandomMovementEnabled ? Activity.Wander : Activity.Idle;
        if (!_controller.Settings.RandomMovementEnabled && _activity is Activity.Wander or Activity.Dock)
            _activity = _controller.Settings.MouseInteractionEnabled
                ? personality == "shy" ? Activity.Avoid : Activity.Chase
                : Activity.Idle;

        var seconds = personality switch
        {
            "shy" => Random.Shared.NextDouble() * 5 + 5,
            "clingy" => Random.Shared.NextDouble() * 3 + 3,
            "chaotic" => Random.Shared.NextDouble() * 1.8 + 0.8,
            _ => Random.Shared.NextDouble() * 3.5 + 2.5
        };
        _nextBehaviorDecision = DateTime.UtcNow.AddSeconds(seconds);
        _wanderTarget = PickTarget(_activity == Activity.Dock);

        if (DateTime.UtcNow - _lastBehaviorSpeech > TimeSpan.FromSeconds(38) && Random.Shared.NextDouble() < 0.13)
        {
            if (_activity == Activity.Chase)
                ShowReaction(_controller.GetInteractionWord("chase", "鼠标别跑，我还没热身完。"));
            else if (_activity == Activity.Avoid)
                ShowReaction(_controller.GetInteractionWord("dodge", "差点！你下手太突然了。"));
            _lastBehaviorSpeech = DateTime.UtcNow;
        }
    }

    private Point PickTarget(bool edge)
    {
        var area = GetWorkingArea();
        var maxX = Math.Max(area.Left, area.Right - Width);
        var maxY = Math.Max(area.Top, area.Bottom - Height);
        if (!edge)
        {
            return new Point(
                area.Left + Random.Shared.NextDouble() * Math.Max(1, maxX - area.Left),
                area.Top + Random.Shared.NextDouble() * Math.Max(1, maxY - area.Top));
        }

        return Random.Shared.Next(4) switch
        {
            0 => new Point(area.Left, area.Top + Random.Shared.NextDouble() * Math.Max(1, maxY - area.Top)),
            1 => new Point(maxX, area.Top + Random.Shared.NextDouble() * Math.Max(1, maxY - area.Top)),
            2 => new Point(area.Left + Random.Shared.NextDouble() * Math.Max(1, maxX - area.Left), area.Top),
            _ => new Point(area.Left + Random.Shared.NextDouble() * Math.Max(1, maxX - area.Left), maxY)
        };
    }

    private void Steer(Vector direction, double speed, double acceleration, double dt)
    {
        if (direction.LengthSquared < 0.01)
        {
            DampVelocity(0.7, dt);
            return;
        }
        direction.Normalize();
        ApproachVelocity(direction * speed, acceleration, dt);
    }

    private void ApproachVelocity(Vector desired, double acceleration, double dt)
    {
        var change = desired - _velocity;
        var maxChange = acceleration * dt;
        if (change.Length > maxChange)
        {
            change.Normalize();
            change *= maxChange;
        }
        _velocity += change;
        UpdateFacing(_velocity.X);
    }

    private void DampVelocity(double frameFactor, double dt)
    {
        _velocity *= Math.Pow(frameFactor, dt * 60);
        if (_velocity.Length < 2) _velocity = default;
    }

    private void MoveAndBounce(double dt, bool react = true)
    {
        var area = GetWorkingArea();
        var minX = area.Left;
        var minY = area.Top;
        var maxX = Math.Max(minX, area.Right - Width);
        var maxY = Math.Max(minY, area.Bottom - Height);
        var nextX = Left + _velocity.X * dt;
        var nextY = Top + _velocity.Y * dt;
        var impactSpeed = _velocity.Length;
        var bounced = false;

        if (nextX < minX)
        {
            nextX = minX;
            _velocity.X = Math.Abs(_velocity.X) * 0.72;
            bounced = true;
        }
        else if (nextX > maxX)
        {
            nextX = maxX;
            _velocity.X = -Math.Abs(_velocity.X) * 0.72;
            bounced = true;
        }
        if (nextY < minY)
        {
            nextY = minY;
            _velocity.Y = Math.Abs(_velocity.Y) * 0.68;
            bounced = true;
        }
        else if (nextY > maxY)
        {
            nextY = maxY;
            _velocity.Y = -Math.Abs(_velocity.Y) * 0.62;
            bounced = true;
        }

        Left = nextX;
        Top = nextY;
        UpdateFacing(_velocity.X);
        if (bounced && react && impactSpeed > 330 && DateTime.UtcNow - _lastBounceSpeech > TimeSpan.FromSeconds(7))
        {
            ShowReaction(_controller.GetInteractionWord("bounce", "碰到边啦，这不叫撞，叫折返。"));
            _lastBounceSpeech = DateTime.UtcNow;
        }
    }

    private void TrySnapToEdge(double threshold)
    {
        var area = GetWorkingArea();
        var maxX = Math.Max(area.Left, area.Right - Width);
        var maxY = Math.Max(area.Top, area.Bottom - Height);
        var distances = new[]
        {
            (Distance: Math.Abs(Left - area.Left), Position: new Point(area.Left, Math.Clamp(Top, area.Top, maxY))),
            (Distance: Math.Abs(Left - maxX), Position: new Point(maxX, Math.Clamp(Top, area.Top, maxY))),
            (Distance: Math.Abs(Top - area.Top), Position: new Point(Math.Clamp(Left, area.Left, maxX), area.Top)),
            (Distance: Math.Abs(Top - maxY), Position: new Point(Math.Clamp(Left, area.Left, maxX), maxY))
        };
        var nearest = distances.MinBy(item => item.Distance);
        if (nearest.Distance > threshold) return;
        Left = nearest.Position.X;
        Top = nearest.Position.Y;
        _velocity = default;
        var seconds = _controller.Settings.Personality switch
        {
            "shy" => 11,
            "clingy" => 5,
            "chaotic" => 3,
            _ => 7
        };
        _dockedUntil = DateTime.UtcNow.AddSeconds(seconds);
    }

    private void RegisterHardThrow()
    {
        var now = DateTime.UtcNow;
        _hardThrows.Enqueue(now);
        while (_hardThrows.TryPeek(out var time) && now - time > TimeSpan.FromSeconds(20)) _hardThrows.Dequeue();
        if (_throwSecretFound || _hardThrows.Count < 3) return;
        _throwSecretFound = true;
        _hardThrows.Clear();
        ShowReaction("连续三次起飞认证：你很会扔，我很会晕。");
        PlaySecretAnimation();
    }

    private void TryCornerSecret()
    {
        if (_cornerSecretFound) return;
        var area = GetWorkingArea();
        var maxX = Math.Max(area.Left, area.Right - Width);
        var maxY = Math.Max(area.Top, area.Bottom - Height);
        var inSecretCorner = Math.Abs(Left - maxX) < 9 && Math.Abs(Top - maxY) < 9;
        if (!inSecretCorner) return;
        _cornerSecretFound = true;
        ShowReaction("嘘，这个角落有一格只属于我们的存档位。");
        PlaySecretAnimation();
    }

    private void SavePosition()
    {
        if (!_isCompanion) _controller.SavePosition(Left, Top);
    }

    private void CancelScriptMotion()
    {
        _scriptTarget = null;
        _velocity = default;
        _scriptCancellation.Dispose();
        _scriptCompletion?.TrySetCanceled();
        _scriptCompletion = null;
    }

    private Point GetCursorPositionInDips()
    {
        var cursor = Forms.Cursor.Position;
        return DeviceToDip(new Point(cursor.X, cursor.Y));
    }

    private Point DeviceToDip(Point point)
    {
        var transform = _source?.CompositionTarget?.TransformFromDevice;
        return transform?.Transform(point) ?? point;
    }

    private void UpdateFacing(double horizontalVelocity = 0)
    {
        if (horizontalVelocity < -18) _facing = -1;
        else if (horizontalVelocity > 18) _facing = 1;
        MirrorTransform.ScaleX = (_controller.Settings.Mirrored ? -1 : 1) * _facing;
    }

    private static Vector ClampVector(Vector value, double maximum)
    {
        if (value.Length <= maximum) return value;
        value.Normalize();
        return value * maximum;
    }
}

public sealed record PetInteractionChoice(string Label, string Value, bool IsPrimary = false);
