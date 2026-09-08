using System.Windows;
using System.Windows.Controls;

namespace ZhuoDazi.Controls;

public static class InteractionCardLayout
{
    public static double MeasureHeight(
        Border card,
        ScrollViewer messageScroll,
        TextBlock message,
        double windowWidth,
        double maximumHeight,
        double minimumMessageHeight)
    {
        card.Width = Math.Min(card.MaxWidth, Math.Max(1, windowWidth - card.Margin.Left - card.Margin.Right));
        var messageWidth = Math.Max(1, card.Width
            - card.Padding.Left - card.Padding.Right
            - card.BorderThickness.Left - card.BorderThickness.Right
            - messageScroll.Margin.Left - messageScroll.Margin.Right
            - messageScroll.Padding.Left - messageScroll.Padding.Right
            - messageScroll.BorderThickness.Left - messageScroll.BorderThickness.Right);
        message.Width = messageWidth;
        messageScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        messageScroll.ClearValue(FrameworkElement.MaxHeightProperty);
        message.Measure(new System.Windows.Size(messageWidth, double.PositiveInfinity));
        messageScroll.Height = message.DesiredSize.Height
            + messageScroll.Padding.Top + messageScroll.Padding.Bottom
            + messageScroll.BorderThickness.Top + messageScroll.BorderThickness.Bottom;
        card.Measure(new System.Windows.Size(windowWidth, double.PositiveInfinity));
        if (card.DesiredSize.Height > maximumHeight)
        {
            messageScroll.Height = Math.Max(minimumMessageHeight,
                messageScroll.Height - (card.DesiredSize.Height - maximumHeight));
            messageScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;
            message.Width = Math.Max(1, messageWidth - SystemParameters.VerticalScrollBarWidth);
            card.Measure(new System.Windows.Size(windowWidth, double.PositiveInfinity));
        }
        return Math.Ceiling(card.DesiredSize.Height);
    }
}
