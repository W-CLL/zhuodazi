using System.Windows;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace ZhuoDazi.Behaviors;

/// <summary>
/// 行为决策的输入设置
/// </summary>
public readonly record struct BehaviorOptions(
    string Personality,
    bool MouseInteractionEnabled,
    bool RandomMovementEnabled);

/// <summary>
/// 宠物行为状态机：管理自主行为的决策、停靠计时和漫游目标。
/// 决策概率表与冷却时间从 PetWindow 原实现中原样迁移。
/// </summary>
public sealed class PetBehaviorStateMachine
{
    public enum Activity
    {
        Idle,    // 待机
        Wander,  // 漫游
        Chase,   // 追逐鼠标
        Avoid,   // 躲避鼠标
        Dock     // 停靠在屏幕边缘
    }

    private static readonly TimeSpan SpeechCooldown = TimeSpan.FromSeconds(38);
    private const double SpeechChance = 0.13;

    private Activity _activity = Activity.Idle;
    private DateTime _nextDecision = DateTime.MinValue;
    private DateTime _dockedUntil = DateTime.MinValue;
    private DateTime _lastSpeech = DateTime.MinValue;
    private Point _wanderTarget;

    public Activity CurrentActivity => _activity;
    public Point WanderTarget => _wanderTarget;
    public bool IsDocked => DateTime.UtcNow < _dockedUntil;

    /// <summary>
    /// 重置行为状态（外观或设置变化时调用）
    /// </summary>
    public void Reset()
    {
        _activity = Activity.Idle;
        _nextDecision = DateTime.MinValue;
        _dockedUntil = DateTime.MinValue;
    }

    /// <summary>
    /// 清除停靠计时（拖拽或进入剧本模式时调用）
    /// </summary>
    public void ClearDock() => _dockedUntil = DateTime.MinValue;

    /// <summary>
    /// 停靠指定秒数
    /// </summary>
    public void Dock(int seconds) => _dockedUntil = DateTime.UtcNow.AddSeconds(seconds);

    /// <summary>
    /// 是否到了做出新决策的时间
    /// </summary>
    public bool ShouldMakeDecision() => DateTime.UtcNow >= _nextDecision;

    /// <summary>
    /// 做出新的行为决策。
    /// </summary>
    /// <returns>要说的话对应的词条 key（"chase"/"dodge"），无话可说时为 null</returns>
    public string? Decide(BehaviorOptions options, Rect area, Size windowSize)
    {
        var personality = options.Personality;
        var roll = Random.Shared.Next(100);
        _activity = personality switch
        {
            "shy" => roll < 28 ? Activity.Wander : roll < 72 ? Activity.Dock : Activity.Idle,
            "clingy" => roll < 72 ? Activity.Chase : roll < 87 ? Activity.Wander : Activity.Dock,
            "chaotic" => roll < 30 ? Activity.Chase : roll < 57 ? Activity.Avoid : roll < 90 ? Activity.Wander : Activity.Dock,
            _ => roll < 42 ? Activity.Chase : roll < 72 ? Activity.Wander : roll < 88 ? Activity.Avoid : Activity.Dock
        };

        if (!options.MouseInteractionEnabled && _activity is Activity.Chase or Activity.Avoid)
            _activity = options.RandomMovementEnabled ? Activity.Wander : Activity.Idle;
        if (!options.RandomMovementEnabled && _activity is Activity.Wander or Activity.Dock)
            _activity = options.MouseInteractionEnabled
                ? personality == "shy" ? Activity.Avoid : Activity.Chase
                : Activity.Idle;

        var seconds = personality switch
        {
            "shy" => Random.Shared.NextDouble() * 5 + 5,
            "clingy" => Random.Shared.NextDouble() * 3 + 3,
            "chaotic" => Random.Shared.NextDouble() * 1.8 + 0.8,
            _ => Random.Shared.NextDouble() * 3.5 + 2.5
        };
        _nextDecision = DateTime.UtcNow.AddSeconds(seconds);
        _wanderTarget = PickTarget(_activity == Activity.Dock, area, windowSize);

        if (DateTime.UtcNow - _lastSpeech > SpeechCooldown && Random.Shared.NextDouble() < SpeechChance)
        {
            var key = _activity switch
            {
                Activity.Chase => "chase",
                Activity.Avoid => "dodge",
                _ => null
            };
            if (key is not null)
            {
                _lastSpeech = DateTime.UtcNow;
                return key;
            }
        }
        return null;
    }

    /// <summary>
    /// 计算当前帧的实际行为：性格会根据鼠标距离对已决策的行为做即时覆盖。
    /// </summary>
    public Activity GetEffectiveActivity(double cursorDistance, BehaviorOptions options)
    {
        var active = _activity;
        if (options.MouseInteractionEnabled)
        {
            if (options.Personality == "shy" && cursorDistance < 350) active = Activity.Avoid;
            if (options.Personality == "clingy") active = cursorDistance > 115 ? Activity.Chase : Activity.Idle;
            if (options.Personality == "chaotic" && cursorDistance < 120 && Random.Shared.NextDouble() < 0.035)
                active = Activity.Avoid;
        }
        else if (active is Activity.Chase or Activity.Avoid)
        {
            active = Activity.Idle;
        }

        if (!options.RandomMovementEnabled && active is Activity.Wander or Activity.Dock)
            active = Activity.Idle;
        return active;
    }

    private static Point PickTarget(bool edge, Rect area, Size windowSize)
    {
        var maxX = Math.Max(area.Left, area.Right - windowSize.Width);
        var maxY = Math.Max(area.Top, area.Bottom - windowSize.Height);
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
}
