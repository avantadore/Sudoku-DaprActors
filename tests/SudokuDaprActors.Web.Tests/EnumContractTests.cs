namespace SudokuDaprActors.Web.Tests;

/// <summary>
/// The API sends Core's enums by name, and the Web reads them into its own copies.
/// Renaming, adding or removing a member on one side only must fail here, not in the browser.
/// </summary>
public class EnumContractTests
{
    [Fact]
    public void The_web_knows_every_game_state_the_api_can_send() =>
        Assert.Equal(Enum.GetNames<Core.GameState>(), Enum.GetNames<GameState>());

    [Fact]
    public void The_web_knows_every_placement_source_the_api_can_send() =>
        Assert.Equal(Enum.GetNames<Core.PlacementSource>(), Enum.GetNames<PlacementSource>());

    [Fact]
    public void The_web_knows_every_unit_kind_the_api_can_send() =>
        Assert.Equal(Enum.GetNames<Core.UnitKind>(), Enum.GetNames<UnitKind>());

    /// <summary>The API names each step's kind after Core's step record: a placement, an elimination, or a contradiction.</summary>
    [Fact]
    public void The_web_knows_every_kind_of_step_the_api_can_send() =>
        Assert.Equal(
            typeof(Core.Step).Assembly.GetTypes()
                .Where(type => type.IsSubclassOf(typeof(Core.Step)) && !type.IsAbstract)
                .Select(type => type.Name)
                .Order(StringComparer.Ordinal),
            Enum.GetNames<StepKind>().Order(StringComparer.Ordinal));
}
