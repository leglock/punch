namespace Punch.CLI;

// App-written UI state, persisted to ~/.punch/state.json between runs. Unlike
// settings.json this file is owned by the app and rewritten freely.
internal sealed class PunchState
{
    // The name of the color theme last selected with F5/Ctrl+R.
    public string? Theme { get; set; }
}
