using System.Collections.Generic;
using UnityEngine;

namespace BridgeSenseDT.Assessment
{
    /// <summary>
    /// 결함이 있는 영역을 가리키는 사각형. 좌표는 0~1로 정규화돼 있고 좌상단이 원점이다.
    /// 정규화해서 담는 이유는 원본 이미지 해상도를 몰라도 화면에 그릴 수 있게 하기 위해서다.
    /// </summary>
    public struct DefectBox
    {
        public float xMin;
        public float yMin;
        public float xMax;
        public float yMax;

        public float Area => Mathf.Max(0f, xMax - xMin) * Mathf.Max(0f, yMax - yMin);
    }

    /// <summary>
    /// AI 추론 결과(BridgeAnalysisResult)를 안전등급 평가용 DetectedDefect 목록으로 환산하는 어댑터.
    /// SafetyGradeEvaluator가 Sentis/모델 타입을 직접 알 필요가 없도록 이 클래스가 경계를 담당한다.
    ///
    /// RT-DETR 검출 결과의 클래스별 최고 score를 confidence로, 검출 박스(원본 이미지 픽셀 좌표)를
    /// 0~1 정규화 좌표로 변환한 DefectBox로 사용한다.
    /// </summary>
    public static class DefectExtractor
    {
        private const int NumDefectClasses = 9; // 결함 9종 (배경 제외)

        /// <summary>이미지 1장의 분석 결과를 결함 목록으로 환산한다.</summary>
        public static List<DetectedDefect> Extract(BridgeAnalysisResult analysis, int imageWidth, int imageHeight)
        {
            var defects = new List<DetectedDefect>();
            if (analysis == null) return defects;

            float[] maxScorePerClass = GetMaxScorePerClass(analysis.Detections);
            List<DefectBox>[] boxesPerClass = GetBoxesPerClass(analysis.Detections, imageWidth, imageHeight);

            for (int classId = 0; classId < NumDefectClasses; classId++)
            {
                float score = maxScorePerClass[classId];
                if (score <= 0f)
                    continue; // 이 클래스는 검출되지 않음

                var type = (DefectType)classId; // DefectType은 RT-DETR 클래스 id와 값이 일치하도록 선언돼 있음

                defects.Add(new DetectedDefect
                {
                    type = type,
                    confidence = score,
                    estimatedWidthMm = -1f, // 촬영거리·GSD 정보가 없어 균열폭 실측 불가
                    isStructurallyCritical = SafetyGradeEvaluator.IsStructurallyCriticalType(type),
                    boxes = boxesPerClass[classId],
                });
            }

            return defects;
        }

        /// <summary>RT-DETR 검출 목록에서 클래스별 최고 score를 뽑는다. 검출 없으면 0.</summary>
        private static float[] GetMaxScorePerClass(List<RtdetrDetection> detections)
        {
            var maxScore = new float[NumDefectClasses];
            if (detections == null) return maxScore;

            foreach (var detection in detections)
            {
                if (detection.ClassId < 0 || detection.ClassId >= NumDefectClasses)
                    continue; // 방어: 스펙 밖 클래스 id는 무시

                if (detection.Score > maxScore[detection.ClassId])
                    maxScore[detection.ClassId] = detection.Score;
            }

            return maxScore;
        }

        /// <summary>
        /// RT-DETR 검출 박스(원본 이미지 픽셀 좌표)를 클래스별로 모아 0~1 정규화 좌표로 변환한다.
        /// </summary>
        private static List<DefectBox>[] GetBoxesPerClass(List<RtdetrDetection> detections, int imageWidth, int imageHeight)
        {
            var result = new List<DefectBox>[NumDefectClasses];
            for (int i = 0; i < NumDefectClasses; i++)
                result[i] = new List<DefectBox>();

            if (detections == null || imageWidth <= 0 || imageHeight <= 0)
                return result;

            foreach (var detection in detections)
            {
                if (detection.ClassId < 0 || detection.ClassId >= NumDefectClasses)
                    continue;

                result[detection.ClassId].Add(new DefectBox
                {
                    xMin = Mathf.Clamp01(detection.X1 / imageWidth),
                    yMin = Mathf.Clamp01(detection.Y1 / imageHeight),
                    xMax = Mathf.Clamp01(detection.X2 / imageWidth),
                    yMax = Mathf.Clamp01(detection.Y2 / imageHeight),
                });
            }

            return result;
        }
    }
}
