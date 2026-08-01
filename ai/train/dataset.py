"""
COCO 포맷(convert_to_coco.py 산출물) 공용 Dataset 로더.

train_rtdetr.py / train_segformer.py에서 공유. 강재(강재_부식, 도장_박리) 클래스가
전체의 0.1%뿐인 불균형 문제 대응으로, 강재 어노테이션을 포함한 이미지를 오버샘플링하는
옵션을 제공한다 (배경: ai/docs/AI_PIPELINE_PLAN.md 5.4절).
"""
from pathlib import Path

from PIL import Image
from pycocotools.coco import COCO
from torch.utils.data import Dataset

# convert_to_coco.py의 CLASS_NAMES 순서와 동일한 이름으로 강재 클래스를 식별한다.
STEEL_CLASS_NAMES = {"강재_부식", "도장_박리"}


class CocoDetectionDataset(Dataset):
    """RT-DETR류(HF DetrImageProcessor 인터페이스) 학습용 COCO Dataset."""

    def __init__(
        self,
        images_dir: str,
        annotation_json: str,
        image_processor,
        oversample_steel: int = 1,
        max_samples: int | None = None,
    ):
        self.images_dir = Path(images_dir)
        self.coco = COCO(annotation_json)
        self.image_processor = image_processor
        self.image_ids = self._build_index(oversample_steel)
        if max_samples is not None:
            self.image_ids = self.image_ids[:max_samples]

    def _build_index(self, oversample_steel: int):
        image_ids = sorted(self.coco.imgs.keys())
        if oversample_steel <= 1:
            return image_ids

        steel_cat_ids = {
            cid for cid, cat in self.coco.cats.items() if cat["name"] in STEEL_CLASS_NAMES
        }
        steel_image_ids = {
            ann["image_id"] for ann in self.coco.anns.values() if ann["category_id"] in steel_cat_ids
        }
        extra = [img_id for img_id in image_ids if img_id in steel_image_ids]
        result = image_ids + extra * (oversample_steel - 1)
        print(
            f"강재 오버샘플링: 강재 포함 이미지 {len(steel_image_ids)}장을 "
            f"{oversample_steel}배로 반복 (총 {len(result)}장, 원본 {len(image_ids)}장)"
        )
        return result

    def __len__(self):
        return len(self.image_ids)

    def __getitem__(self, idx):
        image_id = self.image_ids[idx]
        img_info = self.coco.imgs[image_id]
        image = Image.open(self.images_dir / img_info["file_name"]).convert("RGB")

        ann_ids = self.coco.getAnnIds(imgIds=image_id)
        annotations = self.coco.loadAnns(ann_ids)

        target = {"image_id": image_id, "annotations": annotations}
        encoding = self.image_processor(images=image, annotations=target, return_tensors="pt")
        return {
            "pixel_values": encoding["pixel_values"][0],
            "labels": encoding["labels"][0],
        }


def build_collate_fn(image_processor):
    def collate_fn(batch):
        pixel_values = [item["pixel_values"] for item in batch]
        labels = [item["labels"] for item in batch]
        encoding = image_processor.pad(pixel_values, return_tensors="pt")
        result = {"pixel_values": encoding["pixel_values"], "labels": labels}
        if "pixel_mask" in encoding:
            result["pixel_mask"] = encoding["pixel_mask"]
        return result

    return collate_fn
