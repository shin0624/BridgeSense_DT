using System.Collections.Generic;
using UnityEngine;

namespace BridgeSenseDT.UI.Charts
{
    /// <summary>
    /// 새 그래프 형태(라인, 레이더 등)를 추가하고 싶으면 이 인터페이스만 구현해서
    /// UIChart.RegisterRenderer(새타입, new 새렌더러())로 등록하면 된다.
    /// 기본 제공: BarChartRenderer, DonutChartRenderer.
    /// </summary>
    public interface IChartRenderer
    {
        /// <summary>container는 매 Rebuild마다 새로 생성되는 빈 RectTransform이므로 자유롭게 자식을 만들면 된다.</summary>
        void Render(RectTransform container, List<ChartDataPoint> data, ChartStyle style);
    }
}