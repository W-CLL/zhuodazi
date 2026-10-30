using ZhuoDazi.Services;

namespace ZhuoDazi;

internal static class ConversationPresentation
{
    internal static async Task ShowTheaterLineAsync(
        PetWindow speaker,
        PetWindow listener,
        string line,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        speaker.HideSpeech();
        listener.HideSpeech();
        var duration = ConversationPresentationTiming.TheaterLine(line);
        speaker.ShowReaction(line, duration);
        try
        {
            await Task.Delay(duration, cancellationToken);
        }
        finally
        {
            if (speaker.IsLoaded) speaker.HideSpeech();
        }
    }
}
