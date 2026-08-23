using UnityEngine;
using UnityEngine.UI;
using TMPro;
using BridgeSenseDT.Session;

public class BridgeImageRegistrationController : MonoBehaviour
{
    // 이미지 업로더 + 교량 정보 입력필드 + 이미지 및 정보 등록 완료 버튼을 하나로 묶어서 InputImageObject 프리팹을 생성하는 컨트롤러.
    // 데이터의 원본은 AnalysisSessionManager가 들고 있는 AnalysisSession이며, 이 클래스는 그것을 화면에 비추고 입력을 받아 넘긴다.

    [SerializeField] private TMP_InputField bridgeNameInputField;   // 교량 이름 입력 필드
    [SerializeField] private TMP_InputField locationInputField;     // 교량 위치 입력 필드
    [SerializeField] private TMP_InputField capturedPartInputField; // 촬영 부위 입력 필드
    [SerializeField] private GameObject inputImageObjectPrefab;     // InputImageObject 프리팹
    [SerializeField] private Button registerButton;                // "이미지 및 정보 등록 완료" 버튼
    [SerializeField] private Transform inputImageObjectParent;      // InputImageObject가 생성될 부모(VerticalLayoutGroup이 붙은 리스트 컨테이너)
    [SerializeField] private ImageUploader imageUploader;           // 이미지 업로더 스크립트
    [SerializeField] private GameObject registerInvalidPopup;       // 유효성 검사 실패 시 보여줄 경고 팝업
    [SerializeField] private Button registerInvalidCloseButton;
    [SerializeField] private Button registerInvalidYesButton;

    private int nextEntryId; // 다음에 생성할 InputImageObject에 부여할 순번(삭제돼도 재사용하지 않고 계속 증가만 함)

    // Start가 아니라 OnEnable에서 등록하는 이유: 분석이 끝나면 ImageUploadPanel이 SetActive(false)로
    // 내려갔다가 다시 올라올 수 있다. Start는 생애 단 한 번만 실행되므로, Start에서 등록하고
    // OnDisable에서 제거하면 패널이 한 번 꺼진 뒤에는 등록 버튼이 영영 먹통이 된다.
    private void OnEnable()
    {
        registerButton.onClick.AddListener(OnRegisterButtonClicked); // "이미지 및 정보 등록 완료" 버튼 클릭 시 OnRegisterButtonClicked 호출
        registerInvalidCloseButton.onClick.AddListener(OnCloseButtonClicked);
        registerInvalidYesButton.onClick.AddListener(OnCloseButtonClicked);
    }

    private void OnDisable()
    {
        registerButton.onClick.RemoveListener(OnRegisterButtonClicked); // OnEnable과 짝을 맞춰 제거
        registerInvalidCloseButton.onClick.RemoveListener(OnCloseButtonClicked);
        registerInvalidYesButton.onClick.RemoveListener(OnCloseButtonClicked);
    }

    private void OnRegisterButtonClicked() // "이미지 및 정보 등록 완료" 버튼 클릭 시 호출되는 메서드. 아래 메서드들을 순서대로 호출하는 오케스트레이터 역할
    {
        registerInvalidPopup.SetActive(false); // 이전 클릭에서 떠 있던 경고가 있으면 먼저 지움

        if (!IsInputValid())
        {
            registerInvalidPopup.SetActive(true); // 이번 클릭도 유효하지 않으면 다시 띄움
            return;
        }

        RegisterEntry();        // 세션에 항목을 추가하고 화면에도 반영
        LockBridgeInfoFields(); // 한 개의 교량에 대해서만 입력을 받도록 잠금
        ResetForNextEntry();    // 다음 이미지 입력을 받을 준비
    }

    private bool IsInputValid() // 교량 이미지·정보가 비어있는지 체크하는 메서드
    {
        return imageUploader.HasImage()
            && !string.IsNullOrWhiteSpace(bridgeNameInputField.text)
            && !string.IsNullOrWhiteSpace(locationInputField.text)
            && !string.IsNullOrWhiteSpace(capturedPartInputField.text);
    }

    private void RegisterEntry() // 세션에 새 항목을 추가하고 그 항목을 표시할 InputImageObject를 만드는 메서드
    {
        var session = AnalysisSessionManager.Instance.CurrentSession;

        if (!session.HasEntries)
            WriteBridgeInfoTo(session); // 첫 항목을 등록하는 시점에 교량 정보가 확정된다

        nextEntryId++;

        var entry = new AnalysisEntry
        {
            EntryId = nextEntryId.ToString(),
            CapturedPart = capturedPartInputField.text,
            ImageBytes = imageUploader.GetCurrentImageBytes(),
            ImageFileName = imageUploader.GetCurrentFileName(),
            Analyzed = false, // 등록만 된 상태. 분석은 "AI 분석 시작"에서 수행된다
        };

        session.Entries.Add(entry);
        AnalysisSessionManager.Instance.MarkDirty();

        SpawnEntryView(entry, session.BridgeName, session.Location);
    }

    private void SpawnEntryView(AnalysisEntry entry, string bridgeName, string location) // 세션 항목 하나를 화면 목록에 그리는 메서드
    {
        GameObject prefabInstance = Instantiate(inputImageObjectPrefab, inputImageObjectParent);
        InputImageObject objectInstance = prefabInstance.GetComponent<InputImageObject>();

        objectInstance.Initialize(
            entry.ImageBytes,
            entry.ImageFileName,
            entry.EntryId,
            bridgeName,
            location,
            entry.CapturedPart,
            OnEntryDeleted); // 이 항목이 삭제되면 호출될 콜백을 넘김
    }

    private void OnEntryDeleted(string entryId) // 리스트의 InputImageObject 하나가 삭제될 때 그 항목이 호출하는 콜백
    {
        var session = AnalysisSessionManager.Instance.CurrentSession;

        var entry = session.FindEntry(entryId);
        if (entry != null)
        {
            session.Entries.Remove(entry);
            AnalysisSessionManager.Instance.MarkDirty();
        }

        // 화면 자식 수가 아니라 세션 항목 수로 판단한다.
        // Destroy는 프레임 끝에 실행되므로 이 시점에 자식을 세면 방금 지운 항목이 아직 포함돼 있다.
        if (session.Entries.Count == 0)
            UnlockBridgeInfoFields(); // 등록된 이미지가 하나도 안 남았으면 다른 교량 정보를 다시 입력할 수 있도록 잠금 해제
    }

    private void LockBridgeInfoFields() // 교량명/소재지를 더 이상 수정 못 하게 잠그는 메서드
    {
        bridgeNameInputField.interactable = false;
        locationInputField.interactable = false;
    }

    private void UnlockBridgeInfoFields() // 잠긴 교량명/소재지 입력을 다시 열어주는 메서드
    {
        bridgeNameInputField.interactable = true;
        locationInputField.interactable = true;
    }

    private void ResetForNextEntry() // 다음 이미지 등록을 위해 촬영부재 입력·썸네일만 비우는 메서드(교량명/소재지는 잠긴 값 그대로 유지)
    {
        capturedPartInputField.text = "";
        imageUploader.ClearThumbnail();
    }

    // 현재 "분석 대상 교량" 리스트에 등록돼 있는 InputImageObject 전체를 반환.
    // 별도 List로 중복 관리하지 않고, 항상 실제 자식 오브젝트 구성을 그대로 조회해서 상태가 어긋날 일이 없게 함.
    //
    // includeInactive를 반드시 true로 둘 것.
    // 분석 완료 상태에서는 ImageUploadPanel이 꺼져 있어 이 목록도 비활성 상태인데,
    // 기본값(false)으로 두면 저장본을 불러올 때 항목을 하나도 찾지 못해 결과 카드가 그려지지 않는다.
    public InputImageObject[] GetRegisteredEntries()
    {
        return inputImageObjectParent.GetComponentsInChildren<InputImageObject>(true);
    }

    /// <summary>입력 필드에 있는 교량 정보를 세션에 옮겨 적는다.</summary>
    public void WriteBridgeInfoTo(AnalysisSession session)
    {
        session.BridgeName = bridgeNameInputField.text;
        session.Location = locationInputField.text;
    }

    /// <summary>화면을 아무것도 등록되지 않은 상태로 되돌린다. 세션 데이터는 건드리지 않는다.</summary>
    public void ClearAll()
    {
        DestroyAllEntryViews();

        nextEntryId = 0;

        bridgeNameInputField.text = "";
        locationInputField.text = "";
        capturedPartInputField.text = "";
        UnlockBridgeInfoFields();

        imageUploader.ClearThumbnail();
        registerInvalidPopup.SetActive(false);
    }

    /// <summary>저장본에서 불러온 세션의 내용대로 화면 목록과 입력 필드를 다시 구성한다.</summary>
    public void RebuildFromSession(AnalysisSession session)
    {
        ClearAll();

        bridgeNameInputField.text = session.BridgeName;
        locationInputField.text = session.Location;

        foreach (var entry in session.Entries)
            SpawnEntryView(entry, session.BridgeName, session.Location);

        // 순번을 복원하지 않으면 불러온 뒤 등록할 때 기존 항목과 같은 순번이 다시 발급된다.
        nextEntryId = session.GetNextEntryIdSeed();

        if (session.HasEntries)
            LockBridgeInfoFields(); // 이미 항목이 있는 세션이므로 교량 정보는 잠긴 상태여야 한다
    }

    private void DestroyAllEntryViews()
    {
        // 부모에서 먼저 떼어낸 뒤 파괴한다.
        // Destroy는 프레임 끝에 실행되므로, 떼어내지 않으면 같은 프레임에 GetComponentsInChildren를 호출했을 때
        // 파괴 예정인 옛 항목까지 함께 잡힌다. 불러오기가 "비우기 → 다시 그리기 → 등급 재계산" 순서라 이 문제가 실제로 발생한다.
        for (int i = inputImageObjectParent.childCount - 1; i >= 0; i--)
        {
            Transform child = inputImageObjectParent.GetChild(i);
            child.SetParent(null);
            Destroy(child.gameObject);
        }
    }

    private void OnCloseButtonClicked()
    {
        if(registerInvalidPopup.activeSelf)
        {
            registerInvalidPopup.SetActive(false);
        }
    }
}
