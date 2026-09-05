using System.Diagnostics;
using System.Windows;
using Point = System.Windows.Point;

namespace ZhuoDazi.Controls;

/// <summary>
/// 拖拽控制器，负责处理鼠标拖拽、速度计算和投掷检测
/// </summary>
public sealed class DragController
{
    // 拖拽常量
    private const double VelocitySmoothingFactor = 0.42; // 速度平滑因子（新值权重）
    private const double MinimumDistanceForThrow = 14.0; // 触发投掷的最小拖拽距离（DIP）
    private const double MinimumVelocityForThrow = 115.0; // 触发投掷的最小速度（DIP/秒）
    private const double HardThrowThreshold = 900.0; // 判定为"用力投掷"的速度阈值（DIP/秒）
    private const double ThrowVelocityRetention = 0.9; // 投掷时保留 90% 的速度

    private readonly Stopwatch _clock;
    private bool _isDragging;
    private Point _lastDragPoint;
    private long _lastDragTicks;
    private double _dragDistance;
    private Vector _dragVelocity;

    public bool IsDragging => _isDragging;
    public Vector DragVelocity => _dragVelocity;
    public double DragDistance => _dragDistance;

    public DragController(Stopwatch clock)
    {
        _clock = clock;
    }

    /// <summary>
    /// 开始拖拽
    /// </summary>
    public void StartDrag(Point cursorPosition)
    {
        _isDragging = true;
        _dragVelocity = default;
        _dragDistance = 0;
        _lastDragPoint = cursorPosition;
        _lastDragTicks = _clock.ElapsedTicks;
    }

    /// <summary>
    /// 更新拖拽位置
    /// </summary>
    /// <returns>位置变化量</returns>
    public Vector UpdateDrag(Point cursorPosition)
    {
        if (!_isDragging) return default;

        var nowTicks = _clock.ElapsedTicks;
        var delta = cursorPosition - _lastDragPoint;
        var seconds = Math.Max(0.001, (nowTicks - _lastDragTicks) / (double)Stopwatch.Frequency);

        _dragDistance += delta.Length;
        var instantVelocity = delta / seconds;

        // 指数移动平均平滑速度
        _dragVelocity = (_dragVelocity * (1 - VelocitySmoothingFactor)) +
                       (instantVelocity * VelocitySmoothingFactor);

        _lastDragPoint = cursorPosition;
        _lastDragTicks = nowTicks;

        return delta;
    }

    /// <summary>
    /// 结束拖拽
    /// </summary>
    /// <returns>投掷结果（是否投掷，投掷速度，是否用力投掷）</returns>
    public (bool IsThrow, Vector ThrowVelocity, bool IsHardThrow) EndDrag()
    {
        if (!_isDragging) return (false, default, false);

        _isDragging = false;
        var speed = _dragVelocity.Length;

        if (_dragDistance > MinimumDistanceForThrow && speed > MinimumVelocityForThrow)
        {
            var throwVelocity = _dragVelocity * ThrowVelocityRetention;
            var isHardThrow = speed > HardThrowThreshold;

            // 重置状态
            _dragVelocity = default;
            _dragDistance = 0;

            return (true, throwVelocity, isHardThrow);
        }

        // 没有达到投掷条件
        _dragVelocity = default;
        _dragDistance = 0;
        return (false, default, false);
    }

    /// <summary>
    /// 取消拖拽
    /// </summary>
    public void CancelDrag()
    {
        _isDragging = false;
        _dragVelocity = default;
        _dragDistance = 0;
    }
}
