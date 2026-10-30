using System.Text.Json;
using ZhuoDazi;
using ZhuoDazi.Models;

var checks = 0;
void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
void Reject(Action action, string message)
{
    try { action(); } catch (InvalidOperationException) { checks++; return; }
    throw new Exception(message);
}
InteractionWordPackDefinition Word(int i, bool enabled = false) => new()
{ Id = $"word-{i}", Enabled = enabled, Words = new() { ["idle"] = [$"台词{i}"] } };
TheaterScriptDefinition Script(int i, bool enabled = false) => new()
{ Id = $"script-{i}", Enabled = enabled, Scenes = Enumerable.Range(0, 3)
    .Select(n => new TheaterSceneDefinition { Main = $"开场{n}", Companion = $"回应{n}" }).ToList() };

var controller = new AppController();
controller.Settings.Normalize();
controller.Settings.InteractionWordPacks = Enumerable.Range(1, 12).Select(i => Word(i)).ToList();
controller.Settings.TheaterScripts = Enumerable.Range(1, 12).Select(i => Script(i)).ToList();
for (var i = 1; i <= 10; i++) controller.SetInteractionWordPackEnabled($"word-{i}", true);
Check(controller.Settings.InteractionWordPacks.Count(item => item.Enabled) == 10, "Ten word packs must combine.");
Reject(() => controller.SetInteractionWordPackEnabled("word-11", true), "Eleventh word pack must be rejected.");
controller.SetInteractionWordPackEnabled("word-1", false);
Reject(() => controller.EnableWordGroup(controller.Settings.InteractionWordPacks[10], controller.Settings.InteractionWordPacks[11]),
    "A group crossing the limit must fail atomically.");
Check(!controller.Settings.InteractionWordPacks[10].Enabled && !controller.Settings.InteractionWordPacks[11].Enabled,
    "An over-limit group must not partially enable.");
controller.SetInteractionWordPackEnabled("word-11", true);
Check(controller.Settings.InteractionWordPacks.Count(item => item.Enabled) == 10, "Freed slots must be reusable.");
for (var i = 1; i <= 10; i++) controller.SetTheaterScriptEnabled($"script-{i}", true);
Reject(() => controller.SetTheaterScriptEnabled("script-11", true), "Theater limit is independent and enforced.");
Check(controller.Settings.InteractionWordPacks.Count(item => item.Enabled) == 10
    && controller.Settings.TheaterScripts.Count(item => item.Enabled) == 10, "Both categories allow ten simultaneously.");
controller.FailSave = true;
try { controller.SetInteractionWordPackEnabled("word-2", false); } catch (IOException) { }
try { controller.SetTheaterScriptEnabled("script-1", false); } catch (IOException) { }
Check(controller.Settings.InteractionWordPacks[1].Enabled && controller.Settings.TheaterScripts[0].Enabled,
    "Failed saves must restore word and script enable states.");
controller.FailSave = false;
controller.SetInteractionWordPackEnabled(null, true);
Check(controller.Settings.InteractionWordPacks.All(item => !item.Enabled), "Built-in choice must clear custom word selections.");
controller.HasPremiumAccess = false;
controller.SetInteractionWordPackEnabled("word-1", true);
Check(!controller.Settings.InteractionWordPacks[0].Enabled, "Unavailable premium access must not enable a word pack.");
controller.HasPremiumAccess = true;
controller.Settings.EnabledMarketProductIds = ["p1", "p2"];
controller.Settings.Libraries = Enumerable.Range(1, 4).Select(i => new LibraryDefinition { Id = $"lib-{i}", Path = $"D:/library-{i}" }).ToList();
controller.SetLibraryEnabled("lib-1", true);
Reject(() => controller.SetLibraryEnabled("lib-2", true), "Three-source limit must include market and local sources.");
controller.DisableMarketProduct("p1");
controller.SetLibraryEnabled("lib-2", true);
Check(controller.EnabledPetSourceCount == 3 && controller.Settings.EnabledLibraryIds!.Count == 2,
    "Disabling a purchased source must release a slot for local directories.");

var migrated = new AppSettings
{
    Size = 60,
    Libraries = controller.Settings.Libraries,
    ActiveLibraryId = "lib-2",
    InteractionWordPacks = Enumerable.Range(1, 12).Select(i => Word(i, i <= 9)).ToList(),
    ActiveInteractionWordPackId = "word-10",
    TheaterScripts = Enumerable.Range(1, 12).Select(i => Script(i, true)).ToList()
};
migrated.Normalize();
Check(migrated.Size == 60 && migrated.EnabledLibraryIds!.SequenceEqual(["lib-2"]), "Legacy library and smaller size must migrate.");
Check(migrated.InteractionWordPacks.Count == 12 && migrated.TheaterScripts.Count == 12 && migrated.Libraries.Count == 4,
    "Normalization must retain all installed content and directories.");
Check(migrated.InteractionWordPacks.Count(item => item.Enabled) == 10 && migrated.InteractionWordPacks[9].Enabled,
    "Legacy active word pack must join the enabled set.");
Check(migrated.TheaterScripts.Count(item => item.Enabled) == 10, "Normalization must cap enabled scripts, not storage.");
var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(migrated))!;
restored.Normalize();
Check(restored.EnabledLibraryIds!.SequenceEqual(migrated.EnabledLibraryIds!)
    && restored.InteractionWordPacks.Count(item => item.Enabled) == 10, "Selections must survive persistence.");
restored.EnabledLibraryIds = [];
restored.Normalize();
Check(restored.EnabledLibraryIds.Count == 0, "Explicitly disabled sources must not resurrect from legacy active IDs.");
restored.Size = 1; restored.Normalize();
Check(restored.Size == 60, "Pet size must clamp to the new minimum.");

var protectedRoot = Path.Combine(Path.GetTempPath(), "selection-market-checks", "paid-pet");
var protectedFile = Path.Combine(protectedRoot, "a.gif");
var protectedPet = new PetMarketInstallation { ProductId = "paid", Directory = protectedRoot, Files = [protectedFile] };
controller.Settings.PetMarketInstallations = [protectedPet];
controller.Settings.EnabledMarketProductIds = [];
Check(!controller.CheckPetFile(protectedFile), "Binding a market directory as local must not bypass activation.");
controller.Settings.EnabledMarketProductIds.Add("paid");
Check(!controller.CheckPetFile(protectedFile), "An enabled market source still requires a valid grant.");
protectedPet.UseGrant = new();
Check(controller.CheckPetFile(protectedFile), "Authorized selected market files must remain usable.");
Check(!controller.CheckPetFile(Path.Combine(protectedRoot, "unlisted.gif")), "Unmanifested market files must not join a local pool.");
Check(!controller.CheckPetFile(Path.Combine(Path.GetTempPath(), "selection-market-checks", "orphan.gif")),
    "Unregistered market files must remain excluded.");
Console.WriteLine($"Content selection checks passed ({checks}).");
