using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InputImageObject : MonoBehaviour
{
    // InputImageObject는 InputAndAnalyzePanel에서 사용되는 프리팹으로, "분석 대상 교량" 리스트의 아이템 하나(이미지 1장 + 교량 정보)를 표시한다.
    // 받은 데이터를 화면에 표시만 하도록 하고, 실제 데이터 처리는 BridgeImageRegistrationController에서 수행한다.

    [SerializeField] private RawImage thumbnailImage;   // 업로드된 이미지 썸네일을 표시할 RawImage
    [SerializeField] private TMP_Text bridgeNameText;   // 교량명 표시용 텍스트
    [SerializeField] private TMP_Text locationText;     // 교량 소재지 표시용 텍스트
    [SerializeField] private TMP_Text capturedPartText; // 촬영 부재 표시용 텍스트
    [SerializeField] private Button deleteButton;       // 삭제 버튼(인스펙터에서 직접 연결 - GetComponentInChildren로 아무 Button이나 잡지 않도록)
    [SerializeField] private TextMeshProUGUI idText;

    public Texture2D Thumbnail { get; private set; }  // 등록된 원본 썸네일 - AiInferenceManager.AnalyzeImage에 그대로 넘길 수 있도록 보관
    public string EntryId { get; private set; }       // 등록 순번 - 분석 결과(AnalyzeResultObject)와 이 항목을 매칭할 때 사용
    public string CapturedPart { get; private set; }  // 촬영 부재 - 안전등급 평가 시 체크리스트 항목으로 해석하는 데 사용

    private Action onDeleted; // 이 항목이 삭제될 때 컨트롤러에 알릴 콜백

    private void Awake()
    {
        deleteButton.onClick.AddListener(OnDeleteButtonClicked); // 자기 컴포넌트 준비 시점에 리스너 등록 - Initialize는 데이터만 책임지도록 분리
    }

    public void Initialize(Texture2D thumbnail, string id, string bridgeName, string location, string capturedPart, Action onDeleted)
    {
        Thumbnail = thumbnail;
        EntryId = id;
        CapturedPart = capturedPart;
        this.onDeleted = onDeleted;

        idText.text = id;

        thumbnailImage.texture = thumbnail;   // 썸네일 반영
        bridgeNameText.text = bridgeName;     // 교량명 반영
        locationText.text = location;         // 소재지 반영
        capturedPartText.text = capturedPart; // 촬영 부재 반영
    }

    private void OnDeleteButtonClicked() // 삭제 버튼 클릭 시 호출. 컨트롤러에 먼저 알리고 자기 자신을 파괴
    {
        onDeleted?.Invoke(); // 컨트롤러가 등록 개수를 갱신하고, 필요하면 교량정보 입력 잠금을 풀도록 알림
        Destroy(gameObject);
    }
}
