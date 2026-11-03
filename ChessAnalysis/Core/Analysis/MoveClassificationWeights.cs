namespace ChessAnalysis.Core.Analysis;

/// <summary>
/// Настраиваемые веса и пороговые значения для классификации ходов.
/// Вынесены в отдельный класс, чтобы их можно было легко настраивать и подбирать (grid search / тест).
/// </summary>
public class MoveClassificationWeights
{
    // ==========================================
    // 1. Пороги потери вероятности победы (CAPS Delta P) и сотых пешки (CP Loss)
    // ==========================================
    /// <summary>Максимальная потеря Delta P для Отличного хода (N2)</summary>
    public double ExcellentMaxDeltaP { get; set; } = 10.5;

    /// <summary>Максимальная потеря CP для Отличного хода (N2)</summary>
    public double ExcellentMaxCpLoss { get; set; } = 140.0;

    /// <summary>Максимальная потеря Delta P для Неплохого хода (N3)</summary>
    public double GoodMaxDeltaP { get; set; } = 13.0;

    /// <summary>Максимальная потеря CP для Неплохого хода (N3)</summary>
    public double GoodMaxCpLoss { get; set; } = 240.0;

    /// <summary>Максимальная потеря Delta P для Неточности (B1)</summary>
    public double InaccuracyMaxDeltaP { get; set; } = 20.0;

    /// <summary>Максимальная потеря Delta P для Ошибки (B2)</summary>
    public double MistakeMaxDeltaP { get; set; } = 32.0;

    /// <summary>Порог CP потери, выше которого ход не может быть Неплохим/Отличным и сразу становится Ошибкой</summary>
    public double MistakeMinCpLoss { get; set; } = 240.0;

    /// <summary>Порог CP потери, выше которого не-топ ход сразу становится Зевком (B3)</summary>
    public double BlunderMinCpLoss { get; set; } = 300.0;

    // ==========================================
    // 2. Многолинейность Stockfish (MultiPV)
    // ==========================================
    /// <summary>Количество линий компьютера, удерживаемых в памяти</summary>
    public int MultiPvLinesCount { get; set; } = 5;

    /// <summary>
    /// Разница в CP между линиями, при которой ходы считаются равнозначными.
    /// Если 1-я и 2..4-я линии дают практически равный перевес (разница &lt;= порога),
    /// ход по любой из этих линий (даже 1-й) оценивается как Отличный (N2).
    /// </summary>
    public int EqualTopLinesCpThreshold { get; set; } = 30;

    /// <summary>
    /// Допустимое отставание линии от 1-й (в CP), при котором ход признается Отличным (N2),
    /// если он сохраняет высокий перевес.
    /// </summary>
    public int MultiPvExcellentCpMargin { get; set; } = 140;

    /// <summary>
    /// Допустимое отставание линии от 1-й (в CP), при котором ход признается Неплохим (N3).
    /// </summary>
    public int MultiPvGoodCpMargin { get; set; } = 160;

    // ==========================================
    // 3. Замечательный ход (G2 / !)
    // ==========================================
    /// <summary>Минимальный отрыв 1-й линии от 2-й (в CP) для Замечательного хода (единственный спасающий ход)</summary>
    public int GreatMoveMinScoreDiff { get; set; } = 240;

    /// <summary>Максимальная оценка 2-й линии (в CP для игрока), чтобы 1-й ход считался единственным спасающим/наказывающим</summary>
    public int GreatMoveMaxSecondLineCp { get; set; } = 30;

    /// <summary>Максимальная оценка pre-eval игрока для замечательного хода (не присуждать в позиции с подавляющим перевесом +4..+10)</summary>
    public int GreatMoveMaxPreEvalCp { get; set; } = 400;

    // ==========================================
    // 4. Бриллиантовый ход (G1 / !!)
    // ==========================================
    /// <summary>Минимальное количество полуходов (plies), через которое материал должен оставаться пожертвованным (не быстрый размен)</summary>
    public int BrilliantMinPliesWithoutRecapture { get; set; } = 3;

    /// <summary>Максимальная предварительная оценка позиции в CP для игрока (не присуждать при бессмысленном перевесе более +12.0)</summary>
    public int BrilliantMaxPreEvalCp { get; set; } = 1200;

    /// <summary>Минимальная послеходовая оценка позиции в CP для игрока (позиция должна оставаться выигрышной или с матом)</summary>
    public int BrilliantMinPostEvalCp { get; set; } = 100;

    // ==========================================
    // 5. Упущенный шанс / выигрыш (B4 / ✕)
    // ==========================================
    /// <summary>Минимальная вероятность победы до хода для проверки упущенного выигрыша</summary>
    public double MissedWinPreWinProb { get; set; } = 72.0;

    /// <summary>Минимальная потеря Delta P для упущенного шанса</summary>
    public double MissedWinDeltaP { get; set; } = 15.0;

    /// <summary>Максимальная вероятность победы после хода для упущенного шанса</summary>
    public double MissedWinPostWinProb { get; set; } = 75.0;

    // ==========================================
    // 6. Теоретический / Книжный ход (G3)
    // ==========================================
    /// <summary>Максимальный номер хода партии для книги дебютов</summary>
    public int BookMaxMoveNumber { get; set; } = 15;

    /// <summary>Максимальная потеря Delta P для книги</summary>
    public double BookMaxDeltaP { get; set; } = 15.0;

    public static MoveClassificationWeights Default { get; } = new();
}
