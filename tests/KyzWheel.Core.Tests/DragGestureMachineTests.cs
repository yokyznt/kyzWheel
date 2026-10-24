using KyzWheel.Core.Input;

namespace KyzWheel.Core.Tests;

public sealed class DragGestureMachineTests
{
    private readonly DragGestureMachine _m = new(4);

    private GestureOutput F(GestureInput i, int x = 0, int y = 0, bool shiftAtStart = false, bool shell = true)
        => _m.Feed(i, x, y, shiftAtStart, shell);

    private void StartDrag(bool shiftAtStart = false, bool shell = true)
    {
        F(GestureInput.LeftDown, 100, 100, shiftAtStart, shell);
        F(GestureInput.Move, 110, 100, shiftAtStart, shell);
    }

    [Fact]
    public void Down_move_past_threshold_then_shift_opens_converter()
    {
        StartDrag();
        Assert.Equal(GestureOutput.OpenConverter, F(GestureInput.ShiftDown, 110, 100));
        Assert.True(_m.IsOpen);
    }

    [Fact]
    public void Shift_before_threshold_does_nothing()
    {
        F(GestureInput.LeftDown, 100, 100);
        F(GestureInput.Move, 102, 101);
        Assert.Equal(GestureOutput.None, F(GestureInput.ShiftDown));
    }

    [Fact]
    public void Shift_held_before_drag_does_not_open()
    {
        StartDrag(shiftAtStart: true);
        Assert.Equal(GestureOutput.None, F(GestureInput.ShiftDown, shiftAtStart: true));
    }

    [Fact]
    public void Non_shell_source_does_not_open()
    {
        StartDrag(shell: false);
        Assert.Equal(GestureOutput.None, F(GestureInput.ShiftDown, shell: false));
    }

    [Fact]
    public void Shift_without_mouse_down_does_nothing()
        => Assert.Equal(GestureOutput.None, F(GestureInput.ShiftDown));

    [Fact]
    public void Second_shift_tab_and_wheel_change_pages()
    {
        StartDrag();
        F(GestureInput.ShiftDown);
        F(GestureInput.ShiftUp);
        Assert.Equal(GestureOutput.NextPage, F(GestureInput.ShiftDown));
        Assert.Equal(GestureOutput.NextPage, F(GestureInput.TabDown));
        Assert.Equal(GestureOutput.NextPage, F(GestureInput.WheelDown));
        Assert.Equal(GestureOutput.PrevPage, F(GestureInput.WheelUp));
    }

    [Fact]
    public void Alt_shift_opens_tools()
    {
        StartDrag();
        Assert.Equal(GestureOutput.OpenTools, F(GestureInput.AltShiftDown));
    }

    [Fact]
    public void Tab_while_closed_does_nothing()
    {
        StartDrag();
        Assert.Equal(GestureOutput.None, F(GestureInput.TabDown));
    }

    [Fact]
    public void Esc_cancels_and_leftup_closes()
    {
        StartDrag();
        F(GestureInput.ShiftDown);
        Assert.Equal(GestureOutput.Cancel, F(GestureInput.EscDown));
        Assert.False(_m.IsOpen);
        Assert.Equal(GestureOutput.None, F(GestureInput.ShiftDown));
        Assert.Equal(GestureOutput.None, F(GestureInput.LeftUp));

        StartDrag();
        F(GestureInput.ShiftDown);
        Assert.Equal(GestureOutput.Close, F(GestureInput.LeftUp));
        Assert.False(_m.IsOpen);
    }
}
