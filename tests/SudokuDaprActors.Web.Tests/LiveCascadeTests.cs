namespace SudokuDaprActors.Web.Tests;

/// <summary>Each step read while a move's cascade runs changes the grid on the page as the move's result will.</summary>
public class LiveCascadeTests
{
    private static readonly Guid Id = Guid.NewGuid();

    [Fact]
    public void A_placement_puts_the_digit_and_its_source_in_the_cell_which_keeps_only_that_candidate()
    {
        var grid = LiveCascade.Apply(EmptyGrid(), new GridStep(StepKind.Placement, 2, 3, 8, PlacementSource.Deduction, null, null));

        var cell = CellOf(grid, 2, 3);
        Assert.Equal((8, PlacementSource.Deduction), (cell.Digit, cell.Source));
        Assert.Equal([8], cell.Candidates);
        Assert.Null(CellOf(grid, 2, 4).Digit);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9], CellOf(grid, 2, 4).Candidates);
    }

    [Fact]
    public void An_elimination_removes_the_candidate_from_the_cell()
    {
        var grid = LiveCascade.Apply(EmptyGrid(), new GridStep(StepKind.Elimination, 5, 5, 4, null, null, null));

        Assert.Equal([1, 2, 3, 5, 6, 7, 8, 9], CellOf(grid, 5, 5).Candidates);
    }

    [Theory]
    [InlineData(StepKind.NoCandidateForCell)]
    [InlineData(StepKind.NoCellForDigit)]
    public void A_contradiction_puts_the_game_in_contradiction(StepKind kind)
    {
        var grid = LiveCascade.Apply(EmptyGrid(), new GridStep(kind, 1, 1, null, null, null, null));

        Assert.Equal(GameState.Contradicted, grid.State);
    }

    [Fact]
    public void The_cell_a_step_is_about_is_the_one_it_changed()
    {
        Assert.Equal((2, 3), new GridStep(StepKind.Placement, 2, 3, 8, PlacementSource.Move, null, null).Cell);
        Assert.Null(new GridStep(StepKind.NoCellForDigit, null, null, 9, null, UnitKind.Row, 1).Cell);
    }

    private static Grid EmptyGrid() =>
        new(Id, GameState.InProgress,
            [.. Enumerable.Range(1, 9).SelectMany(row => Enumerable.Range(1, 9).Select(column => new GridCell(row, column, null, null, [1, 2, 3, 4, 5, 6, 7, 8, 9])))],
            0, []);

    private static GridCell CellOf(Grid grid, int row, int column) =>
        grid.Cells.Single(cell => cell.Row == row && cell.Column == column);
}
