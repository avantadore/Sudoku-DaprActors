namespace SudokuDaprActors.Web;

/// <summary>
/// A move's cascade shown as it runs: each step read changes the grid on the page as the move's result will, so the
/// page can show the grid step by step before the move completes.
/// </summary>
public static class LiveCascade
{
    public static Grid Apply(Grid grid, GridStep step) => step.Kind switch
    {
        StepKind.Placement => Change(grid, step, cell => cell with { Digit = step.Digit, Source = step.Source, Candidates = [step.Digit!.Value] }),
        StepKind.Elimination => Change(grid, step, cell => cell with { Candidates = [.. cell.Candidates.Where(candidate => candidate != step.Digit)] }),
        StepKind.NoCandidateForCell or StepKind.NoCellForDigit => grid with { State = GameState.Contradicted },
        _ => throw new ArgumentOutOfRangeException(nameof(step), step, null),
    };

    private static Grid Change(Grid grid, GridStep step, Func<GridCell, GridCell> change) =>
        grid with { Cells = [.. grid.Cells.Select(cell => (cell.Row, cell.Column) == step.Cell ? change(cell) : cell)] };
}
