using System.ComponentModel;
using System.Windows.Media;

namespace FlashSpot.App.Models;

public sealed class SearchListItem : INotifyPropertyChanged
{
    private ImageSource? _iconImage;

    public SearchCategory Category { get; init; }
    public SearchItemKind Kind { get; init; }
    public required string IconText { get; init; }

    public ImageSource? IconImage
    {
        get => _iconImage;
        set
        {
            if (_iconImage != value)
            {
                _iconImage = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconImage)));
            }
        }
    }

    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public required string DateText { get; init; }
    public required string SizeText { get; init; }
    public string? Path { get; init; }
    public string? Value { get; init; }
    public string? ActionUri { get; init; }
    public string? SecondaryActionUri { get; init; }
    public string? InlineValue { get; init; }

    public event PropertyChangedEventHandler? PropertyChanged;
}
