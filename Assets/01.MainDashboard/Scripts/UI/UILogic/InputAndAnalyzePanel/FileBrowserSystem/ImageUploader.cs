using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine.UI;
using UnityEngine.Networking;
using SFB;
using TMPro;
using B83.Win32;

public class ImageUploader : MonoBehaviour
{
    [SerializeField] private RawImage thumbnailImage; // 업로드된 이미지를 표시할 RawImage 컴포넌트
    [SerializeField] private Button fileBrowserButton;// "파일 선택 " 버튼
    [SerializeField] private TextMeshProUGUI errorText; // 오류 메시지를 표시할 TextMeshProUGUI 컴포넌트

    [SerializeField] private RectTransform dropAreaRect; // OS 드래그앤드롭 좌표가 이 패널 영역 안인지 검사할 때 쓸 RectTransform
    private Canvas parentCanvas; // 스크린 좌표를 UI 좌표로 변환할 때 필요한 카메라 참조용

    void Start()
    {
        if(fileBrowserButton != null)
        {
            fileBrowserButton.onClick.AddListener(OpenFileBrowser);
            errorText.text = "등록한 이미지가 표시됩니다";
        }
        parentCanvas = GetComponentInParent<Canvas>(); // Screen Space - Camera 등에서 좌표 변환 시 필요
    }

    public void OpenFileBrowser()// "파일 선택" 버튼 클릭시 브라우저 탐색기를 여는 메서드
    {
        var extensions = new[]
        {
            new ExtensionFilter("Image Files", "png", "jpg", "jpeg"), // 이미지 파일 확장자 필터
        };

        string[] paths = StandaloneFileBrowser.OpenFilePanel("BridgeSense DT_분석 대상 교량 이미지 선택", "", extensions, false);// 브라우저 탐색기에서 선택된 파일 경로를 가져옴

        if(paths.Length > 0 && !string.IsNullOrEmpty(paths[0]))// 선택된 파일 경로가 유효한 경우
        {
            StartCoroutine(LoadImageRoutine(paths[0])); // 선택된 파일 경로를 LoadImageRoutine 코루틴에 전달하여 이미지 로드
        }
    }

    private void OnEnable() // OS 탐색기 파일 드래그앤드롭 훅 등록(빌드된 Windows Standalone에서만 실제로 동작, 에디터/다른 플랫폼에서는 컴파일 조건상 아무 일도 안 함)
    {
        UnityDragAndDropHook.InstallHook();
        UnityDragAndDropHook.OnDroppedFiles += OnFilesDropped;
    }



    private void OnFilesDropped(List<string> filePaths, POINT dropPosition) // OS 탐색기에서 파일이 드롭됐을 때 훅이 호출하는 콜백
    {
        Vector2 screenPoint = new Vector2(dropPosition.x, Screen.height - dropPosition.y); // Win32 좌표(좌상단 원점, y가 아래로 증가)를 Unity 스크린 좌표(좌하단 원점)로 변환

        Camera uiCamera = parentCanvas != null ? parentCanvas.worldCamera : null; // Screen Space - Overlay면 worldCamera가 null인 게 정상(그대로 넘기면 됨)
        if (dropAreaRect == null || !RectTransformUtility.RectangleContainsScreenPoint(dropAreaRect, screenPoint, uiCamera))
            return; // 이 패널 영역 밖에 드롭됐으면 무시

        foreach (var path in filePaths) // 드롭된 파일들 중 이미지 확장자를 가진 첫 파일만 사용(OpenFileBrowser와 동일한 확장자 기준)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".png" || ext == ".jpg" || ext == ".jpeg")
            {
                StartCoroutine(LoadImageRoutine(path)); // 파일 선택 버튼과 동일한 로드 경로를 그대로 재사용
                break;
            }
        }
    }

    private IEnumerator LoadImageRoutine(string filePath)// 이미지파일을 텍스처로 로드하여 UI에 적용하는 메서드
    {
        string url = "file:///" + filePath; // 파일 경로 보정(file:///) 후 URL 생성

        using (UnityWebRequest www = UnityWebRequestTexture.GetTexture(url))// url 이미지에서 HTTP로 이미지 텍스처를 가져온다.
        {
            yield return www.SendWebRequest();// 웹 요청 완료까지 대기

            if(www.result == UnityWebRequest.Result.Success)// 요청 성공 시
            {
                Texture2D texture = DownloadHandlerTexture.GetContent(www);// 텍스처를 가져온다.
                thumbnailImage.texture = texture;// RawImage 텍스처에 url에서 가져온 텍스처를 할당한다.
                RectTransform thumbnailRect = thumbnailImage.GetComponent<RectTransform>();// RawImage의 RectTransform을 가져온다.
                SetRawImageTextureStretch(thumbnailRect);
                errorText.text = ""; // 오류 메시지를 초기화
                
            }
            else
            {
                Debug.LogError("이미지 로드 실패: " + www.error);
                errorText.text = "이미지 파일 로드에 실패하였습니다. 다시 시도해 주세요.\n" + "오류 코드 : " + www.error; // 오류 메시지를 UI에 표시
            }
        }
    }

    private void SetRawImageTextureStretch(RectTransform rect)// RawImage의 RectTransform을 Stretch로 설정하는 메서드
    {
        rect.anchorMin = new Vector2(0.0f, 0.0f);
        rect.anchorMax = new Vector2(1.0f, 1.0f); // 앵커를 부모의 사방 끝으로 설정

        rect.pivot = new Vector2(0.5f, 0.5f);// 피벗을 중앙으로 설정

        rect.offsetMin = Vector2.zero; // 좌측 하단 오프셋을 0으로 설정
        rect.offsetMax = Vector2.zero; // 우측 상단 오프셋을 0으로 설정
    }

    public void ClearThumbnail()// RawImage의 텍스처를 초기화하는 메서드
    {
        thumbnailImage.texture = null;
        errorText.text = "등록한 이미지가 표시됩니다";
    }

    public Texture2D GetCurrentThumbnailTexture()// 현재 텍스처를 읽는 접근자 메서드
    {
        return thumbnailImage.texture as Texture2D; // RawImage.texture는 Texture 타입이지만, LoadImageRoutine에서 항상 Texture2D를 넣으므로 안전하게 캐스팅됨
    }

    private void OnDisable() // 훅 해제 - OnEnable과 반드시 짝을 맞춰야 패널이 꺼졌다 켜졌다 할 때 중복 등록되지 않음
    {
        UnityDragAndDropHook.OnDroppedFiles -= OnFilesDropped;
        UnityDragAndDropHook.UninstallHook();
    }
}
