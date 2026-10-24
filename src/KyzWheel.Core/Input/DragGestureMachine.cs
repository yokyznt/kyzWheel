namespace KyzWheel.Core.Input;

public enum GestureInput { LeftDown, Move, LeftUp, ShiftDown, ShiftUp, AltShiftDown, TabDown, EscDown, WheelUp, WheelDown }

public enum GestureOutput { None, OpenConverter, OpenTools, NextPage, PrevPage, Cancel, Close }

public sealed class DragGestureMachine(int dragThresholdPx)
{
    private enum State { Idle, Pressed, Dragging, Open, Cancelled }

    private State _state;
    private int _startX;
    private int _startY;
    private bool _eligible;

    public bool IsOpen => _state == State.Open;

    public GestureOutput Feed(GestureInput input, int x, int y, bool shiftHeldAtStart, bool sourceIsShell)
    {
        switch (input)
        {
            case GestureInput.LeftDown:
                _state = State.Pressed;
                _startX = x;
                _startY = y;
                _eligible = sourceIsShell && !shiftHeldAtStart;
                return GestureOutput.None;
            case GestureInput.Move:
                if (_state == State.Pressed && (Math.Abs(x - _startX) > dragThresholdPx || Math.Abs(y - _startY) > dragThresholdPx))
                    _state = State.Dragging;
                return GestureOutput.None;
            case GestureInput.LeftUp:
                var wasOpen = _state == State.Open;
                _state = State.Idle;
                return wasOpen ? GestureOutput.Close : GestureOutput.None;
            case GestureInput.ShiftDown:
                if (_state == State.Open) return GestureOutput.NextPage;
                return TryOpen(GestureOutput.OpenConverter);
            case GestureInput.AltShiftDown:
                if (_state == State.Open) return GestureOutput.OpenTools;
                return TryOpen(GestureOutput.OpenTools);
            case GestureInput.TabDown:
            case GestureInput.WheelDown:
                return _state == State.Open ? GestureOutput.NextPage : GestureOutput.None;
            case GestureInput.WheelUp:
                return _state == State.Open ? GestureOutput.PrevPage : GestureOutput.None;
            case GestureInput.EscDown:
                if (_state != State.Open) return GestureOutput.None;
                _state = State.Cancelled;
                return GestureOutput.Cancel;
            default:
                return GestureOutput.None;
        }
    }

    private GestureOutput TryOpen(GestureOutput output)
    {
        if (_state != State.Dragging || !_eligible) return GestureOutput.None;
        _state = State.Open;
        return output;
    }
}
