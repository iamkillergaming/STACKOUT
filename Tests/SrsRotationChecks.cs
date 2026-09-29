using System;
using UnityEngine;

// Standalone regression checks; compile with SrsRotation.cs and UnityEngine.CoreModule.dll.
public static class SrsRotationChecks
{
    private static readonly Vector2Int[] T = { V(0, 0), V(1, 0), V(2, 0), V(1, 1) };
    private static readonly Vector2Int[] I = { V(0, 0), V(1, 0), V(2, 0), V(3, 0) };
    private static Vector2Int V(int x, int y) => new Vector2Int(x, y);
    private static bool Open(Vector2Int[] cells, Vector2Int origin) => true;
    private static bool Board(Vector2Int[] cells, Vector2Int origin)
    {
        foreach (var cell in cells)
        {
            var world = origin + cell;
            if (world.x < 0 || world.x >= 10 || world.y < 0) return false;
        }
        return true;
    }
    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
    }
    private static void Equal(Vector2Int[] actual, Vector2Int[] expected)
    {
        for (int i = 0; i < expected.Length; i++) Check(actual[i] == expected[i], "Cell drift");
    }
    public static void Main()
    {
        Vector2Int[][] shapes = {
            I, new[] { V(0, 0), V(1, 0), V(0, 1), V(1, 1) }, T,
            new[] { V(0, 0), V(1, 0), V(1, 1), V(2, 1) },
            new[] { V(1, 0), V(2, 0), V(0, 1), V(1, 1) },
            new[] { V(0, 0), V(1, 0), V(2, 0), V(0, 1) },
            new[] { V(0, 0), V(1, 0), V(2, 0), V(2, 1) }
        };
        for (int shape = 0; shape < shapes.Length; shape++)
        {
            if (shape == 1) continue;
            foreach (bool clockwise in new[] { true, false })
            {
                var cells = shapes[shape];
                var origin = V(3, 8);
                int state = 0;
                for (int step = 0; step < 4; step++)
                {
                    Check(SrsRotation.TryRotate(shape, cells, origin, state, clockwise, Open,
                        out cells, out origin, out state), "Open rotation");
                }
                Equal(cells, shapes[shape]);
                Check(state == 0 && origin == V(3, 8), "Cycle drift");
            }
        }

        Check(SrsRotation.TryRotate(2, T, V(4, 0), 0, true, Board,
            out var result, out var position, out var orientation), "T floor rotation");
        Check(position == V(3, 1) && orientation == 1, "T must use third floor candidate");
        Check(SrsRotation.TryRotate(0, I, V(3, 0), 0, true, Board,
            out result, out position, out orientation), "I floor rotation");
        Check(position == V(4, 2), "I must use fifth floor candidate");

        SrsRotation.TryRotate(0, I, V(0, 0), 0, true, Open, out var right, out _, out _);
        Equal(right, new[] { V(2, 1), V(2, 0), V(2, -1), V(2, -2) });
        Check(SrsRotation.TryRotate(0, right, V(-2, 6), 1, true, Board,
            out result, out position, out orientation), "I left wall");
        Check(position == V(0, 6), "I left wall two-cell kick");
        SrsRotation.TryRotate(0, I, V(0, 0), 0, false, Open, out var left, out _, out _);
        Check(SrsRotation.TryRotate(0, left, V(8, 6), 3, true, Board,
            out result, out position, out orientation), "I right wall");
        Check(position == V(6, 6), "I right wall two-cell kick");

        // An obstacle at (5,4) blocks T's basic rotation but permits its first left kick.
        Func<Vector2Int[], Vector2Int, bool> stack = (cells, origin) => {
            if (!Board(cells, origin)) return false;
            foreach (var cell in cells) if (cell + origin == V(5, 4)) return false;
            return true;
        };
        Check(SrsRotation.TryRotate(2, T, V(4, 5), 0, true, stack,
            out result, out position, out orientation), "Stack kick");
        Check(position == V(3, 5), "Stack kick priority");

        // Verify all eight transitions in both families try exactly five positions and fail atomically.
        foreach (int shape in new[] { 0, 2 })
        {
            var cells = shapes[shape];
            for (int state = 0; state < 4; state++)
            {
                foreach (bool clockwise in new[] { true, false })
                {
                    int attempts = 0;
                    Check(!SrsRotation.TryRotate(shape, cells, V(3, 7), state, clockwise,
                        (candidate, at) => { attempts++; return false; },
                        out result, out position, out orientation), "Blocked rotation accepted");
                    Check(attempts == 5 && ReferenceEquals(result, cells)
                        && position == V(3, 7) && orientation == state, "Failure changed state");
                }
                SrsRotation.TryRotate(shape, cells, V(0, 0), state, true, Open, out cells, out _, out _);
            }
        }
        Check(!SrsRotation.TryRotate(1, shapes[1], V(4, 0), 0, true,
            (candidate, at) => { throw new Exception("O must not kick"); },
            out result, out position, out orientation), "O moved");
        Equal(result, shapes[1]);
        Check(SrsRotation.TryRotate(0, I, V(3, 19), 0, true, Board,
            out result, out position, out orientation) && position == V(3, 19), "Spawn rotation above board");
        Console.WriteLine("SRS checks passed: cycles, pivot, floor, walls, stack, rejection, O, spawn.");
    }
}
