"""
TensorBoard 대신 쓸 간단한 학습 현황 웹 대시보드.

RT-DETR/SegFormer 각각의 최신 체크포인트(trainer_state.json)를 읽어서 진행률·최근
loss/평가지표·완료 여부를 보여주는 페이지 하나만 자동 새로고침으로 띄운다.
외부 패키지 없이 파이썬 표준 라이브러리(http.server)만 사용.

실행:
    /workspace/.venv/bin/python status_dashboard.py
    (기본 포트 6007 — VS Code PORTS 탭에서 forward해서 브라우저로 확인)
"""
import json
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path

CHECKPOINTS_ROOT = Path("/workspace/ai/checkpoints")  # 모델별 체크포인트가 저장되는 상위 폴더
JOBS = {
    "rtdetr_v2": {"label": "RT-DETR v2", "script": "train_rtdetr.py"},  # 표시 이름과 실행 스크립트 파일명(프로세스 감지용)
    "segformer": {"label": "SegFormer MiT-B2", "script": "train_segformer.py"},
}
PORT = 6007  # TensorBoard(6006)와 안 겹치게 다른 포트 사용


def is_process_running(script_name: str) -> bool:
    """/proc를 직접 뒤져서 해당 학습 스크립트가 지금 실행 중인지 확인(ps 명령 안 씀)."""
    for pid_dir in Path("/proc").iterdir():  # /proc 밑의 모든 프로세스 항목을 순회
        if not pid_dir.name.isdigit():  # 숫자(PID)가 아닌 항목(자기 자신 등)은 건너뜀
            continue
        try:
            cmdline = (pid_dir / "cmdline").read_bytes().decode(errors="ignore")  # 프로세스 실행 커맨드 읽기
        except (FileNotFoundError, ProcessLookupError, PermissionError):  # 조회 도중 프로세스가 사라지는 등의 경우 무시
            continue
        if script_name in cmdline:  # 찾는 학습 스크립트 이름이 커맨드라인에 포함돼 있으면
            return True  # 실행 중이라고 판단
    return False  # 끝까지 못 찾았으면 실행 중이 아님


def latest_checkpoint_dir(job_dir: Path) -> Path | None:
    """가장 스텝 번호가 큰 checkpoint-* 폴더를 찾는다."""
    checkpoints = list(job_dir.glob("checkpoint-*"))  # checkpoint-숫자 형태의 하위 폴더들
    if not checkpoints:
        return None  # 아직 체크포인트가 하나도 없는 경우
    return max(checkpoints, key=lambda p: int(p.name.split("-")[-1]))  # 폴더명 끝 숫자가 가장 큰 것 = 최신


def load_job_status(job_key: str, meta: dict) -> dict:
    job_dir = CHECKPOINTS_ROOT / job_key  # 해당 모델의 체크포인트 루트 폴더
    status = {
        "label": meta["label"],
        "running": is_process_running(meta["script"]),  # 지금 이 학습 프로세스가 살아있는지
        "finished": (job_dir / "final").exists(),  # 학습이 끝까지 완료돼서 final/ 이 생겼는지
        "progress_pct": None,
        "epoch": None,
        "num_train_epochs": None,
        "global_step": None,
        "max_steps": None,
        "recent_logs": [],
        "updated_at": None,
    }

    if not job_dir.exists():  # 아직 학습을 시작조차 안 한 경우
        return status

    ckpt_dir = latest_checkpoint_dir(job_dir)  # 최신 체크포인트 폴더 찾기
    state_path = (ckpt_dir / "trainer_state.json") if ckpt_dir else None  # 그 안의 상태 파일 경로

    if state_path is None or not state_path.exists():  # 체크포인트가 아직 없으면 더 볼 게 없음
        return status

    state = json.loads(state_path.read_text(encoding="utf-8"))  # trainer_state.json을 UTF-8로 읽어서 파싱
    global_step = state.get("global_step")  # 지금까지 진행된 스텝 수
    max_steps = state.get("max_steps")  # 전체 목표 스텝 수
    status["global_step"] = global_step
    status["max_steps"] = max_steps
    status["epoch"] = state.get("epoch")
    status["num_train_epochs"] = state.get("num_train_epochs")
    if global_step and max_steps:  # 둘 다 있으면 진행률(%) 계산
        status["progress_pct"] = round(global_step / max_steps * 100, 1)
    status["updated_at"] = state_path.stat().st_mtime  # 이 파일이 마지막으로 갱신된 시각(학습이 멎었는지 가늠용)

    for entry in reversed(state.get("log_history", [])):  # 최근 로그부터 역순으로
        if "loss" in entry or "eval_map" in entry or "eval_mean_iou" in entry:  # 의미있는 지표가 있는 항목만
            status["recent_logs"].append(entry)  # 최근 로그 목록에 추가
        if len(status["recent_logs"]) >= 5:  # 최근 5개만 모으면 충분
            break

    return status


def render_html() -> str:
    import datetime
    import time

    sections = []
    for job_key, meta in JOBS.items():  # RT-DETR, SegFormer 각각에 대해
        s = load_job_status(job_key, meta)  # 최신 상태 읽기

        if s["finished"]:  # 완료된 경우
            badge = '<span style="color:#2e7d32;font-weight:bold;">완료 ✅</span>'
        elif s["running"]:  # 프로세스가 살아있는 경우
            badge = '<span style="color:#1565c0;font-weight:bold;">진행 중 ▶</span>'
        elif s["global_step"] is not None:  # 시작은 했는데 지금은 프로세스가 없는 경우 = 중단
            badge = '<span style="color:#c62828;font-weight:bold;">중단됨 ⏸</span>'
        else:  # 아예 시작 안 한 경우
            badge = '<span style="color:#757575;">대기 중</span>'

        progress_bar = ""
        if s["progress_pct"] is not None:  # 진행률을 알 수 있으면 막대 그래프 그리기
            progress_bar = f"""
            <div style="background:#e0e0e0;border-radius:6px;overflow:hidden;height:22px;margin:8px 0;">
              <div style="background:#42a5f5;height:100%;width:{s['progress_pct']}%;
                          text-align:center;color:white;font-size:13px;line-height:22px;">
                {s['progress_pct']}%
              </div>
            </div>
            <div>스텝: {s['global_step']:,} / {s['max_steps']:,}
                 (epoch {s['epoch']:.2f} / {s['num_train_epochs']})</div>
            """

        logs_html = ""
        if s["recent_logs"]:  # 최근 로그가 있으면 표로 정리
            rows = ""
            for entry in s["recent_logs"]:
                parts = []
                if "loss" in entry:
                    parts.append(f"loss={entry['loss']:.4f}")
                if "eval_map" in entry:
                    parts.append(f"eval_map={entry['eval_map']:.4f}")
                if "eval_mean_iou" in entry:
                    parts.append(f"eval_mean_iou={entry['eval_mean_iou']:.4f}")
                if "eval_loss" in entry:
                    parts.append(f"eval_loss={entry['eval_loss']:.4f}")
                epoch_str = f"epoch {entry.get('epoch', 0):.2f}"
                rows += f"<tr><td>{epoch_str}</td><td>{', '.join(parts)}</td></tr>"
            logs_html = f"""
            <table style="width:100%;border-collapse:collapse;margin-top:8px;font-size:13px;">
              <tr style="text-align:left;color:#666;"><th>시점</th><th>최근 로그</th></tr>
              {rows}
            </table>
            """

        updated = ""
        if s["updated_at"]:  # 마지막 갱신 시각을 사람이 읽기 쉬운 형태로 표시
            dt = datetime.datetime.fromtimestamp(s["updated_at"])
            ago = int(time.time() - s["updated_at"])
            updated = f'<div style="color:#888;font-size:12px;margin-top:6px;">마지막 업데이트: {dt.strftime("%Y-%m-%d %H:%M:%S")} ({ago}초 전)</div>'

        sections.append(f"""
        <div style="border:1px solid #ddd;border-radius:10px;padding:16px;margin-bottom:16px;
                     font-family:-apple-system,'Malgun Gothic','Apple SD Gothic Neo',sans-serif;">
          <h2 style="margin:0 0 8px 0;font-size:18px;">{s['label']} — {badge}</h2>
          {progress_bar}
          {logs_html}
          {updated}
        </div>
        """)

    body = "\n".join(sections)
    return f"""<!doctype html>
<html lang="ko">
<head>
  <meta charset="utf-8">
  <meta http-equiv="refresh" content="10">
  <title>BridgeSense DT 학습 현황</title>
</head>
<body style="max-width:720px;margin:24px auto;padding:0 16px;">
  <h1 style="font-family:sans-serif;font-size:22px;">BridgeSense DT 학습 현황</h1>
  <p style="color:#888;font-size:13px;font-family:sans-serif;">10초마다 자동 새로고침됩니다.</p>
  {body}
</body>
</html>"""


class StatusHandler(BaseHTTPRequestHandler):
    def do_GET(self):
        html = render_html().encode("utf-8")  # 매 요청마다 최신 상태로 다시 렌더링해서 UTF-8 바이트로 인코딩
        self.send_response(200)
        self.send_header("Content-Type", "text/html; charset=utf-8")  # 한글이 깨지지 않도록 인코딩 명시
        self.send_header("Content-Length", str(len(html)))
        self.end_headers()
        self.wfile.write(html)

    def log_message(self, format, *args):
        pass  # 매 새로고침마다 콘솔에 접속 로그가 찍히는 걸 막음(불필요한 노이즈 제거)


if __name__ == "__main__":
    server = HTTPServer(("0.0.0.0", PORT), StatusHandler)  # 모든 인터페이스에서 접속 가능하도록 바인딩
    print(f"학습 현황 대시보드: http://0.0.0.0:{PORT} (VS Code PORTS 탭에서 {PORT} forward)")
    server.serve_forever()  # 요청이 올 때마다 위 do_GET이 호출됨
