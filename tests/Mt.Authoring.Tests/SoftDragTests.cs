using Mt.Authoring.Graph;

namespace Mt.Authoring.Tests;

public class SoftDragTests
{
    [Fact]
    public void 따라오는_몫은_곁에서_1이고_반경에서_0이다()
    {
        Assert.Equal(1, SoftDrag.Weight(0, 0.2));
        Assert.Equal(0.5, SoftDrag.Weight(0.1, 0.2), 6);
        Assert.Equal(0, SoftDrag.Weight(0.2, 0.2));
        Assert.Equal(0, SoftDrag.Weight(0.5, 0.2));

        // 멀어질수록 준다. 반경이 0이면 아무것도 따라오지 않는다.
        Assert.True(SoftDrag.Weight(0.05, 0.2) > SoftDrag.Weight(0.15, 0.2));
        Assert.Equal(0, SoftDrag.Weight(0, 0));
    }
}
