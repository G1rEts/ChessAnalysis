using System.Drawing;

namespace ChessAnalysis.Core.Analysis;

public enum MoveClassification
{
    Brilliant,   // 💎 Жертва с решающим преимуществом
    Great,       // 🎯 Единственный спасающий или победный ход
    Best,        // ⭐ Лучший ход первой линии Stockfish
    Excellent,   // 👍 Великолепный ход с минимальной потерей
    Good,        // ✓ Хороший, крепкий ход
    Book,        // 📖 Дебютный теоретический ход
    Forced,      // ➔ Вынужденный ход (единственный возможный легальный ход)
    Inaccuracy,  // ❓❗ Неточность (потеря 6-15% вероятности)
    Mistake,     // ❓ Ошибка (потеря 15-30% вероятности)
    Blunder,     // ❓❓ Зевок (потеря >30% вероятности)
    MissedWin    // ✖ Упущенный выигрыш
}

public static class MoveClassificationExtensions
{
    public static string GetTitle(this MoveClassification c) => c switch
    {
        MoveClassification.Brilliant => "Блестящий",
        MoveClassification.Great => "Замечательный",
        MoveClassification.Best => "Лучший",
        MoveClassification.Excellent => "Отличный",
        MoveClassification.Good => "Хороший",
        MoveClassification.Book => "Теоретический",
        MoveClassification.Forced => "Вынужденный",
        MoveClassification.Inaccuracy => "Неточность",
        MoveClassification.Mistake => "Ошибка",
        MoveClassification.Blunder => "Зевок",
        MoveClassification.MissedWin => "Упущенный шанс",
        _ => "Ход"
    };

    public static string GetSymbol(this MoveClassification c) => c switch
    {
        MoveClassification.Brilliant => "!!",
        MoveClassification.Great => "!",
        MoveClassification.Best => "★",
        MoveClassification.Excellent => "",
        MoveClassification.Good => "",
        MoveClassification.Book => "B",
        MoveClassification.Forced => "□",
        MoveClassification.Inaccuracy => "?!",
        MoveClassification.Mistake => "?",
        MoveClassification.Blunder => "??",
        MoveClassification.MissedWin => "✖",
        _ => ""
    };

    public static Color GetThemeColor(this MoveClassification c) => c switch
    {
        MoveClassification.Brilliant => Color.FromArgb(26, 188, 156),   // Turquoise / Diamond
        MoveClassification.Great => Color.FromArgb(88, 144, 255),      // Vibrant Blue
        MoveClassification.Best => Color.FromArgb(129, 182, 76),       // Chess green
        MoveClassification.Excellent => Color.FromArgb(150, 190, 80),  // Light green
        MoveClassification.Good => Color.FromArgb(120, 160, 100),      // Sage green
        MoveClassification.Book => Color.FromArgb(184, 134, 11),       // Golden/wood
        MoveClassification.Forced => Color.FromArgb(149, 165, 166),     // Slate gray
        MoveClassification.Inaccuracy => Color.FromArgb(241, 196, 15), // Yellow
        MoveClassification.Mistake => Color.FromArgb(230, 126, 34),    // Orange
        MoveClassification.Blunder => Color.FromArgb(231, 76, 60),     // Red
        MoveClassification.MissedWin => Color.FromArgb(219, 68, 85),   // Crimson
        _ => Color.Gray
    };

    public static string GetIconFileName(this MoveClassification c) => c switch
    {
        MoveClassification.Brilliant => "brilliant.png",
        MoveClassification.Great => "great.png",
        MoveClassification.Best => "best.png",
        MoveClassification.Excellent => "excellent.png",
        MoveClassification.Good => "good.png",
        MoveClassification.Book => "book.png",
        MoveClassification.Forced => "forced.png",
        MoveClassification.Inaccuracy => "inaccuracy.png",
        MoveClassification.Mistake => "mistake.png",
        MoveClassification.Blunder => "blunder.png",
        MoveClassification.MissedWin => "missed_win.png",
        _ => "move.png"
    };
}
