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
        public static readonly Color Success = new Color(0.604f, 0.804f, 0.361f, 1f); // #9ACD5C 양호(A·B)
        public static readonly Color Warning = new Color(1.000f, 0.769f, 0.361f, 1f); // #FFC45C 주의(C)
        public static readonly Color Danger  = new Color(0.910f, 0.271f, 0.173f, 1f); // #E8452C 미흡·불량(D·E)
        public static readonly Color Unknown = new Color(0.55f, 0.55f, 0.55f, 1f);

        public static Color GetColor(string grade)
        {
            switch (grade)
            {
                case "A":
                case "B":
                    return Success;
                case "C":
                    return Warning;
                case "D":
                case "E":
                    return Danger;
                default:
                    return Unknown;
            }
        }

        public static string GetLabel(string grade)
        {
            switch (grade)
            {
                case "A": return "우수";
                case "B": return "양호";
                case "C": return "주의";
                case "D": return "미흡";
                case "E": return "불량";
                default: return "-";
            }
        }
    }
}