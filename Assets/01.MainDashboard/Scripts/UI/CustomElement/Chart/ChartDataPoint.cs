using System;
using UnityEngine;
using TMPro;

namespace BridgeSenseDT.UI.Charts
{
    [Serializable]
    public class ChartDataPoint
    {
        public string label = "A";
        public float value = 0f;
        public Color color = Color.white;
    }

    /// <summary>인스펙터에서 고를 수 있는 그래프 형태. 새 타입은 UIChart.RegisterRenderer()로 확장.</summary>
    public enum ChartType { Bar, Donut }

    public enum ValueDisplayFormat { None, Value, Percentage, ValueAndPercentage, Custom }

    [Serializable]
    public class ChartStyle
    {
        [Header("공통")]
        public TMP_FontAsset font;
        public int labelFontSize = 13;
        public int valueFontSize = 13;
        public Color labelColor = new Color(0.769f, 0.718f, 0.639f, 1f);
        public Color valueColor = new Color(0.969f, 0.945f, 0.902f, 1f);
        public ValueDisplayFormat displayFormat = ValueDisplayFormat.Value;
        [Tooltip("ValueDisplayFormat.Value일 때 사용하는 숫자 포맷 (예: 0, 0.0)")]
        public string valueNumberFormat = "0";
        [Tooltip("ValueDisplayFormat.Custom일 때 사용. {0}=값, {1}=퍼센트, {2}=라벨")]
        public string customFormat = "{0:0}개 ({1:F1}%)";

        [Header("막대 그래프 전용")]
        public float barThickness = 28f;
        public float barRowSpacing = 10f;
        public float labelColumnWidth = 90f;
        public Color barTrackColor = new Color(0f, 0f, 0f, 0.15f);
        [Tooltip("0이면 데이터 중 최댓값을 기준으로 자동 스케일")]
        public float maxValueOverride = 0f;

        [Header("도넛 그래프 전용")]
        [Range(0f, 0.9f), Tooltip("0 = 꽉 찬 파이차트, 높을수록 얇은 링")]
        public float donutThickness = 0.35f;
        public float donutStartAngleDeg = 0f;
        public bool donutClockwise = true;
        [Tooltip("도넛 구멍 색상 — 이 그래프가 올라갈 패널의 배경색과 맞춰야 자연스럽다")]
        public Color donutHoleColor = Color.white;
        [Range(0.9f, 1.6f)]
        public float donutLabelRadiusRatio = 1.22f;
    }
}