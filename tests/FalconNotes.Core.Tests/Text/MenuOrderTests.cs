using FalconNotes.Core.Text;

namespace FalconNotes.Core.Tests.Text;

// Ported from web/lib/menu.test.ts.
public class MenuOrderTests
{
    [Fact]
    public void Is_the_default_order_when_nothing_was_chosen() => Assert.Equal(MenuOrder.Items, MenuOrder.Read(""));

    [Fact]
    public void Puts_the_chosen_items_first_and_the_rest_after_them_in_their_usual_order() =>
        Assert.Equal(["help", "todo", "home", "quick", "habits", "tags", "archive", "settings"], MenuOrder.Read("help,todo"));

    [Fact]
    public void Ignores_unknown_and_repeated_names() =>
        Assert.Equal(["todo", "home", "quick", "habits", "tags", "archive", "settings", "help"], MenuOrder.Read("inbox,todo,todo,,home"));

    [Fact]
    public void Saves_the_usual_order_as_nothing()
    {
        Assert.Equal("", MenuOrder.Save(MenuOrder.Items));
        Assert.Equal("todo,home,quick,habits,tags,archive,settings,help", MenuOrder.Save(MenuOrder.Move(MenuOrder.Items, 1, 0)));
    }

    [Fact]
    public void Moves_one_item_and_leaves_the_list_alone_for_moves_out_of_range()
    {
        Assert.Equal(["b", "c", "a"], MenuOrder.Move(["a", "b", "c"], 0, 2));
        Assert.Equal(["c", "a", "b"], MenuOrder.Move(["a", "b", "c"], 2, 0));
        IReadOnlyList<string> items = ["a", "b"];
        Assert.Same(items, MenuOrder.Move(items, 0, 2));
        Assert.Same(items, MenuOrder.Move(items, -1, 0));
    }
}
