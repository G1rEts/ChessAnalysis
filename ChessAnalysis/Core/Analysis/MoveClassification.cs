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
        MoveClassification.Good => "Неплохой",
        MoveClassification.Book => "Теоретический",
        MoveClassification.Forced => "Вынужденный",
        MoveClassification.Inaccuracy => "Неточность",
        MoveClassification.Mistake => "Ошибка",
        MoveClassification.Blunder => "Зевок",
        MoveClassification.MissedWin => "Упущенный шанс",
        _ => "Ход"
    };

    public static string GetTag(this MoveClassification c) => c switch
    {
        MoveClassification.Brilliant => "G1",
        MoveClassification.Great => "G2",
        MoveClassification.Book => "G3",
        MoveClassification.Best => "N1",
        MoveClassification.Excellent => "N2",
        MoveClassification.Good => "N3",
        MoveClassification.Inaccuracy => "B1",
        MoveClassification.Mistake => "B2",
        MoveClassification.Blunder => "B3",
        MoveClassification.MissedWin => "B4",
        MoveClassification.Forced => "M1",
        _ => "N1"
    };

    public static int GetNumericCode(this MoveClassification c) => c switch
    {
        MoveClassification.Brilliant => 1,
        MoveClassification.Great => 2,
        MoveClassification.Book => 3,
        MoveClassification.Best => 4,
        MoveClassification.Excellent => 5,
        MoveClassification.Good => 6,
        MoveClassification.Inaccuracy => 7,
        MoveClassification.Mistake => 8,
        MoveClassification.Blunder => 9,
        MoveClassification.MissedWin => 10,
        MoveClassification.Forced => 11,
        _ => 4
    };

    public static MoveClassification? FromTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        return tag.Trim().ToUpperInvariant() switch
        {
            "G1" => MoveClassification.Brilliant,
            "G2" => MoveClassification.Great,
            "G3" => MoveClassification.Book,
            "N1" => MoveClassification.Best,
            "N2" => MoveClassification.Excellent,
            "N3" => MoveClassification.Good,
            "B1" => MoveClassification.Inaccuracy,
            "B2" => MoveClassification.Mistake,
            "B3" => MoveClassification.Blunder,
            "B4" => MoveClassification.MissedWin,
            "M1" => MoveClassification.Forced,
            _ => null
        };
    }

    public static MoveClassification FromNumericCode(int code) => code switch
    {
        1 => MoveClassification.Brilliant,
        2 => MoveClassification.Great,
        3 => MoveClassification.Book,
        4 => MoveClassification.Best,
        5 => MoveClassification.Excellent,
        6 => MoveClassification.Good,
        7 => MoveClassification.Inaccuracy,
        8 => MoveClassification.Mistake,
        9 => MoveClassification.Blunder,
        10 => MoveClassification.MissedWin,
        11 => MoveClassification.Forced,
        _ => MoveClassification.Best
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

    public static System.Drawing.Color GetThemeColor(this MoveClassification c) => c switch
    {
        MoveClassification.Brilliant => System.Drawing.Color.FromArgb(26, 188, 156),   // Turquoise / Diamond
        MoveClassification.Great => System.Drawing.Color.FromArgb(88, 144, 255),      // Vibrant Blue
        MoveClassification.Best => System.Drawing.Color.FromArgb(129, 182, 76),       // Chess green
        MoveClassification.Excellent => System.Drawing.Color.FromArgb(150, 190, 80),  // Light green
        MoveClassification.Good => System.Drawing.Color.FromArgb(120, 160, 100),      // Sage green
        MoveClassification.Book => System.Drawing.Color.FromArgb(184, 134, 11),       // Golden/wood
        MoveClassification.Forced => System.Drawing.Color.FromArgb(149, 165, 166),     // Slate gray
        MoveClassification.Inaccuracy => System.Drawing.Color.FromArgb(241, 196, 15), // Yellow
        MoveClassification.Mistake => System.Drawing.Color.FromArgb(230, 126, 34),    // Orange
        MoveClassification.Blunder => System.Drawing.Color.FromArgb(231, 76, 60),     // Red
        MoveClassification.MissedWin => System.Drawing.Color.FromArgb(219, 68, 85),   // Crimson
        _ => System.Drawing.Color.Gray
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
