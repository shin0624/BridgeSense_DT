using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class BridgeImageRegistrationController : MonoBehaviour
{
    // 이미지 업로더 + 교량 정보 입력필드 + 이미지 및 정보 등록 완료 버튼을 하나로 묶어서 InputImageObject 프리팹을 생성하는 컨트롤러.

    [SerializeField] private TMP_InputField bridgeNameInputField;   // 교량 이름 입력 필드
    [SerializeField] private TMP_InputField locationInputField;     // 교량 위치 입력 필드
    [SerializeField] private TMP_InputField capturedPartInputField; // 촬영 부위 입력 필드
    [SerializeField] private GameObject inputImageObjectPrefab;     // InputImageObject 프리팹
    [SerializeField] private Button registerButton;                // "이미지 및 정보 등록 완료" 버튼
    [SerializeField] private Transform inputImageObjectParent;      // InputImageObject가 생성될 부모(VerticalLayoutGroup이 붙은 리스트 컨테이너)
    [SerializeField] private ImageUploader imageUploader;           // 이미지 업로더 스크립트
    [SerializeField] private GameObject registerInvalidPopup;       // 유효성 검사 실패 시 보여줄 경고 팝업

    private int nextEntryId;     // 다음에 생성할 InputImageObject에 부여할 순번(삭제돼도 재사용하지 않고 계속 증가만 함)
    private int activeEntryCount; // 현재 리스트에 살아있는 InputImageObject 개수 - 0이 되면 교량정보 필드 잠금을 풀어줌

    // Start가 아니라 OnEnable에서 등록하는 이유: 분석이 끝나면 ImageUploadPanel이 SetActive(false)로
    // 내려갔다가 다시 올라올 수 있다. Start는 생애 단 한 번만 실행되므로, Start에서 등록하고
    // OnDisable에서 제거하면 패널이 한 번 꺼진 뒤에는 등록 버튼이 영영 먹통이 된다.
    private void OnEnable()
    {
        registerButton.onClick.AddListener(OnRegisterButtonClicked); // "이미지 및 정보 등록 완료" 버튼 클릭 시 OnRegisterButtonClicked 호출
    }

    private void OnDisable()
    {
        registerButton.onClick.RemoveListener(OnRegisterButtonClicked); // OnEnable과 짝을 맞춰 제거
    }

    private void OnRegisterButtonClicked() // "이미지 및 정보 등록 완료" 버튼 클릭 시 호출되는 메서드. 아래 메서드들을 순서대로 호출하는 오케스트레이터 역할
    {
        registerInvalidPopup.SetActive(false); // 이전 클릭에서 떠 있던 경고가 있으면 먼저 지움

        if (!IsInputValid())
        {
            registerInvalidPopup.SetActive(true); // 이번 클릭도 유효하지 않으면 다시 띄움
            return;
        }

        CreateInputImageObject(); // InputImageObject 생성
        LockBridgeInfoFields();   // 한 개의 교량에 대해서만 입력을 받도록 잠금
        ResetForNextEntry();      // 다음 이미지 입력을 받을 준비
    }

    private bool IsInputValid() // 교량 이미지·정보가 비어있는지 체크하는 메서드
    {
        return imageUploader.GetCurrentThumbnailTexture() != null
            && !string.IsNullOrWhiteSpace(bridgeNameInputField.text)
            && !string.IsNullOrWhiteSpace(locationInputField.text)
            && !string.IsNullOrWhiteSpace(capturedPartInputField.text);
    }

    private void CreateInputImageObject() // InputImageObject를 생성하고 데이터를 채워 넣는 메서드
    {
        GameObject prefabInstance = Instantiate(inputImageObjectPrefab, inputImageObjectParent);
        InputImageObject objectInstance = prefabInstance.GetComponent<InputImageObject>();

        nextEntryId++;
        activeEntryCount++;
        objectInstance.Initialize(
            imageUploader.GetCurrentThumbnailTexture(),
            nextEntryId.ToString(),
            bridgeNameInputField.text,
            locationInputField.text,
            capturedPartInputField.text,
            OnEntryDeleted); // 이 항목이 삭제되면 호출될 콜백을 넘김
    }

    private void OnEntryDeleted() // 리스트의 InputImageObject 하나가 삭제될 때 그 항목이 호출하는 콜백
    {
        activeEntryCount--;
        if (activeEntryCount <= 0)
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
    public InputImageObject[] GetRegisteredEntries()
    {
        return inputImageObjectParent.GetComponentsInChildren<InputImageObject>();
    }
}
