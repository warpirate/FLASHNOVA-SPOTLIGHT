namespace FlashSpot.App;

public sealed class SpotlightUiPreferences
{
    public bool ShowKeyHintsInFooter { get; set; }
    public bool ShowIndexStatusInHeader { get; set; }
    public bool ShowFilterChips { get; set; }

    public SpotlightUiPreferences Clone() => new()
    {
        ShowKeyHintsInFooter = ShowKeyHintsInFooter,
        ShowIndexStatusInHeader = ShowIndexStatusInHeader,
        ShowFilterChips = ShowFilterChips
    };
}
