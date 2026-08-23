using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine.UI;
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

    private static readonly string[] SupportedExtensions = { ".png", ".jpg", ".jpeg" }; // 파일 선택과 드래그앤드롭이 같은 기준을 쓰도록 한 곳에 정의

    private Texture2D previewTexture;  // 이 스크립트가 소유하는 미리보기 텍스처. 교체될 때 직접 파괴한다
    private byte[] currentImageBytes;  // 업로드된 파일의 원본 바이트. 저장 시 그대로 기록하기 위해 보관한다
    private string currentFileName;    // 업로드된 파일의 이름(경로 제외). 저장 파일에 표시용으로 남긴다

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
            LoadImage(paths[0]); // 선택된 파일을 즉시 로드
        }
    }

    private void OnEnable() // OS 탐색기 파일 드래그앤드롭 훅 등록(빌드된 Windows Standalone에서만 실제로 동작, 에디터/다른 플랫폼에서는 컴파일 조건상 아무 일도 안 함)
    {
        UnityDragAndDropHook.InstallHook();
        UnityDragAndDropHook.OnDroppedFiles += OnFilesDropped;
    }

    private void OnDisable() // 훅 해제 - OnEnable과 반드시 짝을 맞춰야 패널이 꺼졌다 켜졌다 할 때 중복 등록되지 않음
    {
        UnityDragAndDropHook.OnDroppedFiles -= OnFilesDropped;
        UnityDragAndDropHook.UninstallHook();
    }

    private void OnFilesDropped(List<string> filePaths, POINT dropPosition) // OS 탐색기에서 파일이 드롭됐을 때 훅이 호출하는 콜백
    {
        Vector2 screenPoint = new Vector2(dropPosition.x, Screen.height - dropPosition.y); // Win32 좌표(좌상단 원점, y가 아래로 증가)를 Unity 스크린 좌표(좌하단 원점)로 변환

        Camera uiCamera = parentCanvas != null ? parentCanvas.worldCamera : null; // Screen Space - Overlay면 worldCamera가 null인 게 정상(그대로 넘기면 됨)
        if (dropAreaRect == null || !RectTransformUtility.RectangleContainsScreenPoint(dropAreaRect, screenPoint, uiCamera))
            return; // 이 패널 영역 밖에 드롭됐으면 무시

        foreach (var path in filePaths) // 드롭된 파일들 중 이미지 확장자를 가진 첫 파일만 사용
        {
            if (IsSupportedImage(path))
            {
                LoadImage(path); // 파일 선택 버튼과 동일한 로드 경로를 그대로 재사용
                break;
            }
        }
    }

    private static bool IsSupportedImage(string filePath) // 확장자가 지원 목록에 있는지 검사
    {
        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        return Array.IndexOf(SupportedExtensions, extension) >= 0;
    }

    // 이미지 파일을 읽어 텍스처로 만들고 UI에 반영하는 메서드.
    // UnityWebRequest 대신 File.ReadAllBytes를 쓰는 이유가 세 가지다.
    // 1. 저장 기능에 필요한 원본 파일 바이트를 그대로 확보할 수 있다(재인코딩 없이 저장 가능).
    // 2. "file:///" + 경로 방식은 URL 이스케이프를 하지 않아 경로에 공백이나 한글이 있으면 실패할 수 있었다.
    // 3. 로컬 파일 읽기는 즉시 끝나므로 코루틴으로 프레임을 넘길 이유가 없다.
    private void LoadImage(string filePath)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(filePath); // 원본 바이트를 그대로 읽는다

            var texture = new Texture2D(2, 2); // LoadImage가 실제 해상도에 맞춰 다시 할당하므로 초기 크기는 의미 없음
            if (!texture.LoadImage(bytes)) // png/jpg 헤더를 해석해 디코딩. 실패하면 false
            {
                Destroy(texture); // 디코딩 실패한 텍스처는 즉시 정리
                ShowError("이미지 형식을 해석할 수 없습니다. png 또는 jpg 파일인지 확인해 주세요.");
                return;
            }

            ReleasePreviewTexture(); // 직전 미리보기 텍스처가 남아있으면 파괴하고 자리를 비운다

            previewTexture = texture;
            currentImageBytes = bytes;
            currentFileName = Path.GetFileName(filePath);

            thumbnailImage.texture = texture;// RawImage에 새 텍스처를 할당
            SetRawImageTextureStretch(thumbnailImage.GetComponent<RectTransform>());
            errorText.text = ""; // 오류 메시지를 초기화
        }
        catch (Exception e) // 파일이 없거나 권한이 없는 경우 등
        {
            Debug.LogError("이미지 로드 실패: " + e.Message);
            ShowError("이미지 파일 로드에 실패하였습니다. 다시 시도해 주세요.\n오류 내용 : " + e.Message);
        }
    }

    private void ShowError(string message) // 오류 메시지를 UI에 표시하는 메서드
    {
        errorText.text = message;
    }

    private void SetRawImageTextureStretch(RectTransform rect)// RawImage의 RectTransform을 Stretch로 설정하는 메서드
    {
        rect.anchorMin = new Vector2(0.0f, 0.0f);
        rect.anchorMax = new Vector2(1.0f, 1.0f); // 앵커를 부모의 사방 끝으로 설정

        rect.pivot = new Vector2(0.5f, 0.5f);// 피벗을 중앙으로 설정

        rect.offsetMin = Vector2.zero; // 좌측 하단 오프셋을 0으로 설정
        rect.offsetMax = Vector2.zero; // 우측 상단 오프셋을 0으로 설정
    }

    private void ReleasePreviewTexture() // 이 스크립트가 소유한 미리보기 텍스처를 파괴하고 참조를 비우는 메서드
    {
        if (previewTexture != null)
        {
            Destroy(previewTexture);
            previewTexture = null;
        }
    }

    public void ClearThumbnail()// 미리보기를 비우는 메서드. 등록 완료 후와 새 분석 시작 시 호출된다
    {
        ReleasePreviewTexture(); // InputImageObject는 바이트로 자기 텍스처를 따로 만들므로 여기서 파괴해도 안전하다
        thumbnailImage.texture = null;
        currentImageBytes = null;
        currentFileName = null;
        errorText.text = "등록한 이미지가 표시됩니다";
    }

    public bool HasImage() // 등록 가능한 이미지가 올라와 있는지 확인하는 메서드
    {
        return currentImageBytes != null && currentImageBytes.Length > 0;
    }

    public byte[] GetCurrentImageBytes() // 업로드된 파일의 원본 바이트를 읽는 접근자
    {
        return currentImageBytes;
    }

    public string GetCurrentFileName() // 업로드된 파일의 이름을 읽는 접근자
    {
        return currentFileName;
    }

    private void OnDestroy()
    {
        ReleasePreviewTexture(); // 씬 종료 시 소유 텍스처 정리
    }
}
