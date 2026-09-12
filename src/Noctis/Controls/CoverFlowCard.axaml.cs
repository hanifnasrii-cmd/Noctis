using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace Noctis.Controls;

/// <summary>
/// One Cover Flow carousel card: a rounded glass frame holding a square cover with a
/// frosted caption strip (title + artist) inside the frame, the way iOS cards read.
/// Every slot in the carousel is one of these — the centre card adds an
/// <see cref="Overlay"/> (animated cover, favourite heart), the explicit badge and an
/// <see cref="ArtistCommand"/> that turns the artist caption into a link; side cards
/// get <see cref="Dim"/> and a 3D tilt from the host's RenderTransform.
/// </summary>
public partial class CoverFlowCard : UserControl
{
    public static readonly StyledProperty<string?> ArtworkPathProperty =
        AvaloniaProperty.Register<CoverFlowCard, string?>(nameof(ArtworkPath));

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<CoverFlowCard, string?>(nameof(Title));

    public static readonly StyledProperty<string?> ArtistProperty =
        AvaloniaProperty.Register<CoverFlowCard, string?>(nameof(Artist));

    public static readonly StyledProperty<bool> IsExplicitProperty =
        AvaloniaProperty.Register<CoverFlowCard, bool>(nameof(IsExplicit));

    /// <summary>0..1 black wash over the whole card (depth cue for cards away from the centre).</summary>
    public static readonly StyledProperty<double> DimProperty =
        AvaloniaProperty.Register<CoverFlowCard, double>(nameof(Dim));

    /// <summary>Side of the square artwork; the card is this wide plus the frame edge.</summary>
    public static readonly StyledProperty<double> ArtworkSizeProperty =
        AvaloniaProperty.Register<CoverFlowCard, double>(nameof(ArtworkSize), 300);

    public static readonly StyledProperty<CornerRadius> CardCornerRadiusProperty =
        AvaloniaProperty.Register<CoverFlowCard, CornerRadius>(nameof(CardCornerRadius), new CornerRadius(18));

    public static readonly StyledProperty<int> DecodeWidthProperty =
        AvaloniaProperty.Register<CoverFlowCard, int>(nameof(DecodeWidth), 512);

    /// <summary>Extra content painted over the artwork (centre card: animated cover, heart).</summary>
    public static readonly StyledProperty<object?> OverlayProperty =
        AvaloniaProperty.Register<CoverFlowCard, object?>(nameof(Overlay));

    /// <summary>When set the artist caption becomes a link that runs this command.</summary>
    public static readonly StyledProperty<ICommand?> ArtistCommandProperty =
        AvaloniaProperty.Register<CoverFlowCard, ICommand?>(nameof(ArtistCommand));

    /// <summary>Caption text width cap: the artwork width minus caption padding and badge room.</summary>
    public static readonly DirectProperty<CoverFlowCard, double> TitleMaxWidthProperty =
        AvaloniaProperty.RegisterDirect<CoverFlowCard, double>(nameof(TitleMaxWidth), o => o.TitleMaxWidth);

    public static readonly DirectProperty<CoverFlowCard, double> PlaceholderFontSizeProperty =
        AvaloniaProperty.RegisterDirect<CoverFlowCard, double>(nameof(PlaceholderFontSize), o => o.PlaceholderFontSize);

    private double _titleMaxWidth = 300 - 28 - 30;
    private double _placeholderFontSize = 300 * 0.23;

    static CoverFlowCard()
    {
        ArtworkSizeProperty.Changed.AddClassHandler<CoverFlowCard>((c, _) => c.OnArtworkSizeChanged());
    }

    public CoverFlowCard()
    {
        InitializeComponent();
    }

    public string? ArtworkPath { get => GetValue(ArtworkPathProperty); set => SetValue(ArtworkPathProperty, value); }
    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Artist { get => GetValue(ArtistProperty); set => SetValue(ArtistProperty, value); }
    public bool IsExplicit { get => GetValue(IsExplicitProperty); set => SetValue(IsExplicitProperty, value); }
    public double Dim { get => GetValue(DimProperty); set => SetValue(DimProperty, value); }
    public double ArtworkSize { get => GetValue(ArtworkSizeProperty); set => SetValue(ArtworkSizeProperty, value); }
    public CornerRadius CardCornerRadius { get => GetValue(CardCornerRadiusProperty); set => SetValue(CardCornerRadiusProperty, value); }
    public int DecodeWidth { get => GetValue(DecodeWidthProperty); set => SetValue(DecodeWidthProperty, value); }
    public object? Overlay { get => GetValue(OverlayProperty); set => SetValue(OverlayProperty, value); }
    public ICommand? ArtistCommand { get => GetValue(ArtistCommandProperty); set => SetValue(ArtistCommandProperty, value); }

    public double TitleMaxWidth
    {
        get => _titleMaxWidth;
        private set => SetAndRaise(TitleMaxWidthProperty, ref _titleMaxWidth, value);
    }

    public double PlaceholderFontSize
    {
        get => _placeholderFontSize;
        private set => SetAndRaise(PlaceholderFontSizeProperty, ref _placeholderFontSize, value);
    }

    private void OnArtworkSizeChanged()
    {
        var size = ArtworkSize;
        // 14px caption padding each side, plus room for the explicit badge beside the title.
        TitleMaxWidth = System.Math.Max(40, size - 28 - 30);
        PlaceholderFontSize = System.Math.Max(24, size * 0.23);
    }
}
