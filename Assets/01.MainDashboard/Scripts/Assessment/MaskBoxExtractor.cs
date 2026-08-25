using System.Collections.Generic;
using UnityEngine;

namespace BridgeSenseDT.Assessment
{
    /// <summary>
    /// 결함이 있는 영역을 가리키는 사각형. 좌표는 0~1로 정규화돼 있고 좌상단이 원점이다.
    /// 정규화해서 담는 이유는 원본 이미지 해상도를 몰라도 화면에 그릴 수 있게 하기 위해서다.
    /// </summary>
    public struct DefectBox
    {
        public float xMin;
        public float yMin;
        public float xMax;
        public float yMax;

        public float Area => Mathf.Max(0f, xMax - xMin) * Mathf.Max(0f, yMax - yMin);
    }

    /// <summary>
    /// SegFormer의 픽셀 분할 결과에서 결함 영역을 감싸는 사각형을 뽑아낸다.
    ///
    /// RT-DETR이 아니라 마스크에서 사각형을 만드는 이유:
    /// 실측 검증에서 RT-DETR은 대부분의 이미지에서 임계값을 넘는 검출을 내지 못했고
    /// (mAP 0.05), 실제로 결함을 잡아낸 것은 SegFormer(mean_iou 0.47)였다.
    /// RT-DETR 결과만 표시하면 결함이 검출된 사진에서도 사각형이 거의 나오지 않는다.
    ///
    /// 클래스 픽셀 전체를 하나의 사각형으로 감싸지 않고 덩어리별로 나누는 이유:
    /// 균열처럼 화면을 가로지르는 결함은 전체를 감싸면 사각형이 이미지 전체가 되어
    /// 어디에 결함이 있는지 알려주지 못한다.
    ///
    /// 픽셀 단위로 연결 요소를 찾으면 비용이 크므로 격자로 줄여서 계산한다.
    /// 512x512를 16픽셀 격자로 줄이면 32x32칸이 되어 연결 요소 탐색이 가볍다.
    /// </summary>
    public static class MaskBoxExtractor
    {
        private const int DefectClassCount = 9; // 배경을 제외한 결함 클래스 수

        /// <summary>격자 한 칸의 픽셀 크기. 작을수록 정밀하지만 사각형이 잘게 쪼개진다.</summary>
        public const int DefaultCellSize = 16;

        /// <summary>이 칸 수보다 작은 덩어리는 잡음으로 보고 버린다.</summary>
        public const int DefaultMinCells = 2;

        /// <summary>한 결함 종류당 표시할 사각형의 최대 개수. 넘치면 넓은 것부터 남긴다.</summary>
        public const int DefaultMaxBoxes = 8;

        /// <summary>
        /// 분할 결과에서 결함 종류별 사각형을 한 번에 뽑는다.
        /// 픽셀 순회를 종류마다 반복하지 않도록 한 번만 훑으면서 모든 종류의 격자를 함께 채운다.
        /// </summary>
        /// <returns>RT-DETR 클래스 id(0~8)로 인덱싱되는 사각형 목록 배열</returns>
        public static List<DefectBox>[] ExtractAll(
            SegformerResult segmentation,
            int cellSize = DefaultCellSize,
            int minCells = DefaultMinCells,
            int maxBoxes = DefaultMaxBoxes)
        {
            var result = new List<DefectBox>[DefectClassCount];
            for (int i = 0; i < DefectClassCount; i++)
                result[i] = new List<DefectBox>();

            if (segmentation?.ClassMap == null || segmentation.Width <= 0 || segmentation.Height <= 0)
                return result;

            int width = segmentation.Width;
            int height = segmentation.Height;

            int columns = Mathf.CeilToInt(width / (float)cellSize);
            int rows = Mathf.CeilToInt(height / (float)cellSize);
            int cellCount = columns * rows;

            // 결함 종류별 격자. 해당 칸에 그 종류의 픽셀이 하나라도 있으면 true.
            var grids = new bool[DefectClassCount][];
            for (int i = 0; i < DefectClassCount; i++)
                grids[i] = new bool[cellCount];

            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * width;
                int cellRow = y / cellSize;

                for (int x = 0; x < width; x++)
                {
                    int segClassId = segmentation.ClassMap[rowOffset + x];
                    if (segClassId <= 0 || segClassId > DefectClassCount)
                        continue; // 0은 배경이고 범위를 벗어난 값은 무시한다

                    // SegFormer는 배경이 0번을 차지하므로 RT-DETR 기준 id로 되돌리려면 1을 뺀다.
                    grids[segClassId - 1][cellRow * columns + x / cellSize] = true;
                }
            }

            for (int classId = 0; classId < DefectClassCount; classId++)
            {
                FindComponents(grids[classId], columns, rows, cellSize, width, height,
                    minCells, maxBoxes, result[classId]);
            }

            return result;
        }

        /// <summary>
        /// 격자에서 이어진 칸 덩어리를 찾아 각각을 감싸는 사각형으로 만든다.
        /// 상하좌우로만 이어진 것을 한 덩어리로 본다.
        /// </summary>
        private static void FindComponents(
            bool[] grid, int columns, int rows, int cellSize,
            int width, int height, int minCells, int maxBoxes, List<DefectBox> results)
        {
            var visited = new bool[grid.Length];
            var stack = new Stack<int>();

            for (int start = 0; start < grid.Length; start++)
            {
                if (!grid[start] || visited[start])
                    continue;

                visited[start] = true;
                stack.Push(start);

                int minColumn = columns, maxColumn = -1;
                int minRow = rows, maxRow = -1;
                int size = 0;

                while (stack.Count > 0)
                {
                    int index = stack.Pop();
                    int column = index % columns;
                    int row = index / columns;

                    size++;
                    if (column < minColumn) minColumn = column;
                    if (column > maxColumn) maxColumn = column;
                    if (row < minRow) minRow = row;
                    if (row > maxRow) maxRow = row;

                    PushIfMarked(grid, visited, stack, column - 1, row, columns, rows);
                    PushIfMarked(grid, visited, stack, column + 1, row, columns, rows);
                    PushIfMarked(grid, visited, stack, column, row - 1, columns, rows);
                    PushIfMarked(grid, visited, stack, column, row + 1, columns, rows);
                }

                if (size < minCells)
                    continue; // 잡음 수준의 작은 덩어리는 버린다

                results.Add(new DefectBox
                {
                    xMin = minColumn * cellSize / (float)width,
                    yMin = minRow * cellSize / (float)height,
                    xMax = Mathf.Min((maxColumn + 1) * cellSize, width) / (float)width,
                    yMax = Mathf.Min((maxRow + 1) * cellSize, height) / (float)height,
                });
            }

            if (results.Count <= maxBoxes)
                return;

            // 너무 많으면 화면이 사각형으로 뒤덮인다. 넓은 것부터 남긴다.
            results.Sort((a, b) => b.Area.CompareTo(a.Area));
            results.RemoveRange(maxBoxes, results.Count - maxBoxes);
        }

        private static void PushIfMarked(
            bool[] grid, bool[] visited, Stack<int> stack, int column, int row, int columns, int rows)
        {
            if (column < 0 || column >= columns || row < 0 || row >= rows)
                return;

            int index = row * columns + column;
            if (!grid[index] || visited[index])
                return;

            visited[index] = true;
            stack.Push(index);
        }
    }
}
