using System.Windows;
using ZhuoDazi.Services;
using Point = System.Windows.Point;

namespace ZhuoDazi;

internal sealed class CompanionVisitorQueue
{
    private readonly Queue<CompanionVisit> _visits = new();
    private PetWindow? _visitorWindow;
    private bool _showing;

    public bool IsShowing => _showing;
    public bool HasPending => _visits.Count > 0;

    public void Enqueue(IEnumerable<CompanionVisit> visits)
    {
        foreach (var visit in visits) _visits.Enqueue(visit);
    }

    public void CloseActive()
    {
        if (_visitorWindow?.IsLoaded == true) _visitorWindow.Close();
    }

    public Task ShowQueuedAsync(AppController controller, Func<PetWindow?> mainWindow)
    {
        if (_showing || _visits.Count == 0) return Task.CompletedTask;
        return ShowQueuedInternalAsync(controller, mainWindow);
    }

    private async Task ShowQueuedInternalAsync(AppController controller, Func<PetWindow?> mainWindow)
    {
        if (_showing) return;
        _showing = true;
        try
        {
            while (_visits.TryDequeue(out var visit))
            {
                if (mainWindow() is not { IsVisible: true } main)
                {
                    _visits.Enqueue(visit);
                    break;
                }
                var visitor = new PetWindow(controller, true);
                _visitorWindow = visitor;
                visitor.RefreshAppearance(visit.FilePath);
                visitor.EnterScriptedMode();
                var area = main.GetWorkingArea();
                var left = main.Left - visitor.Width - 12;
                if (left < area.Left) left = main.Left + main.Width + 12;
                left = Math.Clamp(left, area.Left, Math.Max(area.Left, area.Right - visitor.Width));
                var top = Math.Clamp(main.Top, area.Top, Math.Max(area.Top, area.Bottom - visitor.Height));
                visitor.Place(new Point(left, top));
                visitor.Show();
                visitor.ShowReaction($"{visit.SenderName} 来串门啦");
                try { await Task.Delay(TimeSpan.FromSeconds(10)); }
                finally
                {
                    if (visitor.IsLoaded) visitor.Close();
                    if (ReferenceEquals(_visitorWindow, visitor)) _visitorWindow = null;
                    try { File.Delete(visit.FilePath); } catch { }
                }
            }
        }
        finally
        {
            _showing = false;
        }
    }
}
