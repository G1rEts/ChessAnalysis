using System.Drawing;

namespace ChessAnalysis.UI.Resources;

public static class ChessTheme
{
    // Бело / темно-серая доска (White / Dark-Gray board requested by user)
    public static readonly Color LightSquare = Color.FromArgb(238, 240, 243);    // Чистый светлый / платиновый
    public static readonly Color DarkSquare = Color.FromArgb(74, 78, 86);         // Глубокий темно-серый сланец
    
    // Подсветка последнего хода
    public static readonly Color MoveHighlightFrom = Color.FromArgb(80, 100, 180, 255); // Полупрозрачный лазурный
    public static readonly Color MoveHighlightTo = Color.FromArgb(120, 100, 180, 255);

    // Подсветка выбранной фигуры и возможных ходов
    public static readonly Color SelectedSquare = Color.FromArgb(140, 240, 200, 80);
    public static readonly Color LegalMoveDot = Color.FromArgb(100, 100, 180, 255);

    // Тема приложения (Chess.com Dark Theme)
    public static readonly Color BackgroundDark = Color.FromArgb(48, 46, 43);       // #302E2B
    public static readonly Color PanelDark = Color.FromArgb(38, 37, 34);             // #262522
    public static readonly Color PanelDarker = Color.FromArgb(33, 32, 29);           // #21201D
    public static readonly Color CardBg = Color.FromArgb(43, 41, 38);                // #2B2926
    public static readonly Color BorderColor = Color.FromArgb(61, 59, 56);           // #3D3B38
    public static readonly Color TextPrimary = Color.FromArgb(255, 255, 255);        // #FFFFFF
    public static readonly Color TextSecondary = Color.FromArgb(158, 156, 153);      // #9E9C99
    public static readonly Color AccentBlue = Color.FromArgb(69, 134, 212);
    public static readonly Color AccentGreen = Color.FromArgb(129, 182, 76);         // #81B64C
    public static readonly Color AccentGreenHover = Color.FromArgb(149, 200, 94);
    public static readonly Color AccentRed = Color.FromArgb(195, 60, 49);
}
