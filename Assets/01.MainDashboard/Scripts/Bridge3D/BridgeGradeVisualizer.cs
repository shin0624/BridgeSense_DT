using System.Collections.Generic;
using BridgeSenseDT.Assessment;
using BridgeSenseDT.Session;
using BridgeSenseDT.UI;
using DG.Tweening;
using UnityEngine;

namespace BridgeSenseDT.Bridge3D
{
    /// <summary>
    /// 안전등급 산정 결과를 3D 교량 모델의 부재 색으로 반영하는 시각화 컴포넌트.
    ///
    /// 색상은 renderer.material이 아니라 MaterialPropertyBlock으로 적용한다(BridgeComponentTag 참고).
    /// 미흡(D)·불량(E) 부재는 눈에 띄도록 주기적으로 밝기가 오르내린다.
    ///
    /// 깜빡임을 알파값이 아니라 밝기로 구현한 이유:
    /// 부재 머티리얼이 불투명(Opaque)이라 알파를 낮춰도 화면에 변화가 없다.
    /// GradeColorMap.Highlight가 같은 이유로 이미 만들어져 있어 그대로 사용한다.
    /// </summary>
    public class BridgeGradeVisualizer : MonoBehaviour
    {
        [SerializeField] private BridgeModelRegistry registry; // 비워두면 싱글톤 인스턴스를 사용

        [Tooltip("깜빡임 한 주기(초). 값이 클수록 느리게 숨쉬듯 변한다")]
        [SerializeField] private float pulsePeriod = 0.8f;

        [Tooltip("깜빡임의 최대 밝기 보정량(0~1). 클수록 흰색에 가까워진다")]
        [SerializeField] private float pulseBrightness = 0.9f;

        /// <summary>깜빡일 대상. 등급색과 밝은 등급색을 미리 구해두고 트윈은 둘 사이를 오가기만 한다.</summary>
        private struct PulseTarget
        {
            public BridgeComponentTag tag;
            public int submeshIndex;
            public Color baseColor;
            public Color highlightColor;
        }

        private readonly List<PulseTarget> pulseTargets = new List<PulseTarget>();
        private Tween pulseTween;

        private BridgeModelRegistry Registry => registry != null ? registry : BridgeModelRegistry.Instance;

        private bool subscribed;

        // OnEnable과 Start 양쪽에서 구독을 시도한다.
        // Unity는 오브젝트마다 Awake와 OnEnable을 이어서 호출하므로,
        // 항상 켜져 있는 교량 모델에 붙은 이 컴포넌트는 AnalysisSessionManager.Awake보다
        // 먼저 실행될 수 있다. 그때 그냥 넘어가면 영영 구독하지 못한 채로 남는다.
        // Start는 모든 Awake가 끝난 뒤에 실행되므로 늦어도 여기서는 매니저가 준비돼 있다.
        private void OnEnable()
        {
            TrySubscribe();
        }

        private void Start()
        {
            TrySubscribe();
        }

        private void OnDisable()
        {
            if (AnalysisSessionManager.Instance != null)
                AnalysisSessionManager.Instance.ReportChanged -= Apply;

            subscribed = false;
            StopPulse();
        }

        private void TrySubscribe()
        {
            if (subscribed)
                return;

            var manager = AnalysisSessionManager.Instance;
            if (manager == null)
                return;

            manager.ReportChanged += Apply;
            subscribed = true;

            // 구독 이전에 분석이 끝났을 수 있으므로 현재 결과를 한 번 반영한다.
            Apply(manager.LastReport);
        }

        private void OnDestroy()
        {
            StopPulse();
        }

        /// <summary>산정 결과의 부재별 등급을 색으로 반영한다.</summary>
        public void Apply(BridgeAssessmentReport report)
        {
            var modelRegistry = Registry;
            if (modelRegistry == null)
                return;

            StopPulse();

            // 이번 분석에 포함되지 않은 부재는 원래 색으로 돌려놓는다.
            // 지난 분석의 색이 남아 있으면 어느 부재가 이번에 평가된 것인지 구분할 수 없다.
            modelRegistry.ResetAllColors();

            if (report?.Bridge?.evaluations == null)
                return;

            foreach (var evaluation in report.Bridge.evaluations)
            {
                if (evaluation.NotApplicable)
                    continue;

                string grade = BridgeAssessmentCoordinator.StateGradeToDisplayGrade(evaluation.stateGrade);
                Color baseColor = GradeColorMap.GetColor(grade);
                Color highlightColor = GradeColorMap.Highlight(baseColor, pulseBrightness);

                modelRegistry.ForEachSubmesh(evaluation.item, (tag, submeshIndex) =>
                {
                    tag.SetSubmeshColor(submeshIndex, baseColor);

                    // 등급과 무관하게 평가된 부재는 모두 밝기가 오르내린다.
                    // 색이 칠해진 부재와 평가 대상이 아닌 부재를 움직임으로도 구분할 수 있게 하기 위해서다.
                    pulseTargets.Add(new PulseTarget
                    {
                        tag = tag,
                        submeshIndex = submeshIndex,
                        baseColor = baseColor,
                        highlightColor = highlightColor,
                    });
                });
            }

            StartPulse();
        }

        private void StartPulse()
        {
            if (pulseTargets.Count == 0)
                return;

            // 대상마다 트윈을 만들지 않고 0~1 값 하나를 굴려서 전체에 적용한다.
            // 부재가 수백 개라 개별 트윈을 만들면 관리 비용이 커진다.
            pulseTween = DOVirtual.Float(0f, 1f, pulsePeriod, ApplyPulseValue)
                .SetLoops(-1, LoopType.Yoyo)
                .SetEase(Ease.InOutSine)
                .SetLink(gameObject); // 이 오브젝트가 파괴되면 트윈도 함께 정리되도록 연결
        }

        private void ApplyPulseValue(float t)
        {
            foreach (var target in pulseTargets)
            {
                // 씬이 정리되는 중이면 부재가 먼저 파괴돼 있을 수 있다.
                // 파괴된 컴포넌트는 접근하는 순간 예외를 던지므로 반드시 살아있는지 먼저 확인한다.
                if (target.tag == null)
                    continue;

                target.tag.SetSubmeshColor(target.submeshIndex, Color.Lerp(target.baseColor, target.highlightColor, t));
            }
        }

        private void StopPulse()
        {
            if (pulseTween != null)
            {
                pulseTween.Kill();
                pulseTween = null;
            }

            // 깜빡임을 멈출 때는 등급색 자체로 되돌려 놓는다.
            // 트윈이 중간값에서 멈추면 실제 등급과 다른 밝기로 남는다.
            // 다만 플레이 종료나 씬 전환으로 호출된 경우에는 부재가 이미 파괴됐을 수 있어 확인이 필요하다.
            foreach (var target in pulseTargets)
            {
                if (target.tag == null)
                    continue;

                target.tag.SetSubmeshColor(target.submeshIndex, target.baseColor);
            }

            pulseTargets.Clear();
        }
    }
}
