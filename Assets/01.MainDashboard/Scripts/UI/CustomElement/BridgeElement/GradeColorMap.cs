using UnityEngine;

namespace BridgeSenseDT.UI
{
    /// <summary>
    /// 안전등급(A~E) → 색상/라벨 매핑을 한 곳에서만 관리한다.
    /// UITable, UIElevationDiagram, 3D 부재 머티리얼(_GradeColor) 등 여러 곳에서 재사용.
    /// 값은 BridgeSense_DT_UI_스타일가이드.md의 등급 컬러 표와 동일하게 맞춰둠.
    /// </summary>
    public static class GradeColorMap
    {
        public static readonly Color alertGood = new Color(0.604f, 0.804f, 0.361f, 1f); // #9ACD5C 우수(A)
        public static readonly Color alertSemiGood = new Color(0.0f, 0.518f, 1.0f, 1f); // #0084FF 양호(B)
        public static readonly Color alert_Normal = new Color(1.0f, 0.651f, 0.0f, 1f); // #FFA600 보통(C)
        public static readonly Color alert_Insufficient  = new Color(0.996f, 0.380f, 0.129f, 1f); // #FE6121 미흡
        public static readonly Color alert_Danger = new Color(0.753f, 0.098f, 0.106f, 1f); // #C0191B 불량(E)
        public static readonly Color alert_Unknown = new Color(0.55f, 0.55f, 0.55f, 1f); // #8C8C8C 알 수 없음

        public static Color GetColor(string grade)
        {
            switch (grade)
            {
                case "A":
                    return alertGood;
                case "B":
                    return alertSemiGood;
                case "C":
                    return alert_Normal;
                case "D":
                    return alert_Insufficient;
                case "E":
                    return alert_Danger;
                default:
                    return alert_Unknown;
            }
        }

        public static string GetLabel(string grade)
        {
            switch (grade)
            {
                case "A": return "우수";
                case "B": return "양호";
                case "C": return "보통";
                case "D": return "미흡";
                case "E": return "불량";
                default: return "-";
            }
        }

        /// <summary>
        /// 등급 색상을 흰색 쪽으로 boost만큼 밝게 보정한 색상을 반환한다 (호버/선택 강조용).
        /// 등급 색상은 alpha가 이미 1(완전 불투명)이라 alpha를 더 올리는 방식으로는
        /// 시각적 차이가 생기지 않으므로, 밝기를 올려 원래 색과 구분되도록 한다.
        /// </summary>
        public static Color Highlight(Color color, float boost)
        {
            float t = Mathf.Clamp01(boost);
            return new Color(
                Mathf.Lerp(color.r, 1f, t),
                Mathf.Lerp(color.g, 1f, t),
                Mathf.Lerp(color.b, 1f, t),
                color.a);
        }

        /// <summary>등급 색상의 채도(Saturation)를 boost만큼 올린 색상을 반환한다 (선택 강조용).</summary>
        public static Color BoostSaturation(Color color, float boost)
        {
            Color.RGBToHSV(color, out float h, out float s, out float v);
            s = Mathf.Clamp01(s + boost);
            Color result = Color.HSVToRGB(h, s, v);
            result.a = color.a;
            return result;
        }
    }
}