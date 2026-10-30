using ZhuoDazi.Services;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

Require(ConversationPresentationTiming.DefaultSpeech == TimeSpan.FromSeconds(5),
    "Default reactions must retain their five-second duration.");

var shortTheater = ConversationPresentationTiming.TheaterLine("好呀");
var longerTheater = ConversationPresentationTiming.TheaterLine("等等，这个剧情是不是偷偷改过了？");
var maximumTheater = ConversationPresentationTiming.TheaterLine(new string('长', 200));
Require(shortTheater == TimeSpan.FromSeconds(5),
    "A short theater line must remain readable for five seconds.");
Require(longerTheater > shortTheater && maximumTheater == TimeSpan.FromSeconds(12),
    "Theater timing must grow with text length and remain bounded.");

var shortVisit = ConversationPresentationTiming.HallVisit("小林：你好");
var longerVisit = ConversationPresentationTiming.HallVisit("小林：今天辛苦啦，记得喝水，也别忘了让眼睛休息一下。");
var maximumVisit = ConversationPresentationTiming.HallVisit(new string('久', 200));
Require(shortVisit == TimeSpan.FromSeconds(12),
    "A hall visit must stay visible for at least twelve seconds.");
Require(longerVisit > shortVisit && maximumVisit == TimeSpan.FromSeconds(20),
    "Hall timing must grow with message length and remain bounded.");

var emojiVisit = ConversationPresentationTiming.HallVisit("甲：1234567😀");
Require(emojiVisit == TimeSpan.FromSeconds(12),
    "Supplementary Unicode characters must be counted as one displayed character.");

Console.WriteLine("Conversation presentation checks passed.");
