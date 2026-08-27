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

    public Texture2D Thumbnail { get; private set; }    // 이 항목이 소유하는 텍스처 - AiInferenceManager.AnalyzeImage에 그대로 넘긴다
    public byte[] ImageBytes { get; private set; }      // 업로드된 원본 파일 바이트 - 저장 시 재인코딩 없이 그대로 기록한다
    public string ImageFileName { get; private set; }   // 원본 파일명 - 저장 파일에 표시용으로 남긴다
    public string EntryId { get; private set; }         // 등록 순번 - 분석 결과(AnalyzeResultObject)와 이 항목을 매칭할 때 사용
    public string BridgeName { get; private set; }      // 교량명 - 저장 시 사용
    public string Location { get; private set; }        // 교량 소재지 - 저장 시 사용
    public string CapturedPart { get; private set; }    // 촬영 부재 - 안전등급 평가 시 체크리스트 항목으로 해석하는 데 사용

    // 이 항목이 삭제될 때 컨트롤러에 알릴 콜백. 어느 항목이 지워졌는지 알아야
    // 세션 데이터에서도 같은 항목을 제거할 수 있으므로 등록 순번을 함께 넘긴다.
    private Action<string> onDeleted;

    private void Awake()
    {
        deleteButton.onClick.AddListener(OnDeleteButtonClicked); // 자기 컴포넌트 준비 시점에 리스너 등록 - Initialize는 데이터만 책임지도록 분리
    }

    // 텍스처가 아니라 원본 바이트를 받는 이유:
    // 업로드 흐름과 저장파일 불러오기 흐름이 모두 "바이트"에서 출발하므로 진입점을 하나로 통일할 수 있고,
    // 이 항목이 자기 텍스처를 직접 만들어 소유하게 되어 파괴 책임이 명확해진다.
    public void Initialize(byte[] imageBytes, string imageFileName, string id, string bridgeName, string location, string capturedPart, Action<string> onDeleted)
    {
        ImageBytes = imageBytes;
        ImageFileName = imageFileName;
        EntryId = id;
        BridgeName = bridgeName;
        Location = location;
        CapturedPart = capturedPart;
        this.onDeleted = onDeleted;

        Thumbnail = CreateTextureFromBytes(imageBytes); // 이 항목 전용 텍스처 생성

        idText.text = id;

        thumbnailImage.texture = Thumbnail;   // 썸네일 반영
        bridgeNameText.text = bridgeName;     // 교량명 반영
        locationText.text = location;         // 소재지 반영
        capturedPartText.text = capturedPart; // 촬영 부재 반영
    }

    private static Texture2D CreateTextureFromBytes(byte[] imageBytes) // 원본 바이트를 디코딩해 텍스처를 만드는 메서드
    {
        if (imageBytes == null || imageBytes.Length == 0)
            return null;

        var texture = new Texture2D(2, 2); // LoadImage가 실제 해상도에 맞춰 다시 할당하므로 초기 크기는 의미 없음
        if (!texture.LoadImage(imageBytes))
        {
            Destroy(texture); // 디코딩 실패한 텍스처는 즉시 정리
            return null;
        }

        return texture;
    }

    private void OnDeleteButtonClicked() // 삭제 버튼 클릭 시 호출. 컨트롤러에 먼저 알리고 자기 자신을 파괴
    {
        onDeleted?.Invoke(EntryId); // 컨트롤러가 세션에서 이 항목을 제거하고, 필요하면 교량정보 입력 잠금을 풀도록 알림
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (Thumbnail != null)
        {
            Destroy(Thumbnail); // 이 항목이 만든 텍스처는 이 항목이 파괴한다
            Thumbnail = null;
        }
    }
}
