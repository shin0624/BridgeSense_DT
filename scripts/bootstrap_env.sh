#!/bin/bash
# RunPod 컨테이너 디스크(/, 휘발성)가 리셋될 때마다 재실행할 부트스트랩 스크립트.
# /workspace(볼륨 디스크, 영구)에 저장해둔 상태를 홈 디렉토리로 복원한다.
#
# 사용법: VS Code로 재접속한 직후, 새 터미널에서 가장 먼저 실행할 것
#   (Claude Code 세션을 열기 전에 실행하는 게 가장 안전함 — claude 심볼릭 링크 스왑 때문)
#   bash /workspace/scripts/bootstrap_env.sh
set -e

PERSIST=/workspace/.pod_home

echo "[1/5] tmux 확인..."
if ! command -v tmux >/dev/null 2>&1; then
  apt-get update -qq && apt-get install -y -qq tmux
fi
tmux -V

echo "[1.5/5] gh CLI 확인..."
if ! command -v gh >/dev/null 2>&1; then
  apt-get update -qq && apt-get install -y -qq gh
fi
gh --version | head -1

echo "[2/5] Python venv(/workspace/.venv) 확인..."
if [ ! -x /workspace/.venv/bin/python ]; then
  python3 -m venv --system-site-packages /workspace/.venv
  /workspace/.venv/bin/pip install -r /workspace/ai/requirements.txt
fi
echo "venv 준비됨: /workspace/.venv (source /workspace/.venv/bin/activate 로 활성화)"

echo "[3/5] VS Code Server 확장/설정 심볼릭 링크 복원..."
for d in extensions data; do
  live=/root/.vscode-server/$d
  persisted=$PERSIST/vscode-server-$d
  if [ -L "$live" ]; then
    continue
  fi
  if [ -e "$persisted" ]; then
    rm -rf "$live"
    ln -s "$persisted" "$live"
    echo "  복원됨: $live -> $persisted"
  elif [ -e "$live" ]; then
    cp -a "$live" "$persisted"
    rm -rf "$live"
    ln -s "$persisted" "$live"
    echo "  최초 이전됨: $live -> $persisted"
  fi
done

echo "[4/5] Claude Code 설정/메모리 심볼릭 링크 복원..."
for pair in "claude:/root/.claude" "claude.json:/root/.claude.json"; do
  name="${pair%%:*}"; live="${pair##*:}"
  persisted="$PERSIST/$name"
  if [ -L "$live" ]; then
    continue
  fi
  if [ -e "$persisted" ]; then
    rm -rf "$live"
    ln -s "$persisted" "$live"
    echo "  복원됨: $live -> $persisted"
  elif [ -e "$live" ]; then
    cp -a "$live" "$persisted"
    rm -rf "$live"
    ln -s "$persisted" "$live"
    echo "  최초 이전됨: $live -> $persisted"
  fi
done

echo "[5/5] gh CLI 설정(인증 토큰 포함) 심볼릭 링크 복원..."
for pair in "gh-config:/root/.config/gh"; do
  name="${pair%%:*}"; live="${pair##*:}"
  persisted="$PERSIST/$name"
  if [ -L "$live" ]; then
    continue
  fi
  if [ -e "$persisted" ]; then
    rm -rf "$live"
    ln -s "$persisted" "$live"
    echo "  복원됨: $live -> $persisted"
  elif [ -e "$live" ]; then
    cp -a "$live" "$persisted"
    rm -rf "$live"
    ln -s "$persisted" "$live"
    echo "  최초 이전됨: $live -> $persisted"
  fi
done

echo "부트스트랩 완료."
