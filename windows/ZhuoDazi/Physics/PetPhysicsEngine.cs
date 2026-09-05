using System.Windows;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace ZhuoDazi.Physics;

/// <summary>
/// 宠物物理模拟引擎，负责处理运动、碰撞、重力和惯性
/// </summary>
public sealed class PetPhysicsEngine
{
    // 物理常量
    private const double GravityAcceleration = 420.0; // 重力加速度（DIP/秒²）
    private const double InertiaDampingPerFrame = 0.986; // 惯性阻尼系数（每帧，60 FPS 时）
    private const double MinimumInertiaVelocity = 34.0; // 惯性运动停止的最小速度（DIP/秒）

    private const double BounceRestitutionHorizontal = 0.72; // 水平边缘反弹保留 72% 速度
    private const double BounceRestitutionTop = 0.68; // 顶部边缘反弹保留 68% 速度
    private const double BounceRestitutionBottom = 0.62; // 底部边缘反弹保留 62% 速度

    private Vector _velocity;
    private bool _inertiaActive;

    public Vector Velocity => _velocity;
    public bool IsInertiaActive => _inertiaActive;

    /// <summary>
    /// 设置速度
    /// </summary>
    public void SetVelocity(Vector velocity)
    {
        _velocity = velocity;
    }

    /// <summary>
    /// 启动惯性运动
    /// </summary>
    public void StartInertia(Vector initialVelocity)
    {
        _velocity = initialVelocity;
        _inertiaActive = true;
    }

    /// <summary>
    /// 停止惯性运动
    /// </summary>
    public void StopInertia()
    {
        _inertiaActive = false;
        _velocity = default;
    }

    /// <summary>
    /// 施加惯性运动的重力与阻尼（每帧调用，随后应先移动再调用 TrySettle 判断停止）
    /// </summary>
    /// <param name="dt">时间步长（秒）</param>
    public void UpdateInertia(double dt)
    {
        if (!_inertiaActive) return;
        _velocity.Y += GravityAcceleration * dt;
        _velocity *= Math.Pow(InertiaDampingPerFrame, dt * 60);
    }

    /// <summary>
    /// 若速度低于停止阈值则结束惯性运动
    /// </summary>
    /// <returns>惯性刚刚停止时返回 true</returns>
    public bool TrySettle()
    {
        if (!_inertiaActive || _velocity.Length >= MinimumInertiaVelocity) return false;
        _inertiaActive = false;
        _velocity = default;
        return true;
    }

    /// <summary>
    /// 应用碰撞反弹
    /// </summary>
    /// <param name="position">当前位置</param>
    /// <param name="bounds">边界矩形</param>
    /// <param name="windowSize">窗口大小</param>
    /// <param name="dt">时间步长</param>
    /// <returns>新位置和是否发生了碰撞</returns>
    public (Point NewPosition, bool Bounced, double ImpactSpeed) ApplyBounce(
        Point position,
        Rect bounds,
        Size windowSize,
        double dt)
    {
        var minX = bounds.Left;
        var minY = bounds.Top;
        var maxX = Math.Max(minX, bounds.Right - windowSize.Width);
        var maxY = Math.Max(minY, bounds.Bottom - windowSize.Height);

        var nextX = position.X + _velocity.X * dt;
        var nextY = position.Y + _velocity.Y * dt;
        var impactSpeed = _velocity.Length;
        var bounced = false;

        if (nextX < minX)
        {
            nextX = minX;
            _velocity.X = Math.Abs(_velocity.X) * BounceRestitutionHorizontal;
            bounced = true;
        }
        else if (nextX > maxX)
        {
            nextX = maxX;
            _velocity.X = -Math.Abs(_velocity.X) * BounceRestitutionHorizontal;
            bounced = true;
        }

        if (nextY < minY)
        {
            nextY = minY;
            _velocity.Y = Math.Abs(_velocity.Y) * BounceRestitutionTop;
            bounced = true;
        }
        else if (nextY > maxY)
        {
            nextY = maxY;
            _velocity.Y = -Math.Abs(_velocity.Y) * BounceRestitutionBottom;
            bounced = true;
        }

        return (new Point(nextX, nextY), bounced, impactSpeed);
    }

    /// <summary>
    /// 向目标速度靠拢（用于 AI 移动）
    /// </summary>
    public void ApproachVelocity(Vector desired, double acceleration, double dt)
    {
        var change = desired - _velocity;
        var maxChange = acceleration * dt;

        if (change.Length > maxChange)
        {
            change.Normalize();
            change *= maxChange;
        }

        _velocity += change;
    }

    /// <summary>
    /// 速度阻尼
    /// </summary>
    public void DampVelocity(double frameFactor, double dt)
    {
        _velocity *= Math.Pow(frameFactor, dt * 60);
        if (_velocity.Length < 2) _velocity = default;
    }

    /// <summary>
    /// 限制向量长度
    /// </summary>
    public static Vector ClampVector(Vector value, double maximum)
    {
        if (value.Length <= maximum) return value;
        value.Normalize();
        return value * maximum;
    }
}
