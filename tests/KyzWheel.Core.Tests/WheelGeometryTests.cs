using KyzWheel.Core.Wheel;

namespace KyzWheel.Core.Tests;

public sealed class WheelGeometryTests
{
    [Fact] public void Top_is_slice_zero() => Assert.Equal(0, WheelGeometry.SliceAt(0, -120, 3, 60, 180));
    [Fact] public void Right_lower_is_slice_one_of_three() => Assert.Equal(1, WheelGeometry.SliceAt(100, 60, 3, 60, 180));
    [Fact] public void Left_lower_is_slice_two_of_three() => Assert.Equal(2, WheelGeometry.SliceAt(-100, 60, 3, 60, 180));
    [Fact] public void Hole_returns_minus_one() => Assert.Equal(-1, WheelGeometry.SliceAt(10, 10, 3, 60, 180));
    [Fact] public void Outside_returns_minus_one() => Assert.Equal(-1, WheelGeometry.SliceAt(300, 0, 3, 60, 180));
    [Fact] public void Single_slice_takes_ring() => Assert.Equal(0, WheelGeometry.SliceAt(0, 120, 1, 60, 180));
    [Fact] public void Arc_of_first_of_four() => Assert.Equal((-45d, 90d), WheelGeometry.SliceArc(0, 4));
    [Fact] public void Arc_of_third_of_four() => Assert.Equal((135d, 90d), WheelGeometry.SliceArc(2, 4));

    [Fact]
    public void Clamp_near_right_edge_of_second_monitor()
        => Assert.Equal((3640d, 500d), WheelGeometry.ClampToWorkArea(3830, 500, 200, new Rect(1920, 0, 1920, 1040)));

    [Fact]
    public void Clamp_on_monitor_above_with_negative_coords()
        => Assert.Equal((200d, -880d), WheelGeometry.ClampToWorkArea(50, -1075, 200, new Rect(0, -1080, 1920, 1040)));

    [Fact] public void Locked_dead_zone_returns_minus_one() => Assert.Equal(-1, WheelGeometry.SliceAtAngle(5, -5, 4, 40));
    [Fact] public void Locked_far_outside_still_picks_by_direction() => Assert.Equal(1, WheelGeometry.SliceAtAngle(900, 0, 4, 40));
    [Fact] public void Locked_corner_of_square_picks_slice() => Assert.Equal(1, WheelGeometry.SliceAtAngle(150, 150, 3, 40));
    [Fact] public void Lock_rect_is_square_around_center() => Assert.Equal((800, 300, 1000, 500), WheelGeometry.LockRect(900, 400, 100.7));

    [Fact]
    public void Clamp_keeps_point_inside()
        => Assert.Equal((900d, 500d), WheelGeometry.ClampToWorkArea(900, 500, 200, new Rect(0, 0, 1920, 1040)));
}
