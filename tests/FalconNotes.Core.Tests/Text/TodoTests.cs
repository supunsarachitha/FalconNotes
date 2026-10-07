using FalconNotes.Core.Text;

namespace FalconNotes.Core.Tests.Text;

// Ported from web/lib/todo.test.ts.
public class TodoTests
{
    [Fact]
    public void Reads_the_title_and_the_items()
    {
        var list = Todo.Parse("# Groceries\n\n- [ ] maple syrup\n- [x] oats\n* [X] flour #baking");

        Assert.Equal("Groceries", list.Title);
        Assert.Equal([new("maple syrup", false), new("oats", true), new TodoItem("flour #baking", true)], list.Items);
    }

    [Fact]
    public void Turns_other_lines_into_open_items_and_skips_empty_ones()
    {
        var list = Todo.Parse("# Packing\n\n- tent\nsleeping bag\n\n- [ ]   \n- [x] stove");

        Assert.Equal("Packing", list.Title);
        Assert.Equal([new("tent", false), new("sleeping bag", false), new TodoItem("stove", true)], list.Items);
    }

    [Fact]
    public void Writes_Markdown_that_reads_back_the_same()
    {
        var list = new TodoList("  Weekend  ", [new("hike   the trail", true), new("  ", false), new("call Sam", false)]);

        var markdown = Todo.Serialize(list);

        Assert.Equal("# Weekend\n\n- [x] hike the trail\n- [ ] call Sam", markdown);
        Assert.Equal(markdown, Todo.Serialize(Todo.Parse(markdown)));
        Assert.Equal("# Untitled list", Todo.Serialize(new TodoList("", [])));
    }

    [Fact]
    public void Reads_and_writes_the_items_alone()
    {
        var items = Todo.ParseItems("- [x] tent\r\nsleeping bag\n\n  * [ ] stove  ");

        Assert.Equal([new("tent", true), new("sleeping bag", false), new TodoItem("stove", false)], items);
        Assert.Equal("- [x] tent\n- [ ] sleeping bag\n- [ ] stove", Todo.SerializeItems(items));
        Assert.Equal("", Todo.SerializeItems([]));
    }
}
