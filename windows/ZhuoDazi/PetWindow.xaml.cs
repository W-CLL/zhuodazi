using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ZhuoDazi.Behaviors;
using ZhuoDazi.Controls;
using ZhuoDazi.Interop;
using ZhuoDazi.Physics;
using Activity = ZhuoDazi.Behaviors.PetBehaviorStateMachine.Activity;
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
    // Windows 消息常量
    private const int HotkeyId = 0xDA21;
    private const int WmHotkey = 0x0312;

    // UI 布局常量（单位：DIP，设备独立像素）
    private const double CollapsedBubbleHeight = 120; // 对话气泡折叠时的高度
    private const double MinimumInteractionBubbleHeight = 220; // 交互卡片最小高度
    private const double MinimumScrollableMessageHeight = 96; // 消息文本可滚动区域最小高度
    private const double WindowEdgeGap = 16; // 窗口与屏幕边缘的最小间距

    // 运动相关常量（物理细节在 PetPhysicsEngine / DragController 中）
    private const double MaximumThrowVelocity = 1650.0; // 投掷时的最大速度限制（DIP/秒）
    private const double EdgeSnapThreshold = 34.0; // 边缘吸附触发距离（DIP）
    private const double BounceSpeechImpactSpeed = 330.0; // 触发碰撞台词的最小撞击速度（DIP/秒）

    private readonly AppController _controller;
    private readonly AnimatedGifPlayer _gifPlayer;
    private readonly bool _isCompanion;
    private readonly DispatcherTimer _speechTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _motionTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Queue<DateTime> _hardThrows = new();
    private readonly PetPhysicsEngine _physics = new();
    private readonly DragController _drag;
    private readonly PetBehaviorStateMachine _behavior = new();
    private HwndSource? _source;
    private string? _currentPetPath;
    private bool _petLoaded;
    private int _appearanceVersion;
    private long _lastFrameTicks;
    private double _facing = 1;
    private bool _scripted;
    private Point? _scriptTarget;
    private double _scriptSpeed;
    private TaskCompletionSource<bool>? _scriptCompletion;
    private CancellationTokenRegistration _scriptCancellation;
    private bool _cornerSecretFound;
    private bool _throwSecretFound;
    private DateTime _lastBounceSpeech = DateTime.MinValue;
    private double _baseWindowHeight = 380;
    private Action<PetInteractionChoice?>? _interactionCallback;

    public PetWindow(AppController controller, bool isCompanion = false)
    {
        InitializeComponent();
        _controller = controller;
        _isCompanion = isCompanion;
        _drag = new DragController(_clock);
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

    private BehaviorOptions BehaviorOptions => new(
        _controller.Settings.Personality,
        _controller.Settings.MouseInteractionEnabled,
        _controller.Settings.RandomMovementEnabled);

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
        _behavior.Reset();
        if (!_scripted) _physics.SetVelocity(default);
    }

    public void ShowReaction(string message)
    {
        if (string.IsNullOrWhiteSpace(message) || !IsVisible) return;
        if (IsInteractionVisible)
        {
            Dispatcher.BeginInvoke(() => ShowReaction(message),
                System.Windows.Threading.DispatcherPriority.Background);
            return;
        }
        SpeechText.Text = message;
        SpeechBubble.Visibility = Visibility.Visible;
        _speechTimer.Stop();
        _speechTimer.Start();
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

        var measuredHeight = InteractionCardLayout.MeasureHeight(
            InteractionCard, InteractionMessageScroll, InteractionMessage,
            Width, maximumBubbleHeight, MinimumScrollableMessageHeight);
        var bubbleHeight = Math.Clamp(
            measuredHeight,
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
        _physics.StopInertia();
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
        _physics.StopInertia();
        _behavior.ClearDock();
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

        _physics.StopInertia();
        _behavior.ClearDock();
        _drag.StartDrag(GetCursorPositionInDips());
        CaptureMouse();
        ShowReaction(_controller.GetInteractionWord("grab", "轻点，我的像素会掉渣。"));
        PetStage.Cursor = Cursors.SizeAll;
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_drag.IsDragging) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            FinishDrag();
            return;
        }

        var delta = _drag.UpdateDrag(GetCursorPositionInDips());
        Left += delta.X;
        Top += delta.Y;
        UpdateFacing(_drag.DragVelocity.X);
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_drag.IsDragging) return;
        FinishDrag();
        e.Handled = true;
    }

    private void FinishDrag()
    {
        if (!_drag.IsDragging) return;
        var (isThrow, throwVelocity, isHardThrow) = _drag.EndDrag();
        ReleaseMouseCapture();
        PetStage.Cursor = Cursors.Hand;

        if (isThrow)
        {
            _physics.StartInertia(PetPhysicsEngine.ClampVector(throwVelocity, MaximumThrowVelocity));
            if (isHardThrow) RegisterHardThrow();
        }
        else
        {
            _physics.StopInertia();
            TrySnapToEdge(EdgeSnapThreshold);
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
        if (!IsVisible)
        {
            // 窗口不可见时没有可播的动画，但等待中的脚本运动必须立即结算，
            // 否则 await MoveToAsync 会永远挂住调用方（小剧场会整场冻结）。
            if (_scripted && _scriptTarget is not null) CompleteScriptMotion();
            return;
        }
        if (_drag.IsDragging) return;
        if (IsInteractionVisible)
        {
            _physics.StopInertia();
            return;
        }

        if (_scripted && _scriptTarget is not null)
        {
            UpdateScriptMotion(dt);
            return;
        }

        if (_physics.IsInertiaActive)
        {
            _physics.UpdateInertia(dt);
            MoveAndBounce(dt);
            if (_physics.TrySettle())
            {
                TrySnapToEdge(EdgeSnapThreshold);
                SavePosition();
            }
            return;
        }

        if (!_isCompanion && !_scripted) UpdateAutonomousMotion(dt);
    }

    /// <summary>把窗口落到脚本终点并唤醒等待中的 MoveToAsync。</summary>
    private void CompleteScriptMotion()
    {
        if (_scriptTarget is { } target)
        {
            Left = target.X;
            Top = target.Y;
        }
        _physics.SetVelocity(default);
        _scriptTarget = null;
        _scriptCancellation.Dispose();
        _scriptCompletion?.TrySetResult(true);
        _scriptCompletion = null;
    }

    private void UpdateScriptMotion(double dt)
    {
        if (_scriptTarget is not { } target) return;
        var delta = target - new Point(Left, Top);
        if (delta.Length < 3)
        {
            CompleteScriptMotion();
            return;
        }

        var desired = delta;
        desired.Normalize();
        desired *= Math.Min(_scriptSpeed, Math.Max(70, delta.Length * 4.5));
        _physics.ApproachVelocity(desired, 1250, dt);
        UpdateFacing(_physics.Velocity.X);
        MoveAndBounce(dt, false);
    }

    private void UpdateAutonomousMotion(double dt)
    {
        if (_behavior.IsDocked)
        {
            _physics.SetVelocity(default);
            return;
        }
        var options = BehaviorOptions;
        if (_behavior.ShouldMakeDecision())
        {
            var speechKey = _behavior.Decide(options, GetWorkingArea(), new System.Windows.Size(Width, Height));
            if (speechKey == "chase")
                ShowReaction(_controller.GetInteractionWord("chase", "鼠标别跑，我还没热身完。"));
            else if (speechKey == "dodge")
                ShowReaction(_controller.GetInteractionWord("dodge", "差点！你下手太突然了。"));
        }

        var cursor = GetCursorPositionInDips();
        var center = new Point(Left + Width / 2, Top + Height * 0.67);
        var cursorDelta = cursor - center;
        var cursorDistance = cursorDelta.Length;
        var personality = options.Personality;
        var active = _behavior.GetEffectiveActivity(cursorDistance, options);

        switch (active)
        {
            case Activity.Chase:
                if (cursorDistance > 82)
                    Steer(cursorDelta, personality == "clingy" ? 175 : 225, 620, dt);
                else
                    _physics.DampVelocity(0.78, dt);
                break;
            case Activity.Avoid:
                if (cursorDistance < 470)
                    Steer(-cursorDelta, personality == "shy" ? 270 : 245, 820, dt);
                else
                    _physics.DampVelocity(0.72, dt);
                break;
            case Activity.Wander:
            case Activity.Dock:
                var targetDelta = _behavior.WanderTarget - new Point(Left, Top);
                if (targetDelta.Length > 12)
                    Steer(targetDelta, personality == "chaotic" ? 310 : 120, 430, dt);
                else
                {
                    _physics.SetVelocity(default);
                    if (active == Activity.Dock) TrySnapToEdge(80);
                }
                break;
            default:
                _physics.DampVelocity(0.66, dt);
                break;
        }

        MoveAndBounce(dt);
    }

    private void Steer(Vector direction, double speed, double acceleration, double dt)
    {
        if (direction.LengthSquared < 0.01)
        {
            _physics.DampVelocity(0.7, dt);
            return;
        }
        direction.Normalize();
        _physics.ApproachVelocity(direction * speed, acceleration, dt);
        UpdateFacing(_physics.Velocity.X);
    }

    private void MoveAndBounce(double dt, bool react = true)
    {
        var area = GetWorkingArea();
        var (position, bounced, impactSpeed) = _physics.ApplyBounce(
            new Point(Left, Top), area, new System.Windows.Size(Width, Height), dt);
        Left = position.X;
        Top = position.Y;
        UpdateFacing(_physics.Velocity.X);
        if (bounced && react && impactSpeed > BounceSpeechImpactSpeed
            && DateTime.UtcNow - _lastBounceSpeech > TimeSpan.FromSeconds(7))
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
        _physics.SetVelocity(default);
        var seconds = _controller.Settings.Personality switch
        {
            "shy" => 11,
            "clingy" => 5,
            "chaotic" => 3,
            _ => 7
        };
        _behavior.Dock(seconds);
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
        _physics.SetVelocity(default);
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
}

public sealed record PetInteractionChoice(string Label, string Value, bool IsPrimary = false);
