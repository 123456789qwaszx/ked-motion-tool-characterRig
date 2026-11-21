namespace Mt.Authoring.Graph;

/// <summary>
/// 부드럽게 끌기(마야의 Soft Selection) — 고른 키를 끌 때 곁의 키가 거리에 따라 덜 따라온다.
/// 프레임마다 찍힌 키(구운 모션)의 한 대목을 통째로 올리고 내릴 때 쓴다. 순수 산수.
/// </summary>
public static class SoftDrag
{
    /// <summary>기본 반경(초) — 5프레임.</summary>
    public static readonly double DefaultRadius = 5.0 / TimeSnap.FramesPerSecond;

    /// <summary>
    /// 고른 키에서 그만큼(초) 떨어진 키가 따라오는 몫 — 0초면 1, 반경에 닿으면 0. 사이는 코사인으로 부드럽게 준다.
    /// </summary>
    public static double Weight(double distance, double radius)
    {
        if (radius <= 0 || distance >= radius)
        {
            return 0;
        }

        return distance <= 0 ? 1 : 0.5 * (1 + Math.Cos(Math.PI * distance / radius));
    }
}
