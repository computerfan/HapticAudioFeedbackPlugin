using System.Reflection;
using Loupedeck;
using Loupedeck.HapticAudioFeedback;

var passed = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); }
void Test(string label, Action test) { test(); passed++; Console.WriteLine("PASS " + label); }
object Invoke(object instance, string method, params object[] args) => instance.GetType()
    .GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(instance, args);
void Attach(object action, HapticAudioFeedbackPlugin owner)
{
    // The SDK host normally supplies this reference. Resolve by type, not an obfuscated field name.
    for (var type = action.GetType(); type != null; type = type.BaseType)
    {
        var field = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .SingleOrDefault(f => f.FieldType == typeof(Plugin));
        if (field != null) { field.SetValue(action, owner); return; }
    }
    throw new Exception("SDK plugin binding not found.");
}
IEnumerable<ActionEditorControlBase> Controls(ActionEditorCommand action) => (IEnumerable<ActionEditorControlBase>)Invoke(action.ActionEditor, "GetControls");
ActionEditorState State(ActionEditorCommand action) => new(Controls(action)
    .Select(control => new ActionEditorControlState { Name = control.Name, IsEnabled = true, IsVisible = true }).ToArray());

Test("settings launcher opens only on action activation", () =>
{
    var owner = new HapticAudioFeedbackPlugin(); var action = new ConfigureAudioHaptics(); Attach(action, owner);
    Check(owner.Opens == 0, "Construction opened a browser.");
    Invoke(action, "RunCommand", ""); Check(owner.Opens == 1 && owner.Applies == 0, "Action did not open settings or changed preferences.");
});
Test("profile and preview dropdowns keep assigned selections", () =>
{
    var profile = new SelectAudioProfile(); Attach(profile, new HapticAudioFeedbackPlugin()); var state = State(profile);
    state.GetControlState("Profile").Value = "gentle";
    var items = (ActionEditorListboxItemsRequestedEventArgs)Invoke(profile.ActionEditor, "InvokeListboxItemsRequestedEvent", state, "Profile");
    Check(items.Items.Count == AudioProfiles.All.Count && items.SelectedItemName == null, "Profile selection changed.");
    var preview = new PreviewAudioHaptic(); state = State(preview);
    items = (ActionEditorListboxItemsRequestedEventArgs)Invoke(preview.ActionEditor, "InvokeListboxItemsRequestedEvent", state, "Waveform");
    Check(items.Items.Count == 6 && items.SelectedItemName == "subtle_collision", "Preview choices missing.");
});
Test("assigned profile and preview actions route through the controller", () =>
{
    var owner = new HapticAudioFeedbackPlugin(); var profile = new SelectAudioProfile(); Attach(profile, owner);
    Check((bool)Invoke(profile, "RunCommand", new ActionEditorActionParameters(new Dictionary<string, string> { ["Profile"] = "gentle" })), "Profile execution failed.");
    Check(owner.CurrentSettings.Sensitivity == 35 && !owner.CurrentSettings.Enabled, "Profile resumed paused haptics.");
    var preview = new PreviewAudioHaptic(); Attach(preview, owner);
    Check((bool)Invoke(preview, "RunCommand", new ActionEditorActionParameters(new Dictionary<string, string> { ["Waveform"] = "sharp_collision" })), "Preview execution failed.");
    Check(owner.Previews.Last() == "sharp_collision", "Wrong assigned preview.");
});
Test("every scene profile can be assigned without resuming paused haptics", () =>
{
    var owner = new HapticAudioFeedbackPlugin(); var action = new SelectAudioProfile(); Attach(action, owner);
    foreach (var profile in AudioProfiles.All)
    {
        Check((bool)Invoke(action, "RunCommand", new ActionEditorActionParameters(new Dictionary<string, string> { ["Profile"] = profile.Id })), "Profile action failed: " + profile.Id);
        Check(!owner.CurrentSettings.Enabled, "Profile resumed haptics: " + profile.Id);
        owner.CurrentSettings.Validate();
    }
});
Test("custom profiles appear in action choices and execute with stable IDs", () =>
{
    var owner = new HapticAudioFeedbackPlugin();
    var custom = owner.Profiles.Save(new() { Operation = "save", Name = "Custom action", Settings = new() { Sensitivity = 72 }, ExpectedRevision = 0 });
    var action = new SelectAudioProfile(); Attach(action, owner); var state = State(action);
    state.GetControlState("Profile").Value = custom.SelectedId;
    var items = (ActionEditorListboxItemsRequestedEventArgs)Invoke(action.ActionEditor, "InvokeListboxItemsRequestedEvent", state, "Profile");
    Check(items.Items.Count == AudioProfiles.All.Count + 1 && items.SelectedItemName == null, "Custom action selection was lost.");
    Check((bool)Invoke(action, "RunCommand", new ActionEditorActionParameters(new Dictionary<string, string> { ["Profile"] = custom.SelectedId })), "Custom profile action failed.");
    Check(owner.CurrentSettings.Sensitivity == 72 && !owner.CurrentSettings.Enabled, "Custom action did not preserve paused state.");
});
Test("assigned toggle action pauses and resumes through the controller", () =>
{
    var owner = new HapticAudioFeedbackPlugin(); var toggle = new ToggleAudioHaptics(); Attach(toggle, owner);
    Invoke(toggle, "RunCommand", ""); Check(owner.CurrentSettings.Enabled, "Toggle did not resume.");
    Invoke(toggle, "RunCommand", ""); Check(!owner.CurrentSettings.Enabled && owner.Applies == 2, "Toggle did not pause.");
});
Console.WriteLine($"{passed} SDK action checks passed. Controller calls are simulated; no audio or haptics were used.");